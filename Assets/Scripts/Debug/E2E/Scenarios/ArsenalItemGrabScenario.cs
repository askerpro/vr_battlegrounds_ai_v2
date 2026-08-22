// Ярус C (два процесса) — сценарий arsenal-item-grab: находки NET-16 и NET-17.
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>arsenal-item-grab</c> — находки <b>NET-16</b> и <b>NET-17</b>.
    ///
    /// <para>
    /// <b>Вопрос.</b> Клиент берёт оружие со стены арсенала по-настоящему.
    /// Узнаёт ли об этом сервер? Занятость слота сервер выводит из иерархии
    /// (<c>ArsenalSlotController.CurrentItem</c> — предмет под якорем), а смену
    /// родителя Mirror не реплицирует. Единственный кандидат на доставку — канал
    /// состояния UltimateXR: <c>UxrGrabManager.GrabObject</c> обёрнут в
    /// <c>BeginSync</c>, а с T-12 канал двусторонний. Доедет ли событие до сервера,
    /// зависит от того, совпадает ли <c>UniqueId</c> заспавненного в рантайме оружия
    /// на обеих машинах. Чтением это не решается — только замером.
    /// </para>
    ///
    /// <para>
    /// <b>Почему это стало проверяемо.</b> Захват в headless доступен:
    /// <c>UxrGrabManager.GrabObject(grabber, grabbableObject, grabPoint, propagateEvents)</c>
    /// публичен, а <c>UxrGrabber</c> у аватара есть и без шлема. Шлем (уровень 5
    /// из <c>Docs/testing.md</c>) для ответа не нужен.
    /// </para>
    ///
    /// <para>
    /// <b>Предмет выбирается по netId, а не по слоту.</b> Обе машины берут
    /// заспавненный в рантайме хватаемый объект с наименьшим <c>netId</c>: netId
    /// раздаёт сервер, он общий, а вот привязка «предмет → слот» у клиента может
    /// отсутствовать вовсе (см. NET-23) — и от неё вопрос NET-16 зависеть не должен.
    /// Клиенту для захвата слот не нужен: он берёт сам объект.
    /// </para>
    ///
    /// <para>
    /// <b>NET-17.</b> Заодно замеряется, поднимает ли слот <c>OnItemTaken</c>.
    /// Событие рождается из <c>UxrGrabbableObjectAnchor.Removed</c>, а его поднимает
    /// только <c>UxrGrabManager</c> и только когда у предмета выставлен
    /// <c>CurrentAnchor</c>. Сетевая выдача (<c>AssignNetworkItem</c>) его не выставляет.
    /// </para>
    ///
    /// <para>
    /// <b>Матч не запускается.</b> Стены открывает сам сценарий с сервера
    /// (<c>OpenArsenal(immediate)</c>) — состояние стены серверное (T-15), и клиент
    /// получит открытую стену репликацией. Это убирает из прогона всю машину раунда:
    /// проверяется ровно захват предмета, а путь до фазы Equipment уже проверяет
    /// <c>dedicated-server-arsenal</c>. Отсюда же и один клиент вместо двух.
    /// </para>
    /// </summary>
    public class ArsenalItemGrabScenario : IE2EScenario
    {
        public string Name => "arsenal-item-grab";

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients   = "клиент подключился и сервер создал сессию";
        private const string CheckMap       = "карта загружена, стены арсенала открыты, предмет лежит в слоте";
        private const string CheckArmed     = "клиент отчитался, что нашёл предмет и готов его взять";
        private const string CheckSawGrab   = "сервер увидел, что предмет унесли из слота (NET-16)";
        private const string CheckSlotEvent = "слот на сервере поднял OnItemTaken (NET-17)";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientItem      = "клиент нашёл общий предмет и своего грабера";
        private const string CheckClientGrab      = "клиент взял предмет по-настоящему, через UxrGrabManager";

        // ── Фазы прогона (сервер → клиент, через SyncVar PlayerSession.Score) ──

        /// <summary>Стена открыта, клиент может брать предмет.</summary>
        private const int PhaseGrab = 1;

        // ── Сроки ─────────────────────────────────────────────────────────

        /// <summary>Сколько сервер ждёт отчёта клиента «я готов брать».</summary>
        private const float ArmedWait = 180f;

        /// <summary>
        /// Сколько сервер ждёт, что предмет уйдёт из слота. Срок щедрый: круг
        /// «клиент → Cmd канала состояния → ExecuteStateSyncEvent на сервере»
        /// проходит за доли секунды, и всё, что дольше пары секунд, — уже отказ,
        /// а не медленная доставка. Тридцать секунд взяты, чтобы красный вердикт
        /// нельзя было списать на неудачный тайминг.
        /// </summary>
        private const float GrabSeenWait = 30f;

        /// <summary>Сколько сервер ждёт, пока клиент запишет вердикт, прежде чем гасить процесс.</summary>
        private const float ClientVerdictWait = 45f;

        // ── Наблюдение ────────────────────────────────────────────────────

        /// <summary>Сколько раз слоты подняли <c>OnItemTaken</c> на этой машине.</summary>
        private int _itemTakenEvents;

        /// <summary>Слоты, на которые поставлена подписка.</summary>
        private readonly List<ArsenalSlotController> _watchedSlots = new List<ArsenalSlotController>();


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
            result.Declare(CheckDedicated, CheckClients, CheckMap, CheckArmed, CheckSawGrab, CheckSlotEvent);

            // ── 1. Выделенный сервер ──────────────────────────────────────
            float deadline = Now + 60f;
            while (!NetworkServer.active && Now < deadline)
                yield return null;

            bool dedicated = NetworkServer.active && !NetworkClient.active;
            result.Set(CheckDedicated, dedicated,
                dedicated
                    ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                    : $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}. " +
                      "Нужен именно выделенный сервер: на хосте оба процесса — один, и вопрос NET-16 не существует. " +
                      "Убедись, что сервер запущен с -batchmode -nographics.");

            if (!dedicated)
            {
                result.Summary = "конфигурация не выделенный сервер, прогон недействителен";
                yield break;
            }

            DisableDebugOrchestrator();

            // ── 2. Клиент ─────────────────────────────────────────────────
            deadline = Now + 90f;
            while (SessionCount() < context.ExpectedClients && Now < deadline)
                yield return null;

            int sessions = SessionCount();
            bool clientsOk = sessions >= context.ExpectedClients;
            result.Set(CheckClients, clientsOk,
                clientsOk
                    ? $"сессий на сервере: {sessions} (ждали {context.ExpectedClients})"
                    : $"за 90 с подключилось сессий: {sessions} из {context.ExpectedClients}. " +
                      "Смотри client-*.log рядом с этим файлом.");

            if (!clientsOk)
            {
                result.Summary = "клиент не подключился, вопрос о захвате задать некому";
                yield break;
            }

            // ── 3. Карта ──────────────────────────────────────────────────
            SessionManager sessionManager = SessionManager.Instance;
            if (sessionManager == null || MapManager.Instance == null)
            {
                result.Set(CheckMap, false,
                    $"SessionManager.Instance={(sessionManager == null ? "null" : "есть")}, " +
                    $"MapManager.Instance={(MapManager.Instance == null ? "null" : "есть")} — карту загрузить некому");
                result.Summary = "менеджеры не поднялись, прогон недействителен";
                yield break;
            }

            // Режим выбирается не ради матча (матч здесь не запускается), а ради
            // валидных индексов команд: AvatarManager.ChangeAvatar молча выходит,
            // если TeamRegistry не знает индекс команды сессии, и клиент остаётся
            // без аватара — а значит и без грабера, которым берут предмет.
            sessionManager.SetSession(context.Map, "elimination");
            yield return null;

            GameLog.Debug.Info($"[E2E] Команды распределены: {AssignTeams(sessionManager)}");

            MapManager.Instance.LoadMap(context.Map);

            deadline = Now + 120f;
            while ((SceneManager.GetActiveScene().name != context.Map || GameplayManager.Instance == null)
                   && Now < deadline)
                yield return null;

            // Стены арсенала — сетевые объекты сцены: Mirror поднимает их
            // в SpawnObjects() после смены сцены, поэтому netId появляется не сразу.
            ArsenalWallController[] walls = new ArsenalWallController[0];
            deadline = Now + 30f;
            while (Now < deadline)
            {
                walls = Object.FindObjectsByType<ArsenalWallController>(FindObjectsInactive.Include);
                if (walls.Length > 0 && PickSharedWall(walls) != null)
                    break;

                yield return null;
            }

            // Открываем все стены: предмет с наименьшим netId может лежать на любой,
            // а закрытая стена держит свои слоты заблокированными.
            foreach (ArsenalWallController wall in walls)
            {
                WatchSlots(wall);
                wall.OpenArsenal(true);
            }

            yield return null;

            NetworkIdentity target = PickSharedItem();
            ArsenalSlotController targetSlot = FindSlotHolding(walls, target);

            bool mapOk = walls.Length > 0 && target != null && targetSlot != null;
            result.Set(CheckMap, mapOk,
                mapOk
                    ? $"сцена='{SceneManager.GetActiveScene().name}', стен: {walls.Length}, " +
                      $"занятых слотов: {CountOccupiedSlots(walls)} из {SlotCount(walls)}; " +
                      $"общий предмет netId={target.netId} ('{target.name}') лежит в слоте '{targetSlot.name}'. " +
                      $"UniqueId предмета на сервере: {DescribeUniqueId(target)}"
                    : $"сцена='{SceneManager.GetActiveScene().name}', стен: {walls.Length}, " +
                      $"занятых слотов: {CountOccupiedSlots(walls)} из {SlotCount(walls)}, " +
                      $"общий предмет={(target == null ? "не выбран" : $"netId={target.netId}")}, " +
                      $"слот с ним={(targetSlot == null ? "не найден" : targetSlot.name)}. " +
                      "Брать нечего: либо OnStartServer не заполнил слоты, либо предмет не заспавнен Mirror.");

            if (!mapOk)
            {
                result.Summary = "предмет для захвата не выбран, вопрос NET-16 задать нечем";
                yield break;
            }

            // ── 4. Отмашка клиенту и его отчёт о готовности ───────────────
            BroadcastPhase(PhaseGrab);

            E2EWaitOutcome armed = new E2EWaitOutcome();
            yield return E2EWait.Until(armed,
                "клиент отчитался, что нашёл предмет и готов его взять",
                ArmedWait,
                () => ReportedFlags() >= context.ExpectedClients,
                () => $"отчитались {ReportedFlags()} из {context.ExpectedClients}; " +
                      $"подключений: {NetworkServer.connections.Count}; " +
                      $"предмет netId={target.netId} в слоте: {(targetSlot.CurrentItem == target.gameObject ? "да" : "нет")}",
                () => NetworkServer.connections.Count > 0
                    ? null
                    : "на сервере не осталось подключений — отчитываться уже некому");

            result.Set(CheckArmed, armed.Succeeded, armed.Diagnosis);

            if (!armed.Succeeded)
            {
                result.Summary = "клиент до захвата не дошёл, вердикт по NET-16 вынести нельзя";
                yield return WaitForClientVerdicts(context, walls);
                yield break;
            }

            // ── 5. Доехал ли факт захвата до сервера (NET-16) ─────────────
            E2EWaitOutcome sawGrab = new E2EWaitOutcome();
            yield return E2EWait.Until(sawGrab,
                $"предмет netId={target.netId} ушёл из слота '{targetSlot.name}' на сервере",
                GrabSeenWait,
                () => targetSlot.CurrentItem != (target != null ? target.gameObject : null),
                () => $"предмет в слоте: {(target != null && targetSlot.CurrentItem == target.gameObject ? "да" : "нет")}, " +
                      $"занятых слотов: {CountOccupiedSlots(walls)} из {SlotCount(walls)}, " +
                      $"родитель предмета: {DescribeParent(target)}, " +
                      $"подключений: {NetworkServer.connections.Count}",
                () => NetworkServer.connections.Count > 0
                    ? null
                    : "клиент отключился — событие захвата прийти уже не может");

            result.Set(CheckSawGrab, sawGrab.Succeeded,
                sawGrab.Succeeded
                    ? sawGrab.Diagnosis + " Захват доехал до сервера каналом состояния UltimateXR: " +
                      "UniqueId заспавненного оружия на обеих машинах совпал, и ExecuteStateSyncEvent " +
                      "переиграл GrabObject у сервера. Занятость слота на сервере не врёт."
                    : sawGrab.Diagnosis + " Это NET-16: клиент унёс предмет, а сервер по-прежнему считает " +
                      "слот занятым. NeedsReplenishment() вернёт false, и со второго раунда слот не пополнится. " +
                      "Сверься с client-1.json: если там захват зелёный, теряется именно доставка на сервер.");

            // ── 6. События слота (NET-17) ─────────────────────────────────
            result.Set(CheckSlotEvent, _itemTakenEvents > 0,
                _itemTakenEvents > 0
                    ? $"OnItemTaken на сервере сработало {_itemTakenEvents} раз(а)"
                    : "OnItemTaken на сервере не сработало ни разу. Это NET-17: событие рождается из " +
                      "UxrGrabbableObjectAnchor.Removed, а тот поднимает только UxrGrabManager и только " +
                      "когда у предмета выставлен CurrentAnchor. Сетевая выдача его не выставляет, " +
                      "поэтому RemoveObjectFromAnchor выходит первой же строкой.");

            yield return WaitForClientVerdicts(context, walls);

            result.Summary = result.AllChecksGreen
                ? "захват клиента доехал до сервера, слот увидел уход предмета — NET-16 и NET-17 не воспроизводятся"
                : !sawGrab.Succeeded
                    ? "NET-16 подтверждена: факт захвата до сервера не доезжает"
                    : "захват доехал, но слот не поднял OnItemTaken — NET-17 подтверждена";
        }

        /// <summary>
        /// Держит серверный процесс живым, пока клиент не отчитается, что записал
        /// свой вердикт. Без этого сервер гаснет в тот же кадр, в котором увидел
        /// захват, и клиент теряет связь посреди собственных проверок — ровно
        /// тот класс нестабильности, что описан в TEST-01.
        /// </summary>
        private static IEnumerator WaitForClientVerdicts(E2EContext context, ArsenalWallController[] walls)
        {
            E2EWaitOutcome barrier = new E2EWaitOutcome();

            yield return E2EWait.Until(barrier,
                $"все {context.ExpectedClients} клиент(а) отчитались, что записали свой вердикт",
                ClientVerdictWait,
                () => ReportedFlags() == 0 && SessionCount() >= context.ExpectedClients,
                () => $"держат флаг: {ReportedFlags()} из {SessionCount()} сессий; " +
                      $"подключений: {NetworkServer.connections.Count}; " +
                      $"занятых слотов: {CountOccupiedSlots(walls)}");

            GameLog.Debug.Info($"[E2E] Барьер клиентских вердиктов: {barrier.Diagnosis}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap,
                           CheckClientItem, CheckClientGrab);

            DisableDebugOrchestrator();

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
                    ? $"подключён к {(NetworkManager.singleton != null ? NetworkManager.singleton.networkAddress : "?")}"
                    : $"за 60 с подключиться не удалось: ни Discovery, ни прямое подключение к '{context.ServerAddress}'");

            if (!connected)
            {
                result.Summary = "клиент не подключился";
                yield break;
            }

            deadline = Now + 60f;
            while (PlayerSession.LocalSession == null && Now < deadline)
                yield return null;

            PlayerSession local = PlayerSession.LocalSession;
            result.Set(CheckClientSession, local != null,
                local != null
                    ? $"PlayerSession.LocalSession netId={local.netId}"
                    : "за 60 с сервер не создал PlayerSession для этого клиента");

            if (local == null)
            {
                result.Summary = "сессии нет, отчитываться нечем";
                yield break;
            }

            deadline = Now + 120f;
            while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                yield return null;

            bool onMap = SceneManager.GetActiveScene().name == context.Map;
            result.Set(CheckClientMap, onMap,
                onMap
                    ? $"активная сцена='{SceneManager.GetActiveScene().name}'"
                    : $"за 120 с клиент не переехал на '{context.Map}', остался в '{SceneManager.GetActiveScene().name}'");

            if (!onMap)
            {
                result.Summary = "клиент не на карте, брать нечего";
                yield break;
            }

            // ── Отмашка сервера ──────────────────────────────────────────
            E2EWaitOutcome phase = new E2EWaitOutcome();
            yield return E2EWait.Until(phase,
                "сервер объявил, что стены открыты (PlayerSession.Score >= PhaseGrab)",
                120f,
                () => local != null && local.Score >= PhaseGrab,
                () => $"Score={(local != null ? local.Score : -1)}, связь={(NetworkClient.isConnected ? "есть" : "нет")}",
                () => NetworkClient.isConnected ? null : "связь с сервером пропала");

            // ── Общий предмет и грабер ───────────────────────────────────
            NetworkIdentity target = null;
            UxrGrabber grabber = null;
            deadline = Now + 90f;
            while (Now < deadline)
            {
                target = PickSharedItem();
                grabber = FindGrabber();
                if (target != null && grabber != null)
                    break;

                yield return null;
            }

            UxrGrabbableObject grabbable = target != null ? target.GetComponent<UxrGrabbableObject>() : null;
            ArsenalSlotController clientSlot = FindSlotHolding(
                Object.FindObjectsByType<ArsenalWallController>(FindObjectsInactive.Include), target);

            bool itemOk = grabbable != null && grabber != null;
            result.Set(CheckClientItem, itemOk,
                itemOk
                    ? $"общий предмет netId={target.netId} ('{target.name}'), грабер '{grabber.name}'. " +
                      $"Привязка предмет→слот у клиента: {(clientSlot != null ? $"есть, слот '{clientSlot.name}'" : "отсутствует — см. NET-23")}. " +
                      $"UniqueId предмета на клиенте: {DescribeUniqueId(target)}; UniqueId грабера: {grabber.UniqueId}"
                    : $"предмет={(target == null ? "не найден среди заспавненных в рантайме" : grabbable == null ? $"netId={target.netId}, но без UxrGrabbableObject" : $"netId={target.netId}, годен")}, " +
                      $"грабер={(grabber == null ? "не найден у локального аватара" : grabber.name)}. " +
                      $"UxrAvatar.LocalAvatar={(UxrAvatar.LocalAvatar != null ? UxrAvatar.LocalAvatar.name : "null")}, " +
                      $"LocalSession.ActiveAvatar={(local.ActiveAvatar != null ? "есть" : "нет")}. " +
                      "Настоящий захват недоступен, и вопрос NET-16 остаётся без ответа.");

            if (!itemOk)
            {
                result.Summary = "клиенту нечем или нечего брать";
                ReportFlag(true);
                yield return E2EWait.Hold(1f);
                ReportFlag(false);
                yield break;
            }

            WatchAllSlots();

            // Флаг поднимается до захвата: сервер обязан начать наблюдение раньше,
            // чем предмет уйдёт, иначе он увидит уже свершившийся факт.
            ReportFlag(true);
            yield return E2EWait.Hold(1f);

            GameLog.Debug.Info(
                $"[E2E] Беру предмет '{grabbable.name}' (netId={target.netId}) грабером '{grabber.name}'. " +
                $"Родитель до захвата: {DescribeParent(target)}");

            // Захват считается состоявшимся по событию, а не по тому, что предмет
            // остался в руке. Причина: в headless контроллеров нет, и ближайший
            // UxrGrabManager.UpdateManipulation в том же кадре видит «кнопка не нажата»
            // и отпускает предмет. Для вопроса NET-16 это безразлично — событие захвата
            // канал уже породил, — но проверка «GrabbedObject не пуст» была бы красной
            // всегда и меряла бы отсутствующий ввод, а не код.
            bool grabRaised = false;
            System.EventHandler<UxrManipulationEventArgs> onGrabbed =
                (sender, args) => { if (args.GrabbableObject == grabbable) grabRaised = true; };
            UxrGrabManager.Instance.ObjectGrabbed += onGrabbed;

            // Захват без propagateEvents был бы полуправдой: события Removing/Removed
            // и есть то, чего ждёт слот, — их отсутствие должно быть свойством кода,
            // а не выбором сценария.
            UxrGrabManager.Instance.GrabObject(grabber, grabbable, 0, true);

            yield return null;

            UxrGrabManager.Instance.ObjectGrabbed -= onGrabbed;

            result.Set(CheckClientGrab, grabRaised,
                grabRaised
                    ? $"предмет '{grabbable.name}' (netId={target.netId}) взят: UxrGrabManager поднял ObjectGrabbed. " +
                      $"Родитель после захвата: {DescribeParent(target)}. " +
                      $"В руке сейчас: {(grabber.GrabbedObject == grabbable ? "да" : "нет — headless отпустил в том же кадре, ввода нет")}. " +
                      $"CurrentAnchor предмета на клиенте: {(grabbable.CurrentAnchor == null ? "пуст" : grabbable.CurrentAnchor.name)}. " +
                      $"OnItemTaken у клиента: {_itemTakenEvents} раз(а)"
                    : $"UxrGrabManager.GrabObject отработал, но ObjectGrabbed не поднялось — захват не состоялся. " +
                      $"Предмет '{grabbable.name}': enabled={grabbable.enabled}, IsGrabbable={grabbable.IsGrabbable}, " +
                      $"точек захвата={grabbable.GrabPointCount}. Без захвата вопрос NET-16 остаётся без ответа.");

            result.Summary = result.AllChecksGreen
                ? "клиент взял общий предмет по-настоящему — дальше слово за сервером"
                : "клиент до настоящего захвата не дошёл, см. detail";

            // Вердикт записан: отпускаем сервер.
            ReportFlag(false);
        }

        /// <summary>
        /// Грабер локального аватара. Сначала спрашиваем аватар, привязанный к сессии
        /// (это тот, которым играет именно этот процесс), затем — <c>UxrAvatar.LocalAvatar</c>
        /// как запасной путь.
        /// </summary>
        private static UxrGrabber FindGrabber()
        {
            PlayerController avatar = PlayerSession.LocalSession != null
                ? PlayerSession.LocalSession.ActiveAvatar
                : null;

            if (avatar != null)
            {
                UxrGrabber fromSession = avatar.GetComponentInChildren<UxrGrabber>(true);
                if (fromSession != null)
                    return fromSession;
            }

            if (UxrAvatar.LocalAvatar != null)
                return UxrAvatar.LocalAvatar.GetComponentInChildren<UxrGrabber>(true);

            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        /// <summary>
        /// Предмет, который все процессы понимают одинаково: заспавненный в рантайме
        /// хватаемый объект с наименьшим <c>netId</c>.
        ///
        /// <para>
        /// <c>sceneId != 0</c> отсекает объекты сцены — стены и жетоны. Остаются только
        /// те, что создал <c>ReplenishWeaponsNetwork</c>. Декоративные магазины сюда
        /// не попадают: их <c>FirearmSlotController</c> инстанцирует локально,
        /// без <c>NetworkServer.Spawn</c>.
        /// </para>
        /// </summary>
        private static NetworkIdentity PickSharedItem()
        {
            Dictionary<uint, NetworkIdentity> spawned = NetworkServer.active
                ? NetworkServer.spawned
                : NetworkClient.spawned;

            NetworkIdentity best = null;

            foreach (NetworkIdentity identity in spawned.Values)
            {
                if (identity == null || identity.sceneId != 0) continue;
                if (identity.GetComponent<UxrGrabbableObject>() == null) continue;
                if (best == null || identity.netId < best.netId) best = identity;
            }

            return best;
        }

        /// <summary>Слот, в котором сейчас лежит указанный предмет, или null.</summary>
        private static ArsenalSlotController FindSlotHolding(ArsenalWallController[] walls, NetworkIdentity item)
        {
            if (item == null || walls == null)
                return null;

            foreach (ArsenalWallController wall in walls)
            {
                if (wall == null) continue;

                foreach (ArsenalSlotController slot in wall.Slots)
                {
                    if (slot != null && slot.CurrentItem == item.gameObject)
                        return slot;
                }
            }

            return null;
        }

        /// <summary>
        /// <c>UniqueId</c> хватаемого компонента предмета. Это и есть адрес, по которому
        /// канал состояния UltimateXR ищет объект на принимающей машине: если он разный,
        /// событие захвата приезжает и отбрасывается с <c>UxrComponentNotFoundException</c>.
        /// </summary>
        private static string DescribeUniqueId(NetworkIdentity item)
        {
            if (item == null)
                return "предмета нет";

            UxrGrabbableObject grabbable = item.GetComponent<UxrGrabbableObject>();
            return grabbable == null ? "без UxrGrabbableObject" : grabbable.UniqueId.ToString();
        }

        private static string DescribeParent(NetworkIdentity item)
        {
            if (item == null)
                return "предмет уничтожен";

            Transform parent = item.transform.parent;
            return parent == null ? "(корень сцены)" : parent.name;
        }

        /// <summary>Подписывается на <c>OnItemTaken</c> всех слотов стены.</summary>
        private void WatchSlots(ArsenalWallController wall)
        {
            if (wall == null) return;

            foreach (ArsenalSlotController slot in wall.Slots)
            {
                if (slot == null || _watchedSlots.Contains(slot)) continue;

                slot.OnItemTaken += HandleItemTaken;
                _watchedSlots.Add(slot);
            }
        }

        private void WatchAllSlots()
        {
            foreach (ArsenalWallController wall in Object.FindObjectsByType<ArsenalWallController>(FindObjectsInactive.Include))
                WatchSlots(wall);
        }

        private void HandleItemTaken(ArsenalSlotController slot)
        {
            _itemTakenEvents++;
            GameLog.Debug.Info($"[E2E] Слот '{slot.name}' поднял OnItemTaken");
        }

        private static int SlotCount(ArsenalWallController[] walls)
        {
            int total = 0;
            foreach (ArsenalWallController wall in walls)
            {
                if (wall != null) total += wall.Slots.Count;
            }

            return total;
        }

        /// <summary>Сколько слотов сейчас считают себя занятыми.</summary>
        private static int CountOccupiedSlots(ArsenalWallController[] walls)
        {
            int occupied = 0;
            foreach (ArsenalWallController wall in walls)
            {
                if (wall == null) continue;

                foreach (ArsenalSlotController slot in wall.Slots)
                {
                    if (slot != null && slot.IsItemPresent) occupied++;
                }
            }

            return occupied;
        }

        /// <summary>
        /// Стена, которую все процессы понимают одинаково: с наименьшим ненулевым
        /// <c>netId</c>. Проверка <c>netIdentity == null</c> обязательна: поиск идёт
        /// сразу после смены сцены и с <c>FindObjectsInactive.Include</c>, а
        /// <c>NetworkBehaviour.netIdentity</c> заполняет <c>NetworkIdentity.Awake</c>.
        /// У объекта, чей <c>Awake</c> ещё не отработал, обращение к <c>netId</c>
        /// роняет NRE прямо внутри Mirror.
        /// </summary>
        private static ArsenalWallController PickSharedWall(ArsenalWallController[] walls)
        {
            ArsenalWallController best = null;

            foreach (ArsenalWallController wall in walls)
            {
                if (wall == null || wall.netIdentity == null || wall.netId == 0) continue;
                if (best == null || wall.netId < best.netId) best = wall;
            }

            return best;
        }

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

        /// <summary>
        /// Раскладывает сессии по командам режима по кругу. Нужно не ради матча,
        /// а ради <c>AvatarManager.ChangeAvatar</c>: он молча выходит, если
        /// <c>TeamRegistry</c> не знает индекс команды, и аватар не спавнится.
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

        /// <summary>Сколько сессий держат поднятым обратный флаг.</summary>
        private static int ReportedFlags()
        {
            if (PlayersManager.Instance == null)
                return 0;

            int reported = 0;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    reported++;
            }

            return reported;
        }

        /// <summary>
        /// Обратный канал клиент → сервер. <c>HasGrabbedDogTag</c> взят потому, что это
        /// единственное поле сессии, которое клиент вправе менять командой; матч в этом
        /// сценарии не запускается, поэтому игрового смысла у флага здесь нет.
        /// Поднятый флаг значит «я нашёл предмет и сейчас возьму», опущенный — «вердикт записан».
        /// </summary>
        private static void ReportFlag(bool raised)
        {
            PlayerSession local = PlayerSession.LocalSession;
            if (local == null)
            {
                GameLog.Debug.Info("[E2E] Отчитаться нечем: локальной сессии нет");
                return;
            }

            local.CmdSetDogTagGrabbed(raised);
        }

        /// <summary>Раздаёт фазу прогона клиентам через <c>PlayerSession.Score</c>.</summary>
        private static void BroadcastPhase(int phase)
        {
            if (PlayersManager.Instance == null)
                return;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null)
                    session.Score = phase;
            }

            GameLog.Debug.Info($"[E2E] Фаза прогона -> {phase}");
        }

        private static void DisableDebugOrchestrator()
        {
            DebugOrchestrator orchestrator = Object.FindFirstObjectByType<DebugOrchestrator>();
            if (orchestrator == null || !orchestrator.enabled)
                return;

            orchestrator.enabled = false;
            GameLog.Debug.Info("[E2E] DebugOrchestrator отключён: дирижёром прогона выступает сценарий");
        }
    }
}
#endif
