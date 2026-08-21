// Ярус C (два процесса) — сценарий avatar-swap-death-replication, находки NET-01/NET-02/NET-03.
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    ///     Сценарий <c>avatar-swap-death-replication</c> — находка <b>NET-02</b>
    ///     (а вместе с ней NET-01: тот же канал и тот же способ наблюдения).
    ///
    ///     <para>
    ///     Что доказывает. Здоровье и смерть игрока едут на клиенты не через Mirror,
    ///     а через канал состояния UltimateXR: <c>UxrActor.Life</c> — синхронизируемое
    ///     свойство, его изменение поднимает <c>UxrManager.ComponentStateChanged</c>,
    ///     а рассылает событие <c>UxrMirrorAvatar</c> — компонент на аватаре.
    ///     Аватар пересоздаётся при смене скина, команды и карты, и вместе с ним
    ///     теряется статический <c>_serverBroadcaster</c>: вещать становится некому.
    ///     </para>
    ///
    ///     <para>
    ///     Устройство проверки. Сервер убивает одного и того же игрока дважды: до смены
    ///     аватара (контроль) и после (сама находка). Между ними здоровье возвращается,
    ///     чтобы наблюдение заведомо не залипло. Аватар меняется у <b>другого</b> игрока —
    ///     тогда единственное отличие между контролем и находкой это сама смена аватара,
    ///     а не состояние жертвы.
    ///     </para>
    ///
    ///     <para>
    ///     Кто выносит вердикт. Только сервер: клиент не знает, кого выбрали жертвой,
    ///     и на текущем коде серверную рассылку получает не каждый клиент (владелец
    ///     вещателя игнорирует собственный <c>ClientRpc</c>). Клиенты зеркалят серверу
    ///     своё наблюдение «вижу мёртвого игрока» через <c>PlayerSession.HasGrabbedDogTag</c>,
    ///     а фазы прогона сервер раздаёт им через <c>PlayerSession.Score</c>. Оба поля —
    ///     канал Mirror, независимый от проверяемого канала состояния, поэтому связь
    ///     остаётся даже когда проверяемый канал мёртв. Матч в сценарии не запускается,
    ///     поэтому игровой смысл этих полей никем не используется.
    ///     </para>
    ///
    ///     <para>
    ///     Смерть здесь — это <c>Life = 0</c>, выставленный серверным API
    ///     <see cref="PlayerController.RestoreHealth" />. Именно <c>Life</c> едет по каналу
    ///     состояния, и именно из него клиент считает <c>PlayerController.IsAlive</c>.
    ///     <c>PlayerController.Die()</c> для проверки не годится: сам он <c>Life</c>
    ///     не меняет и шлёт только <c>RpcOnDied</c> по каналу Mirror, то есть мимо
    ///     проверяемого канала. Штатный путь урона недоступен: оружия в headless-режиме нет.
    ///     </para>
    ///
    ///     <para>
    ///     Ожидаемый результат до правки T-12 — <b>красный</b> на последней проверке
    ///     при зелёных контрольных. Зелёный итог до правки означал бы, что сценарий
    ///     не воспроизводит триггер.
    ///     </para>
    /// </summary>
    public class AvatarSwapDeathReplicationScenario : IE2EScenario
    {
        public string Name => "avatar-swap-death-replication";

        // ── Фазы прогона ──────────────────────────────────────────────────
        // Раздаются клиентам через SyncVar PlayerSession.Score. Фазы строго
        // возрастают: клиент реагирует на «фаза не меньше», поэтому пропуск
        // кадра его не собьёт.

        /// <summary>Сервер ещё готовится, клиенту делать нечего.</summary>
        private const int PhaseWait = 0;

        /// <summary>Рукопожатие, шаг 1: поднять флаг наблюдения.</summary>
        private const int PhaseRaise = 1;

        /// <summary>Рукопожатие, шаг 2: опустить флаг наблюдения.</summary>
        private const int PhaseLower = 2;

        /// <summary>Контрольное убийство до смены аватара.</summary>
        private const int PhaseControlKill = 3;

        /// <summary>Контрольный возврат здоровья.</summary>
        private const int PhaseControlRestore = 4;

        /// <summary>Смена аватара и убийство после неё.</summary>
        private const int PhaseSwapKill = 5;

        /// <summary>Прогон завершён, клиент может выносить свой вердикт.</summary>
        private const int PhaseDone = 9;

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated   = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients     = "клиенты подключились и сервер создал сессии";
        private const string CheckAvatars     = "карта загружена, у каждой сессии есть живой аватар";
        private const string CheckBackChannel = "обратный канал наблюдения работает у всех клиентов";
        private const string CheckKillBefore  = "контроль: смерть ДО смены аватара дошла до клиента";
        private const string CheckAliveAgain  = "контроль: возврат здоровья ДО смены аватара дошёл до клиента";
        private const string CheckSwap        = "аватар другого игрока пересоздан на сервере (триггер NET-02)";
        private const string CheckKillAfter   = "смерть ПОСЛЕ смены аватара дошла до клиента";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientMirror    = "обратный канал наблюдения проверен эхом SyncVar";
        private const string CheckClientImplied   = "увидев контрольную смерть, клиент увидел и смерть после смены аватара";

        // ── Наблюдение сервера ────────────────────────────────────────────

        /// <summary>Порядок, в котором аватары спавнились на карте (первый — предполагаемый вещатель).</summary>
        private readonly List<PlayerController> _spawnOrder = new List<PlayerController>();

        /// <summary>Сколько событий канала состояния сервер породил за текущую фазу.</summary>
        private int _stateEventsInPhase;


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
                           CheckKillBefore, CheckAliveAgain, CheckSwap, CheckKillAfter);

            AvatarManager.OnAvatarSpawned += OnAvatarSpawned;
            UltimateXR.Core.UxrManager.ComponentStateChanged += OnComponentStateChanged;

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
                        : "процесс поднялся хостом (NetworkClient.active=true). На хосте канал состояния " +
                          "замыкается внутри процесса, находка не воспроизводится — прогон бессмыслен.");

                if (!dedicated)
                {
                    result.Summary = "конфигурация не выделенный сервер, прогон недействителен";
                    yield break;
                }

                DisableDebugOrchestrator();

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
                if (sessionManager == null || MapManager.Instance == null || AvatarManager.Instance == null)
                {
                    result.Set(CheckAvatars, false,
                        $"SessionManager={(sessionManager == null ? "null" : "есть")}, " +
                        $"MapManager={(MapManager.Instance == null ? "null" : "есть")}, " +
                        $"AvatarManager={(AvatarManager.Instance == null ? "null" : "есть")} — " +
                        "прогон вести нечем");
                    result.Summary = "менеджеры сессии отсутствуют, прогон недействителен";
                    yield break;
                }

                // Режим выбирается не ради матча (матч здесь не запускается), а ради
                // валидных индексов команд: AvatarManager.ChangeAvatar молча выходит,
                // если TeamRegistry не знает индекс команды сессии.
                sessionManager.SetSession(context.Map, "elimination");
                yield return null;

                string teamsReport = AssignTeams(sessionManager);
                GameLog.Debug.Info($"[E2E] Команды распределены: {teamsReport}");

                MapManager.Instance.LoadMap(context.Map);

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

                // Аватары пересоздаются в GameNetworkManager.OnServerReady — то есть
                // только после того, как каждый клиент догрузит карту.
                deadline = Now + 90f;
                while (!AllSessionsHaveAvatar() && Now < deadline)
                    yield return null;

                if (!AllSessionsHaveAvatar())
                {
                    result.Set(CheckAvatars, false,
                        $"за 90 с не у всех сессий появился аватар: {DescribeAvatars()}. " +
                        "GameNetworkManager.OnServerReady спавнит аватар только для готового клиента.");
                    result.Summary = "аватары не заспавнились, вердикт вынести нельзя";
                    yield break;
                }

                bool allAlive = AllAvatarsAliveOnServer();
                result.Set(CheckAvatars, allAlive,
                    allAlive
                        ? $"активная сцена='{SceneManager.GetActiveScene().name}', аватары: {DescribeAvatars()}; " +
                          $"порядок спавна на карте: {DescribeSpawnOrder()}"
                        : $"аватары есть, но не все живы на старте: {DescribeAvatars()} — " +
                          "проверять репликацию смерти в таком состоянии нельзя");

                if (!allAlive)
                {
                    result.Summary = "аватары не в исходном состоянии, вердикт вынести нельзя";
                    yield break;
                }

                // ── 4. Рукопожатие обратного канала ───────────────────────
                // Прежде чем что-то измерять, убеждаемся, что канал наблюдения жив
                // у КАЖДОГО клиента. Иначе «никто не отчитался о смерти» в конце
                // нельзя будет отличить от «отчитаться было нечем».
                SetPhase(PhaseRaise);

                deadline = Now + 60f;
                while (!AllClientsRaised() && Now < deadline)
                    yield return null;

                bool raisedOk = AllClientsRaised();

                SetPhase(PhaseLower);

                deadline = Now + 60f;
                while (AnyClientReportsDead() && Now < deadline)
                    yield return null;

                bool loweredOk = !AnyClientReportsDead();
                bool backChannelOk = raisedOk && loweredOk;

                result.Set(CheckBackChannel, backChannelOk,
                    backChannelOk
                        ? $"все {SessionCount()} клиентов подняли и опустили флаг наблюдения по команде сервера"
                        : $"рукопожатие не состоялось: подняли флаг все={raisedOk}, опустили все={loweredOk}, " +
                          $"флаги сейчас: {DescribeFlags()}. Без рабочего обратного канала вердикт " +
                          "о репликации смерти вынести нельзя.");

                if (!backChannelOk)
                {
                    result.Summary = "обратный канал наблюдения не поднялся, вердикт вынести нельзя";
                    yield break;
                }

                // Жертва и цель смены аватара выбираются так, чтобы триггер NET-02
                // срабатывал детерминированно: пересоздаётся аватар текущего вещателя
                // (иначе поле _serverBroadcaster переживёт смену и находка не проявится),
                // а убивают игрока, аватар которого не трогали.
                PlayerSession swapTarget = ResolveBroadcasterSession();
                string swapReason = swapTarget != null
                    ? "цель — владелец текущего UxrMirrorAvatar._serverBroadcaster"
                    : "поля _serverBroadcaster в SDK нет (правка T-12 применена) — цель по порядку спавна";

                if (swapTarget == null)
                    swapTarget = FirstSpawnedSession();

                PlayerSession victim = OtherSession(swapTarget);

                if (swapTarget == null || victim == null || swapTarget == victim)
                {
                    result.Set(CheckKillBefore, false,
                        $"не удалось выбрать пару «жертва / смена аватара»: цель={Describe(swapTarget)}, " +
                        $"жертва={Describe(victim)}. Нужны две сессии с аватарами.");
                    result.Summary = "не из кого выбрать участников, вердикт вынести нельзя";
                    yield break;
                }

                GameLog.Debug.Info($"[E2E] Смена аватара у {Describe(swapTarget)} ({swapReason}); " +
                                  $"жертва {Describe(victim)}");

                // ── 5. Контроль: смерть ДО смены аватара ──────────────────
                SetPhase(PhaseControlKill);
                _stateEventsInPhase = 0;

                // Life = 0 — это и есть «смерть» с точки зрения клиента:
                // PlayerController.IsAlive читает UxrActor.IsDead, то есть Life <= 0.
                victim.ActiveAvatar.RestoreHealth(0f);

                float started = Now;
                deadline = Now + 25f;
                while (!AnyClientReportsDead() && Now < deadline)
                    yield return null;

                bool killBeforeOk = AnyClientReportsDead();

                // Держим фазу ещё немного после того, как условие выполнилось:
                // клиенты опрашивают номер фазы раз в кадр, и мгновенно проскочившую
                // фазу часть из них просто не увидит — их собственные проверки
                // станут ложно-красными.
                float hold = Now + 3f;
                while (Now < hold)
                    yield return null;

                result.Set(CheckKillBefore, killBeforeOk,
                    (killBeforeOk
                        ? $"клиент подтвердил смерть {Describe(victim)} за {Now - started:F1} с"
                        : $"за 25 с ни один клиент не увидел смерть {Describe(victim)}. " +
                          "Значит канал состояния не доставляет здоровье вообще, и красная проверка " +
                          "после смены аватара уже ничего не докажет про NET-02.") +
                    $" Событий канала состояния на сервере за фазу: {_stateEventsInPhase}. " +
                    $"Вещатель: {DescribeBroadcaster()}. Флаги клиентов: {DescribeFlags()}. " +
                    $"На сервере жертва: Life={LifeOf(victim):F0}, IsAlive={IsAliveOf(victim)}. " +
                    $"Цель смены аватара: {Describe(swapTarget)} ({swapReason}).");

                if (!killBeforeOk)
                {
                    result.Summary = "контроль не прошёл: смерть не доезжает до клиентов даже без смены аватара";
                    yield break;
                }

                // ── 6. Контроль: возврат здоровья ─────────────────────────
                SetPhase(PhaseControlRestore);
                _stateEventsInPhase = 0;
                victim.ActiveAvatar.RestoreHealth(100f);

                deadline = Now + 25f;
                while (AnyClientReportsDead() && Now < deadline)
                    yield return null;

                bool aliveAgainOk = !AnyClientReportsDead();

                hold = Now + 3f;
                while (Now < hold)
                    yield return null;

                result.Set(CheckAliveAgain, aliveAgainOk,
                    (aliveAgainOk
                        ? $"все клиенты снова видят {Describe(victim)} живым — наблюдение не залипло"
                        : $"за 25 с флаги клиентов не вернулись в false: {DescribeFlags()}. " +
                          "Наблюдение залипло, следующая фаза недостоверна.") +
                    $" Событий канала состояния за фазу: {_stateEventsInPhase}.");

                if (!aliveAgainOk)
                {
                    result.Summary = "наблюдение клиентов залипло, вердикт по NET-02 вынести нельзя";
                    yield break;
                }

                // ── 7. Смена аватара — сам триггер ────────────────────────
                SetPhase(PhaseSwapKill);
                uint oldNetId = swapTarget.ActiveAvatarNetId;
                string broadcasterBefore = DescribeBroadcaster();

                AvatarManager.Instance.ChangeAvatar(swapTarget.connectionToClient, swapTarget,
                                                   swapTarget.TeamIndex, swapTarget.AvatarIndex);

                deadline = Now + 30f;
                while ((swapTarget.ActiveAvatarNetId == oldNetId || swapTarget.ActiveAvatar == null)
                       && Now < deadline)
                    yield return null;

                bool swapOk = swapTarget.ActiveAvatarNetId != oldNetId && swapTarget.ActiveAvatar != null;
                result.Set(CheckSwap, swapOk,
                    swapOk
                        ? $"аватар {Describe(swapTarget)} пересоздан: netId {oldNetId} -> {swapTarget.ActiveAvatarNetId}. " +
                          $"Вещатель до смены: {broadcasterBefore}, после: {DescribeBroadcaster()}"
                        : $"за 30 с ChangeAvatar не пересоздал аватар {Describe(swapTarget)}: " +
                          $"netId остался {oldNetId}. Вероятно, TeamRegistry не знает команду " +
                          $"{swapTarget.TeamIndex} или у неё нет префаба с индексом {swapTarget.AvatarIndex}.");

                if (!swapOk)
                {
                    result.Summary = "смена аватара не состоялась, вердикт по NET-02 вынести нельзя";
                    yield break;
                }

                // Даём клиентам догнать спавн нового аватара: иначе красная проверка
                // ниже могла бы означать «клиент ещё не всё получил».
                float settle = Now + 5f;
                while (Now < settle)
                    yield return null;

                // ── 8. Смерть ПОСЛЕ смены аватара — сама находка ──────────
                _stateEventsInPhase = 0;
                victim.ActiveAvatar.RestoreHealth(0f);

                deadline = Now + 25f;
                while (!AnyClientReportsDead() && Now < deadline)
                    yield return null;

                bool killAfterOk = AnyClientReportsDead();

                hold = Now + 3f;
                while (Now < hold)
                    yield return null;

                result.Set(CheckKillAfter, killAfterOk,
                    (killAfterOk
                        ? $"клиент подтвердил смерть {Describe(victim)} и после смены аватара"
                        : $"за 25 с ни один клиент не увидел смерть {Describe(victim)}, хотя ровно та же " +
                          "смерть до смены аватара доезжала (контроль зелёный). Это NET-02: канал состояния " +
                          "держится на статическом UxrMirrorAvatar._serverBroadcaster, а ChangeAvatar сначала " +
                          "спавнит новый аватар (поле занято — вещателем он не становится), потом уничтожает " +
                          "старый (поле обнуляется) — вещателя не остаётся.") +
                    $" Событий канала состояния на сервере за фазу: {_stateEventsInPhase}. " +
                    $"Вещатель: {DescribeBroadcaster()}. Флаги клиентов: {DescribeFlags()}. " +
                    $"На сервере жертва: Life={LifeOf(victim):F0}, IsAlive={IsAliveOf(victim)}. " +
                    $"Аватар жертвы не менялся, менялся аватар {Describe(swapTarget)}.");

                result.Summary = result.AllChecksGreen
                    ? "смерть доезжает до клиентов и до, и после смены аватара — NET-02 не воспроизводится"
                    : !killAfterOk
                        ? "NET-02 подтверждена: до смены аватара смерть реплицируется, после — нет"
                        : "есть красные проверки, см. detail";
            }
            finally
            {
                // Клиенты ждут PhaseDone, чтобы вынести свой вердикт. Ставим его
                // на любом выходе, включая ранний yield break: иначе клиентские
                // процессы досидят до собственного таймаута и прогон станет
                // INCONCLUSIVE вместо честного красного.
                SetPhase(PhaseDone);
                UltimateXR.Core.UxrManager.ComponentStateChanged -= OnComponentStateChanged;
                AvatarManager.OnAvatarSpawned -= OnAvatarSpawned;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap,
                           CheckClientMirror, CheckClientImplied);

            DisableDebugOrchestrator();

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
            bool sentDead = false;
            bool sawDeadControl = false;
            bool sawAliveAgain = false;
            bool sawDeadAfterSwap = false;
            int maxAvatars = 0;
            int lastPhase = -1;

            float hardDeadline = Now + Mathf.Max(30f, context.Timeout - 20f);

            while (Now < hardDeadline)
            {
                // Сервер погас или сессию снесли — дальше наблюдать нечего.
                if (!NetworkClient.isConnected || PlayerSession.LocalSession == null)
                    break;

                local = PlayerSession.LocalSession;
                int phase = local.Score;

                if (phase != lastPhase)
                {
                    GameLog.Debug.Info($"[E2E] Клиент видит фазу прогона: {phase}");
                    lastPhase = phase;
                }

                int avatars = CountAvatars();
                if (avatars > maxAvatars) maxAvatars = avatars;

                // Рукопожатие обратного канала: сервер должен увидеть флаг поднятым,
                // а затем опущенным. Эхо SyncVar подтверждает, что команда доехала.
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

                // Зеркалирование наблюдения: с этого момента флаг означает
                // «вижу в сцене мёртвого игрока».
                if (phase >= PhaseControlKill)
                {
                    bool seesDead = AnyDeadAvatar();
                    if (seesDead != sentDead)
                    {
                        local.CmdSetDogTagGrabbed(seesDead);
                        sentDead = seesDead;
                        GameLog.Debug.Info($"[E2E] Клиент сообщает серверу: вижу мёртвого={seesDead}");
                    }

                    if (phase == PhaseControlKill && seesDead) sawDeadControl = true;
                    if (phase == PhaseControlRestore && !seesDead && sawDeadControl) sawAliveAgain = true;
                    if (phase == PhaseSwapKill && seesDead) sawDeadAfterSwap = true;
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
                      $"эхо спуска={echoDown}, последняя увиденная фаза={lastPhase}. " +
                      "Сервер не узнает, что видел клиент, — его вердикт будет недостоверным");

            // Клиент не знает, кого сервер выбрал жертвой, и на текущем коде серверную
            // рассылку получает не каждый клиент: владелец аватара-вещателя игнорирует
            // собственный ClientRpc. Поэтому проверка сформулирована импликацией —
            // тот, кто увидел контрольную смерть, обязан увидеть и смерть после смены
            // аватара. Для клиента, который не видел ничего, она зелёная: вердикт
            // по самой находке выносит сервер.
            bool implied = !sawDeadControl || sawDeadAfterSwap;
            result.Set(CheckClientImplied, implied,
                implied
                    ? $"контрольную смерть видел: {sawDeadControl}, оживление: {sawAliveAgain}, " +
                      $"смерть после смены аватара: {sawDeadAfterSwap}. Аватаров в сцене максимум: {maxAvatars}"
                    : $"клиент видел контрольную смерть, но после смены аватара — нет " +
                      $"(оживление видел: {sawAliveAgain}, аватаров в сцене максимум: {maxAvatars}). " +
                      "Это NET-02, наблюдённая со стороны клиента.");

            result.Summary = result.AllChecksGreen
                ? "клиент отработал наблюдение и расхождения не увидел"
                : "клиент увидел расхождение между контрольной смертью и смертью после смены аватара";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное — сервер
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        private void OnComponentStateChanged(UltimateXR.Core.StateSync.IUxrStateSync component,
                                             UltimateXR.Core.StateSync.UxrSyncEventArgs eventArgs)
        {
            _stateEventsInPhase++;
        }

        private void OnAvatarSpawned(PlayerController avatar)
        {
            if (avatar != null && !_spawnOrder.Contains(avatar))
                _spawnOrder.Add(avatar);
        }

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

        /// <summary>Хотя бы один клиент прямо сейчас сообщает «вижу мёртвого игрока».</summary>
        private static bool AnyClientReportsDead()
        {
            if (PlayersManager.Instance == null) return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    return true;
            }

            return false;
        }

        /// <summary>Все клиенты подняли флаг наблюдения (шаг 1 рукопожатия).</summary>
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

        private static bool AllAvatarsAliveOnServer()
        {
            if (PlayersManager.Instance == null) return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || session.ActiveAvatar == null || !session.ActiveAvatar.IsAlive)
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
                    : $"{session.PlayerName}: netId={avatar.netId}, Life={avatar.Health:F0}, IsAlive={avatar.IsAlive}");
            }

            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "(нет сессий)";
        }

        private string DescribeSpawnOrder()
        {
            if (_spawnOrder.Count == 0) return "(событий спавна не было)";

            List<string> parts = new List<string>();
            foreach (PlayerController avatar in _spawnOrder)
                parts.Add(avatar != null ? $"{avatar.AvatarPlayerName}(netId={avatar.netId})" : "(уничтожен)");

            return string.Join(" -> ", parts.ToArray());
        }

        private static float LifeOf(PlayerSession session)
        {
            return session != null && session.ActiveAvatar != null ? session.ActiveAvatar.Health : -1f;
        }

        private static bool IsAliveOf(PlayerSession session)
        {
            return session != null && session.ActiveAvatar != null && session.ActiveAvatar.IsAlive;
        }

        private static string Describe(PlayerSession session)
        {
            return session != null ? $"{session.PlayerName}(netId={session.netId})" : "(нет сессии)";
        }

        /// <summary>
        ///     Приватное статическое поле <c>UxrMirrorAvatar._serverBroadcaster</c>.
        ///     Читается рефлексией: поле приватное, а после правки T-12 его не должно
        ///     существовать вовсе — обращение обязано переживать его отсутствие.
        /// </summary>
        private static FieldInfo BroadcasterField()
        {
            return typeof(UltimateXR.Networking.Integrations.Net.Mirror.UxrMirrorAvatar)
                .GetField("_serverBroadcaster", BindingFlags.NonPublic | BindingFlags.Static);
        }

        private static string DescribeBroadcaster()
        {
            FieldInfo field = BroadcasterField();
            if (field == null)
                return "поля _serverBroadcaster в SDK нет (канал вынесен из аватара)";

            Component broadcaster = field.GetValue(null) as Component;
            return broadcaster == null ? "НЕТ (поле пустое)" : broadcaster.gameObject.name;
        }

        /// <summary>Сессия, чей аватар сейчас является вещателем. Null, если поля в SDK уже нет.</summary>
        private static PlayerSession ResolveBroadcasterSession()
        {
            FieldInfo field = BroadcasterField();
            if (field == null) return null;

            Component broadcaster = field.GetValue(null) as Component;
            if (broadcaster == null) return null;

            PlayerController avatar = broadcaster.GetComponent<PlayerController>();
            if (avatar == null) return null;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.ActiveAvatarNetId == avatar.netId)
                    return session;
            }

            return null;
        }

        private PlayerSession FirstSpawnedSession()
        {
            foreach (PlayerController avatar in _spawnOrder)
            {
                if (avatar == null) continue;

                foreach (PlayerSession session in PlayersManager.Instance.Sessions)
                {
                    if (session != null && session.ActiveAvatarNetId == avatar.netId)
                        return session;
                }
            }

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.ActiveAvatar != null)
                    return session;
            }

            return null;
        }

        private static PlayerSession OtherSession(PlayerSession exclude)
        {
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session != exclude && session.ActiveAvatar != null)
                    return session;
            }

            return null;
        }

        /// <summary>
        ///     Раскладывает сессии по командам режима по кругу. Нужно не ради матча,
        ///     а ради <c>AvatarManager.ChangeAvatar</c>: он молча выходит, если
        ///     <c>TeamRegistry</c> не знает индекс команды.
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
        ///     Есть ли в сцене хоть один мёртвый аватар. Считаем и свой, и чужие:
        ///     на текущем коде серверную рассылку получает не владелец аватара-вещателя,
        ///     а остальные, поэтому клиент вполне может узнать о смерти собственного
        ///     аватара и не узнать о чужой.
        /// </summary>
        private static bool AnyDeadAvatar()
        {
            PlayerController[] avatars = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include);
            foreach (PlayerController avatar in avatars)
            {
                if (avatar != null && !avatar.IsAlive)
                    return true;
            }

            return false;
        }

        private static int CountAvatars()
        {
            return Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include).Length;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Общее
        // ══════════════════════════════════════════════════════════════════

        private static void DisableDebugOrchestrator()
        {
            DebugOrchestrator orchestrator = Object.FindFirstObjectByType<DebugOrchestrator>();
            if (orchestrator == null || !orchestrator.enabled)
                return;

            orchestrator.enabled = false;
            GameLog.Debug.Info(
                "[E2E] DebugOrchestrator отключён: дирижёром прогона выступает сценарий");
        }
    }
}
#endif
