// Ярус C (несколько процессов) — серия карт клиентом-админом (этап 3 эпика e2e-regression, итерация 2).
// Отключить из сборки: VRBG_NO_E2E.
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Economy;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// <c>scripted-series-next-map</c> (S + 2): клиент-админ начинает серию [TestMap2 → TestMap1] командой «Новая серия»
    /// планшета, из разминки жмёт «Следующая карта» дважды одним кадром (повтор во время перехода), на второй карте
    /// начинает матч; клиенты доходят до своих зон. Сервер только читает: серия и её карта, сессии, команды и права
    /// пережили смену карты, повтор команды не сдвинул серию, на новой карте — своя экономика.
    /// </summary>
    public sealed partial class ScriptedClientScenario
    {
        public const string SeriesNextMapName = "scripted-series-next-map";
        public const string SeriesStopName = "scripted-series-stop";

        private const string FirstMap = "TestMap2";
        private const string SecondMap = "TestMap1";

        private const string CheckSeriesStarted = "сервер: админ начал серию TestMap2 → TestMap1 — карта 1 загружена заново в разминке";
        private const string CheckNextMap = "сервер: «Следующая карта» — TestMap1 в разминке, серия идёт (карта 2/2), повтор команды отклонён";
        private const string CheckSessionsKept = "сервер: сессии, команды и права пережили обе загрузки карт";
        private const string CheckSecondMapLive = "сервер: на карте 2 админ начал матч, все в своих зонах — Equipment, деньги новой карты";
        private const string CheckStopLive = "сервер: матч начал админ, все в своих зонах — Equipment";
        private const string CheckLobby = "сервер: «Стоп» посреди матча — лобби в разминке, серия остановлена, команды распущены";
        private const string CheckStopRepeat = "сервер: повтор «Стоп» во время перехода отклонён с причиной";
        private const string CheckLobbyKept = "сервер: в лобби — те же сессии с правами, карты серии видны до новой серии";

        private IEnumerator RunSeriesServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClients, CheckTeams, CheckAdminRights, CheckSeriesStarted, CheckNextMap, CheckSessionsKept,
                CheckSecondMapLive);
            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");
            _adminLog.Clear();
            Application.logMessageReceived += OnLog;
            try
            {
                yield return E2EBarrier.WaitClients(context, "input-active", 90f);
                SetClients(result, context);

                yield return E2EBarrier.WaitClients(context, "team-requested", 90f);
                E2EWaitOutcome teams = new E2EWaitOutcome();
                yield return E2EWait.Until(teams, "клиенты в разных командах", 30f,
                    () => DistinctTeams() >= 2 && AllHaveTeam(),
                    () => DescribeTeams());
                result.Set(CheckTeams, teams.Succeeded, DescribeTeams() + " — " + teams.Diagnosis);
                if (!teams.Succeeded) Abort(result, "команды не выбраны");
                CheckRights(result);

                // Снимок до серии: сессии, их команды и права, аватары. Смена карты не должна менять ничего, кроме аватаров.
                var sessions = new List<PlayerSession>(HumanSessions());
                var team = new Dictionary<PlayerSession, int>();
                var admin = new Dictionary<PlayerSession, bool>();
                var avatars = new Dictionary<PlayerSession, uint>();
                foreach (PlayerSession session in sessions)
                {
                    team[session] = session.TeamIndex;
                    admin[session] = session.IsAdmin;
                    avatars[session] = NetId(AvatarOf(session));
                }

                MapReferee startup = MapReferee.Instance;
                context.ServerStep("teams-ok");

                // Карта 1 серии: та же сцена, загруженная заново.
                MapReferee first = null;
                E2EWaitOutcome firstLoaded = new E2EWaitOutcome();
                yield return E2EWait.Until(firstLoaded, $"серия началась, {FirstMap} загружена заново", 120f,
                    () => (first = NewMap(startup, FirstMap)) != null && SeriesAt(0),
                    () => $"карта {DescribeMap(startup)}; серия {DescribeSeries()}");
                yield return E2EBarrier.WaitClients(context, "series-started", 120f);
                result.Set(CheckSeriesStarted, firstLoaded.Succeeded,
                    $"{firstLoaded.Diagnosis}; серия {DescribeSeries()}; {DescribeMap(first)}");
                if (!firstLoaded.Succeeded) Abort(result, "серия не началась");
                bool keptFirst = Kept(sessions, team, admin, out _);
                string afterFirst = DescribeKept(sessions, team, admin, avatars);
                context.ServerStep("series-verified");

                // «Следующая карта» ×2 одним кадром: вторая приходит во время перехода и не должна сдвинуть серию.
                MapReferee second = null;
                E2EWaitOutcome secondLoaded = new E2EWaitOutcome();
                yield return E2EWait.Until(secondLoaded, $"{SecondMap} загружена", 120f,
                    () => (second = NewMap(first, SecondMap)) != null,
                    () => $"карта {DescribeMap(MapReferee.Instance)}; серия {DescribeSeries()}");
                // Отклонённый повтор и события загрузки успевают дойти до лога и реплики.
                yield return E2EWait.Hold(2f);
                bool seriesOk = SeriesAt(1) && second != null && second.CurrentState == MapState.Warmup;
                // Повтор должен дойти до сервера и получить отказ — иначе проверка прошла бы без повтора.
                bool refused = HasAdminLog($"{MapCommand.NextMap} отклонена: идёт смена карты");
                result.Set(CheckNextMap, secondLoaded.Succeeded && seriesOk && refused,
                    $"{secondLoaded.Diagnosis}; серия {DescribeSeries()}; {DescribeMap(second)}; повтор отклонён={refused}; {AdminLogTail()}");

                bool kept = Kept(sessions, team, admin, out string keptDetail);
                result.Set(CheckSessionsKept, keptFirst && kept, $"после карты 1: {afterFirst}; после карты 2: {keptDetail}");
                yield return E2EBarrier.WaitClients(context, "map-2", 120f);
                context.ServerStep("map-verified");

                // Карта 2: матч начинает админ, экономика — новая (стартовые деньги).
                E2EWaitOutcome live = new E2EWaitOutcome();
                yield return E2EWait.Until(live, "матч начат админом на карте 2", 60f,
                    () => MapReferee.Instance != null && MapReferee.Instance.IsLive,
                    () => $"карта {DescribeMap(MapReferee.Instance)}");
                if (!live.Succeeded)
                {
                    result.Set(CheckSecondMapLive, false, live.Diagnosis);
                    Abort(result, "матч на карте 2 не начат");
                }

                EliminationMode mode = null;
                float deadline = Now + 30f;
                while ((mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode as EliminationMode : null) == null &&
                       Now < deadline)
                    yield return null;
                if (mode == null) Abort(result, "ActiveGameMode на карте 2 не стал EliminationMode");
                context.ServerStep("match-live");

                E2EWaitOutcome equipment = new E2EWaitOutcome();
                yield return E2EWait.Until(equipment, "фаза Equipment на карте 2", 150f,
                    () => mode.CurrentRoundPhase == RoundPhase.Equipment,
                    () => $"фаза {mode.CurrentRoundPhase}; {DescribeZones()}");
                bool money = true;
                var wallet = new List<string>();
                foreach (PlayerSession session in sessions)
                {
                    int now = Money(session);
                    money &= now == EconomyRules.StartMoney;
                    wallet.Add($"{session.PlayerName}: {now}");
                }

                result.Set(CheckSecondMapLive, equipment.Succeeded && money,
                    $"{equipment.Diagnosis}; {DescribeZones()}; деньги (ждали {EconomyRules.StartMoney}): {string.Join(", ", wallet)}");
                context.ServerStep("equipment");

                yield return E2EBarrier.WaitClients(context, "done", 90f);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            result.Summary = result.AllChecksGreen
                ? "клиент-админ вёл серию командами планшета: новая серия, следующая карта (повтор отклонён), матч на карте 2"
                : "см. проверки";
        }

        // ══════════════════════════════════════════════════════════════════
        //  «Стоп» → лобби
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// <c>scripted-series-stop</c> (S + 2): на карте стенда (серия из одной карты) админ начинает матч, клиенты в
        /// своих зонах; посреди матча админ жмёт «Стоп» дважды одним кадром. Сервер: лобби в разминке, серия
        /// остановлена, команды распущены, повтор отклонён с причиной; сессии и права те же, карты серии видны до новой.
        /// </summary>
        private IEnumerator RunSeriesStopServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClients, CheckTeams, CheckAdminRights, CheckStopLive, CheckLobby, CheckStopRepeat,
                CheckLobbyKept);
            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");
            _adminLog.Clear();
            Application.logMessageReceived += OnLog;
            try
            {
                yield return E2EBarrier.WaitClients(context, "input-active", 90f);
                SetClients(result, context);

                yield return E2EBarrier.WaitClients(context, "team-requested", 90f);
                E2EWaitOutcome teams = new E2EWaitOutcome();
                yield return E2EWait.Until(teams, "клиенты в разных командах", 30f,
                    () => DistinctTeams() >= 2 && AllHaveTeam(),
                    () => DescribeTeams());
                result.Set(CheckTeams, teams.Succeeded, DescribeTeams() + " — " + teams.Diagnosis);
                if (!teams.Succeeded) Abort(result, "команды не выбраны");
                CheckRights(result);

                var sessions = new List<PlayerSession>(HumanSessions());
                var admin = new Dictionary<PlayerSession, bool>();
                foreach (PlayerSession session in sessions) admin[session] = session.IsAdmin;
                var maps = Series.Instance != null ? new List<string>(Series.Instance.Maps) : new List<string>();
                context.ServerStep("teams-ok");

                E2EWaitOutcome live = new E2EWaitOutcome();
                yield return E2EWait.Until(live, "матч начат админом", 60f,
                    () => MapReferee.Instance != null && MapReferee.Instance.IsLive,
                    () => $"карта {DescribeMap(MapReferee.Instance)}");
                if (!live.Succeeded)
                {
                    result.Set(CheckStopLive, false, live.Diagnosis);
                    Abort(result, "матч не начат");
                }

                EliminationMode mode = null;
                float deadline = Now + 30f;
                while ((mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode as EliminationMode : null) == null &&
                       Now < deadline)
                    yield return null;
                if (mode == null) Abort(result, "ActiveGameMode не стал EliminationMode");
                context.ServerStep("match-live");

                E2EWaitOutcome equipment = new E2EWaitOutcome();
                yield return E2EWait.Until(equipment, "фаза Equipment", 150f,
                    () => mode.CurrentRoundPhase == RoundPhase.Equipment,
                    () => $"фаза {mode.CurrentRoundPhase}; {DescribeZones()}");
                result.Set(CheckStopLive, equipment.Succeeded, $"{equipment.Diagnosis}; серия {DescribeSeries()}; {DescribeZones()}");
                if (!equipment.Succeeded) Abort(result, "закупка не началась");

                MapReferee match = MapReferee.Instance;
                string lobbyScene = LobbyScene();
                context.ServerStep("equipment");

                MapReferee lobby = null;
                E2EWaitOutcome loaded = new E2EWaitOutcome();
                yield return E2EWait.Until(loaded, $"лобби {lobbyScene} загружено", 120f,
                    () => (lobby = NewMap(match, lobbyScene)) != null,
                    () => $"карта {DescribeMap(MapReferee.Instance)}; серия {DescribeSeries()}");
                // Отклонённый повтор и снятие команд успевают дойти до лога и реплики.
                yield return E2EWait.Hold(2f);

                Series series = Series.Instance;
                bool stopped = series != null && !series.IsRunning && series.CurrentIndex == -1;
                bool released = true;
                var teamsNow = new List<string>();
                foreach (PlayerSession session in sessions)
                {
                    released &= session != null && session.TeamIndex == 0;
                    teamsNow.Add(session != null ? $"{session.PlayerName}→{session.TeamIndex}" : "сессия уничтожена");
                }

                bool noEconomy = MatchEconomy.Current == null;
                result.Set(CheckLobby, loaded.Succeeded && stopped && released && noEconomy,
                    $"{loaded.Diagnosis}; {DescribeMap(lobby)}; серия {DescribeSeries()}; команды {string.Join(", ", teamsNow)}; " +
                    $"экономики нет={noEconomy}");

                bool refused = HasAdminLog($"{MapCommand.Stop} отклонена: идёт смена карты");
                result.Set(CheckStopRepeat, refused, $"повтор отклонён={refused}; {AdminLogTail()}");

                bool kept = series != null && series.Maps.Count == maps.Count;
                for (int i = 0; kept && i < maps.Count; i++) kept &= series.Maps[i] == maps[i];
                int now = 0;
                foreach (PlayerSession unused in HumanSessions()) now++;
                kept &= now == sessions.Count;
                var parts = new List<string>();
                foreach (PlayerSession session in sessions)
                {
                    kept &= session != null && session.connectionToClient != null && session.IsAdmin == admin[session];
                    parts.Add(session != null
                        ? $"{session.PlayerName}: соединение={session.connectionToClient != null}, админ {session.IsAdmin} (был {admin[session]})"
                        : "сессия уничтожена");
                }

                result.Set(CheckLobbyKept, kept,
                    $"сессий {now} (было {sessions.Count}); карты серии [{string.Join(", ", maps)}] → " +
                    $"[{(series != null ? string.Join(", ", new List<string>(series.Maps)) : "нет")}]; " + string.Join("; ", parts));

                yield return E2EBarrier.WaitClients(context, "lobby", 120f);
                context.ServerStep("lobby-verified");
                yield return E2EBarrier.WaitClients(context, "done", 90f);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            result.Summary = result.AllChecksGreen
                ? "клиент-админ остановил серию посреди матча — лобби, команды распущены, повтор «Стоп» отклонён"
                : "см. проверки";
        }

        private static string LobbyScene()
        {
            SessionManager session = SessionManager.Instance;
            return session != null && session.MapRegistry != null ? session.MapRegistry.LobbyScene : "Lobby";
        }

        /// <summary>Оркестратор новой карты <paramref name="scene"/>: другой, чем <paramref name="previous"/>, в разминке.</summary>
        private static MapReferee NewMap(MapReferee previous, string scene)
        {
            MapReferee current = MapReferee.Instance;
            return current != null && current != previous && current.SceneName == scene && current.CurrentState == MapState.Warmup
                ? current
                : null;
        }

        private static bool SeriesAt(int index)
        {
            Series series = Series.Instance;
            return series != null && series.IsRunning && series.CurrentIndex == index && series.Maps.Count == 2 &&
                   series.Maps[0] == FirstMap && series.Maps[1] == SecondMap;
        }

        private static string DescribeSeries()
        {
            Series series = Series.Instance;
            if (series == null) return "нет";
            return $"{(series.IsRunning ? "идёт" : "не идёт")}, карта {series.CurrentIndex + 1}/{series.Maps.Count} " +
                   $"[{string.Join(", ", new List<string>(series.Maps))}]";
        }

        private static string DescribeMap(MapReferee map) =>
            map != null ? $"{map.SceneName} ({map.CurrentState})" : "нет";

        /// <summary>Те же сессии с теми же командами и правами; аватар каждого пересоздан на новой карте.</summary>
        private static bool Kept(List<PlayerSession> sessions, Dictionary<PlayerSession, int> team,
            Dictionary<PlayerSession, bool> admin, out string detail)
        {
            bool ok = true;
            var parts = new List<string>();
            foreach (PlayerSession session in sessions)
            {
                bool alive = session != null && session.connectionToClient != null;
                bool same = alive && session.TeamIndex == team[session] && session.IsAdmin == admin[session];
                ok &= same;
                parts.Add(session != null
                    ? $"{session.PlayerName}: соединение={alive}, команда {session.TeamIndex} (была {team[session]}), " +
                      $"админ {session.IsAdmin} (был {admin[session]}), аватар netId={NetId(AvatarOf(session))}"
                    : "сессия уничтожена");
            }

            int now = 0;
            foreach (PlayerSession unused in HumanSessions()) now++;
            ok &= now == sessions.Count;
            detail = $"сессий {now} (было {sessions.Count}); " + string.Join("; ", parts);
            return ok;
        }

        private static string DescribeKept(List<PlayerSession> sessions, Dictionary<PlayerSession, int> team,
            Dictionary<PlayerSession, bool> admin, Dictionary<PlayerSession, uint> avatars)
        {
            Kept(sessions, team, admin, out string detail);
            var replaced = new List<string>();
            foreach (PlayerSession session in sessions)
                if (session != null) replaced.Add($"{session.PlayerName}: аватар {avatars[session]}→{NetId(AvatarOf(session))}");
            return detail + "; " + string.Join(", ", replaced);
        }

        private static uint NetId(UxrAvatar avatar)
        {
            NetworkIdentity identity = avatar != null ? avatar.GetComponentInParent<NetworkIdentity>() : null;
            return identity != null ? identity.netId : 0;
        }
    }
}
#endif
