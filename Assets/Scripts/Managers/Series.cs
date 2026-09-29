using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Серия карт матча: очередь карт, текущая карта, общий счёт серии. Сервер ведёт,
    /// клиенты получают состояние репликацией.
    ///
    /// <para>
    /// <b>Зачем отдельный объект.</b> Счёт и состав матча раньше жили в экземпляре режима
    /// (<c>GameMode._teamScores</c>) и исчезали вместе с ним — а режим на карте теперь
    /// меняется на месте (разминка → матч → разминка), и карта в серии не одна. Серия
    /// живёт на постоянном объекте сессии (<c>SessionContext</c>, рядом с
    /// <see cref="SessionManager"/>) и переживает и смену режима, и смену карт.
    /// Счёт внутри карты (раунды Elimination) по-прежнему у режима: это его структура,
    /// и на следующей карте она начинается с нуля.
    /// </para>
    ///
    /// <para>
    /// <b>Поток.</b> <see cref="ServerBegin"/> (админ нажал «Начать» в лобби) → первая карта
    /// стартует в разминке → «Начать матч» переключает режим на месте → режим объявил
    /// победителя (<c>MapReferee.Finished</c>) → результат в счёт серии, карта
    /// возвращается в разминку и <b>ждёт</b> → админ нажимает «Следующая карта»
    /// (<c>MatchCommand.NextMap</c>) → <see cref="ServerAdvance"/> грузит следующую карту,
    /// после последней — лобби. Сама серия дальше не идёт: когда игроки готовы к следующей
    /// карте, решает админ. Смена сцены — только <c>MapLoader.LoadMap</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Команды.</b> Внутри серии команды (Военные/Повстанцы) сохраняются — у разминки
    /// своих команд нет, и она никого не переназначает. Конец серии распускает команды:
    /// перед возвратом в лобби у всех игроков команда снимается
    /// (<see cref="SessionTeamAssigner.ClearBeforeSceneChange"/>), аватар — киборг,
    /// команду на следующую серию выбирают заново.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.Series)]
    public class Series : NetworkBehaviour
    {
        public static Series Instance { get; private set; }

        private readonly SyncList<string> _maps = new SyncList<string>();

        /// <summary>Общий счёт серии: <c>teamIndex</c> → выиграно карт.</summary>
        private readonly SyncDictionary<int, int> _mapWins = new SyncDictionary<int, int>();

        /// <summary>Победитель каждой сыгранной карты по порядку, −1 — ничья.</summary>
        private readonly SyncList<int> _results = new SyncList<int>();

        [SyncVar] private int _currentIndex = -1;
        [SyncVar] private bool _running;

        private IPlayerRoster _roster;

        /// <summary>Идёт ли серия.</summary>
        public bool IsRunning => _running;

        /// <summary>
        /// Статистика карты без серии: карту загрузили напрямую (отладка, E2E). Серия при этом
        /// не идёт — ни следующей карты, ни лобби, кнопки «Стоп» нет, — но статистика
        /// ведётся по этой карте: одна строка карты, TOTAL равен ей.
        /// </summary>
        public bool IsAdHoc => _adHoc;

        /// <summary>Пишется ли сейчас статистика: идёт серия или карта без серии.</summary>
        public bool IsRecording => _running || _adHoc;

        [SyncVar] private bool _adHoc;

        /// <summary>Оркестратор текущей карты — откуда брать сцену для статистики без серии.</summary>
        private MapReferee _currentManager;

        /// <summary>Карты серии по порядку.</summary>
        public IReadOnlyList<string> Maps => _maps;

        /// <summary>Индекс текущей карты в <see cref="Maps"/>, −1 — серия не начата.</summary>
        public int CurrentIndex => _currentIndex;

        /// <summary>Сцена текущей карты серии или null.</summary>
        public string CurrentMapScene =>
            _currentIndex >= 0 && _currentIndex < _maps.Count ? _maps[_currentIndex] : null;

        /// <summary>Результаты сыгранных карт: <c>teamIndex</c> победителя, −1 — ничья.</summary>
        public IReadOnlyList<int> Results => _results;

        /// <summary>Сколько карт серии выиграла команда.</summary>
        public int GetMapWins(TeamData team)
        {
            if (team == null) return 0;
            return _mapWins.TryGetValue(team.teamIndex, out int wins) ? wins : 0;
        }

        /// <summary>Откуда серия знает игроков (конец серии отпускает их команды). Подмена — тесты.</summary>
        public IPlayerRoster PlayerRoster
        {
            get => _roster ?? (_roster = new PlayersManagerRoster());
            set => _roster = value;
        }

        /// <summary>Загрузчик карт. По умолчанию — <c>MapLoader.LoadMap</c>; подмена — тесты.</summary>
        public Action<string> LoadMapOverride { get; set; }

        // ── Жизненный цикл ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            MapReferee.UnsubscribeFromInstance(HandleMapRefereeReady);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            MapReferee.SubscribeToInstance(HandleMapRefereeReady);
        }

        public override void OnStopServer()
        {
            MapReferee.UnsubscribeFromInstance(HandleMapRefereeReady);
            base.OnStopServer();
        }

        /// <summary>Новая карта загрузилась — слушаем конец матча на ней.</summary>
        private void HandleMapRefereeReady(MapReferee manager)
        {
            if (manager == null) return;
            _currentManager = manager;
            manager.Finished -= HandleMapMatchEnded;
            manager.Finished += HandleMapMatchEnded;
            manager.RoundWon -= HandleRoundWon;
            manager.RoundWon += HandleRoundWon;
            manager.PlayerKilled -= HandlePlayerKilled;
            manager.PlayerKilled += HandlePlayerKilled;
        }

        private void HandleMapMatchEnded(TeamData winner)
        {
            if (!NetworkServer.active) return;

            // Итог — в счёт. Дальше серия не идёт: карта в разминке ждёт «Следующая карта» админа.
            ServerRecordMapResult(winner);
            if (_running)
                GameLog.Match.Info($"[Series] Карта {_currentIndex + 1}/{_maps.Count} сыграна — ждём «Следующая карта» от админа.");
        }

        /// <summary>Идёт последняя карта серии: «Следующая карта» ведёт в лобби.</summary>
        public bool IsLastMap => _running && _currentIndex >= _maps.Count - 1;

        // ── Серверное управление ─────────────────────────────────────────────

        /// <summary>
        /// Начинает серию: сбрасывает общий счёт, запоминает карты и грузит первую.
        /// </summary>
        /// <returns>false — пустой список карт.</returns>
        [Server]
        public bool ServerBegin(IReadOnlyList<string> maps)
        {
            if (maps == null || maps.Count == 0)
            {
                GameLog.Match.Warning("[Series] ServerBegin: пустой список карт — серия не начата.");
                return false;
            }

            _maps.Clear();
            foreach (string map in maps)
                if (!string.IsNullOrEmpty(map)) _maps.Add(map);

            if (_maps.Count == 0)
            {
                GameLog.Match.Warning("[Series] ServerBegin: в списке нет ни одной сцены.");
                return false;
            }

            _mapWins.Clear();
            _results.Clear();
            _teamStats.Clear();
            _playerStats.Clear();
            _currentIndex = 0;
            _adHoc = false;
            _running = true;

            GameLog.Match.Info($"[Series] Серия началась: {string.Join(" → ", ToArray(_maps))}.");
            Load(_maps[0]);
            return true;
        }

        /// <summary>Записывает итог карты в общий счёт серии. Ничья (null) — только в результаты.</summary>
        [Server]
        public void ServerRecordMapResult(TeamData winner)
        {
            if (!EnsureRecording()) return;

            _results.Add(winner != null ? winner.teamIndex : -1);

            if (winner != null)
            {
                _mapWins.TryGetValue(winner.teamIndex, out int wins);
                _mapWins[winner.teamIndex] = wins + 1;
            }

            GameLog.Match.Info(
                $"[Series] Карта {_currentIndex + 1}/{_maps.Count}: победитель {(winner != null ? winner.Name : "ничья")}.");
        }

        /// <summary>
        /// Переход к следующей карте серии; после последней — конец серии и лобби.
        /// </summary>
        /// <returns>Сцена, которая грузится, или null — серия не идёт.</returns>
        [Server]
        public string ServerAdvance()
        {
            if (!_running)
            {
                GameLog.Match.Warning("[Series] ServerAdvance: серия не идёт.");
                return null;
            }

            int next = _currentIndex + 1;
            if (next < _maps.Count)
            {
                _currentIndex = next;
                GameLog.Match.Info($"[Series] Следующая карта {next + 1}/{_maps.Count}: {_maps[next]}.");
                Load(_maps[next]);
                return _maps[next];
            }

            GameLog.Match.Info("[Series] Серия сыграна — возврат в лобби.");
            return FinishAndReturnToLobby();
        }

        /// <summary>
        /// Досрочный конец серии (админ: «Стоп / Лобби»). Общий счёт остаётся до начала
        /// следующей серии — его можно посмотреть в лобби.
        /// </summary>
        /// <returns>Сцена лобби.</returns>
        [Server]
        public string ServerEnd()
        {
            GameLog.Match.Info("[Series] Серия остановлена администратором — возврат в лобби.");
            return FinishAndReturnToLobby();
        }

        [Server]
        private string FinishAndReturnToLobby()
        {
            _running = false;
            _currentIndex = -1;

            MapRegistry maps = SessionManager.Instance != null ? SessionManager.Instance.MapRegistry : null;
            string lobby = maps != null ? maps.LobbyScene : "Lobby";

            ReleaseMatchTeams();
            Load(lobby);
            return lobby;
        }

        /// <summary>
        /// Конец серии: команды распускаются. У каждого игрока команда снимается до смены
        /// сцены — в лобби он появится без команды (киборгом) и выберет её на новую серию.
        /// </summary>
        private void ReleaseMatchTeams()
        {
            foreach (PlayerSession session in PlayerRoster.GetAllPlayers())
            {
                if (session == null || session.Role != GameRole.Player || session.TeamIndex == 0) continue;
                SessionTeamAssigner.ClearBeforeSceneChange(session, "Series/конец серии");
            }
        }

        private void Load(string scene)
        {
            // Снаряжение не переживает перехода на другую карту (и в лобби).
            EquipmentStrip.ServerStripAll($"переход на карту {scene}");

            if (LoadMapOverride != null) { LoadMapOverride(scene); return; }

            if (MapLoader.Instance == null)
            {
                GameLog.Error($"[Series] Нет MapLoader — карта '{scene}' не загружена.");
                return;
            }

            MapLoader.Instance.LoadMap(scene);
        }

        private static string[] ToArray(SyncList<string> list)
        {
            var result = new string[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = list[i];
            return result;
        }

        // ── Сквозная статистика серии ────────────────────────────────────────
        //
        // Строки «карта × команда» и «карта × игрок» в SyncList: состояние, а не сообщения,
        // поэтому подключившийся позже клиент получает всю таблицу начальным значением спавна.
        // Игрок — по сессии (PlayerKey), а не по аватару: аватар пересоздаётся при смене
        // скина и команды, сессия живёт всю игру. TOTAL не хранится — это сумма строк.

        /// <summary>Индекс «все карты серии» для геттеров статистики.</summary>
        public const int Total = -1;

        private readonly SyncList<TeamMapStat> _teamStats = new SyncList<TeamMapStat>();
        private readonly SyncList<PlayerMapStat> _playerStats = new SyncList<PlayerMapStat>();

        /// <summary>Раунды команд по картам.</summary>
        public IReadOnlyList<TeamMapStat> TeamStats => _teamStats;

        /// <summary>Убийства, смерти, ассисты игроков по картам.</summary>
        public IReadOnlyList<PlayerMapStat> PlayerStats => _playerStats;

        /// <summary>
        /// Ключ игрока в статистике: токен устройства (переживает переподключение — тот же,
        /// которым пользуется восстановление сессии), без него — <c>netId</c> сессии.
        /// </summary>
        public static string PlayerKey(PlayerSession session)
        {
            if (session == null) return null;
            return !string.IsNullOrEmpty(session.DeviceToken) ? session.DeviceToken : "net:" + session.netId;
        }

        public int GetRoundsWon(TeamData team, int map)
        {
            if (team == null) return 0;
            int sum = 0;
            foreach (TeamMapStat row in _teamStats)
                if (row.team == team.teamIndex && (map == Total || row.map == map)) sum += row.rounds;
            return sum;
        }

        public int GetKills(PlayerSession session, int map) => SumPlayer(session, map, r => r.kills);
        public int GetDeaths(PlayerSession session, int map) => SumPlayer(session, map, r => r.deaths);
        public int GetAssists(PlayerSession session, int map) => SumPlayer(session, map, r => r.assists);

        private int SumPlayer(PlayerSession session, int map, Func<PlayerMapStat, int> value)
        {
            string key = PlayerKey(session);
            if (key == null) return 0;
            int sum = 0;
            foreach (PlayerMapStat row in _playerStats)
                if (row.player == key && (map == Total || row.map == map)) sum += value(row);
            return sum;
        }

        /// <summary>Раунд выиграла команда — в строку текущей карты. Только раунды, доигранные до конца.</summary>
        [Server]
        public void ServerRecordRoundWin(TeamData team)
        {
            if (team == null || !EnsureRecording()) return;

            for (int i = 0; i < _teamStats.Count; i++)
            {
                TeamMapStat row = _teamStats[i];
                if (row.map != _currentIndex || row.team != team.teamIndex) continue;
                row.rounds++;
                _teamStats[i] = row;
                return;
            }

            _teamStats.Add(new TeamMapStat { map = _currentIndex, team = team.teamIndex, rounds = 1 });
        }

        /// <summary>
        /// Гибель игрока: смерть — жертве, убийство — убийце, ассист — ранившим. Убийца null
        /// (урон без источника) или сама жертва (самоубийство) — убийства нет, смерть есть.
        /// </summary>
        [Server]
        public void ServerRecordKill(PlayerSession victim, PlayerSession killer, IReadOnlyList<PlayerSession> assists)
        {
            if (!EnsureRecording()) return;

            if (killer == victim) killer = null;

            if (victim != null) Bump(victim, deaths: 1);
            if (killer != null) Bump(killer, kills: 1);

            if (assists != null)
            {
                foreach (PlayerSession helper in assists)
                {
                    if (helper == null || helper == victim || helper == killer) continue;
                    Bump(helper, assists: 1);
                }
            }
        }

        private void Bump(PlayerSession session, int kills = 0, int deaths = 0, int assists = 0)
        {
            string key = PlayerKey(session);
            for (int i = 0; i < _playerStats.Count; i++)
            {
                PlayerMapStat row = _playerStats[i];
                if (row.map != _currentIndex || row.player != key) continue;
                row.kills += kills;
                row.deaths += deaths;
                row.assists += assists;
                row.name = session.PlayerName;
                row.team = session.TeamIndex;
                _playerStats[i] = row;
                return;
            }

            _playerStats.Add(new PlayerMapStat
            {
                map = _currentIndex, player = key, name = session.PlayerName, team = session.TeamIndex,
                kills = kills, deaths = deaths, assists = assists
            });
        }

        /// <summary>
        /// Можно ли писать статистику, и куда. Идёт серия — в её текущую карту. Серии нет —
        /// статистика карты без серии: неявная серия из одной текущей карты, без перехода
        /// к следующей и без лобби (<see cref="IsAdHoc"/>). Начинается с первого события
        /// на карте; другая карта без серии начинает её заново — прежняя статистика не
        /// перетекает (отладочные загрузки карт между собой не связаны).
        /// </summary>
        private bool EnsureRecording()
        {
            if (_running) return true;

            string scene = _currentManager != null ? _currentManager.SceneName : null;
            if (string.IsNullOrEmpty(scene)) return false;

            if (_adHoc && _maps.Count == 1 && _maps[0] == scene) return true;

            _maps.Clear();
            _maps.Add(scene);
            _mapWins.Clear();
            _results.Clear();
            _teamStats.Clear();
            _playerStats.Clear();
            _currentIndex = 0;
            _adHoc = true;

            GameLog.Match.Info($"[Series] Карта '{scene}' без серии — статистика ведётся по ней.");
            return true;
        }

        /// <summary>Раунд на карте доигран — в статистику серии.</summary>
        private void HandleRoundWon(TeamData winner)
        {
            if (NetworkServer.active) ServerRecordRoundWin(winner);
        }

        /// <summary>Игрок погиб на карте — в статистику серии.</summary>
        private void HandlePlayerKilled(PlayerSession victim, PlayerSession killer, IReadOnlyList<PlayerSession> assists)
        {
            if (NetworkServer.active) ServerRecordKill(victim, killer, assists);
        }
    }

    /// <summary>Раунды, выигранные командой на карте серии.</summary>
    [System.Serializable]
    public struct TeamMapStat
    {
        public int map;
        public int team;
        public int rounds;
    }

    /// <summary>Убийства, смерти, ассисты игрока на карте серии.</summary>
    [System.Serializable]
    public struct PlayerMapStat
    {
        public int map;
        public string player;
        public string name;
        public int team;
        public int kills;
        public int deaths;
        public int assists;
    }
}
