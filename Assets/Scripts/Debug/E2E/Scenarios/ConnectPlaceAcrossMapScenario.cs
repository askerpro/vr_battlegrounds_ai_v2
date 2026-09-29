// Ярус C (два процесса) — сценарий connect-place-across-map, находка CAL-02.
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>connect-place-across-map</c> — находка <b>CAL-02</b>.
    ///
    /// <para>
    /// <b>Что доказывает.</b> Клиент подключается на одной карте, набирает своё место,
    /// уходит; сервер меняет карту; клиент возвращается. Где он обязан оказаться,
    /// зависит от одного — калибровался он или нет, и правило то же, что при смене
    /// карты (T-30): <b>откалиброванный</b> сохраняет своё физическое место,
    /// <b>неоткалиброванный</b> появляется в зоне своей команды. Чего он не вправе
    /// увидеть ни в одном из двух случаев — это старую <b>мировую</b> точку с прошлой
    /// карты: арена в <c>TestMap1</c> повёрнута на 90° вокруг Y относительно
    /// <c>TestMap2</c>, и та же мировая точка означает там другое место арены.
    /// </para>
    ///
    /// <para>
    /// <b>После выравнивания карт (2026-09).</b> Арены всех карт теперь стоят одинаково
    /// (<c>MapAlignmentTests</c>), повёрнутой карты в проекте нет. Сценарий больше не отличает
    /// «место относительно якорей» от «старой мировой позиции» и честно отвечает «вердикт вынести
    /// нельзя». Перенос по якорям проверяет EditMode-тест
    /// <c>SpawnPlaceRegistryTests.Откалиброванный_снимается_относительно_якорей</c>; для живой
    /// проверки нужна карта с иначе поставленной ареной.
    /// </para>
    ///
    /// <para>
    /// <b>Обе ветки в одном прогоне, одним клиентом.</b> Прогон состоит из двух кругов
    /// «пожил на карте → ушёл → сервер сменил карту → вернулся»: первый круг игрок
    /// проходит <b>без</b> калибровки и обязан вернуться в зону своей команды, второй —
    /// <b>с</b> калибровкой и обязан вернуться на своё место в арене. Одна ветка без
    /// другой ничего не доказывает: «всех в зону» и «никого не двигать» по отдельности
    /// выглядят одинаково правдоподобно.
    /// </para>
    ///
    /// <para>
    /// <b>Почему один клиент, а не два.</b> Здесь дважды рвётся соединение, а разрыв —
    /// самая недетерминированная часть харнесса (см. <c>session-recovery-on-reconnect</c>
    /// и находку NET-20). Второй процесс добавил бы только источников мигания, а обе
    /// ветки правила проверяются и последовательно. Запускать так:
    /// <c>Run-E2E.ps1 -Scenario connect-place-across-map -Clients 1</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Независимый эталон.</b> Место игрока задаётся точкой, вычисленной <b>из зон
    /// спавна</b>: середина между базами, сдвинутая на четыре метра в сторону чужой.
    /// Зоны ставит художник, к калибровке они отношения не имеют. Сдвиг обязателен:
    /// центр арены — пивот её поворота, он у двух карт общий <b>и в мировых координатах
    /// тоже</b>, и наивное сохранение мировой позиции прошло бы проверку в центре
    /// (находка T-30). Сдвинутая точка при повороте арены уезжает на 5,7 м — втрое
    /// больше допуска, — и замер начинает различать три разных ответа: «своё место
    /// в арене», «зона команды» и «старая мировая точка».
    /// </para>
    ///
    /// <para>
    /// <b>Чего сценарий не проверяет.</b> Саму калибровку: она читает положение шлема
    /// и контроллеров, то есть требует железа. Клиент не калибруется, а объявляет
    /// результат командой <c>CmdSetCalibrated</c> — тот же приём, что
    /// в <c>calibration-scale-replication</c> и <c>calibrated-position-persists</c>.
    /// Следствие приёма: <c>PhysicalSpaceSyncManager.IsCalibrated</c> у клиента остаётся
    /// ложным, поэтому во втором круге признак калибровки приезжает на сервер снимком
    /// отключённой сессии, а не битом в сообщении подключения. Бит в сообщении нужен
    /// другому случаю — откалиброванному игроку, которого сервер видит впервые, — и
    /// его проверяет ярус A (<c>SavedAvatarPlaceTests</c>).
    /// </para>
    /// </summary>
    public class ConnectPlaceAcrossMapScenario : IE2EScenario
    {
        public string Name => "connect-place-across-map";

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClient    = "клиент подключился и сервер создал сессию";
        private const string CheckFirstMap  = "первая карта загружена, у сессии есть аватар";
        private const string CheckAnchors   = "якоря обеих карт задают одну и ту же систему координат арены";
        private const string CheckPlainZone = "неоткалиброванный игрок вернулся в зону своей команды, а не на старую мировую точку (CAL-02)";
        private const string CheckDeclared  = "во втором круге игрок объявил калибровку и сервер её увидел";
        private const string CheckKeptPlace = "откалиброванный игрок вернулся на своё место в арене, а не на старую мировую точку (CAL-02)";
        private const string CheckBranches  = "ветки различаются: место в арене и зона команды разведены настолько, что их не спутать";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientReturns   = "клиент пережил оба разрыва и оба раза вернулся тем же устройством";

        // ── Фазы, раздаваемые клиенту через SyncVar Score ─────────────────

        /// <summary>Сервер ещё готовится, клиенту делать нечего.</summary>
        private const int PhaseWait = 0;

        /// <summary>Поднять флаг наблюдения — рукопожатие обратного канала.</summary>
        private const int PhaseRaise = 1;

        /// <summary>Объявить калибровку: подмена результата процедуры по якорям.</summary>
        private const int PhaseDeclare = 3;

        /// <summary>Прогон завершён, клиент вправе записать вердикт.</summary>
        private const int PhaseDone = 9;

        // ── Сроки ─────────────────────────────────────────────────────────

        private const float ServerWait      = 60f;
        private const float ClientWait      = 90f;
        private const float MapWait         = 90f;
        private const float AvatarWait      = 90f;
        private const float HandshakeWait   = 60f;
        private const float DeclareWait     = 60f;
        private const float DisplaceWait    = 30f;
        private const float DropWait        = 60f;
        private const float ReturnWait      = 180f;
        private const float ClientVerdictWait = 45f;

        /// <summary>
        /// Пауза, после которой позиция считается установившейся. Аватар создаёт сервер,
        /// но <c>NetworkTransform</c> на аватарах стоит с <c>syncDirection = ClientToServer</c>:
        /// владелец вправе прислать свою позицию поверх серверной. Замер по установившемуся
        /// состоянию отвечает на вопрос игрока «где я оказался», а не «где меня создали».
        /// </summary>
        private const float SettleHold = 3f;

        // ── Пороги ────────────────────────────────────────────────────────

        /// <summary>Допуск «аватар в точке», метры.</summary>
        private const float PositionTolerance = 1.5f;

        /// <summary>Минимальное разведение двух точек, при котором проверка что-то значит, метры.</summary>
        private const float MinSeparation = 4.5f;

        /// <summary>Насколько точка замера отведена от центра арены вдоль линии «своя база → чужая», метры.</summary>
        private const float ProbeOffset = 4f;

        /// <summary>Насколько геометрия арены обязана совпасть у двух карт в координатах якорей, метры.</summary>
        private const float ArenaCongruence = 0.5f;

        // ── Состояние клиентской роли ─────────────────────────────────────

        private int _clientReturns;

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
            result.Declare(CheckDedicated, CheckClient, CheckFirstMap, CheckAnchors,
                           CheckPlainZone, CheckDeclared, CheckKeptPlace, CheckBranches);

            string firstMap = context.Map;
            string secondMap = SecondMapFor(firstMap);

            try
            {
                // ── 1. Выделенный сервер ──────────────────────────────────
                float deadline = Now + ServerWait;
                while (!NetworkServer.active && Now < deadline)
                    yield return null;

                bool dedicated = NetworkServer.active && !NetworkClient.active;
                result.Set(CheckDedicated, dedicated,
                    dedicated
                        ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                        : $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}. " +
                          "Разрыв, смена карты и повторное подключение воспроизводятся только на выделенном " +
                          "сервере: процесс обязан идти с -batchmode -nographics.");

                if (!dedicated)
                {
                    result.Summary = "конфигурация не выделенный сервер, прогон недействителен";
                    yield break;
                }

                // Первым делом: иначе он сам расставит игроков по зонам спавна.
                DisableDebugOrchestrator();

                // ── 2. Клиент ─────────────────────────────────────────────
                deadline = Now + ClientWait;
                while (SessionCount() < 1 && Now < deadline)
                    yield return null;

                bool clientOk = SessionCount() >= 1;
                result.Set(CheckClient, clientOk,
                    clientOk
                        ? $"сессий на сервере: {SessionCount()}"
                        : $"за {ClientWait:F0} с клиент не подключился. Смотри client-1.log рядом с этим файлом.");

                if (!clientOk)
                {
                    result.Summary = "клиент не подключился, вердикт вынести нельзя";
                    yield break;
                }

                SessionManager sessionManager = SessionManager.Instance;
                if (sessionManager == null || MapLoader.Instance == null || AvatarManager.Instance == null)
                {
                    result.Set(CheckFirstMap, false,
                        $"SessionManager={(sessionManager == null ? "null" : "есть")}, " +
                        $"MapLoader={(MapLoader.Instance == null ? "null" : "есть")}, " +
                        $"AvatarManager={(AvatarManager.Instance == null ? "null" : "есть")}");
                    result.Summary = "менеджеры не поднялись, прогон недействителен";
                    yield break;
                }

                // Режим выбирается ради валидных индексов команд: ChangeAvatar молча
                // выходит, если TeamRegistry не знает индекс команды.
                sessionManager.SetSession(firstMap, "elimination");
                yield return null;

                GameModeData mode = sessionManager.SelectedGameModeData;
                if (mode == null || mode.teams == null || mode.teams.Length < 2)
                {
                    result.Set(CheckFirstMap, false,
                        "режим не отдал две команды — точку замера между базами вычислить нечем " +
                        $"(команд: {(mode == null || mode.teams == null ? 0 : mode.teams.Length)})");
                    result.Summary = "режим без двух команд, вердикт вынести нельзя";
                    yield break;
                }

                string deviceToken = AssignFirstTeam(mode);
                GameLog.Debug.Info($"[E2E] Игрок готов: {deviceToken}");

                // ── 3. Первая карта ───────────────────────────────────────
                MapLoader.Instance.LoadMap(firstMap);

                E2EWaitOutcome onFirstMap = new E2EWaitOutcome();
                yield return E2EWait.Until(onFirstMap,
                    $"сервер переехал на первую карту '{firstMap}' и пересоздал аватар",
                    MapWait,
                    () => SceneManager.GetActiveScene().name == firstMap && FirstSessionHasAvatar(),
                    DescribeSessions,
                    () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

                result.Set(CheckFirstMap, onFirstMap.Succeeded, onFirstMap.Diagnosis);

                if (!onFirstMap.Succeeded)
                {
                    result.Summary = "первая карта не собралась, вердикт вынести нельзя";
                    yield break;
                }

                // ── 4. Годность пары карт и точки замера ──────────────────
                PlayerSession session = FirstSession();
                TeamData team = session != null ? session.Team : null;

                Vector3 probeFirst;
                string probeDiagnosis = string.Empty;
                if (team == null || !TryArenaProbePoint(team, out probeFirst, out probeDiagnosis))
                {
                    result.Set(CheckAnchors, false,
                        $"точку замера на '{firstMap}' не вычислить: " +
                        (team == null ? "команда игроку не назначена" : probeDiagnosis));
                    result.Summary = "точку замера не вычислить, вердикт вынести нельзя";
                    yield break;
                }

                PhysicalSpaceAnchorFrame firstFrame;
                string firstFrameDiagnosis;
                bool firstFrameOk = PhysicalSpaceAnchorFrame.TryBuildFromScene(out firstFrame, out firstFrameDiagnosis);

                TeamSpawnZone zoneFirst = AvatarSpawnPointResolver.FindZone(team);
                float probeToZoneFirst = zoneFirst != null
                    ? Vector3.Distance(probeFirst, zoneFirst.transform.position)
                    : 0f;

                // ── 5. Рукопожатие обратного канала ───────────────────────
                SetPhase(PhaseRaise);

                E2EWaitOutcome raised = new E2EWaitOutcome();
                yield return E2EWait.Until(raised,
                    "клиент поднял флаг наблюдения",
                    HandshakeWait,
                    () => session != null && session.HasGrabbedDogTag,
                    DescribeSessions,
                    () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

                GameLog.Debug.Info($"[E2E] Обратный канал: {raised.Diagnosis}");

                // ══════════════════════════════════════════════════════════
                //  Круг 1: игрок БЕЗ калибровки
                // ══════════════════════════════════════════════════════════

                yield return DisplaceAvatar(session, probeFirst);

                Vector3 staleWorldFirst = session.ActiveAvatar != null
                    ? session.ActiveAvatar.transform.position
                    : probeFirst;

                GameLog.Debug.Info(
                    $"[E2E] Круг 1 (без калибровки): игрок стоит в {Fmt(staleWorldFirst)} на '{firstMap}', " +
                    $"до зоны своей команды {probeToZoneFirst:F2} м. Рву соединение.");

                yield return DropAndChangeMap(session, secondMap);

                E2EWaitOutcome back1 = new E2EWaitOutcome();
                yield return E2EWait.Until(back1,
                    $"клиент вернулся на '{secondMap}' и получил аватар",
                    ReturnWait,
                    () => SceneManager.GetActiveScene().name == secondMap && FirstSessionHasAvatar(),
                    () => DescribeSessions() + $" Подключений: {NetworkServer.connections.Count}.");

                if (!back1.Succeeded)
                {
                    result.Set(CheckPlainZone, false,
                        back1.Diagnosis + " Клиент не вернулся — вердикт по CAL-02 вынести нельзя. " +
                        "Первое, что стоит посмотреть в client-1.log, — не завис ли возврат так, " +
                        "как описывает NET-20.");
                    result.Summary = "клиент не вернулся после первого разрыва";
                    yield break;
                }

                yield return E2EWait.Hold(SettleHold);

                session = FirstSession();
                team = session != null ? session.Team : null;

                Vector3 probeSecond;
                if (team == null || !TryArenaProbePoint(team, out probeSecond, out probeDiagnosis))
                {
                    result.Set(CheckAnchors, false,
                        $"точку замера на '{secondMap}' не вычислить: " +
                        (team == null ? "команда игроку не назначена" : probeDiagnosis));
                    result.Summary = "точку замера на второй карте не вычислить, вердикт вынести нельзя";
                    yield break;
                }

                PhysicalSpaceAnchorFrame secondFrame;
                string secondFrameDiagnosis;
                bool secondFrameOk = PhysicalSpaceAnchorFrame.TryBuildFromScene(out secondFrame, out secondFrameDiagnosis);

                float congruence = firstFrameOk && secondFrameOk
                    ? Vector3.Distance(firstFrame.ToLocal(probeFirst), secondFrame.ToLocal(probeSecond))
                    : float.PositiveInfinity;
                float worldShift = Vector3.Distance(probeFirst, probeSecond);

                bool anchorsOk = firstFrameOk && secondFrameOk
                                 && congruence <= ArenaCongruence
                                 && worldShift > PositionTolerance;

                result.Set(CheckAnchors, anchorsOk,
                    anchorsOk
                        ? $"'{firstMap}': {firstFrame}; '{secondMap}': {secondFrame}. Одна и та же точка арены " +
                          $"относительно якорей — {Fmt(firstFrame.ToLocal(probeFirst))} и " +
                          $"{Fmt(secondFrame.ToLocal(probeSecond))}, расхождение {congruence:F3} м. " +
                          $"В мировых координатах она же разъехалась на {worldShift:F2} м " +
                          $"({Fmt(probeFirst)} против {Fmt(probeSecond)}) — вот почему замер ниже " +
                          "различает «своё место в арене» и «старая мировая точка»."
                        : !firstFrameOk
                            ? $"на '{firstMap}' система координат якорей не построена: {firstFrameDiagnosis}"
                            : !secondFrameOk
                                ? $"на '{secondMap}' система координат якорей не построена: {secondFrameDiagnosis}"
                                : worldShift <= PositionTolerance
                                    ? $"точка замера на обеих картах пришлась почти в одно и то же место мира " +
                                      $"({Fmt(probeFirst)} и {Fmt(probeSecond)}, {worldShift:F2} м). На такой паре " +
                                      "карт наивное сохранение мировой позиции прошло бы проверку — прогон " +
                                      "ничего не доказывает. Ровно этим и плох центр арены: он пивот её поворота."
                                    : $"якоря карт разъехались с ареной: расхождение {congruence:F2} м " +
                                      $"при допуске {ArenaCongruence:F2} м. Одна калибровка на сессию " +
                                      "на такой паре карт работать не может.");

                if (!anchorsOk)
                {
                    result.Summary = "карты непригодны как пара, вердикт вынести нельзя";
                    yield break;
                }

                TeamSpawnZone zoneSecond = AvatarSpawnPointResolver.FindZone(team);
                Vector3 zoneSecondPos = zoneSecond != null ? zoneSecond.transform.position : Vector3.zero;

                Vector3 plainWorld = session.ActiveAvatar != null
                    ? session.ActiveAvatar.transform.position
                    : Vector3.zero;

                float plainToZone = Vector3.Distance(plainWorld, zoneSecondPos);
                float plainToStale = Vector3.Distance(plainWorld, staleWorldFirst);
                float plainToProbe = Vector3.Distance(plainWorld, probeSecond);

                bool plainOk = session.ActiveAvatar != null && zoneSecond != null
                               && plainToZone <= PositionTolerance;

                result.Set(CheckPlainZone, plainOk,
                    (plainOk
                        ? $"игрок вернулся в зону своей команды: {Fmt(plainWorld)}, до зоны {plainToZone:F2} м. " +
                          $"До старой мировой точки с '{firstMap}' {plainToStale:F2} м, до своего прежнего места " +
                          $"в арене {plainToProbe:F2} м — то есть его поставила игра, а не сохранённая позиция."
                        : $"игрок оказался в {Fmt(plainWorld)}: до зоны своей команды {plainToZone:F2} м " +
                          $"при допуске {PositionTolerance:F1} м. " +
                          (plainToStale <= PositionTolerance
                              ? $"Зато до старой мировой точки с '{firstMap}' — {plainToStale:F2} м. Это CAL-02: " +
                                "позиция с прошлой карты применяется дословно и всем подряд, хотя игрок " +
                                "не калибровался и его место должна назначать игра."
                              : plainToProbe <= PositionTolerance
                                  ? "Игрок оказался на своём прежнем месте в арене, хотя калибровку не объявлял: " +
                                    "ветка «не откалиброван» перестала вести в зону."
                                  : "Ни зона, ни старая мировая точка, ни прежнее место в арене — " +
                                    "ищи, кто ещё двигает аватар.")) +
                    $" Зона '{(zoneSecond == null ? "не найдена" : zoneSecond.name)}' в {Fmt(zoneSecondPos)}, " +
                    $"старая мировая точка {Fmt(staleWorldFirst)}, прежнее место в арене {Fmt(probeSecond)}.");

                // ══════════════════════════════════════════════════════════
                //  Круг 2: тот же игрок, но ОТКАЛИБРОВАННЫЙ
                // ══════════════════════════════════════════════════════════

                SetPhase(PhaseDeclare);

                E2EWaitOutcome declared = new E2EWaitOutcome();
                yield return E2EWait.Until(declared,
                    "сессия отмечена как откалиброванная",
                    DeclareWait,
                    () => FirstSession() != null && FirstSession().IsCalibrated,
                    DescribeSessions,
                    () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

                result.Set(CheckDeclared, declared.Succeeded,
                    declared.Succeeded
                        ? $"клиент объявил калибровку: {DescribeSessions()}"
                        : declared.Diagnosis + " CmdSetCalibrated до сервера не доехал — " +
                          "вторую ветку правила проверять нечем.");

                if (!declared.Succeeded)
                {
                    result.Summary = "признак калибровки до сервера не доехал";
                    yield break;
                }

                session = FirstSession();

                yield return DisplaceAvatar(session, probeSecond);

                Vector3 staleWorldSecond = session.ActiveAvatar != null
                    ? session.ActiveAvatar.transform.position
                    : probeSecond;

                Vector3 placeInAnchors = secondFrame.ToLocal(staleWorldSecond);

                GameLog.Debug.Info(
                    $"[E2E] Круг 2 (откалиброван): игрок стоит в {Fmt(staleWorldSecond)} на '{secondMap}', " +
                    $"относительно якорей {Fmt(placeInAnchors)}. Рву соединение.");

                yield return DropAndChangeMap(session, firstMap);

                E2EWaitOutcome back2 = new E2EWaitOutcome();
                yield return E2EWait.Until(back2,
                    $"клиент вернулся на '{firstMap}' и получил аватар",
                    ReturnWait,
                    () => SceneManager.GetActiveScene().name == firstMap && FirstSessionHasAvatar(),
                    () => DescribeSessions() + $" Подключений: {NetworkServer.connections.Count}.");

                if (!back2.Succeeded)
                {
                    result.Set(CheckKeptPlace, false,
                        back2.Diagnosis + " Клиент не вернулся во второй раз — вердикт по второй ветке " +
                        "вынести нельзя.");
                    result.Summary = "клиент не вернулся после второго разрыва";
                    yield break;
                }

                yield return E2EWait.Hold(SettleHold);

                session = FirstSession();
                team = session != null ? session.Team : null;

                Vector3 expectedBack;
                if (team == null || !TryArenaProbePoint(team, out expectedBack, out probeDiagnosis))
                {
                    result.Set(CheckKeptPlace, false,
                        $"точку замера на '{firstMap}' не пересчитать: " +
                        (team == null ? "команда игроку не назначена" : probeDiagnosis));
                    result.Summary = "точку замера не пересчитать, вердикт вынести нельзя";
                    yield break;
                }

                TeamSpawnZone zoneBack = AvatarSpawnPointResolver.FindZone(team);
                Vector3 zoneBackPos = zoneBack != null ? zoneBack.transform.position : Vector3.zero;

                Vector3 keptWorld = session.ActiveAvatar != null
                    ? session.ActiveAvatar.transform.position
                    : Vector3.zero;

                float keptToPlace = Vector3.Distance(keptWorld, expectedBack);
                float keptToZone = Vector3.Distance(keptWorld, zoneBackPos);
                float keptToStale = Vector3.Distance(keptWorld, staleWorldSecond);

                bool keptOk = session.ActiveAvatar != null
                              && session.IsCalibrated
                              && keptToPlace <= PositionTolerance;

                result.Set(CheckKeptPlace, keptOk,
                    (keptOk
                        ? $"откалиброванный игрок вернулся на своё место в арене: {Fmt(keptWorld)}, " +
                          $"до ожидаемой точки {keptToPlace:F2} м. До зоны своей команды {keptToZone:F2} м, " +
                          $"до старой мировой точки с '{secondMap}' {keptToStale:F2} м."
                        : $"игрок оказался в {Fmt(keptWorld)}: до своего места {keptToPlace:F2} м при допуске " +
                          $"{PositionTolerance:F1} м. " +
                          (keptToStale <= PositionTolerance
                              ? $"Зато до старой мировой точки с '{secondMap}' — {keptToStale:F2} м. Это CAL-02: " +
                                "поза едет в мировых координатах, а арена на этих картах повёрнута на 90° — " +
                                "игрока развернуло вместе с ней."
                              : keptToZone <= PositionTolerance
                                  ? "Игрока отправили в зону команды, хотя он откалиброван: его место задано " +
                                    "физически, и переносить его на базу значит расклеить картинку с телом."
                                  : "Ни своё место, ни зона, ни старая мировая точка — ищи, кто ещё двигает аватар.")) +
                    $" Откалиброван={session.IsCalibrated}. Ждали {Fmt(expectedBack)}, " +
                    $"зона '{(zoneBack == null ? "не найдена" : zoneBack.name)}' в {Fmt(zoneBackPos)}, " +
                    $"старая мировая точка {Fmt(staleWorldSecond)}. До разрыва стоял относительно якорей " +
                    $"в {Fmt(placeInAnchors)}, сейчас — {Fmt(firstFrame.ToLocal(keptWorld))}.");

                // ── Годность замера ───────────────────────────────────────
                float placeToZone = Vector3.Distance(expectedBack, zoneBackPos);
                bool branchesOk = placeToZone >= MinSeparation && worldShift >= PositionTolerance * 2f;

                result.Set(CheckBranches, branchesOk,
                    branchesOk
                        ? $"место в арене и зона команды разведены на {placeToZone:F2} м, а мировые координаты " +
                          $"одной и той же точки арены на двух картах — на {worldShift:F2} м. Значит зелёные " +
                          "проверки выше различали три разных ответа, а не совпали случайно."
                        : $"место в арене отстоит от зоны команды на {placeToZone:F2} м при нужных " +
                          $"{MinSeparation:F1} м, мировое расхождение точки между картами {worldShift:F2} м. " +
                          "На таком разведении «своё место», «зона» и «старая мировая точка» неразличимы, " +
                          "и проверки выше ничего не значили бы.");

                result.Summary = result.AllChecksGreen
                    ? "место из сообщения подключения подчиняется правилу калибровки и переживает смену карты — " +
                      "CAL-02 не воспроизводится"
                    : "есть красные проверки, см. detail";

                SetPhase(PhaseDone);
                yield return WaitForClientVerdict();
            }
            finally
            {
                // Клиент ждёт PhaseDone, чтобы записать вердикт. Ставим его на любом
                // выходе, включая ранний yield break.
                SetPhase(PhaseDone);
            }
        }

        /// <summary>
        /// Рвёт соединение с клиентом и, когда сессия снялась с учёта, меняет карту.
        ///
        /// <para>
        /// Порядок важен: карту меняем <b>после</b> разрыва, чтобы снимок отключённой
        /// сессии был снят на старой карте — именно та ситуация, в которой мировая
        /// позиция врёт.
        /// </para>
        /// </summary>
        private IEnumerator DropAndChangeMap(PlayerSession session, string nextMap)
        {
            NetworkConnectionToClient connection = session != null ? session.connectionToClient : null;
            if (connection != null)
            {
                GameLog.Debug.Info($"[E2E] Рву соединение connId={connection.connectionId}");
                connection.Disconnect();
            }

            E2EWaitOutcome dropped = new E2EWaitOutcome();
            yield return E2EWait.Until(dropped,
                "сессия снялась с учёта после разрыва",
                DropWait,
                () => SessionCount() == 0,
                DescribeSessions);

            GameLog.Debug.Info($"[E2E] Разрыв: {dropped.Diagnosis}. Меняю карту на '{nextMap}'.");

            MapLoader.Instance.LoadMap(nextMap);

            E2EWaitOutcome onMap = new E2EWaitOutcome();
            yield return E2EWait.Until(onMap,
                $"сервер переехал на '{nextMap}'",
                MapWait,
                () => SceneManager.GetActiveScene().name == nextMap,
                () => $"активная сцена='{SceneManager.GetActiveScene().name}'");

            GameLog.Debug.Info($"[E2E] Смена карты: {onMap.Diagnosis}");
        }

        /// <summary>Держит серверный процесс живым, пока клиент не запишет вердикт.</summary>
        private static IEnumerator WaitForClientVerdict()
        {
            E2EWaitOutcome barrier = new E2EWaitOutcome();

            yield return E2EWait.Until(barrier,
                "клиент отчитался, что записал вердикт",
                ClientVerdictWait,
                () => FirstSession() != null && !FirstSession().HasGrabbedDogTag,
                DescribeSessions);

            GameLog.Debug.Info($"[E2E] Барьер клиентского вердикта: {barrier.Diagnosis}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession, CheckClientReturns);

            DisableDebugOrchestrator();

            // ── Первое подключение ────────────────────────────────────────
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
                    ? $"подключён, deviceToken='{PlayerPrefs.GetString("DeviceToken", "(нет)")}'"
                    : $"за 60 с подключиться не удалось: ни Discovery, ни прямое подключение к '{context.ServerAddress}'");

            if (!connected)
            {
                result.Summary = "клиент не подключился";
                yield break;
            }

            deadline = Now + 60f;
            while (PlayerSession.LocalSession == null && Now < deadline)
                yield return null;

            bool hasSession = PlayerSession.LocalSession != null;
            result.Set(CheckClientSession, hasSession,
                hasSession
                    ? $"PlayerSession.LocalSession netId={PlayerSession.LocalSession.netId}"
                    : "за 60 с сервер не создал PlayerSession для этого клиента");

            if (!hasSession)
            {
                result.Summary = "сессии нет, наблюдать нечем";
                yield break;
            }

            // ── Главный цикл: слушаем фазы, переживаем разрывы ────────────
            _clientReturns = 0;

            uint servedSession = 0;   // сессия, которой уже подняли флаг
            uint declaredOn = 0;      // сессия, на которой уже объявили калибровку
            bool wasConnected = true;
            bool sawDone = false;

            float hardDeadline = Now + Mathf.Max(60f, context.Timeout - 30f);

            while (Now < hardDeadline && !sawDone)
            {
                if (!NetworkClient.isConnected)
                {
                    if (wasConnected)
                    {
                        wasConnected = false;
                        GameLog.Debug.Info($"[E2E] Разрыв замечен. {DescribeNetwork()}");
                        yield return ReturnToServer(context);

                        wasConnected = NetworkClient.isConnected;
                        if (wasConnected) _clientReturns++;

                        GameLog.Debug.Info($"[E2E] Возврат №{_clientReturns}: подключён={wasConnected}. {DescribeNetwork()}");
                    }

                    yield return null;
                    continue;
                }

                wasConnected = true;

                PlayerSession local = PlayerSession.LocalSession;
                if (local == null)
                {
                    yield return null;
                    continue;
                }

                int phase = local.Score;

                if (phase >= PhaseRaise && NetworkClient.ready && servedSession != local.netId && !local.HasGrabbedDogTag)
                {
                    local.CmdSetDogTagGrabbed(true);

                    if (local.HasGrabbedDogTag)
                        servedSession = local.netId;
                }
                else if (local.HasGrabbedDogTag)
                {
                    servedSession = local.netId;
                }

                // Объявление калибровки — подмена результата процедуры по якорям.
                if (phase >= PhaseDeclare && NetworkClient.ready && declaredOn != local.netId)
                {
                    local.CmdSetCalibrated(true);

                    if (local.IsCalibrated)
                    {
                        declaredOn = local.netId;
                        GameLog.Debug.Info($"[E2E] Клиент объявил калибровку на сессии netId={local.netId}");
                    }
                }

                if (phase >= PhaseDone)
                    sawDone = true;

                yield return null;
            }

            bool returnsOk = _clientReturns >= 2;
            result.Set(CheckClientReturns, returnsOk,
                returnsOk
                    ? $"клиент вернулся тем же устройством {_clientReturns} раза — оба круга прогона состоялись"
                    : $"возвратов после разрыва: {_clientReturns}, а прогон требует двух. {DescribeNetwork()}. " +
                      "Вердикт сервера при этом недостоверен: он мерил бы место игрока, которого нет.");

            result.Summary = result.AllChecksGreen
                ? "клиент дважды ушёл и дважды вернулся тем же устройством; куда его поставили, судит сервер"
                : "клиент не выдержал прогон, вердикт сервера недостоверен";

            // Флаг опускается последним: по нему сервер понимает, что вердикт записан.
            if (PlayerSession.LocalSession != null && NetworkClient.ready)
                PlayerSession.LocalSession.CmdSetDogTagGrabbed(false);
        }

        /// <summary>
        /// Возврат после разрыва.
        ///
        /// <para>
        /// Подключаться сразу нельзя. Mirror обрабатывает разрыв по шагам: гасит клиента,
        /// выносит <c>NetworkManager</c> из <c>DontDestroyOnLoad</c> и грузит сцену
        /// <c>Offline</c>, которая поднимает новую копию ветки менеджеров.
        /// <c>StartClient</c>, вызванный в промежутке, уходит в уже обречённый менеджер,
        /// а <c>NetworkClient</c> статический — <c>connectState</c> навсегда остаётся
        /// в <c>Connecting</c>. Поэтому ждём смены экземпляра менеджера и окончания
        /// загрузки сцены. Подробности — находка NET-20.
        /// </para>
        /// </summary>
        private IEnumerator ReturnToServer(E2EContext context)
        {
            NetworkManager managerBeforeDrop = NetworkManager.singleton;

            float deadline = Now + 45f;
            while (Now < deadline)
            {
                bool replaced = NetworkManager.singleton != null &&
                                !ReferenceEquals(NetworkManager.singleton, managerBeforeDrop);

                if (replaced && NetworkManager.loadingSceneAsync == null && !NetworkClient.active)
                    break;

                yield return null;
            }

            // Сцена Offline подняла свою копию менеджеров — дирижёр отладки среди них.
            DisableDebugOrchestrator();

            deadline = Now + 90f;
            float nextAttempt = 0f;
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
                    GameLog.Debug.Warning("[E2E] connectState завис в Connecting — сбрасываю NetworkClient.Shutdown()");
                    NetworkClient.Shutdown();
                    connectingSince = -1f;
                    nextAttempt = 0f;
                }

                yield return null;
            }

            // Сессию сервер создаёт в ответ на GamePlayerConnectMessage, а его клиент
            // шлёт после загрузки карты: ждём именно сессию, а не только соединение.
            deadline = Now + 90f;
            while (PlayerSession.LocalSession == null && Now < deadline)
                yield return null;
        }

        /// <summary>Прямое подключение мимо Discovery — быстрее и детерминированнее UDP-броадкаста.</summary>
        private static void ConnectDirectly(string address)
        {
            Mirror.Discovery.NetworkDiscovery discovery = UnityEngine.Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>();
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

            GameLog.Debug.Info($"[E2E] Подключаюсь к '{NetworkManager.singleton.networkAddress}'");
            NetworkManager.singleton.StartClient();
        }

        private static string DescribeNetwork()
        {
            NetworkManager manager = NetworkManager.singleton;
            return $"NetworkManager={(manager == null ? "null" : manager.mode.ToString())}, " +
                   $"менеджер живой={(manager == null ? "нет" : manager.isActiveAndEnabled.ToString())}, " +
                   $"active={NetworkClient.active}, connected={NetworkClient.isConnected}, " +
                   $"ready={NetworkClient.ready}, isLoadingScene={NetworkClient.isLoadingScene}, " +
                   $"LocalSession={(PlayerSession.LocalSession == null ? "null" : PlayerSession.LocalSession.netId.ToString())}, " +
                   $"сцена='{SceneManager.GetActiveScene().name}'";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное — сервер
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Уводит игрока в заданную точку игровым способом — <c>PlayerController.Respawn</c>.
        ///
        /// <para>
        /// Прямая запись <c>transform.position</c> на сервере ненадёжна:
        /// <c>NetworkTransform</c> на аватарах стоит с <c>syncDirection = ClientToServer</c>,
        /// и владелец вернёт свою позицию поверх серверной ближайшим же пакетом. Поэтому
        /// сначала <c>ServerDevTeleport</c> — он рассылает <c>RpcDevTeleport</c>, и настоящий переезд
        /// делает сам владелец. Заодно это единственный способ заставить клиента заметить
        /// своё новое место: <c>UxrAvatar.GlobalAvatarMoved</c> поднимает
        /// <c>UxrManager.MoveAvatarTo</c>, а не запись в трансформ.
        /// </para>
        /// </summary>
        private static IEnumerator DisplaceAvatar(PlayerSession session, Vector3 target)
        {
            PlayerController avatar = session != null ? session.ActiveAvatar : null;
            if (avatar == null) yield break;

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

            GameLog.Debug.Info($"[E2E] Увод игрока на точку замера: {moved.Diagnosis}");

            if (!moved.Succeeded && session.ActiveAvatar != null)
            {
                GameLog.Debug.Info("[E2E] Владелец не отчитался — ставлю аватар на место записью на сервере");
                session.ActiveAvatar.transform.position = target;
            }

            UnityEngine.Object.Destroy(marker);

            yield return E2EWait.Hold(SettleHold);
        }

        /// <summary>Назначает игроку первую команду режима. Возвращает его deviceToken — для лога.</summary>
        private static string AssignFirstTeam(GameModeData mode)
        {
            PlayerSession session = FirstSession();
            if (session == null || mode.teams.Length == 0) return "(нет сессии)";

            TeamData team = mode.teams[0];
            if (team != null) session.TeamIndex = team.teamIndex;

            return $"{session.PlayerName}, токен='{session.DeviceToken}', команда=" +
                   $"{(team != null ? team.displayName : "?")}({session.TeamIndex})";
        }

        /// <summary>Раздаёт номер фазы клиенту через SyncVar <c>PlayerSession.Score</c>.</summary>
        private static void SetPhase(int phase)
        {
            if (PlayersManager.Instance == null) return;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null) session.Score = phase;
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

        private static bool FirstSessionHasAvatar()
        {
            PlayerSession session = FirstSession();
            return session != null && session.ActiveAvatar != null;
        }

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

        private static string DescribeSessions()
        {
            if (PlayersManager.Instance == null) return "PlayersManager отсутствует";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                PlayerController avatar = session.ActiveAvatar;
                parts.Add($"{session.PlayerName} (netId={session.netId}, токен='{session.DeviceToken}'): " +
                          $"команда={session.TeamIndex}, откалиброван={session.IsCalibrated}, флаг={session.HasGrabbedDogTag}, " +
                          (avatar == null ? "аватара нет" : $"аватар в {Fmt(avatar.transform.position)}"));
            }

            return (parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "сессий нет") +
                   $" Активная сцена='{SceneManager.GetActiveScene().name}'.";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Общее
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Вторая карта прогона. Пара <c>TestMap1</c> / <c>TestMap2</c> взята намеренно:
        /// это одна и та же арена, повёрнутая на 90°, — то есть та самая ситуация,
        /// в которой мировая позиция врёт, а позиция относительно якорей нет.
        /// </summary>
        private static string SecondMapFor(string firstMap)
        {
            if (string.Equals(firstMap, "TestMap1", StringComparison.OrdinalIgnoreCase)) return "TestMap2";
            if (string.Equals(firstMap, "TestMap2", StringComparison.OrdinalIgnoreCase)) return "TestMap1";

            return firstMap;
        }

        /// <summary>
        /// Точка замера — середина между зонами спавна, сдвинутая на
        /// <see cref="ProbeOffset"/> метров в сторону чужой базы.
        ///
        /// <para>
        /// Эталон вычисляется <b>из зон</b>, а не из якорей: сравнивать место игрока
        /// с тем же кодом, который его и посчитал, значит проверять код им самим.
        /// Зоны ставит художник, и к калибровке они отношения не имеют.
        /// </para>
        ///
        /// <para>
        /// Сдвиг обязателен: центр арены — пивот её поворота, он у двух карт общий
        /// и в мировых координатах тоже, и наивное сохранение мировой позиции прошло бы
        /// проверку в центре (находка T-30). Направление сдвига задано командой игрока —
        /// «от своей базы к чужой», — а не порядком объектов на сцене: только так одна
        /// и та же точка арены получается на обеих картах.
        /// </para>
        /// </summary>
        private static bool TryArenaProbePoint(TeamData ownTeam, out Vector3 probe, out string diagnosis)
        {
            probe = Vector3.zero;

            TeamSpawnZone[] zones = UnityEngine.Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.InstanceID);

            if (zones.Length != 2)
            {
                diagnosis = $"на сцене '{SceneManager.GetActiveScene().name}' зон спавна {zones.Length}, " +
                            "а точка замера определена только для пары";
                return false;
            }

            if (ownTeam == null)
            {
                diagnosis = "команда не назначена — направление «от своей базы к чужой» не определено";
                return false;
            }

            TeamSpawnZone own = zones[0].Team == ownTeam
                ? zones[0]
                : zones[1].Team == ownTeam ? zones[1] : null;

            if (own == null)
            {
                diagnosis = $"зоны команды '{ownTeam.displayName}' на сцене нет";
                return false;
            }

            TeamSpawnZone other = own == zones[0] ? zones[1] : zones[0];

            Vector3 ownPos = own.transform.position;
            Vector3 otherPos = other.transform.position;

            float between = Vector3.Distance(ownPos, otherPos);
            if (between < MinSeparation * 2f)
            {
                diagnosis = $"зоны спавна разведены всего на {between:F2} м — точку замера не отличить от самих зон";
                return false;
            }

            Vector3 center = (ownPos + otherPos) * 0.5f;
            Vector3 toOther = Vector3.Scale(otherPos - ownPos, new Vector3(1f, 0f, 1f)).normalized;

            probe = center + toOther * ProbeOffset;
            diagnosis = string.Empty;
            return true;
        }

        private static float Now => Time.realtimeSinceStartup;

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
        }

        private static void DisableDebugOrchestrator()
        {
            DebugOrchestrator orchestrator = UnityEngine.Object.FindFirstObjectByType<DebugOrchestrator>();
            if (orchestrator == null || !orchestrator.enabled)
                return;

            orchestrator.enabled = false;
            GameLog.Debug.Info("[E2E] DebugOrchestrator отключён: дирижёром прогона выступает сценарий");
        }
    }
}
#endif
