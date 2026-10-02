using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// «Матч с ботами» на сервере (T-48): одна кнопка на «Обзоре» планшета ведёт игрока от лобби до идущего матча.
    ///
    /// <list type="number">
    /// <item>В лобби (на карте нет режима матча) — серия из одной карты: режим <see cref="BotMatchPlan.PickMode"/>
    ///       (Elimination), первая совместимая карта реестра. Карта грузится обычным путём серии
    ///       (<c>SessionManager.StartSession</c> → <c>MapLoader.LoadMap</c>), запрос ждёт её разминки.</item>
    /// <item>На карте в разминке — боты до полных команд (<see cref="BotMatchPlan.BotsToAdd"/>: один человек —
    ///       два на два), «Начать матч» (<c>MapReferee.GoLive</c>), людям без команды — команда автобалансом
    ///       (иначе матч ждал бы их выбора в планшете).</item>
    /// </list>
    ///
    /// <para>
    /// Право — <see cref="BotMatchPlan.CanRequest"/>: админ или единственный человек на сервере. Режим отладки и
    /// админская сборка не нужны. Боты остаются после матча (следующий «Матч с ботами» их переиспользует);
    /// убрать — «Отладка» → «Убрать всех».
    /// </para>
    /// </summary>
    public sealed class BotMatchStarter : MonoBehaviour
    {
        /// <summary>Сколько ждать загрузки карты, прежде чем забыть запрос.</summary>
        private const float PendingTimeout = 60f;

        private static BotMatchStarter _instance;

        private bool _pending;
        private float _pendingSince;

        /// <summary>Запрос игрока <paramref name="requester"/>. Возвращает текст для лога/ответа.</summary>
        public static bool ServerRequest(PlayerSession requester, out string result)
        {
            if (!NetworkServer.active)
            {
                result = "сервер не поднят";
                return false;
            }

            bool admin = SessionPermissions.IsAdmin(requester);
            bool player = requester != null && requester.Role == GameRole.Player;
            int otherHumans = CountHumans(PlayersManager.Instance != null ? PlayersManager.Instance.Sessions : null, requester);

            if (!BotMatchPlan.CanRequest(admin, player, otherHumans))
            {
                result = $"нельзя: на сервере есть другие игроки ({otherHumans}) — матч с ботами запускает админ";
                GameLog.Match.Warning($"[BotMatchStarter] {requester?.PlayerName}: {result}.");
                return false;
            }

            MapReferee referee = MapReferee.Instance;
            if (referee != null && referee.IsLiveOrPaused)
            {
                result = "матч уже идёт";
                return false;
            }

            EnsureInstance().Begin(requester);
            result = "запускаю";
            return true;
        }

        /// <summary>Люди (роль «игрок», не боты), кроме <paramref name="except"/>.</summary>
        public static int CountHumans(IEnumerable<PlayerSession> sessions, PlayerSession except)
        {
            if (sessions == null) return 0;
            return sessions.Count(s => s != null && s != except && s.Role == GameRole.Player && !BotDirector.IsBotToken(s.DeviceToken));
        }

        private static BotMatchStarter EnsureInstance()
        {
            if (_instance != null) return _instance;

            var go = new GameObject("BotMatchStarter");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BotMatchStarter>();
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Begin(PlayerSession requester)
        {
            if (MapHasMatch())
            {
                GoLive();
                return;
            }

            // Лобби: сначала серия на боевую карту, матч — когда она встанет в разминку.
            if (!StartSeries())
            {
                GameLog.Match.Warning("[BotMatchStarter] Нет режима матча или совместимой карты — матч с ботами не начат.");
                return;
            }

            _pending = true;
            _pendingSince = Time.unscaledTime;
            GameLog.Match.Info($"[BotMatchStarter] {requester?.PlayerName}: матч с ботами — грузим карту.");
        }

        private void Update()
        {
            if (!NetworkServer.active)
            {
                Destroy(gameObject);
                return;
            }

            if (!_pending) return;

            MapReferee referee = MapReferee.Instance;
            bool live = referee != null && referee.isServer && referee.IsLiveOrPaused;
            switch (BotMatchPlan.Step(NetworkServer.isLoadingScene, Time.unscaledTime - _pendingSince, PendingTimeout, MapHasMatch(), live))
            {
                case BotMatchPlan.PendingStep.GoLive:
                    _pending = false;
                    GoLive();
                    break;
                case BotMatchPlan.PendingStep.Fill:
                    _pending = false;
                    GameLog.Match.Info("[BotMatchStarter] Матч на карте уже начат — добираем ботов и раздаём команды.");
                    Fill(referee);
                    break;
                case BotMatchPlan.PendingStep.Expire:
                    _pending = false;
                    GameLog.Match.Warning("[BotMatchStarter] Карта не встала в разминку за отведённое время — запрос снят.");
                    break;
            }
        }

        /// <summary>Карта с режимом матча в разминке — можно начинать.</summary>
        private static bool MapHasMatch()
        {
            MapReferee referee = MapReferee.Instance;
            return referee != null && referee.isServer && AdminMapCommands.IsAvailable(MapCommand.GoLive);
        }

        private static bool StartSeries()
        {
            SessionManager session = SessionManager.Instance;
            if (session == null || session.ModeRegistry == null) return false;

            List<GameModeData> modes = session.ModeRegistry.MatchModes.Where(m => m != null).ToList();
            string modeId = BotMatchPlan.PickMode(modes.Select(m => m.modeId).ToList());
            GameModeData mode = modes.FirstOrDefault(m => m.modeId == modeId);
            if (mode == null || session.MapRegistry == null || session.MapRegistry.maps == null) return false;

            MapData map = session.MapRegistry.maps.FirstOrDefault(m => m != null && MapModeRules.IsCompatible(m, mode));
            if (map == null) return false;

            GameLog.Match.Info($"[BotMatchStarter] Серия с ботами: режим '{mode.modeId}', карта '{map.sceneName}'.");
            session.SetSeries(mode.modeId, new[] { map.sceneName });
            session.StartSession();
            return true;
        }

        private static void GoLive()
        {
            PlayersManager players = PlayersManager.Instance;
            MapReferee referee = MapReferee.Instance;
            if (players == null || referee == null) return;

            if (!referee.GoLive())
            {
                GameLog.Match.Warning("[BotMatchStarter] «Начать матч» не прошёл — см. лог MapReferee.");
                return;
            }

            Fill(referee);
        }

        /// <summary>Боты до полных команд и команда людям без неё — в уже начатом матче.</summary>
        private static void Fill(MapReferee referee)
        {
            PlayersManager players = PlayersManager.Instance;
            if (players == null || referee == null) return;

            GameMode mode = referee.ActiveGameMode;
            int teams = mode != null && mode.Teams != null ? mode.Teams.Count(t => t != null) : 0;
            int humans = CountHumans(players.Sessions, null);

            BotDirector director = BotDirector.EnsureInstance();
            if (director == null) return;

            int add = BotMatchPlan.BotsToAdd(humans, director.Bots.Count, teams);
            director.EnsureCount(director.Bots.Count + add);

            AssignHumans(mode, players.Sessions);

            GameLog.Match.Info($"[BotMatchStarter] Матч с ботами: людей {humans}, ботов {director.Bots.Count}, команд {teams}.");
        }

        /// <summary>
        /// Людям без команды режима — команда автобалансом (боты выбирают свою сами, <see cref="BotTeamChoice"/>).
        /// Выбравший команду в лобби остаётся в ней.
        /// </summary>
        private static void AssignHumans(GameMode mode, IEnumerable<PlayerSession> sessions)
        {
            if (mode == null || mode.Teams == null || mode.Teams.Length == 0) return;

            List<PlayerSession> humans = sessions
                .Where(s => s != null && s.Role == GameRole.Player && !BotDirector.IsBotToken(s.DeviceToken))
                .ToList();

            foreach (KeyValuePair<PlayerSession, TeamData> pair in TeamAutoBalance.Plan(mode.Teams, humans, s => s.TeamIndex))
                SessionTeamAssigner.Apply(pair.Key, pair.Value, "матч с ботами");
        }
    }
}
