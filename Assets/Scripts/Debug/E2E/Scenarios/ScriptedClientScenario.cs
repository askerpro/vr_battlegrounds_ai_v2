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
    /// </list>
    /// </summary>
    public sealed class ScriptedClientScenario : IE2EScenario
    {
        public const string SmokeName = "scripted-input-smoke";
        public const string PurchaseName = "scripted-wall-purchase";

        private const string CheckClients = "сервер: клиенты подключены обычными соединениями, ботов нет";
        private const string CheckWalked = "сервер: голова каждого клиента ушла от корня (поза реплицирована)";
        private const string CheckTeams = "сервер: команды выбраны клиентами, обе команды заняты";
        private const string CheckLive = "сервер: матч начат эквивалентом кнопки админа";
        private const string CheckEquipment = "сервер: фаза Equipment — все в своих зонах, стены открыты";
        private const string CheckWalls = "сервер: у каждого клиента своя стена";
        private const string CheckPurchase = "сервер: касса провела покупку каждого клиента";

        private readonly string _name;
        private readonly string _script;
        private readonly List<EconomyTransaction> _transactions = new List<EconomyTransaction>();

        public ScriptedClientScenario(string name, string script)
        {
            _name = name;
            _script = script;
        }

        public string Name => _name;

        public IEnumerator Run(E2EContext context, E2EResult result)
        {
            if (context.IsServerSide)
                return _name == PurchaseName ? RunPurchaseServer(context, result) : RunSmokeServer(context, result);

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
            result.Declare(CheckClients, CheckTeams, CheckLive, CheckEquipment, CheckWalls, CheckPurchase);
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

            // Эквивалент кнопки админа «Начать»: то же правило доступности и тот же вызов, без проверки прав сессии.
            bool available = AdminMapCommands.IsAvailable(MapCommand.GoLive);
            bool live = available && MapReferee.Instance != null && MapReferee.Instance.GoLive();
            result.Set(CheckLive, live, $"IsAvailable(GoLive)={available}, GoLive()={live}");
            if (!live) Abort(result, "матч не начат");

            EliminationMode mode = null;
            float deadline = Now + 30f;
            while ((mode = MapReferee.Instance.ActiveGameMode as EliminationMode) == null && Now < deadline)
                yield return null;
            if (mode == null) Abort(result, "ActiveGameMode не стал EliminationMode");

            MatchEconomy.TransactionServer += OnTransaction;
            try
            {
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
                yield return E2EBarrier.WaitClients(context, "done", 90f);
            }
            finally
            {
                MatchEconomy.TransactionServer -= OnTransaction;
            }

            result.Summary = result.AllChecksGreen
                ? "скриптовые клиенты купили оружие со своих стен вводом, касса провела покупки"
                : "см. проверки";
        }

        private void CheckPurchases(E2EResult result, Dictionary<string, string> grabbed, Dictionary<PlayerSession, int> before)
        {
            var report = new List<string>();
            bool ok = grabbed != null;
            var byNetId = new Dictionary<uint, string>();
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
                report.Add($"{role}: netId={netId} '{(item != null ? item.name : "нет")}', держит " +
                           $"{(holder != null ? holder.PlayerName : "никто")} (соединение {(human ? "есть" : "нет")}), " +
                           $"покупка {(purchase.HasValue ? $"{purchase.Value.Delta} ({purchase.Value.Detail})" : "нет")}, " +
                           $"деньги {start}→{money}, чек {(receipt ? "есть" : "нет")}, в слоте {(inSlot ? "да" : "нет")}");
            }

            ok &= byNetId.Count >= 2;
            result.Set(CheckPurchase, ok, string.Join("; ", report));
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
