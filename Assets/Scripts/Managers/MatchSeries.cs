using System;
using System.Collections;
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
    /// Счёт внутри карты (сеты Elimination) по-прежнему у режима: это его структура,
    /// и на следующей карте она начинается с нуля.
    /// </para>
    ///
    /// <para>
    /// <b>Поток.</b> <see cref="ServerBegin"/> (админ нажал «Начать» в лобби) → первая карта
    /// стартует в разминке → «Начать матч» переключает режим на месте → режим объявил
    /// победителя (<c>GameplayManager.GameplayEnded</c>) → результат в счёт серии, карта
    /// возвращается в разминку → через <see cref="_nextMapDelay"/> <see cref="ServerAdvance"/>
    /// грузит следующую карту, после последней — лобби. Смена сцены — только
    /// <c>MapManager.LoadMap</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Команды.</b> Внутри серии команды матча (CT/T) сохраняются — разминка их не
    /// трогает (<see cref="TeamAssignmentKind.KeepOrDefault"/>). Конец серии отпускает их:
    /// перед возвратом в лобби каждый игрок с командой не из разминки получает команду
    /// разминки со своим скином (<see cref="SessionTeamAssigner.ApplyBeforeSceneChange"/>).
    /// Иначе в лобби игрок стоял бы с командой матча без её зоны и выбирал бы скины только
    /// из неё.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.MatchSeries)]
    public class MatchSeries : NetworkBehaviour
    {
        public static MatchSeries Instance { get; private set; }

        [Tooltip("Сколько секунд карта стоит в разминке после конца матча, прежде чем серия " +
                 "перейдёт к следующей карте (или в лобби). Время увидеть итог.")]
        [Min(0f)]
        [SerializeField] private float _nextMapDelay = 10f;

        private readonly SyncList<string> _maps = new SyncList<string>();

        /// <summary>Общий счёт серии: <c>teamIndex</c> → выиграно карт.</summary>
        private readonly SyncDictionary<int, int> _mapWins = new SyncDictionary<int, int>();

        /// <summary>Победитель каждой сыгранной карты по порядку, −1 — ничья.</summary>
        private readonly SyncList<int> _results = new SyncList<int>();

        [SyncVar] private int _currentIndex = -1;
        [SyncVar] private bool _running;

        private IPlayerRoster _roster;
        private Coroutine _advanceRoutine;

        /// <summary>Идёт ли серия.</summary>
        public bool IsRunning => _running;

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

        /// <summary>Загрузчик карт. По умолчанию — <c>MapManager.LoadMap</c>; подмена — тесты.</summary>
        public Action<string> MapLoader { get; set; }

        // ── Жизненный цикл ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameplayManager.UnsubscribeFromInstance(HandleGameplayManagerReady);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            GameplayManager.SubscribeToInstance(HandleGameplayManagerReady);
        }

        public override void OnStopServer()
        {
            GameplayManager.UnsubscribeFromInstance(HandleGameplayManagerReady);
            base.OnStopServer();
        }

        /// <summary>Новая карта загрузилась — слушаем конец матча на ней.</summary>
        private void HandleGameplayManagerReady(GameplayManager manager)
        {
            if (manager == null) return;
            manager.GameplayEnded -= HandleMapMatchEnded;
            manager.GameplayEnded += HandleMapMatchEnded;
        }

        private void HandleMapMatchEnded(TeamData winner)
        {
            if (!NetworkServer.active) return;

            ServerRecordMapResult(winner);
            if (!_running) return;

            StopPendingAdvance();

            if (_nextMapDelay <= 0f)
            {
                ServerAdvance();
                return;
            }

            _advanceRoutine = StartCoroutine(AdvanceAfterDelay());
        }

        private IEnumerator AdvanceAfterDelay()
        {
            GameLog.Match.Info($"[MatchSeries] Карта сыграна — через {_nextMapDelay:0} с следующая.");
            yield return new WaitForSeconds(_nextMapDelay);
            _advanceRoutine = null;
            ServerAdvance();
        }

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
                GameLog.Match.Warning("[MatchSeries] ServerBegin: пустой список карт — серия не начата.");
                return false;
            }

            StopPendingAdvance();

            _maps.Clear();
            foreach (string map in maps)
                if (!string.IsNullOrEmpty(map)) _maps.Add(map);

            if (_maps.Count == 0)
            {
                GameLog.Match.Warning("[MatchSeries] ServerBegin: в списке нет ни одной сцены.");
                return false;
            }

            _mapWins.Clear();
            _results.Clear();
            _currentIndex = 0;
            _running = true;

            GameLog.Match.Info($"[MatchSeries] Серия началась: {string.Join(" → ", ToArray(_maps))}.");
            Load(_maps[0]);
            return true;
        }

        /// <summary>Записывает итог карты в общий счёт серии. Ничья (null) — только в результаты.</summary>
        [Server]
        public void ServerRecordMapResult(TeamData winner)
        {
            if (!_running) return;

            _results.Add(winner != null ? winner.teamIndex : -1);

            if (winner != null)
            {
                _mapWins.TryGetValue(winner.teamIndex, out int wins);
                _mapWins[winner.teamIndex] = wins + 1;
            }

            GameLog.Match.Info(
                $"[MatchSeries] Карта {_currentIndex + 1}/{_maps.Count}: победитель {(winner != null ? winner.displayName : "ничья")}.");
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
                GameLog.Match.Warning("[MatchSeries] ServerAdvance: серия не идёт.");
                return null;
            }

            int next = _currentIndex + 1;
            if (next < _maps.Count)
            {
                _currentIndex = next;
                GameLog.Match.Info($"[MatchSeries] Следующая карта {next + 1}/{_maps.Count}: {_maps[next]}.");
                Load(_maps[next]);
                return _maps[next];
            }

            GameLog.Match.Info("[MatchSeries] Серия сыграна — возврат в лобби.");
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
            GameLog.Match.Info("[MatchSeries] Серия остановлена администратором — возврат в лобби.");
            return FinishAndReturnToLobby();
        }

        [Server]
        private string FinishAndReturnToLobby()
        {
            StopPendingAdvance();

            _running = false;
            _currentIndex = -1;

            MapRegistry maps = SessionManager.Instance != null ? SessionManager.Instance.MapRegistry : null;
            string lobby = maps != null ? maps.LobbyScene : "Lobby";

            ReleaseMatchTeams(maps != null ? maps.lobby : null);
            Load(lobby);
            return lobby;
        }

        /// <summary>
        /// Конец серии: команды матча больше ничего не значат. Игрок с командой не из
        /// разминки получает команду разминки (скин сохраняется) до смены сцены.
        /// </summary>
        private void ReleaseMatchTeams(MapData lobby)
        {
            GameModeRegistry registry = SessionManager.Instance != null ? SessionManager.Instance.ModeRegistry : null;
            GameModeData warmup = MapModeRules.ResolveWarmup(lobby, registry);
            if (warmup == null || warmup.teams == null || warmup.teams.Length == 0 || warmup.teams[0] == null) return;

            TeamData lobbyTeam = warmup.teams[0];

            foreach (PlayerSession session in PlayerRoster.GetAllPlayers())
            {
                if (session == null || session.Role != GameRole.Player) continue;
                if (Array.Exists(warmup.teams, t => t != null && t.teamIndex == session.TeamIndex)) continue;

                SessionTeamAssigner.ApplyBeforeSceneChange(session, lobbyTeam, "MatchSeries/конец серии");
            }
        }

        private void Load(string scene)
        {
            if (MapLoader != null) { MapLoader(scene); return; }

            if (MapManager.Instance == null)
            {
                GameLog.Error($"[MatchSeries] Нет MapManager — карта '{scene}' не загружена.");
                return;
            }

            MapManager.Instance.LoadMap(scene);
        }

        private void StopPendingAdvance()
        {
            if (_advanceRoutine == null) return;
            StopCoroutine(_advanceRoutine);
            _advanceRoutine = null;
        }

        private static string[] ToArray(SyncList<string> list)
        {
            var result = new string[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = list[i];
            return result;
        }
    }
}
