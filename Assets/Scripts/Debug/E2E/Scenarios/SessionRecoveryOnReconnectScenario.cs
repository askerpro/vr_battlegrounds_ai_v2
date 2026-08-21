// Ярус C (два процесса) — сценарий session-recovery-on-reconnect, находка ARCH-01.
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
    ///     Сценарий <c>session-recovery-on-reconnect</c> — находка <b>ARCH-01</b>:
    ///     <see cref="SessionRecoveryManager" /> не размещён ни в одной сцене и ни в одном
    ///     префабе, поэтому <c>Instance</c> всегда <c>null</c>, а оба вызова из
    ///     <see cref="PlayersManager" /> обёрнуты в <c>if (Instance != null)</c> и молча
    ///     не выполняются.
    ///
    ///     <para>
    ///     Что доказывает. Игрок набирает состояние (команда, счёт, убийства, здоровье,
    ///     позиция), сервер рвёт ему соединение, клиент возвращается <b>тем же</b>
    ///     <c>deviceToken</c> — и должен вернуться со своим состоянием. Не возвращается:
    ///     снимок никто не сохранял, и сервер заводит игрока как нового.
    ///     </para>
    ///
    ///     <para>
    ///     Почему один клиент, а не два. Матч здесь не запускается — а он единственное,
    ///     что требует игрока в каждой команде (<c>EliminationMode.IsPlayersReady</c>).
    ///     Восстановлению сессии второй игрок не нужен ни для чего, а лишний процесс
    ///     добавил бы только источников недетерминизма. Запускать так:
    ///     <c>Run-E2E.ps1 -Scenario session-recovery-on-reconnect -Clients 1</c>.
    ///     </para>
    ///
    ///     <para>
    ///     Одинаковый <c>deviceToken</c> обеспечен дирижёром: он передаёт клиенту
    ///     <c>-e2eDeviceToken e2e-client-1</c>, харнесс кладёт токен в <c>PlayerPrefs</c>
    ///     до первого подключения, а <c>GameNetworkManager.SendConnectMessage</c> читает
    ///     его оттуда при <b>каждом</b> подключении. То есть вернувшийся процесс
    ///     представляется серверу тем же устройством без дополнительных усилий.
    ///     </para>
    ///
    ///     <para>
    ///     <b>Контроль, отличающий «восстановление сломано» от «переподключение не
    ///     работает».</b> Проверка <see cref="CheckReconnected" /> требует, чтобы после
    ///     разрыва появилась новая сессия с тем же токеном и чтобы она отвечала на
    ///     команду клиента (<c>CmdSetDogTagGrabbed</c>). Её зелёный цвет при красных
    ///     проверках восстановления и означает находку: связь есть, сессия есть,
    ///     она просто пустая. Клиентская роль целиком состоит из такого контроля —
    ///     вердикт по самой находке выносит только сервер.
    ///     </para>
    ///
    ///     <para>
    ///     Ожидание: <b>красный</b> на текущем коде. Зелёный означал бы, что сценарий
    ///     проверяет не то — например, что состояние «восстановилось» совпадением
    ///     со значениями по умолчанию.
    ///     </para>
    /// </summary>
    public class SessionRecoveryOnReconnectScenario : IE2EScenario
    {
        public string Name => "session-recovery-on-reconnect";

        // ── Состояние, которое набирает игрок перед разрывом ────────────────
        // Значения выбраны так, чтобы ни одно не совпадало со значением по
        // умолчанию новой сессии (там всё нули, а имя — "Player_XXXX").

        private const string MarkedName = "e2e-recovered";
        private const int MarkedKills = 4;
        private const int MarkedDeaths = 3;
        private const int MarkedScore = 7;
        private const float MarkedHealth = 42f;

        /// <summary>Насколько отодвигаем аватар от точки спавна, чтобы позиция стала опознаваемой.</summary>
        private static readonly Vector3 PositionOffset = new Vector3(6.5f, 0f, -4.25f);

        /// <summary>Допуск сравнения позиций, метры. Аватар на сервере может слегка осесть под гравитацией.</summary>
        private const float PositionTolerance = 0.5f;

        // ── Имена проверок сервера ─────────────────────────────────────────

        private const string CheckDedicated  = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients    = "клиент подключился и сервер создал сессию";
        private const string CheckAvatar     = "карта загружена, у сессии есть живой аватар";
        private const string CheckHandshake  = "обратный канал клиента поднялся до измерений";
        private const string CheckManager    = "SessionRecoveryManager размещён в проекте и поднялся на сервере";
        private const string CheckStateSet   = "состояние набрано: имя, команда, счёт, здоровье, позиция";
        private const string CheckSaved      = "при разрыве сервер сохранил снимок сессии";
        private const string CheckReconnected = "контроль: клиент вернулся тем же устройством и получил новую сессию";
        private const string CheckLogic      = "восстановлены имя, команда, скин, счёт, убийства и смерти";
        private const string CheckPhysical   = "восстановлены здоровье и позиция аватара";

        // ── Имена проверок клиента ─────────────────────────────────────────

        private const string CheckClientConnected   = "клиент подключился к серверу";
        private const string CheckClientSession     = "сервер создал сессию для клиента";
        private const string CheckClientMap         = "клиент переехал на карту вместе с сервером";
        private const string CheckClientDropped     = "клиент увидел разрыв соединения, инициированный сервером";
        private const string CheckClientReconnected = "клиент вернулся и получил новую живую сессию";


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
            result.Declare(CheckDedicated, CheckClients, CheckAvatar, CheckHandshake,
                           CheckManager, CheckStateSet, CheckSaved, CheckReconnected,
                           CheckLogic, CheckPhysical);

            // ── 1. Выделенный сервер ──────────────────────────────────────
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
                    : "процесс поднялся хостом (NetworkClient.active=true). На хосте сервер и клиент " +
                      "делят один процесс, разрыв соединения проверять нечем — прогон бессмыслен.");

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
                    ? $"сессий на сервере: {sessions} (ждали {context.ExpectedClients}); {DescribeSessions()}"
                    : $"за 90 с подключилось сессий: {sessions} из {context.ExpectedClients}. " +
                      "Клиент не нашёл сервер (Mirror NetworkDiscovery — UDP-броадкаст) " +
                      "или его процесс упал — смотри client-*.log рядом с этим файлом.");

            if (!clientsOk)
            {
                result.Summary = "клиент не подключился, вердикт вынести нельзя";
                yield break;
            }

            // ── 3. Карта и аватар ─────────────────────────────────────────
            SessionManager sessionManager = SessionManager.Instance;
            if (sessionManager == null || MapManager.Instance == null)
            {
                result.Set(CheckAvatar, false,
                    $"SessionManager={(sessionManager == null ? "null" : "есть")}, " +
                    $"MapManager={(MapManager.Instance == null ? "null" : "есть")} — прогон вести нечем");
                result.Summary = "менеджеры сессии отсутствуют, прогон недействителен";
                yield break;
            }

            // Режим выбирается не ради матча (он здесь не запускается), а ради валидных
            // индексов команд: команда с индексом 0 в TeamRegistry не существует
            // по определению (TeamData.teamIndex помечен [Min(1)]).
            sessionManager.SetSession(context.Map, "elimination");
            yield return null;

            // Команду надо раздать ДО загрузки карты. Смена сцены уничтожает аватары
            // на сервере, а пересоздаёт их GameNetworkManager.OnServerReady через
            // AvatarManager.ChangeAvatar — тот молча выходит, если TeamRegistry не знает
            // индекс команды. С индексом 0 (значение по умолчанию новой сессии) аватара
            // после переезда на карту не будет вовсе: проверено прогоном.
            string teamsReport = AssignFirstTeam(sessionManager);
            GameLog.Debug.Info($"[E2E] Команды распределены: {teamsReport}");

            TeamData markedTeam = LastTeamOf(sessionManager);
            MapManager.Instance.LoadMap(context.Map);

            deadline = Now + 120f;
            while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                yield return null;

            if (SceneManager.GetActiveScene().name != context.Map)
            {
                result.Set(CheckAvatar, false,
                    $"за 120 с сервер не переехал на '{context.Map}', " +
                    $"активная сцена='{SceneManager.GetActiveScene().name}'");
                result.Summary = "карта не загрузилась, вердикт вынести нельзя";
                yield break;
            }

            // Аватары пересоздаются в GameNetworkManager.OnServerReady — то есть только
            // после того, как клиент догрузит карту.
            deadline = Now + 90f;
            while (!AllSessionsHaveAliveAvatar() && Now < deadline)
                yield return null;

            bool avatarOk = AllSessionsHaveAliveAvatar();
            result.Set(CheckAvatar, avatarOk,
                avatarOk
                    ? $"активная сцена='{SceneManager.GetActiveScene().name}', {DescribeSessions()}"
                    : $"за 90 с у сессии не появился живой аватар: {DescribeSessions()}. " +
                      "GameNetworkManager.OnServerReady спавнит аватар только для готового клиента.");

            if (!avatarOk)
            {
                result.Summary = "аватар не заспавнился, вердикт вынести нельзя";
                yield break;
            }

            PlayerSession session = FirstSession();

            // ── 4. Рукопожатие обратного канала ───────────────────────────
            // Прежде чем что-то измерять, убеждаемся, что клиент умеет достучаться
            // до сервера через свою сессию. Иначе «новая сессия не отозвалась»
            // после переподключения нельзя будет отличить от «канала не было вовсе».
            deadline = Now + 60f;
            while (!session.HasGrabbedDogTag && Now < deadline)
                yield return null;

            bool handshakeOk = session.HasGrabbedDogTag;
            result.Set(CheckHandshake, handshakeOk,
                handshakeOk
                    ? "клиент поднял флаг через CmdSetDogTagGrabbed — обратный канал жив"
                    : "за 60 с клиент не поднял флаг наблюдения. Без рабочего обратного канала " +
                      "контрольная проверка переподключения ничего не докажет.");

            if (!handshakeOk)
            {
                result.Summary = "обратный канал клиента не поднялся, вердикт вынести нельзя";
                yield break;
            }

            // ── 5. Сам менеджер восстановления ────────────────────────────
            // Проверка намеренно не прерывает прогон: даже когда менеджера нет,
            // контроль (переподключение) обязан отработать — иначе красный
            // результат ничего не различает.
            bool managerPlaced = SessionRecoveryManager.Instance != null;
            result.Set(CheckManager, managerPlaced,
                managerPlaced
                    ? $"SessionRecoveryManager.Instance есть на объекте " +
                      $"'{SessionRecoveryManager.Instance.gameObject.name}', снимков в хранилище: " +
                      $"{SessionRecoveryManager.Instance.StoredSnapshotsCount}"
                    : "SessionRecoveryManager.Instance == null. Компонент не размещён ни в одной сцене " +
                      "и ни в одном префабе: GUID ef1ead3edbbf84e42aafa57e8a4eabd8 встречается только " +
                      "в собственном .meta. Оба вызова из PlayersManager (SaveDisconnectedSession при " +
                      "отключении и GetAndRemoveSavedSession при подключении) обёрнуты в " +
                      "'if (SessionRecoveryManager.Instance != null)' и молча не выполняются. " +
                      "Это же зафиксировано в ManagerBootstrap.PersistentRoster: слот заведён " +
                      "необязательным с комментарием «нигде не размещён».");

            // ── 6. Набираем состояние ─────────────────────────────────────
            string deviceToken = session.DeviceToken;
            uint oldSessionNetId = session.netId;
            PlayerController avatar = session.ActiveAvatar;

            Vector3 positionBefore = avatar.transform.position;
            int markedTeamIndex = markedTeam != null ? markedTeam.teamIndex : 0;
            int markedAvatarIndex = markedTeam != null && markedTeam.avatars.Count > 1 ? 1 : 0;

            session.PlayerName = MarkedName;
            session.TeamIndex = markedTeamIndex;
            session.AvatarIndex = markedAvatarIndex;
            session.Kills = MarkedKills;
            session.Deaths = MarkedDeaths;
            session.Score = MarkedScore;

            avatar.RestoreHealth(MarkedHealth);
            avatar.transform.position = positionBefore + PositionOffset;

            // Даём кадрам пройти: позицию мог бы отыграть назад чужой код,
            // и тогда набранное состояние надо признать недействительным.
            float settle = Now + 2f;
            while (Now < settle)
                yield return null;

            Vector3 expectedPosition = avatar.transform.position;
            float expectedHealth = avatar.Health;
            Quaternion expectedRotation = avatar.transform.rotation;

            bool teamOk = markedTeamIndex > 0 && session.TeamIndex == markedTeamIndex;
            bool healthOk = Mathf.Abs(expectedHealth - MarkedHealth) < 1f;
            bool movedOk = Vector3.Distance(expectedPosition, positionBefore) > 1f;
            bool statsOk = session.Kills == MarkedKills && session.Deaths == MarkedDeaths &&
                           session.Score == MarkedScore && session.PlayerName == MarkedName;
            bool stateOk = teamOk && healthOk && movedOk && statsOk;

            result.Set(CheckStateSet, stateOk,
                (stateOk
                    ? "состояние набрано и держится на сервере"
                    : "набрать состояние не удалось, сравнивать после переподключения будет не с чем") +
                $" Имя='{session.PlayerName}' (ждали '{MarkedName}'), команда={session.TeamIndex} " +
                $"(ждали {markedTeamIndex}, ноль означал бы, что режим не отдал ни одной команды), " +
                $"скин={session.AvatarIndex}, K/D/S={session.Kills}/{session.Deaths}/{session.Score} " +
                $"(ждали {MarkedKills}/{MarkedDeaths}/{MarkedScore}), здоровье={expectedHealth:F1} " +
                $"(ждали {MarkedHealth:F0}), позиция {Fmt(positionBefore)} -> {Fmt(expectedPosition)} " +
                $"(сдвиг {Vector3.Distance(expectedPosition, positionBefore):F2} м).");

            if (!stateOk)
            {
                result.Summary = "состояние игрока набрать не удалось, вердикт вынести нельзя";
                yield break;
            }

            GameLog.Debug.Info($"[E2E] Состояние набрано для {deviceToken}: команда={markedTeamIndex}, " +
                              $"счёт={MarkedScore}, здоровье={expectedHealth:F0}, позиция={Fmt(expectedPosition)}");

            // ── 7. Разрыв соединения ──────────────────────────────────────
            // Рвёт сервер, а не клиент: так момент разрыва детерминирован и
            // приходится ровно на набранное состояние.
            NetworkConnectionToClient connection = session.connectionToClient;
            if (connection == null)
            {
                result.Set(CheckSaved, false, "у сессии нет connectionToClient — разорвать соединение нечем");
                result.Summary = "разрыв соединения выполнить нечем, вердикт вынести нельзя";
                yield break;
            }

            GameLog.Debug.Info($"[E2E] Рву соединение connId={connection.connectionId}");
            connection.Disconnect();

            deadline = Now + 60f;
            while (SessionCount() > 0 && Now < deadline)
                yield return null;

            if (SessionCount() > 0)
            {
                result.Set(CheckSaved, false,
                    $"за 60 с сессия не снялась с учёта после Disconnect(): {DescribeSessions()}. " +
                    "GameNetworkManager.OnServerDisconnect зовёт PlayersManager.UnregisterSession — " +
                    "именно там сохраняется снимок, и если его не вызвали, дальше проверять нечего.");
                result.Summary = "сервер не увидел отключения, вердикт вынести нельзя";
                yield break;
            }

            int storedSnapshots = SessionRecoveryManager.Instance != null
                ? SessionRecoveryManager.Instance.StoredSnapshotsCount
                : -1;

            bool savedOk = storedSnapshots >= 1;
            result.Set(CheckSaved, savedOk,
                savedOk
                    ? $"снимков в хранилище после разрыва: {storedSnapshots}"
                    : storedSnapshots < 0
                        ? "снимок сохранить было некому: SessionRecoveryManager.Instance == null. " +
                          "PlayersManager.UnregisterSession отработал (сессия снята с учёта), но его " +
                          "ветка сохранения целиком под 'if (SessionRecoveryManager.Instance != null)'. " +
                          "Ни здоровье, ни позиция, ни счёт никуда не записаны."
                        : "менеджер восстановления есть, но после разрыва в его хранилище ноль снимков — " +
                          "значит SaveDisconnectedSession не вызвали или он вышел на проверке аргументов.");

            // ── 8. Контроль: клиент возвращается ──────────────────────────
            // Ключевой контроль всего сценария. Если он красный, красные проверки
            // восстановления ниже ничего не доказывают: они означали бы «клиент
            // не вернулся», а не «состояние не восстановилось».
            deadline = Now + 180f;
            float nextConnectionReport = Now + 15f;

            while (FindSessionByToken(deviceToken, oldSessionNetId) == null && Now < deadline)
            {
                if (Now >= nextConnectionReport)
                {
                    GameLog.Debug.Info($"[E2E] Жду возврата клиента: {DescribeConnections()}, " +
                                      $"сцена сервера='{NetworkManager.networkSceneName}'");
                    nextConnectionReport = Now + 15f;
                }

                yield return null;
            }

            PlayerSession restored = FindSessionByToken(deviceToken, oldSessionNetId);
            if (restored == null)
            {
                result.Set(CheckReconnected, false,
                    $"за 180 с клиент с токеном '{deviceToken}' не вернулся. Сейчас на сервере: " +
                    $"{DescribeSessions()}; {DescribeConnections()}; " +
                    $"сцена сервера='{NetworkManager.networkSceneName}'. " +
                    "Возврат инициирует сам сценарий прямым StartClient, не дожидаясь " +
                    "UDP-броадкаста Discovery. Первое, что стоит проверить в client-1.log, — " +
                    "не завис ли клиент с isLoadingScene=true и выключенным NetworkManager: " +
                    "так выглядит возврат находки NET-20. " +
                    "Смотри client-1.log.");
                result.Summary = "клиент не переподключился — вердикт о восстановлении вынести нельзя";
                yield break;
            }

            // Живость новой сессии подтверждает сам клиент: поднимает флаг через
            // Command. Без этого «сессия есть» означало бы только, что сервер
            // создал объект, а не что связь работает.
            deadline = Now + 60f;
            while (!restored.HasGrabbedDogTag && Now < deadline)
                yield return null;

            bool reconnectedOk = restored.HasGrabbedDogTag;
            result.Set(CheckReconnected, reconnectedOk,
                (reconnectedOk
                    ? $"клиент вернулся тем же токеном '{deviceToken}': сессия netId {oldSessionNetId} -> " +
                      $"{restored.netId}, она отвечает на CmdSetDogTagGrabbed. " +
                      "То есть соединение и сессия работают — и всё, что красное ниже, относится " +
                      "именно к восстановлению состояния, а не к переподключению."
                    : $"сессия с токеном '{deviceToken}' появилась (netId {restored.netId}), но за 60 с " +
                      "не отозвалась на команду клиента — связь с ней неполноценная") +
                $" Содержимое новой сессии: имя='{restored.PlayerName}', команда={restored.TeamIndex}, " +
                $"скин={restored.AvatarIndex}, K/D/S={restored.Kills}/{restored.Deaths}/{restored.Score}.");

            if (!reconnectedOk)
            {
                result.Summary = "новая сессия не отвечает клиенту — вердикт о восстановлении вынести нельзя";
                yield break;
            }

            // ── 9. Что восстановилось ─────────────────────────────────────
            List<string> logicMismatch = new List<string>();
            if (restored.PlayerName != MarkedName) logicMismatch.Add($"имя '{restored.PlayerName}' вместо '{MarkedName}'");
            if (restored.TeamIndex != markedTeamIndex) logicMismatch.Add($"команда {restored.TeamIndex} вместо {markedTeamIndex}");
            if (restored.AvatarIndex != markedAvatarIndex) logicMismatch.Add($"скин {restored.AvatarIndex} вместо {markedAvatarIndex}");
            if (restored.Kills != MarkedKills) logicMismatch.Add($"убийства {restored.Kills} вместо {MarkedKills}");
            if (restored.Deaths != MarkedDeaths) logicMismatch.Add($"смерти {restored.Deaths} вместо {MarkedDeaths}");
            if (restored.Score != MarkedScore) logicMismatch.Add($"счёт {restored.Score} вместо {MarkedScore}");

            bool logicOk = logicMismatch.Count == 0;
            result.Set(CheckLogic, logicOk,
                logicOk
                    ? $"сессия вернулась со своим состоянием: имя='{restored.PlayerName}', " +
                      $"команда={restored.TeamIndex}, скин={restored.AvatarIndex}, " +
                      $"K/D/S={restored.Kills}/{restored.Deaths}/{restored.Score}"
                    : $"расхождений {logicMismatch.Count}: {string.Join("; ", logicMismatch.ToArray())}. " +
                      "Это значения по умолчанию новой сессии: PlayersManager.CreatePlayerSession " +
                      "заполняет их из snapshot, а snapshot == null, потому что " +
                      "GetAndRemoveSavedSession никто не звал — SessionRecoveryManager.Instance пуст.");

            // Аватар на восстановленной сессии появляется отдельным шагом: сначала
            // SpawnAvatar из HandlePlayerConnect, затем возможный ChangeAvatar из
            // OnServerReady, когда клиент догрузит карту.
            deadline = Now + 90f;
            while ((restored.ActiveAvatar == null || !restored.ActiveAvatar.IsAlive) && Now < deadline)
                yield return null;

            PlayerController restoredAvatar = restored.ActiveAvatar;
            if (restoredAvatar == null)
            {
                result.Set(CheckPhysical, false,
                    $"за 90 с у вернувшейся сессии не появился аватар: {DescribeSessions()}. " +
                    "Сравнивать здоровье и позицию не с чем.");
                result.Summary = "у вернувшегося игрока нет аватара — физическое состояние сравнить нечем";
                yield break;
            }

            // Даём аватару кадр-другой: NetworkTransform и гравитация успевают
            // сдвинуть его на доли метра, и сравнение «в лоб» было бы шумным.
            settle = Now + 2f;
            while (Now < settle)
                yield return null;

            Vector3 actualPosition = restoredAvatar.transform.position;
            float actualHealth = restoredAvatar.Health;
            float distance = Vector3.Distance(actualPosition, expectedPosition);

            bool healthRestored = Mathf.Abs(actualHealth - expectedHealth) < 1f;
            bool positionRestored = distance < PositionTolerance;
            bool physicalOk = healthRestored && positionRestored;

            result.Set(CheckPhysical, physicalOk,
                (physicalOk
                    ? "аватар вернулся туда же и с тем же здоровьем"
                    : "аватар заведён заново, как у нового игрока: " +
                      (healthRestored ? "" : $"здоровье {actualHealth:F1} вместо {expectedHealth:F1}; ") +
                      (positionRestored ? "" : $"позиция {Fmt(actualPosition)} вместо {Fmt(expectedPosition)}, " +
                                               $"промах {distance:F2} м; ") +
                      "AvatarManager.SpawnAvatar берёт позицию из snapshot только при " +
                      "snapshot.NeedsPhysicalRestore, а snapshot здесь null") +
                $" Ждали здоровье={expectedHealth:F1}, позицию={Fmt(expectedPosition)}, " +
                $"поворот={expectedRotation.eulerAngles.y:F0}°; получили здоровье={actualHealth:F1}, " +
                $"позицию={Fmt(actualPosition)}, поворот={restoredAvatar.transform.rotation.eulerAngles.y:F0}°.");

            result.Summary = result.AllChecksGreen
                ? "переподключение по тому же deviceToken возвращает игроку его состояние"
                : !managerPlaced
                    ? "ARCH-01 подтверждена: клиент переподключился и получил новую пустую сессию — " +
                      "SessionRecoveryManager не размещён в проекте, снимок никто не сохранял"
                    : "есть красные проверки, см. detail";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента — целиком контроль переподключения
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap,
                           CheckClientDropped, CheckClientReconnected);

            DisableDebugOrchestrator();

            // ── Подключение ───────────────────────────────────────────────
            float deadline = Now + 30f;
            while (!NetworkClient.isConnected && Now < deadline)
                yield return null;

            if (!NetworkClient.isConnected && !string.IsNullOrEmpty(context.ServerAddress))
            {
                GameLog.Debug.Info($"[E2E] Discovery молчит 30 с, подключаюсь напрямую к {context.ServerAddress}");
                ConnectDirectly(context.ServerAddress);

                deadline = Now + 30f;
                while (!NetworkClient.isConnected && Now < deadline)
                    yield return null;
            }

            bool connected = NetworkClient.isConnected;
            result.Set(CheckClientConnected, connected,
                connected
                    ? $"подключён к {NetworkManager.singleton?.networkAddress}, " +
                      $"deviceToken='{PlayerPrefs.GetString("DeviceToken", "(нет)")}'"
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

            uint firstSessionNetId = local.netId;

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

            // ── Рукопожатие: сервер должен увидеть, что клиент на связи ────
            yield return RaiseFlagWhenSessionAlive(firstSessionNetId, sameNetIdAllowed: true, budgetSeconds: 45f);

            // ── Разрыв ────────────────────────────────────────────────────
            // Рвёт сервер. Клиенту остаётся заметить разрыв и вернуться.
            // Текущий менеджер запоминаем заранее: по его смене видно, что Mirror
            // довёл до конца весь путь отключения (см. ниже).
            NetworkManager managerBeforeDrop = NetworkManager.singleton;

            deadline = Now + 150f;
            while (NetworkClient.isConnected && Now < deadline)
                yield return null;

            bool dropped = !NetworkClient.isConnected;
            result.Set(CheckClientDropped, dropped,
                dropped
                    ? "соединение разорвано сервером — клиент это увидел"
                    : "за 150 с сервер так и не разорвал соединение: клиент всё ещё подключён. " +
                      "Проверять переподключение нечем — смотри server.json.");

            if (!dropped)
            {
                result.Summary = "разрыва не было, контроль переподключения не выполнен";
                yield break;
            }

            // ── Возврат ───────────────────────────────────────────────────
            // После разрыва Mirror уводит клиента в сцену Offline, а её копия
            // префаба менеджеров поднимает новый GameNetworkDiscovery, который
            // в билде сам выбирает роль Client и снова начинает искать сервер.
            // До исправления NET-20 этого не происходило: дубликат ветки гасили
            // прямо в Awake, и «спасшийся» объект Mirror оставался выключенным —
            // ни Discovery, ни LateUpdate менеджера не работали.
            //
            // Сценарий всё равно подключается сам, прямым StartClient: ждать
            // UDP-броадкаст дольше и менее детерминированно, а проверяем мы здесь
            // восстановление сессии, а не работу Discovery. ConnectDirectly
            // останавливает Discovery, поэтому два пути не гоняются наперегонки.
            //
            // deviceToken при этом не меняется: он лежит в PlayerPrefs
            // с самого старта харнесса, а GameNetworkManager.SendConnectMessage
            // читает его при каждом подключении.

            // Подключаться сразу нельзя. После разрыва Mirror делает это по шагам:
            // NetworkClient.Shutdown() -> перенос NetworkManager из DontDestroyOnLoad
            // в текущую сцену -> загрузка сцены Offline, которая этот менеджер
            // уничтожает и поднимает новый из своей копии префаба «--- MANAGERS ---».
            // StartClient, вызванный в промежутке, уходит в уже обречённый менеджер:
            // connectState становится Connecting, менеджер умирает вместе со своим
            // транспортом, а NetworkClient статический — Connecting остаётся навсегда,
            // и каждый следующий StartClient молча выходит по «Client already started».
            // Проверено прогоном: ровно так вело себя первое исполнение сценария.
            // Поэтому ждём смены экземпляра менеджера и окончания загрузки сцены.
            deadline = Now + 45f;
            bool managerReplaced = false;

            while (Now < deadline)
            {
                managerReplaced = NetworkManager.singleton != null &&
                                  !ReferenceEquals(NetworkManager.singleton, managerBeforeDrop);

                if (managerReplaced && NetworkManager.loadingSceneAsync == null && !NetworkClient.active)
                    break;

                yield return null;
            }

            // Сцена Offline подняла свою копию менеджеров — дирижёр отладки среди них.
            DisableDebugOrchestrator();

            GameLog.Debug.Info($"[E2E] Разрыв обработан (менеджер сменился={managerReplaced}), " +
                              $"начинаю возврат. {DescribeNetwork()}");

            ReportClientStateAfterOfflineScene();

            deadline = Now + 60f;
            float nextAttempt = 0f;
            float nextReport = Now + 10f;
            float connectingSince = -1f;

            while (!NetworkClient.isConnected && Now < deadline)
            {
                if (!NetworkClient.active)
                {
                    connectingSince = -1f;

                    if (Now >= nextAttempt)
                    {
                        ConnectDirectly(context.ServerAddress);
                        nextAttempt = Now + 10f;
                    }
                }
                else if (connectingSince < 0f)
                {
                    connectingSince = Now;
                }
                else if (Now - connectingSince > 20f)
                {
                    // Страховка от того же зависания: если connectState завис
                    // в Connecting, сбрасываем клиента и пробуем заново.
                    GameLog.Debug.Warning("[E2E] connectState завис в Connecting — сбрасываю NetworkClient.Shutdown()");
                    NetworkClient.Shutdown();
                    connectingSince = -1f;
                    nextAttempt = 0f;
                }

                if (Now >= nextReport)
                {
                    GameLog.Debug.Info($"[E2E] Возврат в процессе: {DescribeNetwork()}");
                    nextReport = Now + 10f;
                }

                yield return null;
            }

            bool reconnected = NetworkClient.isConnected;
            GameLog.Debug.Info($"[E2E] Итог возврата: подключён={reconnected}. {DescribeNetwork()}");

            deadline = Now + 60f;
            nextReport = Now + 15f;

            while (!HasFreshSession(firstSessionNetId) && Now < deadline)
            {
                if (Now >= nextReport)
                {
                    GameLog.Debug.Info($"[E2E] Жду новую сессию: {DescribeNetwork()}");
                    nextReport = Now + 15f;
                }

                yield return null;
            }

            bool freshSession = HasFreshSession(firstSessionNetId);
            if (freshSession)
                yield return RaiseFlagWhenSessionAlive(firstSessionNetId, sameNetIdAllowed: false, budgetSeconds: 50f);

            PlayerSession fresh = PlayerSession.LocalSession;
            result.Set(CheckClientReconnected, freshSession,
                freshSession
                    ? $"клиент вернулся прямым подключением к '{context.ServerAddress}' и получил новую сессию: " +
                      $"netId {firstSessionNetId} -> {fresh.netId}, имя='{fresh.PlayerName}', " +
                      $"команда={fresh.TeamIndex}, K/D/S={fresh.Kills}/{fresh.Deaths}/{fresh.Score}"
                    : "после разрыва клиент так и не получил новую сессию. " +
                      $"{DescribeNetwork()}. Соединение восстановлено={reconnected}.");

            result.Summary = result.AllChecksGreen
                ? "клиент отключился и вернулся тем же устройством — переподключение работает; " +
                  "что именно вернулось в сессии, судит сервер"
                : "контроль переподключения не выполнен, вердикт сервера о восстановлении недостоверен";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное — клиент
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        ///     Есть ли у клиента живая сессия, отличная от прежней. Сравнение по netId,
        ///     а не по ссылке: старый объект к этому моменту уже уничтожен Mirror.
        /// </summary>
        private static bool HasFreshSession(uint previousNetId)
        {
            PlayerSession local = PlayerSession.LocalSession;
            return local != null && local.netId != previousNetId;
        }

        /// <summary>
        ///     Поднимает флаг обратного канала и добивается подтверждения эхом.
        ///     Флаг — <c>PlayerSession.HasGrabbedDogTag</c>: он не входит в снимок
        ///     восстановления, поэтому не мешает проверяемым полям.
        ///
        ///     <para>
        ///     Почему цикл, а не один вызов. Команда, отправленная до
        ///     <c>NetworkClient.Ready()</c>, <b>молча теряется</b>: Mirror пишет
        ///     «Command … called while NetworkClient is not ready» и наружу ничего
        ///     не отдаёт. А активная сцена карты становится текущей раньше, чем Mirror
        ///     успевает объявить клиента готовым: <c>NetworkClient.Ready()</c> зовётся
        ///     из <c>FinishLoadSceneClientOnly</c>, то есть уже после
        ///     <c>SceneManager.GetActiveScene()</c>. Проверено прогоном: единственный
        ///     вызов сразу после проверки сцены до сервера не доехал.
        ///     Поэтому шлём повторно, пока SyncVar не вернётся эхом.
        ///     </para>
        /// </summary>
        private IEnumerator RaiseFlagWhenSessionAlive(uint previousNetId, bool sameNetIdAllowed, float budgetSeconds)
        {
            float deadline = Now + budgetSeconds;
            float nextSend = 0f;
            uint reportedNetId = 0;

            while (Now < deadline)
            {
                PlayerSession local = PlayerSession.LocalSession;

                if (local != null && (sameNetIdAllowed || local.netId != previousNetId))
                {
                    if (local.HasGrabbedDogTag)
                    {
                        GameLog.Debug.Info($"[E2E] Флаг обратного канала подтверждён эхом на сессии netId={local.netId}");
                        yield break;
                    }

                    if (NetworkClient.ready && Now >= nextSend)
                    {
                        local.CmdSetDogTagGrabbed(true);
                        nextSend = Now + 1f;

                        if (reportedNetId != local.netId)
                        {
                            GameLog.Debug.Info($"[E2E] Клиент поднимает флаг обратного канала на сессии netId={local.netId}");
                            reportedNetId = local.netId;
                        }
                    }
                }

                yield return null;
            }

            GameLog.Debug.Warning($"[E2E] За {budgetSeconds:F0} с эхо флага обратного канала не пришло " +
                                 $"(NetworkClient.ready={NetworkClient.ready})");
        }

        /// <summary>
        ///     Снимок состояния клиента сразу после возврата в сцену <c>Offline</c>.
        ///
        ///     <para>
        ///     Метод <b>ничего не чинит</b> — только пишет в лог. Здесь был обход
        ///     находки <b>NET-20</b>: сценарий руками включал объект
        ///     <c>NetworkManager</c> и сбрасывал <c>NetworkClient.isLoadingScene</c>,
        ///     иначе клиент не разбирал входящие сообщения и переподключиться
        ///     не мог в принципе. Обход снят: возврат в строй после разрыва —
        ///     ответственность игрового кода, а не харнесса, и именно это утверждение
        ///     сценарий теперь и проверяет.
        ///     </para>
        ///
        ///     <para>
        ///     Диагностику оставили: если переподключение снова сломается, первая
        ///     же строка лога покажет, в каком из трёх состояний застрял клиент —
        ///     менеджера нет, менеджер выключен, или флаг загрузки сцены не снят.
        ///     </para>
        /// </summary>
        private void ReportClientStateAfterOfflineScene()
        {
            NetworkManager manager = NetworkManager.singleton;

            if (manager == null)
                GameLog.Debug.Warning("[E2E] После возврата в Offline NetworkManager.singleton пуст.");
            else if (!manager.isActiveAndEnabled)
                GameLog.Debug.Warning($"[E2E] После возврата в Offline NetworkManager '{manager.gameObject.name}' " +
                                     "неактивен: его LateUpdate не вызывается, значит UpdateScene никогда " +
                                     "не закроет загрузку сцены (NET-20).");

            if (NetworkClient.isLoadingScene || NetworkManager.loadingSceneAsync != null)
                GameLog.Debug.Warning("[E2E] После возврата в Offline у клиента не снят признак загрузки сцены " +
                                     $"(isLoadingScene={NetworkClient.isLoadingScene}, " +
                                     $"loadingSceneAsync={(NetworkManager.loadingSceneAsync != null)}). " +
                                     "При взведённом флаге NetworkClient.OnTransportData не разбирает входящие " +
                                     "сообщения вовсе — переподключение невозможно (NET-20).");

            GameLog.Debug.Info($"[E2E] Состояние клиента после возврата в Offline: {DescribeNetwork()}");
        }

        /// <summary>Состояние сети клиента одной строкой — единственный способ разобрать зависший возврат по логу.</summary>
        private static string DescribeNetwork()
        {
            NetworkManager manager = NetworkManager.singleton;
            return $"NetworkManager={(manager == null ? "null" : manager.mode.ToString())}, " +
                   $"менеджер живой={(manager == null ? "нет" : manager.isActiveAndEnabled.ToString())}, " +
                   $"active={NetworkClient.active}, connected={NetworkClient.isConnected}, " +
                   $"ready={NetworkClient.ready}, авторизован=" +
                   $"{(NetworkClient.connection == null ? "нет соединения" : NetworkClient.connection.isAuthenticated.ToString())}, " +
                   $"адрес='{(manager == null ? "-" : manager.networkAddress)}', " +
                   $"networkSceneName='{NetworkManager.networkSceneName}', " +
                   $"isLoadingScene={NetworkClient.isLoadingScene}, " +
                   $"грузится={(NetworkManager.loadingSceneAsync != null)}, " +
                   $"LocalSession={(PlayerSession.LocalSession == null ? "null" : PlayerSession.LocalSession.netId.ToString())}, " +
                   $"сцена='{SceneManager.GetActiveScene().name}'";
        }

        /// <summary>Прямое подключение мимо Discovery — фоллбэк, когда UDP-броадкаст не доехал.</summary>
        private void ConnectDirectly(string address)
        {
            Mirror.Discovery.NetworkDiscovery discovery = Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>();
            if (discovery != null)
                discovery.StopDiscovery();

            if (NetworkManager.singleton == null)
            {
                GameLog.Error("[E2E] NetworkManager.singleton пуст — подключиться нечем. " +
                              "Сцена Offline не поднялась после разрыва?");
                return;
            }

            if (NetworkClient.active)
                return;

            if (!string.IsNullOrEmpty(address))
                NetworkManager.singleton.networkAddress = address;

            if (string.IsNullOrWhiteSpace(NetworkManager.singleton.networkAddress))
            {
                GameLog.Error("[E2E] Адрес сервера не задан ни аргументом -e2eServerAddress, ни на менеджере.");
                return;
            }

            GameLog.Debug.Info(
                $"[E2E] Подключаюсь к '{NetworkManager.singleton.networkAddress}'");
            NetworkManager.singleton.StartClient();
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное — сервер
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

        private static PlayerSession FirstSession()
        {
            if (PlayersManager.Instance == null) return null;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null) return session;
            }

            return null;
        }

        /// <summary>Сессия того же устройства, но не та, что была до разрыва.</summary>
        private static PlayerSession FindSessionByToken(string deviceToken, uint excludeNetId)
        {
            if (PlayersManager.Instance == null) return null;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || session.netId == excludeNetId) continue;
                if (session.DeviceToken == deviceToken) return session;
            }

            return null;
        }

        private static bool AllSessionsHaveAliveAvatar()
        {
            if (PlayersManager.Instance == null || PlayersManager.Instance.Sessions.Count == 0)
                return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || session.ActiveAvatar == null || !session.ActiveAvatar.IsAlive)
                    return false;
            }

            return true;
        }

        /// <summary>Соединения Mirror на сервере — без них не отличить «клиент не пришёл» от «пришёл, но молчит».</summary>
        private static string DescribeConnections()
        {
            List<string> parts = new List<string>();
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn == null) continue;

                parts.Add($"connId={conn.connectionId}(авторизован={conn.isAuthenticated}, готов={conn.isReady}, " +
                          $"объект игрока={(conn.identity == null ? "нет" : conn.identity.netId.ToString())})");
            }

            return $"соединений {NetworkServer.connections.Count}" +
                   (parts.Count > 0 ? ": " + string.Join(", ", parts.ToArray()) : "");
        }

        private static string DescribeSessions()
        {
            if (PlayersManager.Instance == null) return "(нет PlayersManager)";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                PlayerController avatar = session.ActiveAvatar;
                parts.Add($"{session.PlayerName}(netId={session.netId}, токен='{session.DeviceToken}', " +
                          $"команда={session.TeamIndex}) — " +
                          (avatar == null
                              ? "аватара нет"
                              : $"аватар netId={avatar.netId}, здоровье={avatar.Health:F0}, " +
                                $"позиция={Fmt(avatar.transform.position)}"));
            }

            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "(нет сессий)";
        }

        /// <summary>
        ///     Ставит всем сессиям первую команду режима. Нужно не ради матча (он здесь
        ///     не запускается), а ради спавна аватара после смены карты: см. комментарий
        ///     в месте вызова.
        /// </summary>
        private static string AssignFirstTeam(SessionManager sessionManager)
        {
            GameModeData mode = sessionManager.SelectedGameModeData;
            if (mode == null || mode.teams == null || mode.teams.Length == 0 || mode.teams[0] == null)
                return "режим не отдал ни одной команды — команды не назначены";

            TeamData team = mode.teams[0];
            List<string> report = new List<string>();

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                session.TeamIndex = team.teamIndex;
                report.Add($"{session.PlayerName}->{team.displayName}({team.teamIndex})");
            }

            return report.Count > 0 ? string.Join(", ", report.ToArray()) : "(нет сессий)";
        }

        /// <summary>
        ///     Последняя команда режима. Берётся именно последняя, а не первая:
        ///     первую сессии получают перед загрузкой карты, и совпадение сделало бы
        ///     проверку восстановления команды бессодержательной.
        /// </summary>
        private static TeamData LastTeamOf(SessionManager sessionManager)
        {
            GameModeData mode = sessionManager.SelectedGameModeData;
            if (mode == null || mode.teams == null || mode.teams.Length == 0)
                return null;

            for (int i = mode.teams.Length - 1; i >= 0; i--)
            {
                if (mode.teams[i] != null) return mode.teams[i];
            }

            return null;
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
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
