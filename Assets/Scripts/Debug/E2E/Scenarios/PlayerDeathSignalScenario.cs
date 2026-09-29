// Ярус C (два процесса) — сценарий player-death-signal: остаток находки NET-04.
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
    /// Сценарий <c>player-death-signal</c> — остаток находки <b>NET-04</b>.
    ///
    /// <para>
    /// <b>Что доказывает.</b> Мёртвый игрок обязан узнать о своей смерти в момент
    /// гибели: по событию <c>PlayerController.PlayerDied</c> обновляет видимость
    /// <c>TeamSpawnZone</c>, а <c>PlayerGrabManager</c> роняет предметы из рук.
    /// Событие поднималось внутри <c>Die()</c>, помеченного <c>[Server]</c>, — на
    /// выделенном сервере у клиента этот метод заглушка, и до подписчиков не доходило
    /// ничего. Мёртвый игрок видел свою зону не в момент гибели, а на ближайшей смене
    /// фазы раунда. На хосте не воспроизводится: там <c>Die()</c> исполняется
    /// по-настоящему.
    /// </para>
    ///
    /// <para>
    /// <b>Смерть здесь настоящая</b>, а не выставленный <c>Life = 0</c>. Сервер бьёт
    /// жертву через <c>UxrActor.ReceiveDamage</c> — тот же вход, которым пользуется
    /// оружие. Разница принципиальна: смертельный урон приводит к <c>DieInternal</c>,
    /// а его канал состояния UltimateXR переигрывает у клиента, и там поднимается
    /// <c>UxrActor.Died</c>. Именно из него растёт вся цепочка уведомлений. Соседний
    /// сценарий <c>avatar-swap-death-replication</c> ходит мимо <c>Died</c> намеренно:
    /// ему нужен голый факт доставки <c>Life</c> по каналу, и он ставит <c>Life = 0</c>
    /// свойством.
    /// </para>
    ///
    /// <para>
    /// <b>Устройство проверки.</b> Клиент подписывается на <c>PlayerDied</c> своего
    /// аватара и поднимает обратный флаг. Сервер, увидев флаг, наносит смертельный
    /// урон. Клиент проверяет две вещи подряд: доехало ли само состояние
    /// (<c>IsAlive</c> стало false — это контроль, он же канал состояния) и пришло ли
    /// уведомление (<c>PlayerDied</c>). Контроль зелёный при красном уведомлении —
    /// это и есть находка в чистом виде: состояние есть, сигнала нет.
    /// </para>
    ///
    /// <para>
    /// Матч не запускается: зона спавна для вердикта не нужна, она лишь подписчик.
    /// Проверяется то, от чего зависит она и всё остальное, — доходит ли до клиента
    /// сам сигнал смерти.
    /// </para>
    /// </summary>
    public class PlayerDeathSignalScenario : IE2EScenario
    {
        public string Name => "player-death-signal";

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated  = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients    = "клиент подключился и сервер создал сессию";
        private const string CheckAvatar     = "карта загружена, у сессии есть живой аватар";
        private const string CheckArmed      = "клиент отчитался, что подписан на смерть своего аватара";
        private const string CheckServerDied = "смертельный урон убил жертву на сервере";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientAvatar    = "у клиента есть свой аватар, подписка на смерть поставлена";
        private const string CheckClientLife      = "контроль: состояние смерти доехало до клиента (IsAlive=false)";
        private const string CheckClientEvent     = "клиент получил событие PlayerDied в момент гибели (NET-04)";

        // ── Сроки ─────────────────────────────────────────────────────────

        /// <summary>Сколько сервер ждёт отчёта «подписка поставлена».</summary>
        private const float ArmedWait = 120f;

        /// <summary>Сколько клиент ждёт, что состояние смерти доедет до него.</summary>
        private const float DeathStateWait = 30f;

        /// <summary>
        /// Сколько клиент ждёт события <c>PlayerDied</c> после того, как состояние
        /// уже доехало. Событие обязано подняться в том же кадре, что и смена
        /// состояния: и то и другое рождается внутри одного <c>DieInternal</c>.
        /// Пять секунд — заведомо избыточный запас, чтобы красный вердикт нельзя
        /// было списать на медленную доставку.
        /// </summary>
        private const float DeathEventWait = 5f;

        /// <summary>Сколько сервер ждёт, пока клиент запишет вердикт, прежде чем гасить процесс.</summary>
        private const float ClientVerdictWait = 45f;

        // ── Наблюдение клиента ────────────────────────────────────────────

        /// <summary>Сколько раз локальный аватар поднял <c>PlayerDied</c> на этой машине.</summary>
        private int _diedEvents;

        /// <summary>Реальное время первого <c>PlayerDied</c>, или -1.</summary>
        private float _diedEventTime = -1f;

        /// <summary>Аватар, на который поставлена подписка.</summary>
        private PlayerController _watchedAvatar;


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
            result.Declare(CheckDedicated, CheckClients, CheckAvatar, CheckArmed, CheckServerDied);

            float deadline = Now + 60f;
            while (!NetworkServer.active && Now < deadline)
                yield return null;

            bool dedicated = NetworkServer.active && !NetworkClient.active;
            result.Set(CheckDedicated, dedicated,
                dedicated
                    ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                    : $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}. " +
                      "На хосте находка не воспроизводится: там Die() исполняется по-настоящему. " +
                      "Сервер обязан идти с -batchmode -nographics.");

            if (!dedicated)
            {
                result.Summary = "конфигурация не выделенный сервер, прогон недействителен";
                yield break;
            }

            DisableDebugOrchestrator();

            deadline = Now + 90f;
            while (SessionCount() < context.ExpectedClients && Now < deadline)
                yield return null;

            int sessions = SessionCount();
            bool clientsOk = sessions >= context.ExpectedClients;
            result.Set(CheckClients, clientsOk,
                clientsOk
                    ? $"сессий на сервере: {sessions} (ждали {context.ExpectedClients})"
                    : $"за 90 с подключилось сессий: {sessions} из {context.ExpectedClients}");

            if (!clientsOk)
            {
                result.Summary = "клиент не подключился, убивать некого";
                yield break;
            }

            SessionManager sessionManager = SessionManager.Instance;
            if (sessionManager == null || MapLoader.Instance == null)
            {
                result.Set(CheckAvatar, false,
                    $"SessionManager.Instance={(sessionManager == null ? "null" : "есть")}, " +
                    $"MapLoader.Instance={(MapLoader.Instance == null ? "null" : "есть")}");
                result.Summary = "менеджеры не поднялись, прогон недействителен";
                yield break;
            }

            // Режим выбирается не ради матча (матч здесь не запускается), а ради
            // валидных индексов команд: AvatarManager.ChangeAvatar молча выходит,
            // если TeamRegistry не знает индекс команды сессии, и аватар не спавнится.
            sessionManager.SetSession(context.Map, "elimination");
            yield return null;

            GameLog.Debug.Info($"[E2E] Команды распределены: {AssignTeams(sessionManager)}");

            MapLoader.Instance.LoadMap(context.Map);

            deadline = Now + 120f;
            while (SceneManager.GetActiveScene().name != context.Map && Now < deadline)
                yield return null;

            PlayerSession victim = null;
            deadline = Now + 90f;
            while (Now < deadline)
            {
                victim = FirstSessionWithAvatar();
                if (victim != null) break;

                yield return null;
            }

            bool avatarOk = victim != null && victim.ActiveAvatar != null && victim.ActiveAvatar.IsAlive;
            result.Set(CheckAvatar, avatarOk,
                avatarOk
                    ? $"сцена='{SceneManager.GetActiveScene().name}', жертва {victim.PlayerName}: " +
                      $"аватар netId={victim.ActiveAvatar.netId}, Life={victim.ActiveAvatar.Health:F0}"
                    : $"за 90 с на сервере не нашлось сессии с живым аватаром. Сцена='{SceneManager.GetActiveScene().name}', " +
                      $"сессий: {SessionCount()}. {DescribeSessions()}");

            if (!avatarOk)
            {
                result.Summary = "живого аватара нет, вердикт по NET-04 вынести нельзя";
                yield break;
            }

            // ── Отмашка и отчёт клиента о готовности ──────────────────────
            E2EWaitOutcome armed = new E2EWaitOutcome();
            yield return E2EWait.Until(armed,
                "клиент отчитался, что подписался на смерть своего аватара",
                ArmedWait,
                () => ReportedFlags() >= context.ExpectedClients,
                () => $"отчитались {ReportedFlags()} из {context.ExpectedClients}; {DescribeSessions()}",
                () => NetworkServer.connections.Count > 0
                    ? null
                    : "на сервере не осталось подключений — отчитываться уже некому");

            result.Set(CheckArmed, armed.Succeeded, armed.Diagnosis);

            if (!armed.Succeeded)
            {
                result.Summary = "клиент не подтвердил подписку, убивать рано";
                yield break;
            }

            // ── Настоящий смертельный урон ────────────────────────────────
            PlayerController avatar = victim.ActiveAvatar;
            bool serverEventSeen = false;
            System.Action<PlayerController> onServerDied = _ => serverEventSeen = true;
            avatar.PlayerDied += onServerDied;

            GameLog.Debug.Info(
                $"[E2E] Бью жертву {victim.PlayerName} насмерть: ReceiveDamage(1000). Life={avatar.Health:F0}");

            // Именно ReceiveDamage, а не RestoreHealth(0): смертельный урон уводит
            // UxrActor в DieInternal, а тот поднимает Died и уезжает каналом состояния.
            // Присвоение Life = 0 доставило бы клиенту только число.
            avatar._actor.ReceiveDamage(1000f);

            yield return null;

            bool serverDead = !avatar.IsAlive;
            avatar.PlayerDied -= onServerDied;

            result.Set(CheckServerDied, serverDead && serverEventSeen,
                serverDead && serverEventSeen
                    ? $"жертва мертва на сервере: Life={avatar.Health:F0}, PlayerDied на сервере сработало"
                    : !serverDead
                        ? $"после ReceiveDamage(1000) жертва всё ещё жива: Life={avatar.Health:F0}. " +
                          "UxrActor не принял урон — проверь AutomaticDamageHandling и AutomaticDeadHandling на префабе аватара."
                        : $"жертва мертва (Life={avatar.Health:F0}), но PlayerDied не сработало даже на сервере — " +
                          "цепочка UxrActor.Died -> PlayerController оборвана на самом сервере.");

            yield return WaitForClientVerdicts(context, avatar);

            result.Summary = result.AllChecksGreen
                ? "жертва убита настоящим уроном, клиентский вердикт см. в client-1.json"
                : "есть красные проверки, см. detail";
        }

        /// <summary>
        /// Держит серверный процесс живым, пока клиент не запишет свой вердикт.
        /// Гасить сервер сразу нельзя: обрыв связи посреди клиентских ожиданий
        /// выглядит как «сигнал не пришёл» и даёт мигающие ворота (TEST-01).
        /// </summary>
        private static IEnumerator WaitForClientVerdicts(E2EContext context, PlayerController victim)
        {
            E2EWaitOutcome barrier = new E2EWaitOutcome();

            yield return E2EWait.Until(barrier,
                $"все {context.ExpectedClients} клиент(а) отчитались, что записали вердикт",
                ClientVerdictWait,
                () => ReportedFlags() == 0 && SessionCount() >= context.ExpectedClients,
                () => $"держат флаг: {ReportedFlags()} из {SessionCount()} сессий; " +
                      $"подключений: {NetworkServer.connections.Count}; " +
                      $"жертва: Life={(victim != null ? victim.Health.ToString("F0") : "нет аватара")}");

            GameLog.Debug.Info($"[E2E] Барьер клиентских вердиктов: {barrier.Diagnosis}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap,
                           CheckClientAvatar, CheckClientLife, CheckClientEvent);

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
                result.Summary = "клиент не на карте, аватара ждать негде";
                yield break;
            }

            // ── Свой аватар и подписка на смерть ─────────────────────────
            deadline = Now + 90f;
            while (Now < deadline)
            {
                if (local.ActiveAvatar != null && local.ActiveAvatar.IsAlive)
                    break;

                yield return null;
            }

            _watchedAvatar = local.ActiveAvatar;
            bool avatarOk = _watchedAvatar != null;

            if (avatarOk)
                _watchedAvatar.PlayerDied += HandlePlayerDied;

            result.Set(CheckClientAvatar, avatarOk,
                avatarOk
                    ? $"аватар netId={_watchedAvatar.netId}, Life={_watchedAvatar.Health:F0}, " +
                      $"IsAlive={_watchedAvatar.IsAlive}; подписка на PlayerDied поставлена"
                    : "за 90 с у клиента не появился свой аватар (PlayerSession.ActiveAvatar == null). " +
                      "Проверять доставку сигнала смерти не на чем.");

            if (!avatarOk)
            {
                result.Summary = "у клиента нет аватара";
                ReportFlag(true);
                yield return E2EWait.Hold(1f);
                ReportFlag(false);
                yield break;
            }

            // Флаг поднимается только теперь: сервер бьёт жертву сразу после него,
            // и подписка обязана быть на месте раньше удара.
            ReportFlag(true);

            // ── Контроль: доехало ли само состояние ──────────────────────
            E2EWaitOutcome lifeGone = new E2EWaitOutcome();
            yield return E2EWait.Until(lifeGone,
                "состояние смерти доехало до клиента (IsAlive стало false)",
                DeathStateWait,
                () => _watchedAvatar == null || !_watchedAvatar.IsAlive,
                () => DescribeAvatar(),
                () => NetworkClient.isConnected
                    ? null
                    : "связь с сервером пропала — состояние сюда уже не приедет");

            result.Set(CheckClientLife, lifeGone.Succeeded,
                lifeGone.Succeeded
                    ? lifeGone.Diagnosis + " Канал состояния UltimateXR работает — значит красное событие " +
                      "ниже это не обрыв канала, а отсутствие самого уведомления."
                    : lifeGone.Diagnosis + " До клиента не доехало даже здоровье: канал состояния мёртв, " +
                      "и вердикт по NET-04 вынести нельзя. Сверься с avatar-swap-death-replication.");

            // ── Само уведомление ─────────────────────────────────────────
            E2EWaitOutcome diedEvent = new E2EWaitOutcome();
            yield return E2EWait.Until(diedEvent,
                "локальный аватар поднял PlayerController.PlayerDied",
                DeathEventWait,
                () => _diedEvents > 0,
                () => DescribeAvatar(),
                () => NetworkClient.isConnected
                    ? null
                    : "связь с сервером пропала");

            _watchedAvatar.PlayerDied -= HandlePlayerDied;

            result.Set(CheckClientEvent, diedEvent.Succeeded,
                diedEvent.Succeeded
                    ? $"PlayerDied сработало {_diedEvents} раз(а) через {_diedEventTime:F2} с после подписки. " +
                      "Мёртвый игрок узнаёт о гибели на своей машине — TeamSpawnZone обновит видимость сразу, " +
                      "а не на ближайшей смене фазы раунда."
                    : diedEvent.Diagnosis + " Это остаток NET-04: PlayerDied поднимается внутри Die(), " +
                      "помеченного [Server], и на выделенном сервере у клиента этот метод — заглушка. " +
                      "Состояние приехало (проверка выше зелёная), а уведомления нет: " +
                      "TeamSpawnZone узнает о смерти только на следующей смене фазы раунда.");

            result.Summary = result.AllChecksGreen
                ? "клиент увидел и состояние смерти, и уведомление о ней"
                : lifeGone.Succeeded && !diedEvent.Succeeded
                    ? "остаток NET-04 подтверждён: состояние смерти доезжает, событие PlayerDied — нет"
                    : "есть красные проверки, см. detail";

            ReportFlag(false);
        }

        private void HandlePlayerDied(PlayerController player)
        {
            _diedEvents++;
            if (_diedEventTime < 0f)
                _diedEventTime = Now;

            GameLog.Debug.Info($"[E2E] PlayerDied получено для '{player.name}'");
        }

        private string DescribeAvatar()
        {
            if (_watchedAvatar == null)
                return "аватар уничтожен";

            return $"Life={_watchedAvatar.Health:F0}, IsAlive={_watchedAvatar.IsAlive}, " +
                   $"событий PlayerDied={_diedEvents}, связь={(NetworkClient.isConnected ? "есть" : "нет")}";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

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

        private static PlayerSession FirstSessionWithAvatar()
        {
            if (PlayersManager.Instance == null)
                return null;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.ActiveAvatar != null && session.ActiveAvatar.IsAlive)
                    return session;
            }

            return null;
        }

        private static string DescribeSessions()
        {
            if (PlayersManager.Instance == null)
                return "PlayersManager отсутствует";

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                if (builder.Length > 0) builder.Append(", ");
                builder.Append(session.PlayerName)
                       .Append(": аватар=")
                       .Append(session.ActiveAvatar == null
                           ? "нет"
                           : $"netId={session.ActiveAvatar.netId}, Life={session.ActiveAvatar.Health:F0}")
                       .Append(", флаг=")
                       .Append(session.HasGrabbedDogTag ? "поднят" : "опущен");
            }

            return builder.Length == 0 ? "сессий нет" : builder.ToString();
        }

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
        /// Обратный канал клиент → сервер. <c>HasGrabbedDogTag</c> — единственное поле
        /// сессии, которое клиент вправе менять командой; матч в этом сценарии
        /// не запускается, поэтому игрового смысла у флага здесь нет. Поднятый флаг
        /// значит «подписка стоит, можно бить», опущенный — «вердикт записан».
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
