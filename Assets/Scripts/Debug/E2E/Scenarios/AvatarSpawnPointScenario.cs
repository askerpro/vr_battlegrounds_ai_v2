// Ярус C (два процесса) — сценарий avatar-spawn-point, находка WPN-03.
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>avatar-spawn-point</c> — находка <b>WPN-03</b>.
    ///
    /// <para>
    /// <b>Что доказывает.</b> <c>AvatarManager.ChangeAvatar</c> обслуживает три разных
    /// события одним куском кода и позицию нового аватара брал у старого. Пока старый жив
    /// (смена скина) это верно. Но <c>GameNetworkManager.OnServerReady</c> зовёт тот же
    /// метод <b>после смены карты</b>, где старый аватар уже уничтожен Mirror вместе
    /// со сценой, — и все игроки материализовались в <c>(0, 0, 0)</c>, вплотную
    /// к реквизиту карты.
    /// </para>
    ///
    /// <para>
    /// <b>Три случая — три проверки.</b> Сценарий разводит их намеренно, потому что
    /// «всегда брать точку спавна» — такая же ошибка, как «всегда брать позицию старого»:
    /// </para>
    /// <list type="number">
    /// <item><b>После смены карты</b> аватар обязан оказаться в зоне спавна своей
    ///       команды, а не в начале координат. Красная до правки.</item>
    /// <item><b>Смена скина</b> не имеет права двигать живого игрока. Чтобы проверка
    ///       что-то значила, игрока сначала уводят из зоны: пока он стоит в ней,
    ///       «остался на месте» и «переехал в зону» дают один и тот же ответ.</item>
    /// <item><b>Смена команды</b> обязана перенести на точку спавна <b>новой</b>
    ///       команды: иначе игрок остаётся стоять в базе противника. Красная до правки.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Почему нужен выделенный сервер и настоящая смена карты.</b> Ветка
    /// «старого аватара нет» достижима только так: Mirror уничтожает аватары при
    /// <c>ServerChangeScene</c>, а пересоздаёт их <c>OnServerReady</c> — по одному
    /// на каждого догрузившего клиента. Ни один тест яруса A/B этой последовательности
    /// не воспроизводит.
    /// </para>
    ///
    /// <para>
    /// <b>DebugOrchestrator гасится первым делом.</b> Именно он маскирует находку
    /// в отладочных запусках: увидев первый аватар сессии, он телепортирует игрока
    /// в зону его команды. С ним измерять нечего — точку спавна поставит он, а не
    /// проверяемый код.
    /// </para>
    ///
    /// <para>
    /// Матч не запускается: зоны спавна нужны здесь как точки на карте, а не как
    /// условие готовности. Хватает одного клиента — <c>-Clients 1</c>.
    /// </para>
    /// </summary>
    public class AvatarSpawnPointScenario : IE2EScenario
    {
        public string Name => "avatar-spawn-point";

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients   = "клиент подключился и сервер создал сессию";
        private const string CheckZones     = "на карте нашлись зоны спавна обеих команд и они разведены с началом координат";
        private const string CheckAfterMap  = "после смены карты аватар создан в зоне спавна своей команды, а не в начале координат (WPN-03)";
        private const string CheckSkinKeeps = "смена скина не сдвинула игрока с места";
        private const string CheckTeamMoves = "смена команды не сдвинула игрока (этап Б: выбор команды не перемещает)";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientMap       = "клиент переехал на карту вместе с сервером";
        private const string CheckClientZone      = "клиент видит свой аватар в зоне спавна, а не в начале координат (WPN-03)";

        // ── Фазы, раздаваемые клиенту через SyncVar Score ─────────────────

        /// <summary>Карта загружена, аватар пересоздан — клиенту пора снимать свой замер.</summary>
        private const int PhaseMeasure = 1;

        /// <summary>Сервер отработал: клиент вправе записать вердикт и успокоиться.</summary>
        private const int PhaseDone = 9;

        // ── Сроки ─────────────────────────────────────────────────────────

        /// <summary>Сколько сервер ждёт, пока Mirror переедет на карту.</summary>
        private const float MapWait = 120f;

        /// <summary>Сколько сервер ждёт пересоздания аватара после смены карты.</summary>
        private const float AvatarWait = 90f;

        /// <summary>Сколько ждём, что <c>ChangeAvatar</c> выдаст новый аватар.</summary>
        private const float SwapWait = 30f;

        /// <summary>Сколько ждём, что увод игрока из зоны доедет до серверной копии.</summary>
        private const float DisplaceWait = 15f;

        /// <summary>Сколько сервер ждёт клиентского замера.</summary>
        private const float ClientMeasureWait = 120f;

        /// <summary>Сколько сервер ждёт, пока клиент запишет вердикт, прежде чем гасить процесс.</summary>
        private const float ClientVerdictWait = 45f;

        /// <summary>Сколько клиент ждёт фазы от сервера.</summary>
        private const float ClientPhaseWait = 180f;

        /// <summary>
        /// Пауза, после которой позиция считается установившейся. Аватар создаёт сервер,
        /// но <c>NetworkTransform</c> на аватарах стоит с <c>syncDirection = ClientToServer</c>:
        /// владелец вправе прислать свою позицию поверх серверной. Замер по установившемуся
        /// состоянию отвечает на вопрос игрока «где я оказался», а не «где меня создали».
        /// </summary>
        private const float SettleHold = 3f;

        // ── Пороги ────────────────────────────────────────────────────────

        /// <summary>
        /// Допуск «аватар в точке», метры. Сервер создаёт аватар ровно в точке зоны,
        /// но владелец может прислать свою позицию поверх, а сам аватар — осесть
        /// на коллайдер пола. Полтора метра заведомо меньше любого расстояния,
        /// которое проверка обязана различать (см. <see cref="MinSeparation"/>).
        /// </summary>
        private const float PositionTolerance = 1.5f;

        /// <summary>
        /// Минимальное разведение двух точек, при котором проверка вообще что-то значит,
        /// метры. Втрое больше допуска: перепутать «там же» и «в другом месте» нельзя.
        /// </summary>
        private const float MinSeparation = 4.5f;

        /// <summary>
        /// На сколько зона спавна обязана отстоять от начала координат, метры.
        /// Стоящая в самом начале координат зона делает исправленный код неотличимым
        /// от сломанного, и прогон на такой карте недействителен.
        /// </summary>
        private const float ZoneOriginClearance = 6f;

        /// <summary>
        /// На сколько игрока уводят из зоны перед контрольной сменой скина, метры,
        /// вдоль собственной оси X зоны. Коробка триггера в префабе зоны — 15,5 м
        /// по этой оси, поэтому шесть метров не выносят игрока за пределы базы
        /// и не втыкают его в стену, но с запасом больше <see cref="MinSeparation"/>.
        /// </summary>
        private const float DisplaceDistance = 6f;

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
            result.Declare(CheckDedicated, CheckClients, CheckZones,
                           CheckAfterMap, CheckSkinKeeps, CheckTeamMoves);

            try
            {
                // ── 1. Выделенный сервер ──────────────────────────────────
                float deadline = Now + 60f;
                while (!NetworkServer.active && Now < deadline)
                    yield return null;

                bool dedicated = NetworkServer.active && !NetworkClient.active;
                result.Set(CheckDedicated, dedicated,
                    dedicated
                        ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                        : $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}. " +
                          "Смена карты через ServerChangeScene и пересоздание аватаров в OnServerReady " +
                          "воспроизводятся только на выделенном сервере: процесс обязан идти с -batchmode -nographics.");

                if (!dedicated)
                {
                    result.Summary = "конфигурация не выделенный сервер, прогон недействителен";
                    yield break;
                }

                // Первым делом: иначе он сам расставит игроков по зонам спавна
                // и измерять будет нечего.
                DisableDebugOrchestrator();

                // ── 2. Клиент ─────────────────────────────────────────────
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
                    result.Summary = "клиент не подключился, вердикт вынести нельзя";
                    yield break;
                }

                SessionManager sessionManager = SessionManager.Instance;
                if (sessionManager == null || MapLoader.Instance == null || AvatarManager.Instance == null)
                {
                    result.Set(CheckZones, false,
                        $"SessionManager={(sessionManager == null ? "null" : "есть")}, " +
                        $"MapLoader={(MapLoader.Instance == null ? "null" : "есть")}, " +
                        $"AvatarManager={(AvatarManager.Instance == null ? "null" : "есть")}");
                    result.Summary = "менеджеры не поднялись, прогон недействителен";
                    yield break;
                }

                // Режим выбирается не ради матча, а ради валидных индексов команд:
                // ChangeAvatar молча выходит, если TeamRegistry не знает индекс команды.
                sessionManager.SetSession(context.Map, "elimination");
                yield return null;

                GameModeData mode = sessionManager.SelectedGameModeData;
                if (mode == null || mode.teams == null || mode.teams.Length < 2)
                {
                    result.Set(CheckZones, false,
                        "режим не отдал две команды — сравнивать зоны спавна не с чем " +
                        $"(команд: {(mode == null || mode.teams == null ? 0 : mode.teams.Length)})");
                    result.Summary = "режим без двух команд, вердикт вынести нельзя";
                    yield break;
                }

                TeamData ownTeam   = mode.teams[0];
                TeamData otherTeam = mode.teams[1];

                PlayerSession session = FirstSession();
                if (session == null)
                {
                    result.Set(CheckZones, false, "в PlayersManager нет ни одной сессии");
                    result.Summary = "сессии нет, вердикт вынести нельзя";
                    yield break;
                }

                session.TeamIndex = ownTeam.teamIndex;
                GameLog.Debug.Info(
                    $"[E2E] {session.PlayerName} назначен в '{ownTeam.displayName}' ({ownTeam.teamIndex}); " +
                    $"вторая команда — '{otherTeam.displayName}' ({otherTeam.teamIndex})");

                // ── 3. Смена карты ────────────────────────────────────────
                MapLoader.Instance.LoadMap(context.Map);

                E2EWaitOutcome onMap = new E2EWaitOutcome();
                yield return E2EWait.Until(onMap,
                    $"сервер переехал на карту '{context.Map}'",
                    MapWait,
                    () => SceneManager.GetActiveScene().name == context.Map,
                    () => $"активная сцена='{SceneManager.GetActiveScene().name}'");

                if (!onMap.Succeeded)
                {
                    result.Set(CheckZones, false, onMap.Diagnosis);
                    result.Summary = "карта не загрузилась, вердикт вынести нельзя";
                    yield break;
                }

                // ── 4. Зоны спавна: без них измерять нечем ────────────────
                TeamSpawnZone ownZone   = AvatarSpawnPointResolver.FindZone(ownTeam);
                TeamSpawnZone otherZone = AvatarSpawnPointResolver.FindZone(otherTeam);

                bool zonesFound = ownZone != null && otherZone != null;
                Vector3 ownZonePos   = zonesFound ? ownZone.transform.position   : Vector3.zero;
                Vector3 otherZonePos = zonesFound ? otherZone.transform.position : Vector3.zero;

                float ownFromOrigin   = zonesFound ? Vector3.Distance(ownZonePos,   Vector3.zero) : 0f;
                float otherFromOrigin = zonesFound ? Vector3.Distance(otherZonePos, Vector3.zero) : 0f;
                float betweenZones    = zonesFound ? Vector3.Distance(ownZonePos,   otherZonePos) : 0f;

                bool zonesUsable = zonesFound
                                   && ownFromOrigin   >= ZoneOriginClearance
                                   && otherFromOrigin >= ZoneOriginClearance
                                   && betweenZones    >= MinSeparation;

                result.Set(CheckZones, zonesUsable,
                    zonesUsable
                        ? $"зона '{ownTeam.displayName}' — {Fmt(ownZonePos)}, до начала координат {ownFromOrigin:F2} м; " +
                          $"зона '{otherTeam.displayName}' — {Fmt(otherZonePos)}, до начала координат {otherFromOrigin:F2} м; " +
                          $"между зонами {betweenZones:F2} м. Все три расстояния больше порогов " +
                          $"({ZoneOriginClearance:F1} / {MinSeparation:F1} м) — значит «в своей зоне», «в чужой зоне» " +
                          "и «в начале координат» различимы."
                        : !zonesFound
                            ? $"на карте '{context.Map}' не нашлось зоны спавна: " +
                              $"'{ownTeam.displayName}' — {(ownZone == null ? "нет" : "есть")}, " +
                              $"'{otherTeam.displayName}' — {(otherZone == null ? "нет" : "есть")}. " +
                              "Зоны живут внутри префаба окружения карты; без них WPN-03 не измерить."
                            : "зоны есть, но стоят слишком близко к началу координат или друг к другу: " +
                              $"своя {ownFromOrigin:F2} м, чужая {otherFromOrigin:F2} м, между ними {betweenZones:F2} м " +
                              $"(нужно {ZoneOriginClearance:F1} / {ZoneOriginClearance:F1} / {MinSeparation:F1} м). " +
                              "Различить исправленный код и сломанный на такой карте нельзя.");

                if (!zonesUsable)
                {
                    result.Summary = "зоны спавна непригодны для измерения, вердикт вынести нельзя";
                    yield break;
                }

                // ── 5. Находка: где оказался пересозданный аватар ─────────
                // Аватар пересоздаёт GameNetworkManager.OnServerReady — то есть только
                // после того, как клиент догрузит карту.
                E2EWaitOutcome avatarBack = new E2EWaitOutcome();
                yield return E2EWait.Until(avatarBack,
                    "после смены карты у сессии снова появился аватар",
                    AvatarWait,
                    () => session != null && session.ActiveAvatar != null,
                    () => DescribeSessions(),
                    () => NetworkServer.connections.Count > 0
                        ? null
                        : "на сервере не осталось подключений — аватар уже не появится");

                if (!avatarBack.Succeeded)
                {
                    result.Set(CheckAfterMap, false,
                        avatarBack.Diagnosis +
                        " GameNetworkManager.OnServerReady спавнит аватар только для готового клиента.");
                    result.Summary = "аватар не пересоздался, вердикт по WPN-03 вынести нельзя";
                    yield break;
                }

                Vector3 firstSight = session.ActiveAvatar.transform.position;
                yield return E2EWait.Hold(SettleHold);

                if (session.ActiveAvatar == null)
                {
                    result.Set(CheckAfterMap, false,
                        $"аватар появился в {Fmt(firstSight)}, но за {SettleHold:F0} с исчез — измерять нечего");
                    result.Summary = "аватар не дожил до замера, вердикт вынести нельзя";
                    yield break;
                }

                Vector3 settled    = session.ActiveAvatar.transform.position;
                float toZone       = Vector3.Distance(settled, ownZonePos);
                float toOrigin     = Vector3.Distance(settled, Vector3.zero);
                float ownerDrift   = Vector3.Distance(settled, firstSight);
                bool spawnedInZone = toZone <= PositionTolerance;

                result.Set(CheckAfterMap, spawnedInZone,
                    (spawnedInZone
                        ? $"аватар в зоне '{ownTeam.displayName}': {Fmt(settled)}, до зоны {toZone:F2} м, " +
                          $"до начала координат {toOrigin:F2} м."
                        : $"аватар оказался в {Fmt(settled)}: до зоны '{ownTeam.displayName}' {toZone:F2} м " +
                          $"(допуск {PositionTolerance:F1} м), до начала координат {toOrigin:F2} м. " +
                          (toOrigin <= PositionTolerance
                              ? "Это WPN-03 в чистом виде: ChangeAvatar берёт позицию у старого аватара, " +
                                "а после смены карты его уже нет — остаётся Vector3.zero."
                              : "Позиция не совпала ни с зоной, ни с началом координат — ищи, кто ещё двигает аватар.")) +
                    $" Замер при появлении: {Fmt(firstSight)}, через {SettleHold:F0} с: {Fmt(settled)} " +
                    $"(владелец сдвинул на {ownerDrift:F2} м). Зона: {Fmt(ownZonePos)}.");

                // Клиент снимает свой замер именно сейчас: команду ниже поменяют,
                // и «своя зона» у него станет другой.
                SetPhase(PhaseMeasure);

                E2EWaitOutcome clientMeasured = new E2EWaitOutcome();
                yield return E2EWait.Until(clientMeasured,
                    "клиент отчитался, что снял свой замер",
                    ClientMeasureWait,
                    () => ReportedFlags() >= context.ExpectedClients,
                    () => $"отчитались {ReportedFlags()} из {context.ExpectedClients}; {DescribeSessions()}",
                    () => NetworkServer.connections.Count > 0
                        ? null
                        : "на сервере не осталось подключений — замера уже не будет");

                GameLog.Debug.Info($"[E2E] Клиентский замер: {clientMeasured.Diagnosis}");

                // ── 6. Контроль: смена скина не двигает игрока ────────────
                // Сначала уводим игрока из зоны: пока он стоит в ней, ответы
                // «остался на месте» и «переехал в зону» неразличимы.
                Vector3 displaceTarget = ownZonePos + ownZone.transform.right * DisplaceDistance;
                yield return DisplaceAvatar(session, displaceTarget);

                float fromZoneAfterDisplace = session.ActiveAvatar != null
                    ? Vector3.Distance(session.ActiveAvatar.transform.position, ownZonePos)
                    : 0f;

                bool displaced = session.ActiveAvatar != null && fromZoneAfterDisplace >= MinSeparation;

                if (!displaced)
                {
                    result.Set(CheckSkinKeeps, false,
                        $"не удалось увести игрока из зоны: цель {Fmt(displaceTarget)}, аватар " +
                        $"{(session.ActiveAvatar == null ? "исчез" : Fmt(session.ActiveAvatar.transform.position))}, " +
                        $"до зоны {fromZoneAfterDisplace:F2} м при нужных {MinSeparation:F1} м. " +
                        "Стоя в зоне, «остался на месте» и «переехал в зону» неразличимы, поэтому " +
                        "проверка объявлена красной, а не зелёной. Смотри лог увода игрока в server.log.");
                }
                else
                {
                    Vector3 beforeSkin = session.ActiveAvatar.transform.position;
                    int otherSkin = OtherAvatarIndex(ownTeam, session.AvatarIndex);

                    E2EWaitOutcome skinSwapped = new E2EWaitOutcome();
                    yield return SwapAvatar(session, ownTeam.teamIndex, otherSkin, skinSwapped);

                    if (!skinSwapped.Succeeded || session.ActiveAvatar == null)
                    {
                        result.Set(CheckSkinKeeps, false,
                            skinSwapped.Diagnosis + $" Скин просили сменить на {otherSkin}.");
                    }
                    else
                    {
                        Vector3 afterSkin = session.ActiveAvatar.transform.position;
                        float skinShift = Vector3.Distance(beforeSkin, afterSkin);
                        bool keptPlace = skinShift <= PositionTolerance;

                        result.Set(CheckSkinKeeps, keptPlace,
                            (keptPlace
                                ? $"игрок остался на месте: {Fmt(beforeSkin)} -> {Fmt(afterSkin)}, сдвиг {skinShift:F2} м."
                                : $"смена скина утащила игрока: {Fmt(beforeSkin)} -> {Fmt(afterSkin)}, " +
                                  $"сдвиг {skinShift:F2} м при допуске {PositionTolerance:F1} м. До зоны своей команды " +
                                  $"теперь {Vector3.Distance(afterSkin, ownZonePos):F2} м — похоже, точку спавна " +
                                  "применили там, где нужна была позиция живого аватара.") +
                            $" Игрок стоял в {fromZoneAfterDisplace:F2} м от своей зоны, то есть проверка различала " +
                            $"«остался» и «переехал в зону». Скин {session.AvatarIndex}, команда без изменений.");
                    }
                }

                // ── 7. Смена команды не двигает игрока (этап Б) ──────
                if (session.ActiveAvatar == null)
                {
                    result.Set(CheckTeamMoves, false,
                        "к моменту смены команды у сессии не осталось аватара — измерять нечего. " +
                        DescribeSessions());
                    result.Summary = "аватар потерян до проверки смены команды";
                    yield break;
                }

                Vector3 beforeTeam = session.ActiveAvatar.transform.position;

                E2EWaitOutcome teamSwapped = new E2EWaitOutcome();
                yield return SwapAvatar(session, otherTeam.teamIndex, 0, teamSwapped);

                if (!teamSwapped.Succeeded || session.ActiveAvatar == null)
                {
                    result.Set(CheckTeamMoves, false,
                        teamSwapped.Diagnosis +
                        $" Команду просили сменить на '{otherTeam.displayName}' ({otherTeam.teamIndex}).");
                    result.Summary = "смена команды не состоялась, вердикт вынести нельзя";
                    yield break;
                }

                Vector3 afterTeam   = session.ActiveAvatar.transform.position;
                float toOtherZone   = Vector3.Distance(afterTeam, otherZonePos);
                float movedOnSwap   = Vector3.Distance(beforeTeam, afterTeam);
                bool stayedInPlace  = movedOnSwap <= PositionTolerance;

                // Этап Б: смена команды игрока не двигает. Раньше (WPN-03) она переносила
                // в зону новой команды; теперь игрок физически стоит в зале, и выбор
                // команды на карте — действие в меню, а не перенос.
                result.Set(CheckTeamMoves, stayedInPlace,
                    (stayedInPlace
                        ? $"игрок остался на месте: {Fmt(beforeTeam)} -> {Fmt(afterTeam)}, сдвиг {movedOnSwap:F2} м."
                        : $"смена команды сдвинула игрока: {Fmt(beforeTeam)} -> {Fmt(afterTeam)}, сдвиг {movedOnSwap:F2} м " +
                          $"при допуске {PositionTolerance:F1} м, до зоны новой команды {toOtherZone:F2} м.") +
                    $" Зона новой команды: {Fmt(otherZonePos)}, зона прежней: {Fmt(ownZonePos)}.");

                result.Summary = result.AllChecksGreen
                    ? "точка спавна выбирается верно во всех трёх случаях — WPN-03 не воспроизводится"
                    : "есть красные проверки, см. detail";

                SetPhase(PhaseDone);
                yield return WaitForClientVerdicts(context);
            }
            finally
            {
                // Клиент ждёт PhaseDone, чтобы записать вердикт. Ставим его на любом
                // выходе, включая ранний yield break: иначе клиент досидит до своего
                // таймаута и прогон станет INCONCLUSIVE вместо честного красного.
                SetPhase(PhaseDone);
            }
        }

        /// <summary>
        /// Держит серверный процесс живым, пока клиент не запишет вердикт. Обрыв связи
        /// посреди клиентских ожиданий выглядит как «сигнал не пришёл» и даёт мигающие
        /// ворота (TEST-01).
        /// </summary>
        private static IEnumerator WaitForClientVerdicts(E2EContext context)
        {
            E2EWaitOutcome barrier = new E2EWaitOutcome();

            yield return E2EWait.Until(barrier,
                $"все {context.ExpectedClients} клиент(а) отчитались, что записали вердикт",
                ClientVerdictWait,
                () => ReportedFlags() == 0 && SessionCount() >= context.ExpectedClients,
                () => $"держат флаг: {ReportedFlags()} из {SessionCount()} сессий; " +
                      $"подключений: {NetworkServer.connections.Count}");

            GameLog.Debug.Info($"[E2E] Барьер клиентских вердиктов: {barrier.Diagnosis}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientMap, CheckClientZone);

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
                result.Summary = "клиент не на карте, замерять нечего";
                yield break;
            }

            // ── Замер по отмашке сервера ──────────────────────────────────
            E2EWaitOutcome phase = new E2EWaitOutcome();
            yield return E2EWait.Until(phase,
                "сервер объявил фазу замера",
                ClientPhaseWait,
                () => local.Score >= PhaseMeasure,
                () => $"фаза={local.Score}, аватар=" +
                      $"{(local.ActiveAvatar == null ? "нет" : Fmt(local.ActiveAvatar.transform.position))}",
                () => NetworkClient.isConnected ? null : "связь с сервером пропала");

            if (!phase.Succeeded)
            {
                result.Set(CheckClientZone, false, phase.Diagnosis);
                result.Summary = "фазы замера не дождались";
                yield break;
            }

            TeamData team = local.Team;
            TeamSpawnZone zone = AvatarSpawnPointResolver.FindZone(team);
            PlayerController avatar = local.ActiveAvatar;

            if (avatar == null || zone == null)
            {
                result.Set(CheckClientZone, false,
                    $"замерять нечем: аватар={(avatar == null ? "нет" : "есть")}, " +
                    $"команда={(team == null ? "не назначена" : team.displayName)}, " +
                    $"зона её спавна={(zone == null ? "не найдена" : zone.name)}.");

                ReportFlag(true);
                yield return WaitForDone(local);
                result.Summary = "клиенту нечего замерять";
                ReportFlag(false);
                yield break;
            }

            Vector3 avatarPosition = avatar.transform.position;
            Vector3 zonePosition   = zone.transform.position;
            float toZone   = Vector3.Distance(avatarPosition, zonePosition);
            float toOrigin = Vector3.Distance(avatarPosition, Vector3.zero);
            bool inZone    = toZone <= PositionTolerance;

            result.Set(CheckClientZone, inZone,
                (inZone
                    ? $"свой аватар в зоне '{team.displayName}': {Fmt(avatarPosition)}, до зоны {toZone:F2} м."
                    : $"свой аватар в {Fmt(avatarPosition)}: до зоны '{team.displayName}' {toZone:F2} м " +
                      $"(допуск {PositionTolerance:F1} м), до начала координат {toOrigin:F2} м. " +
                      (toOrigin <= PositionTolerance
                          ? "Игрок стоит в начале координат карты — это WPN-03, увиденная со стороны клиента."
                          : "Позиция не совпала ни с зоной, ни с началом координат.")) +
                $" Зона: {Fmt(zonePosition)}.");

            // Флаг поднимается только после замера: по нему сервер понимает,
            // что вправе двигать игрока дальше.
            ReportFlag(true);

            yield return WaitForDone(local);

            result.Summary = result.AllChecksGreen
                ? "клиент увидел свой аватар в зоне спавна"
                : "клиент увидел свой аватар не там, где ждали, см. detail";

            ReportFlag(false);
        }

        /// <summary>Досиживает до отмашки сервера «измерения закончены, вердикт можно записывать».</summary>
        private static IEnumerator WaitForDone(PlayerSession local)
        {
            E2EWaitOutcome done = new E2EWaitOutcome();

            yield return E2EWait.Until(done,
                "сервер закончил измерения (PhaseDone)",
                ClientPhaseWait,
                () => local == null || local.Score >= PhaseDone,
                () => $"фаза={(local == null ? "сессии нет" : local.Score.ToString())}",
                () => NetworkClient.isConnected ? null : "связь с сервером пропала");

            GameLog.Debug.Info($"[E2E] Ожидание конца измерений: {done.Diagnosis}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное — сервер
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Уводит игрока из зоны спавна игровым способом — <c>PlayerController.Respawn</c>.
        ///
        /// <para>
        /// Прямая запись <c>transform.position</c> на сервере тут ненадёжна:
        /// <c>NetworkTransform</c> на аватарах стоит с <c>syncDirection = ClientToServer</c>,
        /// и владелец вернёт свою позицию поверх серверной ближайшим же пакетом. Поэтому
        /// сначала <c>ServerDevTeleport</c> — он рассылает <c>RpcDevTeleport</c>, и настоящий переезд
        /// делает сам владелец, — а прямая запись остаётся запасным вариантом на случай,
        /// если владелец за <see cref="DisplaceWait"/> так и не отчитался.
        /// </para>
        /// </summary>
        private static IEnumerator DisplaceAvatar(PlayerSession session, Vector3 target)
        {
            PlayerController avatar = session.ActiveAvatar;
            if (avatar == null)
                yield break;

            GameObject marker = new GameObject("E2E_DisplaceTarget");
            marker.transform.SetPositionAndRotation(target, avatar.transform.rotation);

            avatar.ServerDevTeleport(marker.transform.position, marker.transform.rotation);

            E2EWaitOutcome moved = new E2EWaitOutcome();
            yield return E2EWait.Until(moved,
                $"серверная копия аватара доехала до {Fmt(target)}",
                DisplaceWait,
                () => session.ActiveAvatar != null &&
                      Vector3.Distance(session.ActiveAvatar.transform.position, target) <= PositionTolerance,
                () => session.ActiveAvatar == null
                    ? "аватар исчез"
                    : $"аватар в {Fmt(session.ActiveAvatar.transform.position)}, до цели " +
                      $"{Vector3.Distance(session.ActiveAvatar.transform.position, target):F2} м",
                () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

            GameLog.Debug.Info($"[E2E] Увод игрока из зоны через Respawn: {moved.Diagnosis}");

            if (!moved.Succeeded && session.ActiveAvatar != null)
            {
                GameLog.Debug.Info("[E2E] Владелец не отчитался — ставлю аватар на место записью на сервере");
                session.ActiveAvatar.transform.position = target;
            }

            Object.Destroy(marker);

            // Даём позиции устояться: владелец мог ещё не прислать финальное значение.
            yield return E2EWait.Hold(SettleHold);
        }

        /// <summary>
        /// Зовёт <c>ChangeAvatar</c> и ждёт, пока сессия получит новый аватар
        /// (по смене <c>ActiveAvatarNetId</c>), после чего даёт позиции устояться.
        /// </summary>
        private static IEnumerator SwapAvatar(PlayerSession session, int teamIndex, int avatarIndex,
                                              E2EWaitOutcome outcome)
        {
            uint oldNetId = session.ActiveAvatarNetId;

            GameLog.Debug.Info(
                $"[E2E] ChangeAvatar для {session.PlayerName}: команда {teamIndex}, скин {avatarIndex}; " +
                $"старый netId={oldNetId}");

            AvatarManager.Instance.ChangeAvatar(session.connectionToClient, session, teamIndex, avatarIndex);

            yield return E2EWait.Until(outcome,
                $"ChangeAvatar пересоздал аватар (команда {teamIndex}, скин {avatarIndex})",
                SwapWait,
                () => session.ActiveAvatarNetId != oldNetId && session.ActiveAvatar != null,
                () => $"netId={session.ActiveAvatarNetId} (был {oldNetId}), аватар=" +
                      $"{(session.ActiveAvatar == null ? "нет" : Fmt(session.ActiveAvatar.transform.position))}",
                () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

            yield return E2EWait.Hold(SettleHold);
        }

        /// <summary>Любой скин команды, отличный от текущего. Если он один — тот же самый.</summary>
        private static int OtherAvatarIndex(TeamData team, int current)
        {
            int count = team != null && team.avatars != null ? team.avatars.Count : 0;
            if (count <= 1) return current;

            return (current + 1) % count;
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

        private static PlayerSession FirstSession()
        {
            if (PlayersManager.Instance == null) return null;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null) return session;
            }

            return null;
        }

        private static string DescribeSessions()
        {
            if (PlayersManager.Instance == null)
                return "PlayersManager отсутствует";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                PlayerController avatar = session.ActiveAvatar;
                parts.Add($"{session.PlayerName}: команда={session.TeamIndex}, " +
                          (avatar == null
                              ? "аватара нет"
                              : $"аватар netId={avatar.netId} в {Fmt(avatar.transform.position)}") +
                          $", флаг={(session.HasGrabbedDogTag ? "поднят" : "опущен")}");
            }

            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "сессий нет";
        }

        private static int ReportedFlags()
        {
            if (PlayersManager.Instance == null) return 0;

            int reported = 0;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    reported++;
            }

            return reported;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Общее
        // ══════════════════════════════════════════════════════════════════

        private static float Now => Time.realtimeSinceStartup;

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
        }

        /// <summary>
        /// Обратный канал клиент → сервер. <c>HasGrabbedDogTag</c> — единственное поле
        /// сессии, которое клиент вправе менять командой; матч здесь не запускается,
        /// поэтому игрового смысла у флага нет. Поднятый флаг значит «замер снят»,
        /// опущенный — «вердикт записан».
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
