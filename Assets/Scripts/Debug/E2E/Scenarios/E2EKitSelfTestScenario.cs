// Ярус C (сервер + N клиентов) — сценарий e2e-kit-selftest: самопроверка барьера шагов каркаса через перезагрузку карты.
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>e2e-kit-selftest</c>: проверка каркаса, а не игры. Барьер шагов (<see cref="E2EBarrier"/>) обязан
    /// провести сервер и всех клиентов через два запуска одной карты: шаги сервера доходят до клиентов, шаги клиентов —
    /// до сервера, пересылка шагов после смены сцены дедуплицируется.
    ///
    /// <para>
    /// <b>Ход.</b> Выделенный сервер ждёт всех клиентов, начинает серию из карты <c>-e2eMap</c> и ждёт server Ready →
    /// шаг <c>map-1-ready</c> → барьер <c>client-on-map-1</c> (клиенты на карте с LocalPlayable). Затем перезагрузка той
    /// же карты через <see cref="MapLoader"/> (новый <see cref="MapRunKey"/>) → шаг <c>map-2-ready</c> → барьер
    /// <c>client-on-map-2</c> → шаг <c>selftest-done</c>, после которого клиенты завершаются.
    /// </para>
    /// </summary>
    public class E2EKitSelfTestScenario : IE2EScenario
    {
        public string Name => "e2e-kit-selftest";

        private const string StepMap1 = "map-1-ready";
        private const string StepMap2 = "map-2-ready";
        private const string StepClientMap1 = "client-on-map-1";
        private const string StepClientMap2 = "client-on-map-2";
        private const string StepDone = "selftest-done";

        private const string CheckDedicated = "сервер поднят как выделенный (ServerOnly)";
        private const string CheckSessions = "все клиенты подключились";
        private const string CheckMap1 = "карта собрана и открыла server Ready (запуск 1)";
        private const string CheckBarrier1 = "барьер client-on-map-1: все клиенты на той же карте";
        private const string CheckReload = "перезагрузка той же карты дала новый запуск и server Ready (запуск 2)";
        private const string CheckBarrier2 = "барьер client-on-map-2: все клиенты прошли после смены карты";
        private const string CheckDedup = "шаги клиентов приняты без дублей (пересылка после смены сцены дедуплицирована)";

        private const string CheckConnected = "клиент подключился к серверу";
        private const string CheckServerMap1 = "шаг сервера map-1-ready получен с ожидаемым data";
        private const string CheckLocalMap1 = "своя карта готова (LocalPlayable), шаг client-on-map-1 отправлен";
        private const string CheckServerMap2 = "шаг сервера map-2-ready получен с ожидаемым data";
        private const string CheckLocalMap2 = "своя карта снова готова с новым ключом запуска, шаг client-on-map-2 отправлен";
        private const string CheckDone = "сервер подтвердил конец самопроверки";

        public IEnumerator Run(E2EContext context, E2EResult result) =>
            context.IsServerSide ? RunServer(context, result) : RunClient(context, result);

        // ── Сервер ───────────────────────────────────────────────────────────

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckDedicated, CheckSessions, CheckMap1, CheckBarrier1, CheckReload, CheckBarrier2, CheckDedup);
            string map = context.Map;

            var active = new E2EWaitOutcome();
            yield return E2EWait.Until(active, "NetworkServer.active", 60f, () => NetworkServer.active);
            bool dedicated = NetworkServer.active && !NetworkClient.active;
            result.Set(CheckDedicated, dedicated, $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}");
            if (!dedicated) { result.Summary = "не выделенный сервер, прогон недействителен"; yield break; }

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");

            var sessions = new E2EWaitOutcome();
            yield return E2EWait.Until(sessions, "сессии всех клиентов", 120f, () => SessionCount() >= context.ClientCount,
                () => $"сессий {SessionCount()} из {context.ClientCount}");
            result.Set(CheckSessions, sessions.Succeeded, sessions.Diagnosis);
            if (!sessions.Succeeded) { result.Summary = "клиенты не подключились"; yield break; }

            SessionManager session = SessionManager.Instance;
            session.SetSession(map, "elimination");
            AssignTeams(session);
            session.StartSession();

            var ready1 = new E2EWaitOutcome();
            yield return E2EWait.Until(ready1, "server Ready карты", 120f, () => ServerReadyKey(map).IsValid,
                () => $"сцена '{SceneManager.GetActiveScene().name}', запуск {DescribeRun()}");
            MapRunKey firstKey = ServerReadyKey(map);
            result.Set(CheckMap1, ready1.Succeeded, ready1.Succeeded ? $"запуск {firstKey}" : ready1.Diagnosis);
            if (!ready1.Succeeded) { result.Summary = "карта не собралась"; yield break; }

            context.ServerStep(StepMap1, $"map={map};run=1;key={firstKey}");
            yield return E2EBarrier.WaitClients(context, StepClientMap1, 120f,
                onReached: data => SetClientsCheck(result, CheckBarrier1, data, map));

            bool accepted = MapLoader.Instance != null && MapLoader.Instance.LoadMap(map);
            var ready2 = new E2EWaitOutcome();
            yield return E2EWait.Until(ready2, "server Ready перезагруженной карты", 120f,
                () => accepted && ServerReadyKey(map).IsValid && ServerReadyKey(map) != firstKey,
                () => $"загрузка принята={accepted}, запуск {DescribeRun()}",
                () => accepted ? null : "MapLoader не принял LoadMap");
            MapRunKey secondKey = ServerReadyKey(map);
            result.Set(CheckReload, ready2.Succeeded, ready2.Succeeded ? $"запуск {firstKey} → {secondKey}" : ready2.Diagnosis);
            if (!ready2.Succeeded) { result.Summary = "перезагрузка карты не дала server Ready"; yield break; }

            context.ServerStep(StepMap2, $"map={map};run=2;key={secondKey}");
            yield return E2EBarrier.WaitClients(context, StepClientMap2, 180f,
                onReached: data => SetClientsCheck(result, CheckBarrier2, data, map));

            SetDedupCheck(context, result);

            // Клиенты завершаются по этому шагу; выдержка — чтобы он успел уйти до выхода сервера.
            context.ServerStep(StepDone, $"map={map}");
            yield return E2EWait.Hold(5f);
        }

        /// <summary>Каждый ожидаемый клиент дошёл, его data называет ту же карту и его собственный индекс.</summary>
        private static void SetClientsCheck(E2EResult result, string check, IReadOnlyDictionary<string, string> data, string map)
        {
            var problems = new List<string>();
            var seen = new List<string>();
            foreach (KeyValuePair<string, string> pair in data)
            {
                seen.Add($"{pair.Key} [{pair.Value}]");
                string clientMap = Field(pair.Value, "map");
                string idx = Field(pair.Value, "idx");
                if (clientMap != map) problems.Add($"{pair.Key}: карта '{clientMap}' вместо '{map}'");
                if (idx != E2EStepRules.ClientIndexOf(pair.Key).ToString()) problems.Add($"{pair.Key}: idx '{idx}'");
            }
            result.Set(check, problems.Count == 0,
                (problems.Count == 0 ? string.Empty : string.Join("; ", problems) + " | ") + string.Join(", ", seen));
        }

        /// <summary>
        /// Журнал сервера: каждый шаг клиента принят ровно один раз (<c>received</c>), без <c>updated</c> — хотя после
        /// смены сцены клиент пересылает серверу все свои шаги заново.
        /// </summary>
        private static void SetDedupCheck(E2EContext context, E2EResult result)
        {
            List<string> roles = E2EStepRules.ExpectedClientRoles(context.ClientCount, context.LateRole, includeLate: false);
            var problems = new List<string>();
            var counts = new List<string>();
            foreach (string role in roles)
            {
                int received1 = 0, received2 = 0, updated = 0;
                foreach (E2EStepRecord step in result.Steps)
                {
                    if (step.Role != role) continue;
                    if (step.Outcome == E2EStepOutcome.Updated) updated++;
                    if (step.Outcome != E2EStepOutcome.Received) continue;
                    if (step.Name == StepClientMap1) received1++;
                    else if (step.Name == StepClientMap2) received2++;
                }
                counts.Add($"{role}: {StepClientMap1}×{received1}, {StepClientMap2}×{received2}, updated×{updated}");
                if (received1 != 1 || received2 != 1 || updated != 0) problems.Add(role);
            }
            string dropped = result.StepsDropped > 0 ? $"; журнал отбросил {result.StepsDropped} записей" : string.Empty;
            result.Set(CheckDedup, problems.Count == 0 && result.StepsDropped == 0,
                (problems.Count == 0 ? string.Empty : "дубли/пропуски у " + string.Join(", ", problems) + " | ") +
                string.Join("; ", counts) + dropped);
        }

        private static MapRunKey ServerReadyKey(string map)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != map) return default;
            MapBootstrap bootstrap = MapBootstrap.ForScene(scene);
            return bootstrap != null && bootstrap.IsServerReady ? bootstrap.LocalRunKey : default;
        }

        private static string DescribeRun()
        {
            MapRunAuthority authority = MapRunAuthority.Instance;
            return authority != null ? $"{authority.Current.Key} {authority.Current.Status}" : "authority нет";
        }

        private static int SessionCount() => PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;

        /// <summary>Команды по кругу: без индекса команды AvatarManager не создаёт тело.</summary>
        private static void AssignTeams(SessionManager session)
        {
            GameModeData mode = session.SelectedGameModeData;
            if (mode == null || mode.teams == null || mode.teams.Length == 0) return;
            IReadOnlyList<PlayerSession> sessions = PlayersManager.Instance.Sessions;
            for (int i = 0; i < sessions.Count; i++)
                if (sessions[i].TeamIndex == 0 && mode.teams[i % mode.teams.Length] != null)
                    sessions[i].TeamIndex = mode.teams[i % mode.teams.Length].teamIndex;
        }

        // ── Клиент ───────────────────────────────────────────────────────────

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckConnected, CheckServerMap1, CheckLocalMap1, CheckServerMap2, CheckLocalMap2, CheckDone);
            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");

            var connected = new E2EWaitOutcome();
            yield return E2EWait.Until(connected, "подключение", 240f, () => NetworkClient.isConnected);
            result.Set(CheckConnected, connected.Succeeded, connected.Diagnosis);
            if (!connected.Succeeded) { result.Summary = "клиент не подключился"; yield break; }

            // Красный вердикт клиента сервер увидит шагом failed в своём барьере (E2ERunner.Finish), а не только таймаутом.
            // Шаг 1: карта первого запуска.
            string server1 = null;
            yield return E2EBarrier.WaitServer(context, StepMap1, 180f, data => server1 = data);
            bool server1Ok = Field(server1, "map") == context.Map && Field(server1, "run") == "1";
            result.Set(CheckServerMap1, server1Ok, $"data '{server1}', ожидали map={context.Map};run=1");
            if (!server1Ok) { result.Summary = "шаг map-1-ready с неожиданным data"; yield break; }

            var local1 = new E2EWaitOutcome();
            yield return E2EWait.Until(local1, "LocalPlayable на карте (запуск 1)", 180f,
                () => PlayableKey(context.Map).IsValid, DescribeClient);
            MapRunKey firstKey = PlayableKey(context.Map);
            result.Set(CheckLocalMap1, local1.Succeeded, local1.Succeeded ? $"запуск {firstKey}" : local1.Diagnosis);
            if (!local1.Succeeded) { result.Summary = "карта первого запуска не стала LocalPlayable"; yield break; }
            context.Step(StepClientMap1, $"map={SceneManager.GetActiveScene().name};idx={context.ClientIndex};key={firstKey}");

            // Шаг 2: та же карта после перезагрузки.
            string server2 = null;
            yield return E2EBarrier.WaitServer(context, StepMap2, 180f, data => server2 = data);
            bool server2Ok = Field(server2, "map") == context.Map && Field(server2, "run") == "2";
            result.Set(CheckServerMap2, server2Ok, $"data '{server2}', ожидали map={context.Map};run=2");
            if (!server2Ok) { result.Summary = "шаг map-2-ready с неожиданным data"; yield break; }

            var local2 = new E2EWaitOutcome();
            yield return E2EWait.Until(local2, "LocalPlayable перезагруженной карты (запуск 2)", 180f,
                () => PlayableKey(context.Map).IsValid && PlayableKey(context.Map).LoadSequence > firstKey.LoadSequence,
                DescribeClient);
            MapRunKey secondKey = PlayableKey(context.Map);
            result.Set(CheckLocalMap2, local2.Succeeded, local2.Succeeded ? $"запуск {firstKey} → {secondKey}" : local2.Diagnosis);
            if (!local2.Succeeded) { result.Summary = "перезагруженная карта не стала LocalPlayable"; yield break; }
            context.Step(StepClientMap2, $"map={SceneManager.GetActiveScene().name};idx={context.ClientIndex};key={secondKey}");

            // Не выходим, пока сервер не принял шаг: иначе он мог бы не уйти до закрытия процесса.
            yield return E2EBarrier.WaitServer(context, StepDone, 120f);
            result.Set(CheckDone, true, "шаг selftest-done получен");
        }

        /// <summary>Ключ запуска карты, для которого на этом клиенте открыт LocalPlayable и применён снимок; иначе пустой.</summary>
        private static MapRunKey PlayableKey(string map)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != map || !MapRunAdmission.ComputeLocalPlayable(scene)) return default;
            MapBootstrap bootstrap = MapBootstrap.ForScene(scene);
            NetworkStateRelay relay = NetworkStateRelay.Instance;
            if (bootstrap == null || relay == null) return default;
            MapRunKey key = bootstrap.LocalRunKey;
            return relay.HasInitialState(key) ? key : default;
        }

        private static string DescribeClient()
        {
            Scene scene = SceneManager.GetActiveScene();
            MapBootstrap bootstrap = MapBootstrap.ForScene(scene);
            return $"сцена '{scene.name}', run {(bootstrap != null ? bootstrap.LocalRunKey.ToString() : "нет")}, " +
                   $"descriptor {DescribeRun()}, LocalPlayable {MapRunAdmission.ComputeLocalPlayable(scene)}, " +
                   $"рукопожатие {(E2EBarrierCore.Shared.HasHandshake ? "есть" : "нет")}";
        }

        /// <summary>Значение поля <c>key=value</c> из data шага вида <c>a=1;b=2</c>; null — поля нет.</summary>
        private static string Field(string data, string key)
        {
            if (string.IsNullOrEmpty(data)) return null;
            foreach (string part in data.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && string.Equals(part.Substring(0, eq), key, StringComparison.Ordinal))
                    return part.Substring(eq + 1);
            }
            return null;
        }
    }
}
#endif
