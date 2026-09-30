// Ярус C (два процесса) — сценарий calibration-scale-replication, находка VR-01 (задача T-14).
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    ///     Сценарий <c>calibration-scale-replication</c> — находка <b>VR-01</b>.
    ///
    ///     <para>
    ///     Что доказывает. Пропорции игрока, снятые калибровкой роста, должны доезжать
    ///     до всех машин и применяться там к его аватару. До T-14 масштаб ставился
    ///     только локально: <c>PhysicalSpaceSyncManager.ApplyScale</c> трогал
    ///     <c>Dummy Forward</c> у <c>UxrAvatar.LocalAvatar</c> и ни у кого больше.
    ///     Своего <c>NetworkTransform</c> у <c>Dummy Forward</c> нет и быть не может —
    ///     объект создаёт сам SDK в рантайме (<c>UxrBodyIK.Initialize</c>), — а на корневых
    ///     <c>NetworkTransform</c> аватарных префабов <c>syncScale</c> выключен.
    ///     Итог: игрок ростом 1.5 м на чужих экранах оставался стандартного роста,
    ///     а коллайдеры ехали за костями — стрелять надо было в одно место, а видно другое.
    ///     </para>
    ///
    ///     <para>
    ///     Чего сценарий <b>не</b> проверяет. Саму калибровку: она читает положение шлема
    ///     и контроллеров, то есть требует железа. Клиенты не калибруются, а сразу шлют
    ///     серверу готовый результат — ровно то значение, которым закончилась бы вторая
    ///     фаза калибровки. Проверяется всё, что после неё.
    ///     </para>
    ///
    ///     <para>
    ///     Устройство проверки. Два клиента объявляют <b>разные</b> пропорции (0.80 и 1.30):
    ///     совпадение не должно быть случайным следствием того, что оба остались единичными.
    ///     Дальше вердикт выносится с двух сторон. Сервер смотрит свои экземпляры аватаров —
    ///     это он считает попадания, и если у него аватар не отмасштабирован, находка жива
    ///     независимо от картинки. Каждый клиент смотрит <b>чужой</b> аватар — тот самый
    ///     «чужой экран» из формулировки находки.
    ///     </para>
    ///
    ///     <para>
    ///     Свой собственный аватар клиент из проверки исключает намеренно. Локально масштаб
    ///     ставит ещё и <c>PhysicalSpaceSyncManager</c> из накопленного значения калибровки,
    ///     а в прогоне никто не калибровался — там осталась единица. В живой игре оба
    ///     источника согласованы (менеджер и сессия хранят один и тот же результат),
    ///     здесь же расхождение — артефакт того, что калибровку подменили отправкой значения.
    ///     </para>
    ///
    ///     <para>
    ///     Обратный канал наблюдения — тот же, что в <c>avatar-swap-death-replication</c>:
    ///     фазы прогона едут клиентам через <c>PlayerSession.Score</c>, ответ клиента
    ///     возвращается серверу через <c>CmdSetDogTagGrabbed</c>.
    ///     </para>
    /// </summary>
    public class CalibrationScaleReplicationScenario : IE2EScenario
    {
        public string Name => "calibration-scale-replication";

        // ── Фазы прогона ──────────────────────────────────────────────────

        /// <summary>Сервер ещё готовится, клиенту делать нечего.</summary>
        private const int PhaseWait = 0;

        /// <summary>Рукопожатие, шаг 1: поднять флаг наблюдения.</summary>
        private const int PhaseRaise = 1;

        /// <summary>Рукопожатие, шаг 2: опустить флаг наблюдения.</summary>
        private const int PhaseLower = 2;

        /// <summary>Клиент объявляет свои пропорции — подмена результата калибровки.</summary>
        private const int PhaseApply = 3;

        /// <summary>Клиент сверяет чужие аватары со значениями их сессий.</summary>
        private const int PhaseVerify = 4;

        /// <summary>Прогон завершён, клиент может выносить свой вердикт.</summary>
        private const int PhaseDone = 9;

        // ── Пропорции, которые объявляют клиенты ──────────────────────────

        private const float ScaleClient1 = 0.80f;
        private const float ScaleClient2 = 1.30f;

        /// <summary>Допуск сравнения. Значение едет float-ом без пересчётов, так что запас велик.</summary>
        private const float Tolerance = 1e-3f;

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated    = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients      = "клиенты подключились и сервер создал сессии";
        private const string CheckAvatars      = "карта загружена, у каждой сессии есть аватар";
        private const string CheckBackChannel  = "обратный канал наблюдения работает у всех клиентов";
        private const string CheckScaleArrived = "сервер принял от клиентов разные пропорции";
        private const string CheckServerScaled = "на сервере аватар каждого игрока в его пропорциях";
        private const string CheckClientsAgree = "клиенты подтвердили, что видят чужой аватар в его пропорциях";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientMirror    = "обратный канал наблюдения проверен эхом SyncVar";
        private const string CheckClientRemote    = "чужой аватар отмасштабирован по пропорциям своего игрока";


        public IEnumerator Run(E2EContext context, E2EResult result)
        {
            if (context.IsServerRole)
                return RunServer(context, result);

            return RunClient(context, result);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль сервера
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckDedicated, CheckClients, CheckAvatars, CheckBackChannel,
                           CheckScaleArrived, CheckServerScaled, CheckClientsAgree);

            try
            {
                // ── 1. Выделенный сервер ──────────────────────────────────
                float deadline = Now + 60f;
                while (!NetworkServer.active && Now < deadline)
                    yield return null;

                if (!NetworkServer.active)
                {
                    result.Set(CheckDedicated, false,
                        "NetworkServer.active так и не стал true за 60 с — сервер не поднялся. " +
                        "Процесс должен идти с -batchmode -nographics: роль выбирается по Mirror.Utils.IsHeadless().");
                    result.Summary = "сервер не поднялся, прогон недействителен";
                    yield break;
                }

                bool dedicated = NetworkServer.active && !NetworkClient.active;
                result.Set(CheckDedicated, dedicated,
                    dedicated
                        ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                        : "процесс поднялся хостом (NetworkClient.active=true). На хосте сервер делит " +
                          "экземпляр аватара с локальным клиентом, и «сервер видит масштаб» получилось бы " +
                          "само собой — прогон ничего не доказал бы.");

                if (!dedicated)
                {
                    result.Summary = "конфигурация не выделенный сервер, прогон недействителен";
                    yield break;
                }

                DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");

                // ── 2. Клиенты ────────────────────────────────────────────
                deadline = Now + 90f;
                while (SessionCount() < context.ExpectedClients && Now < deadline)
                    yield return null;

                int sessions = SessionCount();
                bool clientsOk = sessions >= context.ExpectedClients;
                result.Set(CheckClients, clientsOk,
                    clientsOk
                        ? $"сессий на сервере: {sessions} (ждали {context.ExpectedClients})"
                        : $"за 90 с подключилось сессий: {sessions} из {context.ExpectedClients}. " +
                          "Клиенты не нашли сервер (Mirror NetworkDiscovery — UDP-броадкаст) " +
                          "или их процессы упали — смотри client-*.log рядом с этим файлом.");

                if (!clientsOk)
                {
                    result.Summary = "клиенты не подключились, вердикт вынести нельзя";
                    yield break;
                }

                // ── 3. Карта и аватары ────────────────────────────────────
                SessionManager sessionManager = SessionManager.Instance;
                if (sessionManager == null || MapLoader.Instance == null)
                {
                    result.Set(CheckAvatars, false,
                        $"SessionManager={(sessionManager == null ? "null" : "есть")}, " +
                        $"MapLoader={(MapLoader.Instance == null ? "null" : "есть")} — прогон вести нечем");
                    result.Summary = "менеджеры сессии отсутствуют, прогон недействителен";
                    yield break;
                }

                sessionManager.SetSession(context.Map, "elimination");
                yield return null;

                string teamsReport = AssignTeams(sessionManager);
                GameLog.Debug.Info($"[E2E] Команды распределены: {teamsReport}");

                MapLoader.Instance.LoadMap(context.Map);

                deadline = Now + 120f;
                while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                    yield return null;

                if (SceneManager.GetActiveScene().name != context.Map)
                {
                    result.Set(CheckAvatars, false,
                        $"за 120 с сервер не переехал на '{context.Map}', " +
                        $"активная сцена='{SceneManager.GetActiveScene().name}'");
                    result.Summary = "карта не загрузилась, вердикт вынести нельзя";
                    yield break;
                }

                deadline = Now + 90f;
                while (!AllSessionsHaveAvatar() && Now < deadline)
                    yield return null;

                bool avatarsOk = AllSessionsHaveAvatar();
                result.Set(CheckAvatars, avatarsOk,
                    avatarsOk
                        ? $"активная сцена='{SceneManager.GetActiveScene().name}', аватары: {DescribeAvatars()}"
                        : $"за 90 с не у всех сессий появился аватар: {DescribeAvatars()}. " +
                          "GameNetworkManager.OnServerReady спавнит аватар только для готового клиента.");

                if (!avatarsOk)
                {
                    result.Summary = "аватары не заспавнились, вердикт вынести нельзя";
                    yield break;
                }

                // ── 4. Рукопожатие обратного канала ───────────────────────
                // Без него «клиенты не подтвердили» в конце нельзя отличить
                // от «подтверждать было нечем».
                SetPhase(PhaseRaise);

                deadline = Now + 60f;
                while (!AllClientsRaised() && Now < deadline)
                    yield return null;

                bool raisedOk = AllClientsRaised();

                SetPhase(PhaseLower);

                deadline = Now + 60f;
                while (AnyClientFlag() && Now < deadline)
                    yield return null;

                bool loweredOk = !AnyClientFlag();
                bool backChannelOk = raisedOk && loweredOk;

                result.Set(CheckBackChannel, backChannelOk,
                    backChannelOk
                        ? $"все {SessionCount()} клиентов подняли и опустили флаг наблюдения по команде сервера"
                        : $"рукопожатие не состоялось: подняли флаг все={raisedOk}, опустили все={loweredOk}, " +
                          $"флаги сейчас: {DescribeFlags()}.");

                if (!backChannelOk)
                {
                    result.Summary = "обратный канал наблюдения не поднялся, вердикт вынести нельзя";
                    yield break;
                }

                // ── 5. Клиенты объявляют пропорции ────────────────────────
                SetPhase(PhaseApply);

                deadline = Now + 60f;
                while (!AllScalesDeclared() && Now < deadline)
                    yield return null;

                bool scalesOk = AllScalesDeclared() && ScalesAreDistinct();
                result.Set(CheckScaleArrived, scalesOk,
                    scalesOk
                        ? $"сервер принял пропорции: {DescribeScales()}"
                        : $"за 60 с сервер не получил от клиентов различающихся пропорций: {DescribeScales()}. " +
                          "Либо CmdSetCalibrationScale не доехал, либо сервер обрезал значения в одно " +
                          "(границы — PlayerSession.MinCalibrationScale/MaxCalibrationScale).");

                if (!scalesOk)
                {
                    result.Summary = "пропорции не доехали до сервера, вердикт вынести нельзя";
                    yield break;
                }

                // ── 6. Сервер: масштаб применён к его экземплярам ─────────
                // Главная проверка для стрельбы: попадания считает сервер, и если
                // у него аватар в исходных пропорциях, коллайдеры расходятся
                // с тем, что видит стрелок, независимо от картинки на клиентах.
                deadline = Now + 30f;
                while (!AllServerAvatarsScaled() && Now < deadline)
                    yield return null;

                bool serverScaledOk = AllServerAvatarsScaled();
                result.Set(CheckServerScaled, serverScaledOk,
                    serverScaledOk
                        ? $"на сервере масштабы аватаров совпали с пропорциями сессий: {DescribeServerScales()}"
                        : $"на сервере аватар остался в исходных пропорциях: {DescribeServerScales()}. " +
                          "Это VR-01 со стороны сервера: коллайдеры едут за костями, попадание " +
                          "считается по одной геометрии, а стрелок видит другую. " +
                          "Хук SyncVar на выделенном сервере не вызывается — масштаб обязан " +
                          "применяться прямо в CmdSetCalibrationScale.");

                // ── 7. Вердикт клиентов ───────────────────────────────────
                SetPhase(PhaseVerify);

                deadline = Now + 60f;
                while (!AllClientsRaised() && Now < deadline)
                    yield return null;

                bool clientsAgree = AllClientsRaised();
                result.Set(CheckClientsAgree, clientsAgree,
                    clientsAgree
                        ? "каждый клиент подтвердил: чужой аватар отмасштабирован по пропорциям своего игрока"
                        : $"за 60 с не все клиенты подтвердили совпадение: {DescribeFlags()}. " +
                          "Это VR-01: на чужих экранах игрок остаётся в исходных пропорциях. " +
                          "Подробности — в client-*.json рядом с этим файлом.");

                result.Summary = result.AllChecksGreen
                    ? "пропорции игрока доезжают до сервера и до чужих клиентов — VR-01 не воспроизводится"
                    : !serverScaledOk
                        ? "VR-01 подтверждена на сервере: масштаб не применён к его экземплярам аватаров"
                        : !clientsAgree
                            ? "VR-01 подтверждена на клиентах: чужой аватар остался в исходных пропорциях"
                            : "есть красные проверки, см. detail";
            }
            finally
            {
                SetPhase(PhaseDone);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap,
                           CheckClientMirror, CheckClientRemote);

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");

            float myScale = TargetScaleFor(context.Role);
            GameLog.Debug.Info($"[E2E] Роль {context.Role}: объявляю пропорции {myScale:F2}");

            // ── Подключение ───────────────────────────────────────────────
            float deadline = Now + 30f;
            while (!NetworkClient.isConnected && Now < deadline)
                yield return null;

            if (!NetworkClient.isConnected && !string.IsNullOrEmpty(context.ServerAddress))
            {
                GameLog.Debug.Info($"[E2E] Discovery молчит 30 с, подключаюсь напрямую к {context.ServerAddress}");

                Mirror.Discovery.NetworkDiscovery discovery = Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>();
                if (discovery != null)
                    discovery.StopDiscovery();

                if (NetworkManager.singleton != null && !NetworkClient.active)
                {
                    NetworkManager.singleton.networkAddress = context.ServerAddress;
                    NetworkManager.singleton.StartClient();
                }

                deadline = Now + 30f;
                while (!NetworkClient.isConnected && Now < deadline)
                    yield return null;
            }

            bool connected = NetworkClient.isConnected;
            result.Set(CheckClientConnected, connected,
                connected
                    ? $"подключён к {NetworkManager.singleton?.networkAddress}"
                    : "за 60 с подключиться не удалось: ни Discovery, ни прямое подключение " +
                      $"к '{context.ServerAddress}' не сработали");

            if (!connected)
            {
                result.Summary = "клиент не подключился";
                yield break;
            }

            deadline = Now + 60f;
            while (PlayerSession.LocalSession == null && Now < deadline)
                yield return null;

            PlayerSession local = PlayerSession.LocalSession;
            bool hasSession = local != null;
            result.Set(CheckClientSession, hasSession,
                hasSession
                    ? $"PlayerSession.LocalSession netId={local.netId}"
                    : "за 60 с сервер не создал PlayerSession для этого клиента");

            if (!hasSession)
            {
                result.Summary = "клиент не получил сессию";
                yield break;
            }

            deadline = Now + 150f;
            while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                yield return null;

            bool onMap = SceneManager.GetActiveScene().name == context.Map;
            result.Set(CheckClientMap, onMap,
                onMap
                    ? $"активная сцена='{SceneManager.GetActiveScene().name}'"
                    : $"за 150 с клиент не переехал на '{context.Map}', " +
                      $"остался в '{SceneManager.GetActiveScene().name}'");

            if (!onMap)
            {
                result.Summary = "клиент не переехал на карту";
                yield break;
            }

            // ── Наблюдение по фазам сервера ───────────────────────────────
            bool raised = false;
            bool lowered = false;
            bool echoUp = false;
            bool echoDown = false;
            bool declared = false;
            bool reportedMatch = false;
            bool sawRemoteScaled = false;
            string lastMismatch = "(проверка ещё не выполнялась)";
            string lastSeen = "(аватаров ещё не видел)";
            int lastPhase = -1;

            float hardDeadline = Now + Mathf.Max(30f, context.Timeout - 20f);

            while (Now < hardDeadline)
            {
                if (!NetworkClient.isConnected || PlayerSession.LocalSession == null)
                    break;

                local = PlayerSession.LocalSession;
                int phase = local.Score;

                if (phase != lastPhase)
                {
                    GameLog.Debug.Info($"[E2E] Клиент видит фазу прогона: {phase}");
                    lastPhase = phase;
                }

                // Рукопожатие обратного канала.
                if (phase >= PhaseRaise && !raised)
                {
                    local.CmdSetDogTagGrabbed(true);
                    raised = true;
                }

                if (raised && !lowered && local.HasGrabbedDogTag)
                    echoUp = true;

                if (phase >= PhaseLower && !lowered)
                {
                    local.CmdSetDogTagGrabbed(false);
                    lowered = true;
                }

                if (lowered && !local.HasGrabbedDogTag)
                    echoDown = true;

                // Объявляем свои пропорции — подмена результата калибровки.
                if (phase >= PhaseApply && !declared && echoDown)
                {
                    local.CmdSetCalibrationScale(myScale);
                    declared = true;
                    GameLog.Debug.Info($"[E2E] Клиент отправил пропорции {myScale:F2}");
                }

                // Сверяем чужие аватары с пропорциями их сессий.
                if (phase >= PhaseVerify)
                {
                    string mismatch;
                    string seen;
                    bool ok = RemoteAvatarsMatchSessions(out mismatch, out seen);

                    lastMismatch = mismatch;
                    lastSeen = seen;

                    if (ok && !sawRemoteScaled)
                    {
                        sawRemoteScaled = true;
                        GameLog.Debug.Info($"[E2E] Клиент видит чужой аватар в его пропорциях: {seen}");
                    }

                    if (sawRemoteScaled != reportedMatch)
                    {
                        local.CmdSetDogTagGrabbed(sawRemoteScaled);
                        reportedMatch = sawRemoteScaled;
                    }
                }

                if (phase >= PhaseDone)
                    break;

                yield return null;
            }

            bool mirrorOk = echoUp && echoDown;
            result.Set(CheckClientMirror, mirrorOk,
                mirrorOk
                    ? "CmdSetDogTagGrabbed(true/false) вернулся эхом SyncVar — сервер видит наблюдение клиента"
                    : $"эхо не пришло: поднял={raised}, эхо подъёма={echoUp}, опустил={lowered}, " +
                      $"эхо спуска={echoDown}, последняя увиденная фаза={lastPhase}.");

            result.Set(CheckClientRemote, sawRemoteScaled,
                sawRemoteScaled
                    ? $"чужой аватар отмасштабирован по пропорциям своего игрока: {lastSeen}"
                    : $"чужой аватар не в пропорциях своего игрока: {lastMismatch}. " +
                      $"Видел: {lastSeen}. Свои пропорции отправлял: {declared} ({myScale:F2}), " +
                      $"последняя фаза={lastPhase}. Это VR-01: калибровка роста меняет геометрию " +
                      "только локально, на чужих экранах игрок остаётся стандартного роста.");

            result.Summary = result.AllChecksGreen
                ? "клиент видит чужого игрока в его пропорциях"
                : "клиент не увидел чужой аватар в объявленных пропорциях";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное — общее
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        /// <summary>
        ///     Разные значения у разных клиентов: если оба останутся единичными,
        ///     «масштабы совпали» будет верно и при полностью сломанной репликации.
        /// </summary>
        private static float TargetScaleFor(string role)
        {
            return role == "client-2" ? ScaleClient2 : ScaleClient1;
        }

        /// <summary>
        ///     Масштаб скелета аватара. <c>Dummy Forward</c> создаёт SDK в рантайме,
        ///     поэтому читаем по имени — как это делает и сам игровой код.
        ///     Возвращает -1, если объекта нет: это отличимо от любого валидного масштаба.
        /// </summary>
        private static float DummyForwardScale(PlayerController avatar)
        {
            if (avatar == null) return -1f;

            Transform dummyForward = avatar.transform.Find("Dummy Forward");
            return dummyForward != null ? dummyForward.localScale.x : -1f;
        }


        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное — сервер
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Раздаёт номер фазы клиентам через SyncVar <c>PlayerSession.Score</c>.</summary>
        private static void SetPhase(int phase)
        {
            if (PlayersManager.Instance == null) return;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null)
                    session.Score = phase;
            }
        }

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

        private static bool AnyClientFlag()
        {
            if (PlayersManager.Instance == null) return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    return true;
            }

            return false;
        }

        private static bool AllClientsRaised()
        {
            if (PlayersManager.Instance == null || PlayersManager.Instance.Sessions.Count == 0)
                return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || !session.HasGrabbedDogTag)
                    return false;
            }

            return true;
        }

        private static string DescribeFlags()
        {
            if (PlayersManager.Instance == null) return "(нет PlayersManager)";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null)
                    parts.Add($"{session.PlayerName}={session.HasGrabbedDogTag}");
            }

            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "(нет сессий)";
        }

        private static bool AllSessionsHaveAvatar()
        {
            if (PlayersManager.Instance == null || PlayersManager.Instance.Sessions.Count == 0)
                return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || session.ActiveAvatar == null)
                    return false;
            }

            return true;
        }

        private static string DescribeAvatars()
        {
            if (PlayersManager.Instance == null) return "(нет PlayersManager)";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                PlayerController avatar = session.ActiveAvatar;
                parts.Add(avatar == null
                    ? $"{session.PlayerName}: аватара нет"
                    : $"{session.PlayerName}: netId={avatar.netId}");
            }

            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "(нет сессий)";
        }

        /// <summary>Каждая сессия объявила пропорции, отличные от единицы.</summary>
        private static bool AllScalesDeclared()
        {
            if (PlayersManager.Instance == null || PlayersManager.Instance.Sessions.Count == 0)
                return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || Mathf.Abs(session.CalibrationScale - 1f) < Tolerance)
                    return false;
            }

            return true;
        }

        /// <summary>Пропорции клиентов попарно различны — иначе совпадение ничего не значит.</summary>
        private static bool ScalesAreDistinct()
        {
            if (PlayersManager.Instance == null) return false;

            IReadOnlyList<PlayerSession> sessions = PlayersManager.Instance.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                for (int j = i + 1; j < sessions.Count; j++)
                {
                    if (sessions[i] == null || sessions[j] == null) continue;

                    if (Mathf.Abs(sessions[i].CalibrationScale - sessions[j].CalibrationScale) < Tolerance)
                        return false;
                }
            }

            return true;
        }

        private static string DescribeScales()
        {
            if (PlayersManager.Instance == null) return "(нет PlayersManager)";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null)
                    parts.Add($"{session.PlayerName}={session.CalibrationScale:F2}");
            }

            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "(нет сессий)";
        }

        /// <summary>У каждого серверного экземпляра аватара масштаб равен пропорциям его сессии.</summary>
        private static bool AllServerAvatarsScaled()
        {
            if (PlayersManager.Instance == null || PlayersManager.Instance.Sessions.Count == 0)
                return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || session.ActiveAvatar == null)
                    return false;

                if (Mathf.Abs(DummyForwardScale(session.ActiveAvatar) - session.CalibrationScale) > Tolerance)
                    return false;
            }

            return true;
        }

        private static string DescribeServerScales()
        {
            if (PlayersManager.Instance == null) return "(нет PlayersManager)";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                parts.Add($"{session.PlayerName}: сессия={session.CalibrationScale:F2}, " +
                          $"Dummy Forward={DummyForwardScale(session.ActiveAvatar):F2}");
            }

            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "(нет сессий)";
        }

        /// <summary>
        ///     Раскладывает сессии по командам режима по кругу. Нужно ради валидных
        ///     индексов команд: без них аватары спавнятся не у всех.
        /// </summary>
        private static string AssignTeams(SessionManager sessionManager)
        {
            GameModeData mode = sessionManager.SelectedGameModeData;
            if (mode == null || mode.teams == null || mode.teams.Length == 0)
                return "режим не отдал список команд — команды не назначены";

            IReadOnlyList<PlayerSession> sessions = PlayersManager.Instance.Sessions;
            List<string> report = new List<string>();

            for (int i = 0; i < sessions.Count; i++)
            {
                TeamData team = mode.teams[i % mode.teams.Length];
                if (team == null) continue;

                sessions[i].TeamIndex = team.teamIndex;
                report.Add($"{sessions[i].PlayerName}->{team.displayName}({team.teamIndex})");
            }

            return string.Join(", ", report.ToArray());
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное — клиент
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        ///     Сверяет масштаб <b>чужих</b> аватаров с пропорциями их сессий.
        ///
        ///     <para>
        ///     Свой аватар исключён намеренно: локально его масштаб ставит ещё и
        ///     <c>PhysicalSpaceSyncManager</c> из накопленной калибровки, а в прогоне
        ///     никто не калибровался. Находка VR-01 и формулируется про чужие экраны.
        ///     </para>
        ///
        ///     <para>
        ///     Требуется хотя бы один чужой аватар с масштабом, отличным от единицы:
        ///     иначе «совпало» означало бы лишь, что обе стороны остались стандартными.
        ///     </para>
        /// </summary>
        private static bool RemoteAvatarsMatchSessions(out string mismatch, out string seen)
        {
            mismatch = string.Empty;
            List<string> observed = new List<string>();

            PlayerController[] avatars = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include);
            int remoteChecked = 0;
            bool allMatch = true;

            foreach (PlayerController avatar in avatars)
            {
                if (avatar == null) continue;

                // Свой аватар пропускаем — см. комментарий к методу.
                if (avatar.netIdentity != null && avatar.netIdentity.isOwned) continue;

                PlayerSession session = avatar.Session;
                if (session == null)
                {
                    observed.Add($"netId={avatar.netId}: сессия не разрешилась");
                    allMatch = false;
                    continue;
                }

                float actual = DummyForwardScale(avatar);
                float expected = session.CalibrationScale;

                observed.Add($"{session.PlayerName}: сессия={expected:F2}, Dummy Forward={actual:F2}");

                // Пропорции ещё не объявлены — сверять нечего, ждём следующий кадр.
                if (Mathf.Abs(expected - 1f) < Tolerance)
                {
                    allMatch = false;
                    continue;
                }

                if (Mathf.Abs(actual - expected) > Tolerance)
                {
                    mismatch = $"{session.PlayerName}: ожидали {expected:F2}, на аватаре {actual:F2}";
                    allMatch = false;
                    continue;
                }

                remoteChecked++;
            }

            seen = observed.Count > 0 ? string.Join("; ", observed.ToArray()) : "(чужих аватаров в сцене нет)";

            if (remoteChecked == 0 && string.IsNullOrEmpty(mismatch))
                mismatch = "ни одного чужого аватара с объявленными пропорциями ещё не видно";

            return allMatch && remoteChecked > 0;
        }
    }
}
#endif
