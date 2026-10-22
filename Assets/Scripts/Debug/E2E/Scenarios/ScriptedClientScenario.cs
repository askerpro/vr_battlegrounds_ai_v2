// Ярус C (несколько процессов) — сценарии скриптовых клиентов: ввод шлема и контроллеров симулируется, всё
// остальное — штатный путь игрока (контракт tasks/bots-control-modes/contracts/xr-input.md).
// Отключить из сборки: VRBG_NO_E2E.
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.E2E.Input;
using VrBattlegrounds.Economy;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий обычных сетевых клиентов со скриптовым вводом. Клиент проигрывает JSON-сценарий
    /// (<see cref="InputScript"/>, встроенный <c>Assets/Scripts/Debug/E2E/Input/Scripts/&lt;script&gt;.json</c> или
    /// <c>-e2eArg script=&lt;путь&gt;</c>); сервер — оракул: проверяет то, что видит он сам. Сервер ничего не делает
    /// за игроков: команда, ходьба, хват и покупка — только вводом клиентов.
    ///
    /// <list type="bullet">
    /// <item><c>scripted-input-smoke</c> (S + 1..3): ходьба головой, точность поз; сервер видит голову клиента
    ///       ушедшей от корня — поза реплицирована обычным путём игрока.</item>
    /// <item><c>scripted-wall-purchase</c> (S + 2): команды выбирают клиенты, старт — серверный эквивалент кнопки
    ///       админа (<c>AdminMapCommands.IsAvailable</c> + <c>MapReferee.GoLive</c>: у выделенного сервера своей сессии
    ///       нет, у клиентов стенда нет прав админа); клиенты идут в свои зоны, к своим стенам, берут оружие по Grip;
    ///       сервер проверяет кассу: деньги, чек, слот, кто держит.</item>
    /// <item><c>scripted-combat-hit</c> (S + 2): закупка пистолетов как в <c>scripted-wall-purchase</c>, возврат в зоны,
    ///       Combat (готовность пока по пределу ожидания режима), клиент 1 наводит ствол на клиента 2 и жмёт Trigger;
    ///       сервер видит повтор выстрела на оружии стрелка, урон по жертве от его актора и падение здоровья.</item>
    /// <item><c>scripted-combat-round</c> (S + 2): то же, но готовность — жетоны, снятые рукой, клиент 1 стреляет до
    ///       смерти клиента 2; сервер проверяет готовность не по пределу ожидания, PlayerKilled и смену тела жертвы,
    ///       награду за убийство, Resolution со счётом 1:0 и деньгами за раунд.</item>
    /// <item><c>scripted-admin-pause</c> (S + 2): матч начинает клиент-админ командой планшета, не-админу отказ,
    ///       пауза и продолжение админом.</item>
    /// <item><c>scripted-series-next-map</c> (S + 2): серия из двух карт клиентом-админом, «Следующая карта»
    ///       (<c>ScriptedClientScenario.Series.cs</c>).</item>
    /// </list>
    /// </summary>
    public sealed partial class ScriptedClientScenario : IE2EScenario
    {
        public const string SmokeName = "scripted-input-smoke";
        public const string PurchaseName = "scripted-wall-purchase";
        public const string CombatHitName = "scripted-combat-hit";
        public const string CombatRoundName = "scripted-combat-round";
        public const string CombatSeriesName = "scripted-combat-next-round";
        public const string AdminPauseName = "scripted-admin-pause";

        private const string CheckClients = "сервер: клиенты подключены обычными соединениями, ботов нет";
        private const string CheckWalked = "сервер: голова каждого клиента ушла от корня (поза реплицирована)";
        private const string CheckTeams = "сервер: команды выбраны клиентами, обе команды заняты";
        private const string CheckLive = "сервер: матч начат эквивалентом кнопки админа";
        private const string CheckEquipment = "сервер: фаза Equipment — все в своих зонах, стены открыты";
        private const string CheckWalls = "сервер: у каждого клиента своя стена";
        private const string CheckPurchase = "сервер: касса провела покупку каждого клиента";
        private const string CheckCombat = "сервер: бой начался (Combat, оружие включено), стрелок и жертва живы";
        private const string CheckShotReplayed = "сервер: выстрел стрелка повторён сервером на его оружии";
        private const string CheckDamage = "сервер: урон по жертве от актора стрелка, здоровье упало";
        private const string CheckReadiness = "сервер: готовность жетонами — оба сняли жетон, бой не по пределу ожидания";
        private const string CheckKill = "сервер: жертва убита стрелком — PlayerKilled, выбыла, тело заменено";
        private const string CheckKillReward = "сервер: касса начислила стрелку награду за убийство оружием из покупки";
        private const string CheckRound = "сервер: Resolution — раунд и счёт за командой стрелка, деньги за раунд";
        private const string CheckNextRound = "сервер: раунд 2 — Equipment, готовность сброшена, оба живы в своих зонах, руки пусты";
        private const string CheckCarryOver = "сервер: деньги раунда 1 перенесены в раунд 2 без сброса";
        private const string CheckRebuy = "сервер: касса провела покупку каждого клиента в раунде 2";
        private const string CheckReadyAgain = "сервер: готовность раунда 2 — снова жетонами, бой не по пределу ожидания";
        private const string CheckAdminRights = "сервер: права админа — у одного клиента (режим отладки), у другого нет";
        private const string CheckLiveByAdmin = "сервер: матч начал клиент-админ командой планшета GoLive";
        private const string CheckAdminDenied = "сервер: команда не-админа отклонена, карта не изменилась";
        private const string CheckPaused = "сервер: пауза админа — разминка, все живы, руки пусты, экономики нет";
        private const string CheckResumed = "сервер: продолжение — раунд переигран, деньги на начало раунда, готовность сброшена";

        private const string ShooterRole = "client-1";
        private const string TargetRole = "client-2";

        private readonly string _name;
        private readonly string _script;
        private readonly List<EconomyTransaction> _transactions = new List<EconomyTransaction>();
        private readonly Dictionary<string, Bought> _bought = new Dictionary<string, Bought>();
        private readonly List<string> _combatLog = new List<string>();
        private readonly List<UxrDamageEventArgs> _damage = new List<UxrDamageEventArgs>();
        private readonly List<(PlayerSession victim, PlayerSession killer)> _kills = new List<(PlayerSession, PlayerSession)>();
        private readonly List<string> _adminLog = new List<string>();
        private int _serverShots;

        /// <summary>Что купил клиент: предмет и сессия, которая его держит (по данным сервера).</summary>
        private struct Bought
        {
            public NetworkIdentity Item;
            public PlayerSession Holder;
        }

        public ScriptedClientScenario(string name, string script)
        {
            _name = name;
            _script = script;
        }

        public string Name => _name;

        public IEnumerator Run(E2EContext context, E2EResult result)
        {
            if (context.IsServerSide)
                return _name == SmokeName ? RunSmokeServer(context, result)
                    : _name == SeriesNextMapName ? RunSeriesServer(context, result)
                    : RunPurchaseServer(context, result);

            return RunClient(context, result);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Клиент
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");
            InputScript script = InputScript.Load(_script, context.Arg("script"), out string error);
            if (script == null)
            {
                result.Declare("сценарий загружен");
                result.Set("сценарий загружен", false, error);
                E2EBarrier.ReportClientFailure(context, error);
                result.Summary = error;
                yield break;
            }

            var runner = new ScriptedClientRunner(context, result, script);
            result.Declare(runner.Labels());
            yield return runner.Run();
            result.Summary = $"сценарий '{script.name}' пройден вводом устройства";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Сервер: смоук ввода
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunSmokeServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClients, CheckWalked);
            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");

            yield return E2EBarrier.WaitClients(context, "input-active", 90f);
            SetClients(result, context);

            Dictionary<string, string> walked = null;
            yield return E2EBarrier.WaitClients(context, "walked", 90f, onReached: d => walked = new Dictionary<string, string>(d));

            var report = new List<string>();
            bool ok = true;
            foreach (PlayerSession session in HumanSessions())
            {
                UxrAvatar avatar = AvatarOf(session);
                Transform camera = avatar != null ? avatar.CameraTransform : null;
                float offset = camera != null
                    ? Vector3.ProjectOnPlane(camera.position - avatar.transform.position, avatar.transform.up).magnitude
                    : -1f;
                bool moved = offset >= 1.5f;
                ok &= moved;
                report.Add($"{session.PlayerName}: голова от корня {F(offset)} м");
            }

            result.Set(CheckWalked, ok && report.Count >= context.ClientCount, string.Join("; ", report) + " (ждали ≥ 1,5 м)");
            context.ServerStep("walked-seen");

            yield return E2EBarrier.WaitClients(context, "smoke-done", 120f);
            result.Summary = result.AllChecksGreen ? "ввод клиентов дошёл до сервера обычным путём игрока" : "см. проверки";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Сервер: покупка со стены
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunPurchaseServer(E2EContext context, E2EResult result)
        {
            bool series = _name == CombatSeriesName;
            bool round = series || _name == CombatRoundName;
            bool combat = round || _name == CombatHitName;
            bool admin = _name == AdminPauseName;
            result.Declare(CheckClients, CheckTeams, admin ? CheckLiveByAdmin : CheckLive, CheckEquipment, CheckWalls, CheckPurchase);
            if (admin) result.Declare(CheckAdminRights, CheckAdminDenied, CheckPaused, CheckResumed);
            if (combat) result.Declare(CheckCombat, CheckShotReplayed, CheckDamage);
            if (round) result.Declare(CheckReadiness, CheckKill, CheckKillReward, CheckRound);
            if (series) result.Declare(CheckNextRound, CheckCarryOver, CheckRebuy, CheckReadyAgain);
            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");

            yield return E2EBarrier.WaitClients(context, "input-active", 90f);
            SetClients(result, context);

            yield return E2EBarrier.WaitClients(context, "team-requested", 90f);
            E2EWaitOutcome teams = new E2EWaitOutcome();
            yield return E2EWait.Until(teams, "клиенты в разных командах", 30f,
                () => DistinctTeams() >= 2 && AllHaveTeam(),
                () => DescribeTeams());
            result.Set(CheckTeams, teams.Succeeded, DescribeTeams() + " — " + teams.Diagnosis);
            if (!teams.Succeeded) Abort(result, "команды не выбраны — матч не стартует (minPlayers и обе команды)");

            _adminLog.Clear();
            Application.logMessageReceived += OnLog;
            MatchEconomy.TransactionServer += OnTransaction;
            try
            {
                if (admin)
                {
                    // Матч начинает клиент-админ кнопкой планшета; сервер только проверяет права и итог.
                    // Лог исполнения (уровень Info канала Match) — диагностика, не условие: уровень канала настраивается.
                    CheckRights(result);
                    context.ServerStep("teams-ok");
                    E2EWaitOutcome started = new E2EWaitOutcome();
                    yield return E2EWait.Until(started, "матч начат админом", 60f,
                        () => MapReferee.Instance != null && MapReferee.Instance.IsLive,
                        () => $"состояние {(MapReferee.Instance != null ? MapReferee.Instance.CurrentState.ToString() : "нет судьи")}");
                    PlayerSession adminSession = AdminSession();
                    bool logged = adminSession != null && HasAdminLog($"{adminSession.PlayerName}: {MapCommand.GoLive}.");
                    result.Set(CheckLiveByAdmin, started.Succeeded && adminSession != null,
                        $"{started.Diagnosis}; лог исполнения от {(adminSession != null ? adminSession.PlayerName : "?")}: {logged}");
                    if (!started.Succeeded) Abort(result, "админ не начал матч");
                }
                else
                {
                    // Эквивалент кнопки админа «Начать»: то же правило доступности и тот же вызов, без проверки прав сессии.
                    bool available = AdminMapCommands.IsAvailable(MapCommand.GoLive);
                    bool live = available && MapReferee.Instance != null && MapReferee.Instance.GoLive();
                    result.Set(CheckLive, live, $"IsAvailable(GoLive)={available}, GoLive()={live}");
                    if (!live) Abort(result, "матч не начат");
                }

                EliminationMode mode = null;
                float deadline = Now + 30f;
                while ((mode = MapReferee.Instance.ActiveGameMode as EliminationMode) == null && Now < deadline)
                    yield return null;
                if (mode == null) Abort(result, "ActiveGameMode не стал EliminationMode");

                context.ServerStep("match-live");

                E2EWaitOutcome equipment = new E2EWaitOutcome();
                yield return E2EWait.Until(equipment, "фаза Equipment", 150f,
                    () => mode.CurrentRoundPhase == RoundPhase.Equipment,
                    () => $"фаза {mode.CurrentRoundPhase}; {DescribeZones()}");
                result.Set(CheckEquipment, equipment.Succeeded, equipment.Diagnosis + "; " + DescribeZones());
                if (!equipment.Succeeded) Abort(result, "закупка не началась");

                var before = new Dictionary<PlayerSession, int>();
                foreach (PlayerSession session in HumanSessions())
                    before[session] = MatchEconomy.Current != null ? MatchEconomy.Current.GetMoney(session) : -1;

                E2EWaitOutcome walls = new E2EWaitOutcome();
                yield return E2EWait.Until(walls, "стены закреплены за клиентами", 10f, OwnWallsAssigned, DescribeWalls);
                result.Set(CheckWalls, walls.Succeeded, DescribeWalls());

                _transactions.Clear();
                context.ServerStep("equipment");

                Dictionary<string, string> grabbed = null;
                yield return E2EBarrier.WaitClients(context, "grabbed", 120f, onReached: d => grabbed = new Dictionary<string, string>(d));

                // Отказ кассы возвращает предмет в слот кадром позже — даём ему время проявиться.
                yield return E2EWait.Hold(1.5f);
                CheckPurchases(result, grabbed, before);

                context.ServerStep("oracle-done");
                if (combat) yield return RunCombatHit(context, result, mode, round);
                if (series) yield return RunNextRound(context, result, mode);
                if (admin) yield return RunAdminPause(context, result, mode, before);
                yield return E2EBarrier.WaitClients(context, "done", 90f);
            }
            finally
            {
                MatchEconomy.TransactionServer -= OnTransaction;
                Application.logMessageReceived -= OnLog;
            }

            result.Summary = !result.AllChecksGreen ? "см. проверки"
                : admin ? "клиент-админ вёл матч командами планшета: старт, пауза, продолжение; не-админу отказано"
                : series ? "два раунда вводом: убийство и Resolution, затем раунд 2 — возрождение, перенос денег, покупка, жетоны"
                : round ? "раунд сыгран вводом: жетоны, убийство курком, Resolution — счёт и деньги за командой стрелка"
                : combat ? "скриптовый клиент выстрелил курком в противника, сервер увидел урон от его актора"
                : "скриптовые клиенты купили оружие со своих стен вводом, касса провела покупки";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Сервер: бой — выстрел и урон
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunCombatHit(E2EContext context, E2EResult result, EliminationMode mode, bool round)
        {
            _bought.TryGetValue(ShooterRole, out Bought shooter);
            _bought.TryGetValue(TargetRole, out Bought target);
            if (shooter.Holder == null || target.Holder == null)
                Abort(result, $"стрелок ({ShooterRole}) или жертва ({TargetRole}) без купленного оружия в руках");

            yield return E2EBarrier.WaitClients(context, "in-zone", 60f);

            UxrProjectileSource source = shooter.Item != null ? shooter.Item.GetComponent<UxrProjectileSource>() : null;
            // В Resolution режим снимает оружие у всех (EquipmentStrip) — имя и netId фиксируются до боя.
            string weapon = shooter.Item != null
                ? $"{shooter.Item.name} netId={shooter.Item.netId.ToString(CultureInfo.InvariantCulture)}"
                : "нет";
            UxrActor shooterActor = ActorOf(shooter.Holder);
            UxrActor targetActor = ActorOf(target.Holder);
            if (source == null || shooterActor == null || targetActor == null)
                Abort(result, $"нет UxrProjectileSource у оружия стрелка или UxrActor у игроков: источник={source != null}, " +
                              $"стрелок={shooterActor != null}, жертва={targetActor != null}");

            _serverShots = 0;
            _damage.Clear();
            _combatLog.Clear();
            System.Action<int> onShot = _ => _serverShots++;
            System.EventHandler<UxrNonDamagingImpactEventArgs> onImpact = (_, e) =>
                Log($"снаряд {(e.WeaponOwner != null ? e.WeaponOwner.name : "?")} в {(e.RaycastHit.collider != null ? e.RaycastHit.collider.name : "?")}");
            _kills.Clear();
            System.Action<PlayerSession, PlayerSession, IReadOnlyList<PlayerSession>> onKilled = (victim, killer, _) =>
            {
                _kills.Add((victim, killer));
                Log($"убит {(victim != null ? victim.PlayerName : "?")}, убийца {(killer != null ? killer.PlayerName : "нет")}");
            };

            source.ShotFired += onShot;
            UxrWeaponManager.Instance.DamageReceiving += OnDamage;
            UxrWeaponManager.Instance.NonActorImpacted += onImpact;
            MapReferee.Instance.PlayerKilled += onKilled;
            try
            {
                float equipmentEnd = Now;
                E2EWaitOutcome fight = new E2EWaitOutcome();
                yield return E2EWait.Until(fight, "фаза Combat и оружие включено", 120f,
                    () => mode.CurrentRoundPhase == RoundPhase.Combat && UxrWeaponManager.Instance.WeaponSystemEnabled,
                    () => $"фаза {mode.CurrentRoundPhase}, оружие {(UxrWeaponManager.Instance.WeaponSystemEnabled ? "вкл" : "выкл")}; {DescribeZones()}");
                bool alive = shooter.Holder.ActiveAvatar != null && shooter.Holder.ActiveAvatar.IsAlive &&
                             target.Holder.ActiveAvatar != null && target.Holder.ActiveAvatar.IsAlive;
                bool limitExpired = mode.RoundPhases.Readiness.LimitExpired;
                result.Set(CheckCombat, fight.Succeeded && alive,
                    $"{fight.Diagnosis}; готовность по пределу ожидания={limitExpired}, " +
                    $"от зон до боя {F(Now - equipmentEnd)} с; {DescribeZones()}");
                if (round)
                    result.Set(CheckReadiness, !limitExpired && shooter.Holder.HasGrabbedDogTag && target.Holder.HasGrabbedDogTag,
                        $"предел ожидания истёк={limitExpired}; жетон снят: {shooter.Holder.PlayerName}={shooter.Holder.HasGrabbedDogTag}, " +
                        $"{target.Holder.PlayerName}={target.Holder.HasGrabbedDogTag}; от зон до боя {F(Now - equipmentEnd)} с");
                if (!fight.Succeeded || !alive) Abort(result, "бой не начался");

                PlayerController targetBody = target.Holder.ActiveAvatar;
                float healthBefore = targetActor.Life;
                context.ServerStep("combat");

                Dictionary<string, string> shot = null;
                yield return E2EBarrier.WaitClients(context, "shot", 90f, onReached: d => shot = new Dictionary<string, string>(d));
                // Урон применяет сервер при повторе выстрела — даём кадрам снаряда дойти.
                yield return E2EWait.Hold(1f);

                string reported = shot != null && shot.TryGetValue(ShooterRole, out string data) ? data : "нет";
                result.Set(CheckShotReplayed, _serverShots > 0,
                    $"ShotFired на {weapon}: {_serverShots}; клиент: {reported}");

                UxrDamageEventArgs hit = null;
                foreach (UxrDamageEventArgs e in _damage)
                    if (e.ActorTarget == targetActor && e.ActorSource == shooterActor && !e.IsCanceled) hit = e;

                float healthAfter = targetActor != null ? targetActor.Life : 0f;
                bool dropped = healthAfter < healthBefore;
                string part = hit != null && Hitbox.TryGetPart(hit.RaycastHit.collider, out HitZone zone) ? zone.ToString() : "?";
                result.Set(CheckDamage, hit != null && dropped,
                    $"{target.Holder.PlayerName}: здоровье {F(healthBefore)}→{F(healthAfter)}, " +
                    (hit != null ? $"урон {F(hit.Damage)} в {part} от {shooter.Holder.PlayerName}" : "урона от стрелка нет") +
                    $"; событий урона {_damage.Count}" + (_combatLog.Count > 0 ? "; " + string.Join("; ", _combatLog) : ""));

                if (!round)
                {
                    context.ServerStep("hit-verified");
                    yield break;
                }

                VerifyKill(result, shooter.Holder, target.Holder, targetBody);
                VerifyKillReward(result, shooter.Holder);

                E2EWaitOutcome resolution = new E2EWaitOutcome();
                yield return E2EWait.Until(resolution, "фаза Resolution", 15f,
                    () => mode.CurrentRoundPhase == RoundPhase.Resolution || mode.CurrentRoundPhase == RoundPhase.Scoreboard,
                    () => $"фаза {mode.CurrentRoundPhase}");
                // Деньги за раунд начисляются на входе в Resolution — тем же кадром; запас на доставку событий.
                yield return E2EWait.Hold(0.5f);
                VerifyRoundResult(result, mode, resolution, shooter.Holder, target.Holder);

                context.ServerStep("round-verified");
            }
            finally
            {
                source.ShotFired -= onShot;
                if (UxrWeaponManager.Instance != null)
                {
                    UxrWeaponManager.Instance.DamageReceiving -= OnDamage;
                    UxrWeaponManager.Instance.NonActorImpacted -= onImpact;
                }

                if (MapReferee.Instance != null) MapReferee.Instance.PlayerKilled -= onKilled;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Сервер: следующий раунд
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// После Resolution раунда 1: призраки вернулись в зоны и возродились, раунд 2 в Equipment с чистой
        /// готовностью и пустыми руками, деньги перенесены; клиенты покупают снова и снимают жетоны — бой раунда 2
        /// начинается по готовности, а не по пределу ожидания.
        /// </summary>
        private IEnumerator RunNextRound(E2EContext context, E2EResult result, EliminationMode mode)
        {
            var sessions = new List<PlayerSession>(HumanSessions());
            var afterRound = new Dictionary<PlayerSession, int>();
            foreach (PlayerSession session in sessions) afterRound[session] = Money(session);
            int firstRound = mode.CurrentRoundNumber;
            int transactionsBefore = _transactions.Count;

            E2EWaitOutcome next = new E2EWaitOutcome();
            yield return E2EWait.Until(next, $"раунд {firstRound + 1}: фаза Equipment", 120f,
                () => mode.CurrentRoundNumber == firstRound + 1 && mode.CurrentRoundPhase == RoundPhase.Equipment,
                () => $"раунд {mode.CurrentRoundNumber}, фаза {mode.CurrentRoundPhase}; {DescribeZones()}");

            bool ok = next.Succeeded;
            var report = new List<string>();
            foreach (PlayerSession session in sessions)
            {
                bool alive = session.ActiveAvatar != null && session.ActiveAvatar.IsAlive && !session.IsEliminated;
                bool reset = !session.ReadyState && !session.HasGrabbedDogTag;
                bool empty = HandsEmpty(session);
                ok &= alive && session.IsInSpawnZone && reset && empty;
                report.Add($"{session.PlayerName}: жив={alive}, в зоне={session.IsInSpawnZone}, готовность сброшена={reset}, " +
                           $"руки пусты={empty}");
            }

            result.Set(CheckNextRound, ok, $"{next.Diagnosis}; " + string.Join("; ", report));
            if (!next.Succeeded) Abort(result, "раунд 2 не начался");

            bool carried = true;
            var money = new List<string>();
            foreach (PlayerSession session in sessions)
            {
                int now = Money(session);
                carried &= now == afterRound[session];
                money.Add($"{session.PlayerName}: {afterRound[session]}→{now}");
            }

            var reasons = new List<string>();
            for (int i = transactionsBefore; i < _transactions.Count; i++)
                reasons.Add($"{_transactions[i].Reason} {_transactions[i].Delta:+0;-0}");
            result.Set(CheckCarryOver, carried,
                string.Join("; ", money) + $"; транзакции между раундами: {(reasons.Count > 0 ? string.Join(", ", reasons) : "нет")}");

            var before = new Dictionary<PlayerSession, int>();
            foreach (PlayerSession session in sessions) before[session] = Money(session);
            context.ServerStep("equipment-2");

            Dictionary<string, string> grabbed = null;
            yield return E2EBarrier.WaitClients(context, "grabbed-2", 120f, onReached: d => grabbed = new Dictionary<string, string>(d));
            yield return E2EWait.Hold(1.5f);
            CheckPurchases(result, grabbed, before, CheckRebuy);
            context.ServerStep("rebuy-verified");

            yield return E2EBarrier.WaitClients(context, "in-zone-2", 60f);
            E2EWaitOutcome fight = new E2EWaitOutcome();
            yield return E2EWait.Until(fight, $"раунд {firstRound + 1}: Countdown или Combat", 60f,
                () => mode.CurrentRoundNumber == firstRound + 1 &&
                      (mode.CurrentRoundPhase == RoundPhase.Countdown || mode.CurrentRoundPhase == RoundPhase.Combat),
                () => $"раунд {mode.CurrentRoundNumber}, фаза {mode.CurrentRoundPhase}; {DescribeZones()}");
            bool limit = mode.RoundPhases.Readiness.LimitExpired;
            bool tags = true;
            var ready = new List<string>();
            foreach (PlayerSession session in sessions)
            {
                tags &= session.HasGrabbedDogTag;
                ready.Add($"{session.PlayerName}: жетон={session.HasGrabbedDogTag}");
            }

            result.Set(CheckReadyAgain, fight.Succeeded && !limit && tags,
                $"{fight.Diagnosis}; предел ожидания истёк={limit}; " + string.Join(", ", ready));
            context.ServerStep("series-verified");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Сервер: команды админа
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Не-админ жмёт «Пауза» — отказ; админ ставит паузу (разминка, все живы, снаряжение изъято, экономики нет) и
        /// продолжает (новый режим, раунд переигран, деньги — на начало раунда, готовность сброшена).
        /// </summary>
        private IEnumerator RunAdminPause(E2EContext context, E2EResult result, EliminationMode mode,
            Dictionary<PlayerSession, int> roundStart)
        {
            PlayerSession adminSession = AdminSession();
            var sessions = new List<PlayerSession>(HumanSessions());
            PlayerSession other = sessions.Find(s => s != adminSession);
            int round = mode.CurrentRoundNumber;
            uint modeNetId = mode.netId;

            yield return E2EBarrier.WaitClients(context, "denied-tried", 60f);
            yield return E2EWait.Hold(0.5f);
            bool stillLive = MapReferee.Instance != null && MapReferee.Instance.IsLive && mode != null && mode.netId == modeNetId;
            bool denied = other != null && HasAdminLog($"{MapCommand.Pause} отклонена: {other.PlayerName} не админ");
            result.Set(CheckAdminDenied, stillLive && denied,
                $"{(other != null ? other.PlayerName : "?")}: Pause; карта Live={stillLive}; лог отказа={denied}; {AdminLogTail()}");
            context.ServerStep("denied-verified");

            yield return E2EBarrier.WaitClients(context, "paused", 60f);
            yield return E2EWait.Hold(0.5f);
            MapReferee referee = MapReferee.Instance;
            bool paused = referee != null && referee.IsPaused && referee.ActiveGameMode != null && referee.ActiveGameMode.IsWarmup;
            bool alive = true, empty = true;
            var report = new List<string>();
            foreach (PlayerSession session in sessions)
            {
                bool a = session.ActiveAvatar != null && session.ActiveAvatar.IsAlive && !session.IsEliminated;
                bool e = HandsEmpty(session);
                alive &= a;
                empty &= e;
                report.Add($"{session.PlayerName}: жив={a}, руки пусты={e}");
            }

            bool noEconomy = MatchEconomy.Current == null;
            bool pauseLogged = adminSession != null && HasAdminLog($"{adminSession.PlayerName}: {MapCommand.Pause}.");
            result.Set(CheckPaused, paused && alive && empty && noEconomy,
                $"Paused={paused}, режим {(referee != null && referee.ActiveGameMode != null ? referee.ActiveGameMode.GetType().Name : "нет")}, " +
                $"экономики нет={noEconomy}, лог={pauseLogged}; " + string.Join("; ", report));
            context.ServerStep("pause-verified");

            yield return E2EBarrier.WaitClients(context, "home", 90f);
            EliminationMode resumed = null;
            E2EWaitOutcome back = new E2EWaitOutcome();
            yield return E2EWait.Until(back, "матч продолжен: новый режим в Equipment", 90f,
                () => (resumed = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode as EliminationMode : null) != null &&
                      MapReferee.Instance.IsLive && resumed.CurrentRoundPhase == RoundPhase.Equipment,
                () => $"состояние {(MapReferee.Instance != null ? MapReferee.Instance.CurrentState.ToString() : "нет")}, " +
                      $"фаза {(resumed != null ? resumed.CurrentRoundPhase.ToString() : "нет режима")}; {DescribeZones()}");

            bool fresh = resumed != null && resumed.netId != modeNetId;
            bool replay = resumed != null && resumed.CurrentRoundNumber == round;
            bool money = true, reset = true;
            var state = new List<string>();
            foreach (PlayerSession session in sessions)
            {
                int now = Money(session);
                roundStart.TryGetValue(session, out int start);
                money &= now == start;
                reset &= !session.ReadyState && !session.HasGrabbedDogTag;
                state.Add($"{session.PlayerName}: деньги {now} (начало раунда {start}), готовность {session.ReadyState}");
            }

            bool resumeLogged = adminSession != null && HasAdminLog($"{adminSession.PlayerName}: {MapCommand.Resume}.");
            result.Set(CheckResumed, back.Succeeded && fresh && replay && money && reset,
                $"{back.Diagnosis}; новый режим={fresh}, раунд {(resumed != null ? resumed.CurrentRoundNumber : -1)} (был {round}), " +
                $"деньги на начало раунда={money}, готовность сброшена={reset}, лог={resumeLogged}; " +
                string.Join("; ", state));
            context.ServerStep("resume-verified");
        }

        /// <summary>Ровно у одного клиента права админа (выдал режим отладки), у другого — нет.</summary>
        private static void CheckRights(E2EResult result)
        {
            var parts = new List<string>();
            int admins = 0, players = 0;
            foreach (PlayerSession session in HumanSessions())
            {
                players++;
                if (SessionPermissions.IsAdmin(session)) admins++;
                parts.Add($"{session.PlayerName}: IsAdmin={session.IsAdmin}");
            }

            result.Set(CheckAdminRights, players >= 2 && admins == 1, string.Join(", ", parts));
        }

        private static PlayerSession AdminSession()
        {
            foreach (PlayerSession session in HumanSessions())
                if (SessionPermissions.IsAdmin(session)) return session;
            return null;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (condition != null && condition.Contains("[AdminMapCommands]") && _adminLog.Count < 64) _adminLog.Add(condition);
        }

        private bool HasAdminLog(string fragment)
        {
            foreach (string line in _adminLog)
                if (line.Contains(fragment)) return true;
            return false;
        }

        private string AdminLogTail() =>
            _adminLog.Count > 0 ? "лог: " + string.Join(" | ", _adminLog.GetRange(Mathf.Max(0, _adminLog.Count - 3), Mathf.Min(3, _adminLog.Count))) : "лога команд нет";

        private static int Money(PlayerSession session) =>
            MatchEconomy.Current != null && session != null ? MatchEconomy.Current.GetMoney(session) : -1;

        private static bool HandsEmpty(PlayerSession session)
        {
            UxrAvatar avatar = AvatarOf(session);
            if (avatar == null) return false;
            return !UxrGrabManager.Instance.GetObjectBeingGrabbed(avatar, UltimateXR.Core.UxrHandSide.Left, out _) &&
                   !UxrGrabManager.Instance.GetObjectBeingGrabbed(avatar, UltimateXR.Core.UxrHandSide.Right, out _);
        }

        /// <summary>Сервер объявил смерть жертвы от стрелка; жертва выбыла, тело сменилось (призрак).</summary>
        private void VerifyKill(E2EResult result, PlayerSession shooter, PlayerSession target, PlayerController targetBody)
        {
            bool announced = false;
            var kills = new List<string>();
            foreach ((PlayerSession victim, PlayerSession killer) in _kills)
            {
                kills.Add($"{(victim != null ? victim.PlayerName : "?")}←{(killer != null ? killer.PlayerName : "нет")}");
                announced |= victim == target && killer == shooter;
            }

            PlayerController body = target.ActiveAvatar;
            bool replaced = body != targetBody;
            result.Set(CheckKill, announced && target.IsEliminated && replaced,
                $"PlayerKilled: {(kills.Count > 0 ? string.Join(", ", kills) : "нет")}; {target.PlayerName}: выбыл={target.IsEliminated}, " +
                $"тело {(replaced ? "заменено" : "прежнее")} ({(body != null ? body.name : "нет")})");
        }

        /// <summary>Касса начислила стрелку награду за убийство — KillAward оружия в руке (по чеку покупки).</summary>
        private void VerifyKillReward(E2EResult result, PlayerSession shooter)
        {
            string key = MatchEconomy.KeyOf(shooter);
            EconomyTransaction? kill = null;
            EconomyTransaction? purchase = null;
            foreach (EconomyTransaction t in _transactions)
            {
                if (t.Player != key) continue;
                if (t.Reason == EconomyReason.Kill) kill = t;
                if (t.Reason == EconomyReason.Purchase) purchase = t;
            }

            WeaponInfo info = null;
            if (kill.HasValue)
                foreach (WeaponInfo candidate in Resources.FindObjectsOfTypeAll<WeaponInfo>())
                    if (candidate != null && candidate.DisplayName == kill.Value.Detail) info = candidate;

            int expected = info != null ? EconomyRules.KillReward(info.KillAward, false) : -1;
            bool ok = kill.HasValue && kill.Value.Delta > 0 && (info == null || kill.Value.Delta == expected) &&
                      purchase.HasValue && kill.Value.Detail == purchase.Value.Detail;
            result.Set(CheckKillReward, ok,
                kill.HasValue
                    ? $"{shooter.PlayerName}: Kill {kill.Value.Delta:+0;-0} за «{kill.Value.Detail}» (ждали {(info != null ? expected.ToString(CultureInfo.InvariantCulture) : "KillAward ?")}), " +
                      $"деньги после {kill.Value.Money}; куплено «{(purchase.HasValue ? purchase.Value.Detail : "нет")}»"
                    : $"{shooter.PlayerName}: транзакции Kill нет; транзакций {_transactions.Count}");
        }

        /// <summary>Resolution: раунд за командой стрелка, счёт 1:0, деньги за победу и поражение.</summary>
        private void VerifyRoundResult(E2EResult result, EliminationMode mode, E2EWaitOutcome resolution,
                                      PlayerSession shooter, PlayerSession target)
        {
            TeamData shooterTeam = TeamOf(mode, shooter.TeamIndex);
            TeamData targetTeam = TeamOf(mode, target.TeamIndex);
            TeamData winner = mode.RoundPhases.RoundWinner;
            int shooterScore = mode.GetScore(shooterTeam);
            int targetScore = mode.GetScore(targetTeam);

            int win = 0, loss = 0;
            foreach (EconomyTransaction t in _transactions)
            {
                if (t.Reason == EconomyReason.RoundWin && t.Player == MatchEconomy.KeyOf(shooter)) win = t.Delta;
                if (t.Reason == EconomyReason.RoundLoss && t.Player == MatchEconomy.KeyOf(target)) loss = t.Delta;
            }

            bool ok = resolution.Succeeded && winner != null && winner.teamIndex == shooter.TeamIndex &&
                      shooterScore == 1 && targetScore == 0 && win == EconomyRules.RoundWinReward && loss > 0;
            result.Set(CheckRound, ok,
                $"{resolution.Diagnosis}; победитель {(winner != null ? winner.teamIndex.ToString(CultureInfo.InvariantCulture) : "нет")} " +
                $"(стрелок в {shooter.TeamIndex}); счёт {shooterScore}:{targetScore}; RoundWin {win} (ждали {EconomyRules.RoundWinReward}), " +
                $"RoundLoss {loss}; раунд {mode.CurrentRoundNumber}");
        }

        private static TeamData TeamOf(EliminationMode mode, int teamIndex)
        {
            foreach (TeamRuntimeData state in mode.TeamStates.Values)
                if (state != null && state.Team != null && state.Team.teamIndex == teamIndex) return state.Team;
            return null;
        }

        private void OnDamage(object sender, UxrDamageEventArgs e)
        {
            _damage.Add(e);
            Log($"урон {F(e.Damage)} по {(e.ActorTarget != null ? e.ActorTarget.name : "?")} от " +
                $"{(e.ActorSource != null ? e.ActorSource.name : "?")}{(e.IsCanceled ? " (отменён)" : "")}");
        }

        private void Log(string line)
        {
            if (_combatLog.Count < 16) _combatLog.Add(line);
            GameLog.Debug.Info($"[E2E] [бой] {line}");
        }

        private static UxrActor ActorOf(PlayerSession session)
        {
            PlayerController controller = session != null ? session.ActiveAvatar : null;
            return controller != null ? controller.GetComponent<UxrActor>() : null;
        }

        private void CheckPurchases(E2EResult result, Dictionary<string, string> grabbed, Dictionary<PlayerSession, int> before,
                                    string check = CheckPurchase)
        {
            var report = new List<string>();
            bool ok = grabbed != null;
            var byNetId = new Dictionary<uint, string>();
            _bought.Clear();
            if (grabbed != null)
                foreach (KeyValuePair<string, string> pair in grabbed)
                    if (TryNetId(pair.Value, out uint netId)) byNetId[netId] = pair.Key;

            foreach (KeyValuePair<uint, string> pair in byNetId)
            {
                uint netId = pair.Key;
                string role = pair.Value;
                NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity);
                UxrGrabbableObject item = identity != null ? identity.GetComponent<UxrGrabbableObject>() : null;
                PlayerSession holder = null;
                if (item != null && UxrGrabManager.Instance.GetGrabbingHand(item, 0, out UxrGrabber grabber) && grabber.Avatar != null)
                {
                    PlayerController controller = grabber.Avatar.GetComponent<PlayerController>();
                    holder = controller != null ? controller.Session : null;
                }

                string key = holder != null ? MatchEconomy.KeyOf(holder) : null;
                EconomyTransaction? purchase = null;
                foreach (EconomyTransaction t in _transactions)
                    if (t.Player == key && t.Reason == EconomyReason.Purchase && t.Delta < 0) purchase = t;

                int money = holder != null && MatchEconomy.Current != null ? MatchEconomy.Current.GetMoney(holder) : -1;
                int start = holder != null && before.TryGetValue(holder, out int m) ? m : -1;
                bool receipt = MatchEconomy.Current != null && MatchEconomy.Current.HasReceipt(netId);
                bool inSlot = InAnySlot(identity);
                bool human = holder != null && holder.connectionToClient != null;
                bool paid = purchase.HasValue && money == start + purchase.Value.Delta;

                bool itemOk = item != null && holder != null && human && purchase.HasValue && paid && receipt && !inSlot;
                ok &= itemOk;
                if (itemOk) _bought[role] = new Bought { Item = identity, Holder = holder };
                report.Add($"{role}: netId={netId} '{(item != null ? item.name : "нет")}', держит " +
                           $"{(holder != null ? holder.PlayerName : "никто")} (соединение {(human ? "есть" : "нет")}), " +
                           $"покупка {(purchase.HasValue ? $"{purchase.Value.Delta} ({purchase.Value.Detail})" : "нет")}, " +
                           $"деньги {start}→{money}, чек {(receipt ? "есть" : "нет")}, в слоте {(inSlot ? "да" : "нет")}");
            }

            ok &= byNetId.Count >= 2;
            result.Set(check, ok, string.Join("; ", report));
        }

        private void OnTransaction(EconomyTransaction transaction) => _transactions.Add(transaction);

        // ── Состояние сервера (только чтение) ────────────────────────────────

        private static void SetClients(E2EResult result, E2EContext context)
        {
            int humans = 0, bots = 0;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;
                if (session.connectionToClient != null) humans++;
                else bots++;
            }

            result.Set(CheckClients, humans >= context.ClientCount && bots == 0,
                $"сессий с соединением {humans} (ждали {context.ClientCount}), без соединения {bots}");
        }

        private static IEnumerable<PlayerSession> HumanSessions()
        {
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
                if (session != null && session.connectionToClient != null && session.Role == GameRole.Player)
                    yield return session;
        }

        private static UxrAvatar AvatarOf(PlayerSession session)
        {
            PlayerController controller = session != null ? session.ActiveAvatar : null;
            return controller != null ? controller.GetComponent<UxrAvatar>() : null;
        }

        private static int DistinctTeams()
        {
            var teams = new HashSet<int>();
            foreach (PlayerSession session in HumanSessions()) teams.Add(session.TeamIndex);
            return teams.Count;
        }

        private static bool AllHaveTeam()
        {
            foreach (PlayerSession session in HumanSessions())
                if (session.TeamIndex <= 0) return false;
            return true;
        }

        private static string DescribeTeams()
        {
            var parts = new List<string>();
            foreach (PlayerSession session in HumanSessions()) parts.Add($"{session.PlayerName}→{session.TeamIndex}");
            return string.Join(", ", parts);
        }

        private static string DescribeZones()
        {
            var parts = new List<string>();
            foreach (PlayerSession session in HumanSessions())
                parts.Add($"{session.PlayerName}: в зоне={session.IsInSpawnZone}, выбыл={session.IsEliminated}");
            return string.Join(", ", parts);
        }

        private static bool OwnWallsAssigned()
        {
            var walls = new HashSet<uint>();
            foreach (PlayerSession session in HumanSessions())
            {
                ArsenalWallController wall = WallOf(session);
                if (wall == null || !walls.Add(wall.netId)) return false;
            }

            return walls.Count > 0;
        }

        private static string DescribeWalls()
        {
            var parts = new List<string>();
            foreach (PlayerSession session in HumanSessions())
            {
                ArsenalWallController wall = WallOf(session);
                parts.Add($"{session.PlayerName}: стена {(wall != null ? wall.netId.ToString(CultureInfo.InvariantCulture) + (wall.CanTrade ? " (торгует)" : " (закрыта)") : "нет")}");
            }

            return string.Join(", ", parts);
        }

        private static ArsenalWallController WallOf(PlayerSession session)
        {
            foreach (ArsenalWallController wall in Object.FindObjectsByType<ArsenalWallController>(FindObjectsSortMode.None))
                if (wall.OwnerSessionNetId == session.netId) return wall;
            return null;
        }

        private static bool InAnySlot(NetworkIdentity identity)
        {
            if (identity == null) return false;
            foreach (ArsenalWallController wall in Object.FindObjectsByType<ArsenalWallController>(FindObjectsSortMode.None))
                foreach (ArsenalSlotController slot in wall.Slots)
                    if (slot != null && slot.CurrentItem == identity.gameObject) return true;
            return false;
        }

        private static bool TryNetId(string data, out uint netId)
        {
            netId = 0;
            const string prefix = "netId=";
            return data != null && data.StartsWith(prefix, System.StringComparison.Ordinal) &&
                   uint.TryParse(data.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out netId) &&
                   netId != 0;
        }

        private static void Abort(E2EResult result, string reason)
        {
            result.Summary = reason;
            E2EBarrier.FailServer(reason);
            throw new E2EScenarioFailure("сервер", reason);
        }

        private static float Now => Time.realtimeSinceStartup;
        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
#endif
