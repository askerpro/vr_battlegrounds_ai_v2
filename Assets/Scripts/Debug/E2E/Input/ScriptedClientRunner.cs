// Ярус C (несколько процессов) — исполнитель сценария скриптового клиента (контракт
// tasks/bots-control-modes/contracts/xr-input.md). Отключить из сборки: VRBG_NO_E2E.
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Economy;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>
    /// Проигрывает <see cref="InputScript"/> на клиенте: намерения отдаёт <see cref="ScriptedPilot"/>, ожидает
    /// реплицированное состояние игры. Каждый шаг — проверка результата процесса с тем же именем
    /// (<see cref="InputStep.Label"/>); провал шага останавливает сценарий и сообщается серверу шагом <c>failed</c>.
    /// </summary>
    public sealed class ScriptedClientRunner
    {
        /// <summary>Пороги точности поз (веха 1, приёмка 2026-10-19).</summary>
        public const float HeadPositionTolerance = 0.01f;
        public const float HeadAngleTolerance = 0.1f;
        public const float WristPositionTolerance = 0.001f;

        private const float ReachTolerance = 0.01f;
        private const float ReachDistance = 0.55f;

        // Поза переноски грабера относительно глаз и курса: у пояса, чуть вперёд; ствол на 45° вниз.
        private const float CarrySide = 0.2f;
        private const float CarryDown = 0.5f;
        private const float CarryForward = 0.3f;
        private const float CarryPitch = 45f;
        private const float CarrySettleSeconds = 3f;

        /// <summary>Наименьшая ровность оружия (крен вокруг ствола до ≈ 25°) при переноске и выстреле.</summary>
        private const float UprightMin = 0.9f;

        /// <summary>Наибольший сдвиг головы при смене тела (смерть → призрак), м: шлем при этом не двигается.</summary>
        private const float GhostJumpMax = 0.3f;

        private readonly E2EContext _context;
        private readonly E2EResult _result;
        private readonly InputScript _script;
        private readonly ScriptedPilot _pilot = new ScriptedPilot();

        /// <summary>Оркестратор карты, на которой клиент был при последней проверке карты (<c>activate</c>, <c>expect-map</c>).</summary>
        private MapReferee _knownMap;

        /// <summary>netId предмета, взятого последним шагом <c>grab-wall-item</c>; 0 — ничего не взято.</summary>
        public uint GrabbedNetId { get; private set; }

        public ScriptedClientRunner(E2EContext context, E2EResult result, InputScript script)
        {
            _context = context;
            _result = result;
            _script = script;
        }

        /// <summary>Сводка стрельбы последнего шага <c>shoot-enemy</c>; null — не стрелял.</summary>
        public ScriptedGunner.Outcome LastShooting { get; private set; }

        /// <summary>Имена проверок шагов этого клиента — для <see cref="E2EResult.Declare"/>.</summary>
        public string[] Labels()
        {
            var labels = new List<string>();
            for (int i = 0; i < _script.steps.Length; i++)
                if (_script.steps[i].AppliesTo(_context.ClientIndex)) labels.Add(_script.steps[i].Label(i));
            return labels.ToArray();
        }

        public IEnumerator Run()
        {
            try
            {
                for (int i = 0; i < _script.steps.Length; i++)
                {
                    InputStep step = _script.steps[i];
                    if (!step.AppliesTo(_context.ClientIndex)) continue;
                    string label = step.Label(i);
                    GameLog.Debug.Info($"[E2E] Шаг {label}");
                    yield return Execute(step, label);
                }
            }
            finally
            {
                E2EScriptedInput.Deactivate();
                _pilot.Dispose();
            }
        }

        private IEnumerator Execute(InputStep step, string label)
        {
            switch (step.op)
            {
                case "activate": return Activate(step, label);
                case "measure-start": return Instant(() => { _pilot.ResetPrecision(); _pilot.SetMeasuring(true); Pass(label, "замер начат"); });
                case "measure-check": return Instant(() => CheckPrecision(label));
                case "walk": return Walk(step, label);
                case "turn": return Turn(step, label);
                case "choose-team": return ChooseTeam(step, label);
                case "grab-wall-item": return GrabWallItem(step, label);
                case "grab-dog-tag": return GrabDogTag(step, label);
                case "carry": return Carry(step, label);
                case "shoot-enemy": return ShootEnemy(step, label);
                case "expect-eliminated": return ExpectEliminated(step, label);
                case "admin-rights": return AdminRights(step, label);
                case "admin-command": return AdminCommand(step, label);
                case "admin-command-denied": return AdminCommandDenied(step, label);
                case "admin-start-series": return AdminStartSeries(step, label);
                case "expect-map": return ExpectMap(step, label);
                case "release": return Release(step, label);
                case "hold": return Hold(step, label);
                case "expect-held": return Instant(() => ExpectHeld(step, label));
                case "expect-peers-holding": return Instant(() => ExpectPeersHolding(label));
                case "step": return Instant(() => ReachStep(step, label));
                case "wait-server": return WaitServer(step, label);
                default: return Instant(() => Fail(label, $"неизвестная операция '{step.op}'"));
            }
        }

        // ── Ввод ─────────────────────────────────────────────────────────────

        private IEnumerator Activate(InputStep step, string label)
        {
            string refusal = E2EScriptedInput.Activate(_pilot);
            if (refusal != null) Fail(label, refusal);

            E2EWaitOutcome bound = new E2EWaitOutcome();
            yield return E2EWait.Until(bound, "скриптовый ввод подан своему аватару", step.timeoutS,
                () => E2EScriptedInput.Avatar != null && E2EScriptedInput.AppliedFrames > 10,
                () => $"аватар={(E2EScriptedInput.Avatar != null ? E2EScriptedInput.Avatar.name : "нет")}, " +
                      $"кадров={E2EScriptedInput.AppliedFrames}, отказ={E2EScriptedInput.Refusal ?? "нет"}",
                () => E2EScriptedInput.Refusal);
            _knownMap = MapReferee.Instance;
            Require(label, bound, $"аватар {Name(E2EScriptedInput.Avatar)}, рост глаз {F(_pilot.EyeHeight)} м");
        }

        private void CheckPrecision(string label)
        {
            ScriptedPilot.PrecisionStats p = _pilot.Precision;
            bool ok = p.Frames > 0 && p.HeadPosition <= HeadPositionTolerance && p.HeadAngle <= HeadAngleTolerance &&
                      p.WristPosition <= WristPositionTolerance;
            if (!ok) Fail(label, $"{p}; пороги: голова {HeadPositionTolerance * 100f} см/{HeadAngleTolerance}°, запястья {WristPositionTolerance * 1000f} мм");
            Pass(label, p.ToString());
        }

        // ── Перемещение ──────────────────────────────────────────────────────

        private IEnumerator Walk(InputStep step, string label)
        {
            PlayerSession session = PlayerSession.LocalSession;
            float deadline = Now + step.timeoutS;
            Vector3 start = _pilot.FloorPosition;

            if (step.target == "forward")
            {
                Vector3 direction = Quaternion.AngleAxis(_pilot.Yaw, Vector3.up) * Vector3.forward;
                Vector3 goal = start + direction * step.meters;
                while (!_pilot.StepTowards(goal, step.speed, Time.deltaTime))
                {
                    if (Now > deadline) Fail(label, $"не дошёл за {step.timeoutS:0} с: до цели {F(Distance(goal))} м");
                    yield return null;
                }

                Pass(label, $"прошёл {F(step.meters)} м головой, корень аватара на месте");
                yield break;
            }

            if (step.target == "own-spawn-zone" || step.target == "own-spawn-point")
            {
                bool toPoint = step.target == "own-spawn-point";
                TeamSpawnZone zone = null;
                while ((zone = OwnZone(session)) == null)
                {
                    if (Now > deadline) Fail(label, $"нет зоны своей команды (TeamIndex={(session != null ? session.TeamIndex : -1)})");
                    yield return null;
                }

                Vector3 goal = zone.SpawnPoint.position;
                bool arrived = false;
                float upright = 1f;
                bool carrying = false;
                while (session.IsEliminated || !session.IsInSpawnZone || (toPoint && !arrived))
                {
                    if (Now > deadline)
                        Fail(label, $"за {step.timeoutS:0} с не возродился в своей зоне: выбыл={session.IsEliminated}, " +
                                    $"в зоне={session.IsInSpawnZone}, до точки зоны {F(Distance(goal))} м");
                    arrived = _pilot.StepTowards(goal, step.speed, Time.deltaTime, 0.1f) || Distance(goal) <= 0.1f;
                    carrying |= HeldUpright(out float now);
                    upright = Mathf.Min(upright, now);
                    yield return null;
                }

                // Ввод «как у человека»: оружие несут ровно, не на боку и не вверх ногами.
                if (carrying && upright < UprightMin)
                    Fail(label, $"нёс оружие не ровно: ровность {F(upright)} < {F(UprightMin)}");
                Pass(label, $"в зоне команды {session.TeamIndex}{(toPoint ? $", в точке спавна ({F(Distance(goal))} м)" : "")}, " +
                            $"жив; аватар {Name(E2EScriptedInput.Avatar)}{(carrying ? $"; оружие в руке, ровность ≥ {F(upright)}" : "")}");
                yield break;
            }

            if (step.target == "own-wall")
            {
                ArsenalWallController wall = null;
                while ((wall = OwnWall(session)) == null)
                {
                    if (Now > deadline) Fail(label, "за отведённое время стена арсенала не закреплена за этим игроком");
                    yield return null;
                }

                ArsenalStationAnchor anchor = wall.GetComponent<ArsenalStationAnchor>();
                Vector3 goal = anchor != null ? anchor.StandingPosition : wall.transform.position;
                while (!_pilot.StepTowards(goal, step.speed, Time.deltaTime, 0.1f))
                {
                    if (Now > deadline) Fail(label, $"не дошёл до стены за {step.timeoutS:0} с: осталось {F(Distance(goal))} м");
                    yield return null;
                }

                Pass(label, $"у своей стены netId={wall.netId}, путь {F((goal - start).magnitude)} м по прямой");
                yield break;
            }

            Fail(label, $"неизвестная цель ходьбы '{step.target}'");
        }

        private IEnumerator Turn(InputStep step, string label)
        {
            Vector3 direction = Quaternion.AngleAxis(_pilot.Yaw + step.degrees, Vector3.up) * Vector3.forward;
            float deadline = Now + step.timeoutS;
            while (!_pilot.TurnTowards(direction, Time.deltaTime))
            {
                if (Now > deadline) Fail(label, "не повернулся вовремя");
                yield return null;
            }

            Pass(label, $"курс {F(_pilot.Yaw)}°");
        }

        // ── Команда ──────────────────────────────────────────────────────────

        private IEnumerator ChooseTeam(InputStep step, string label)
        {
            PlayerSession session = PlayerSession.LocalSession;
            if (session == null) Fail(label, "локальной сессии нет");

            List<int> teams = MapTeams();
            if (teams.Count == 0) Fail(label, "на карте нет зон спавна команд — выбирать не из чего");

            int team = teams[(_context.ClientIndex - 1 + teams.Count) % teams.Count];
            // Тот же Command, что у планшета (MenuTeamSelection.OnAvatarSelected); скин — первый.
            session.CmdRequestTeamChange(team, 0);

            E2EWaitOutcome accepted = new E2EWaitOutcome();
            yield return E2EWait.Until(accepted, $"сервер принял команду {team}", step.timeoutS,
                () => session != null && session.TeamIndex == team,
                () => $"TeamIndex={(session != null ? session.TeamIndex : -1)}");
            Require(label, accepted, $"команда {team} (команды карты: {string.Join(",", teams)})");
        }

        // ── Предметы ─────────────────────────────────────────────────────────

        private IEnumerator GrabWallItem(InputStep step, string label)
        {
            UxrHandSide side = Side(step);
            PlayerSession session = PlayerSession.LocalSession;
            ArsenalWallController wall = OwnWall(session);
            if (wall == null) Fail(label, "своей стены нет");

            float deadline = Now + step.timeoutS;
            while (!wall.CanTrade)
            {
                if (Now > deadline) Fail(label, $"стена не открылась для торговли: {wall.CurrentState}");
                yield return null;
            }

            ArsenalSlotController slot = CheapestAffordable(wall, session, step.item, out int money);
            if (slot == null) Fail(label, $"на стене нет доступного предмета{(string.IsNullOrEmpty(step.item) ? "" : $" '{step.item}'")} (денег {money})");

            UxrGrabbableObject item = slot.CurrentItem.GetComponent<UxrGrabbableObject>();
            NetworkIdentity identity = slot.CurrentItem.GetComponent<NetworkIdentity>();
            if (item == null || identity == null) Fail(label, $"предмет слота {slot.name} без UxrGrabbableObject/NetworkIdentity");

            yield return ReachAndGrab(item, side, step.speed, deadline, label);

            GrabbedNetId = identity.netId;
            Pass(label, $"{item.name} netId={identity.netId} цена {slot.Price}, денег до хвата {money}; хват по Grip {side}");
        }

        /// <summary>
        /// Готовность жетоном: снять жетон своей стены рукой <c>hand</c> (Grip) и отпустить. Готовность объявляет
        /// штатный путь стены; шаг ждёт её реплицированный признак у своей сессии.
        /// </summary>
        private IEnumerator GrabDogTag(InputStep step, string label)
        {
            UxrHandSide side = Side(step);
            PlayerSession session = PlayerSession.LocalSession;
            ArsenalWallController wall = OwnWall(session);
            if (wall == null) Fail(label, "своей стены нет");

            DogTagController tagController = wall.GetComponentInChildren<DogTagController>(true);
            if (tagController == null) Fail(label, $"у стены netId={wall.netId} нет панели жетона");

            // Жетон выдаёт сервер в рантайме: у клиента он появляется после спавна и привязки к крючку.
            float deadline = Now + step.timeoutS;
            UxrGrabbableObject tag = tagController.TagObject;
            while (tag == null || wall.CurrentState != ArsenalWallController.ArsenalState.Open || !tagController.IsTagOnRack || !tag.IsGrabbable)
            {
                if (Now > deadline)
                    Fail(label, tag == null
                        ? $"у стены netId={wall.netId} жетон не появился"
                        : $"жетон недоступен: стена {wall.CurrentState}, на крючке={tagController.IsTagOnRack}, можно взять={tag.IsGrabbable}");
                yield return null;
                tag = tagController.TagObject;
            }

            yield return ReachAndGrab(tag, side, step.speed, deadline, label);

            // Отпустить: жетон снят, готовность объявлена; на крючок его вернёт стена к следующей закупке.
            _pilot.Press(side, UxrInputButtons.Grip, false);
            yield return null;
            yield return null;
            _pilot.Rest(side);

            E2EWaitOutcome ready = new E2EWaitOutcome();
            yield return E2EWait.Until(ready, "сервер принял готовность", 10f,
                () => session.HasGrabbedDogTag && session.ReadyState,
                () => $"HasGrabbedDogTag={session.HasGrabbedDogTag}, ReadyState={session.ReadyState}");
            Require(label, ready, $"жетон снят рукой {side} со стены netId={wall.netId}");
        }

        /// <summary>Подойти на длину руки, довести грабер руки до точки хвата 0 предмета и взять по фронту Grip.</summary>
        private IEnumerator ReachAndGrab(UxrGrabbableObject item, UxrHandSide side, float speed, float deadline, string label)
        {
            // Подойти на длину руки и повернуться к предмету.
            while (true)
            {
                Vector3 toItem = Vector3.ProjectOnPlane(item.transform.position - _pilot.FloorPosition, Vector3.up);
                bool turned = _pilot.TurnTowards(toItem, Time.deltaTime, 2f);
                bool close = toItem.magnitude <= ReachDistance ||
                             _pilot.StepTowards(item.transform.position - toItem.normalized * (ReachDistance - 0.1f), speed, Time.deltaTime, 0.05f);
                if (turned && close) break;
                if (Now > deadline) Fail(label, $"не подошёл к {item.name}: {F(toItem.magnitude)} м");
                yield return null;
            }

            UxrAvatar avatar = E2EScriptedInput.Avatar;
            UxrGrabber grabber = avatar != null ? avatar.GetGrabber(side) : null;
            if (grabber == null) Fail(label, $"у аватара нет грабера {side}");

            // Grip отпущен хотя бы кадр: SDK берёт по фронту нажатия.
            _pilot.Press(side, UxrInputButtons.Grip, false);
            int settled = 0;
            while (settled < 3)
            {
                if (!item.ComputeRequiredGrabberTransform(grabber, 0, out Vector3 position, out Quaternion rotation))
                    Fail(label, $"SDK не дал позу грабера для {item.name}");

                _pilot.ReachGrabber(side, position, rotation);
                settled = _pilot.GrabberAtTarget(side, ReachTolerance) ? settled + 1 : 0;
                if (Now > deadline) Fail(label, $"кисть не дошла до точки хвата {item.name}: {F((grabber.transform.position - position).magnitude)} м");
                yield return null;
            }

            _pilot.Press(side, UxrInputButtons.Grip, true);

            E2EWaitOutcome grabbed = new E2EWaitOutcome();
            yield return E2EWait.Until(grabbed, $"{item.name} в руке {side}", 3f,
                () => grabber.GrabbedObject == item,
                () => $"в руке: {(grabber.GrabbedObject != null ? grabber.GrabbedObject.name : "ничего")}, " +
                      $"кисть от точки {F((grabber.transform.position - item.transform.position).magnitude)} м");
            if (!grabbed.Succeeded) Fail(label, grabbed.Diagnosis);
        }

        /// <summary>
        /// Опустить предмет в позу переноски и дождаться, пока рука до неё дойдёт: человек сначала опускает
        /// оружие, потом идёт, — шаги ходьбы проверяют, что оружие несут ровно.
        /// </summary>
        private IEnumerator Carry(InputStep step, string label)
        {
            UxrHandSide side = Side(step);
            UxrAvatar avatar = E2EScriptedInput.Avatar;
            UxrGrabber grabber = avatar != null ? avatar.GetGrabber(side) : null;
            if (grabber == null || grabber.GrabbedObject == null) Fail(label, $"в руке {side} нечего нести");

            UxrGrabbableObject held = grabber.GrabbedObject;
            CarryHeld(side);
            Transform frame = ScriptedGunner.AimFrame(held);
            float deadline = Now + CarrySettleSeconds;
            int stable = 0;
            float level = ScriptedGunner.Level(frame);
            while (stable < 3)
            {
                level = ScriptedGunner.Level(frame);
                stable = level >= UprightMin ? stable + 1 : 0;
                if (Now > deadline) Fail(label, $"{held.name} не дошёл до позы переноски за {CarrySettleSeconds:0.#} с: ровность {F(level)}");
                yield return null;
            }

            Pass(label, $"{held.name} в руке {side}: ствол вперёд-вниз, верх вверх (ровность {F(level)}); Grip держится");
        }

        /// <summary>
        /// Рука с предметом — в позу переноски, как у человека: ствол (или ось предмета) вперёд-вниз, верх вверх, рука
        /// у пояса и идёт за головой. Поза считается от того, как предмет сидит в руке; пустая рука — «опущена».
        /// </summary>
        private void CarryHeld(UxrHandSide side)
        {
            UxrAvatar avatar = E2EScriptedInput.Avatar;
            UxrGrabber grabber = avatar != null ? avatar.GetGrabber(side) : null;
            UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
            if (held == null)
            {
                _pilot.Rest(side);
                return;
            }

            Transform frame = ScriptedGunner.AimFrame(held);
            Quaternion carry = Quaternion.Euler(CarryPitch, 0f, 0f);
            Quaternion frameInGrabber = Quaternion.Inverse(grabber.transform.rotation) * frame.rotation;
            Quaternion grabberInHead = carry * Quaternion.Inverse(frameInGrabber);
            Vector3 position = new Vector3(side == UxrHandSide.Left ? -CarrySide : CarrySide, -CarryDown, CarryForward);
            _pilot.HoldObject(side, position, frame, carry, grabberInHead);
        }

        /// <summary>
        /// Есть ли оружие в руках и его наименьшая ровность (<see cref="ScriptedGunner.Level"/>) по рукам.
        /// </summary>
        private static bool HeldUpright(out float upright)
        {
            upright = 1f;
            bool any = false;
            UxrAvatar avatar = E2EScriptedInput.Avatar;
            if (avatar == null) return false;
            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                UxrGrabber grabber = avatar.GetGrabber(side);
                UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
                if (held == null || held.GetComponentInParent<UltimateXR.Mechanics.Weapons.UxrFirearmWeapon>() == null) continue;
                any = true;
                upright = Mathf.Min(upright, ScriptedGunner.Level(ScriptedGunner.AimFrame(held)));
            }

            return any;
        }

        // ── Бой ──────────────────────────────────────────────────────────────

        private IEnumerator ShootEnemy(InputStep step, string label)
        {
            var outcome = new ScriptedGunner.Outcome();
            LastShooting = outcome;
            bool untilKill = step.until == "kill";
            yield return new ScriptedGunner(_pilot).ShootEnemy(Side(step), step.meters > 0f ? step.meters : 5f, step.speed,
                                                              Mathf.Max(1, step.shots), untilKill, step.timeoutS, outcome);
            CarryHeld(Side(step));

            string seen = outcome.Seen.Count > 0 ? "; " + string.Join("; ", outcome.Seen) : "";
            if (!outcome.Hit || (untilKill && !outcome.Killed))
                Fail(label, (outcome.Failure ?? (untilKill ? "противник жив" : "попадания нет")) + seen);
            // Ввод «как у человека»: стреляют, держа оружие ровно, а не на боку.
            if (outcome.Upright < UprightMin)
                Fail(label, $"стрелял с оружием на боку: ровность {F(outcome.Upright)} < {F(UprightMin)}{seen}");
            if (outcome.CarryUpright < UprightMin)
                Fail(label, $"нёс оружие к цели не ровно: ровность {F(outcome.CarryUpright)} < {F(UprightMin)}{seen}");
            Pass(label, $"{outcome.Weapon} → {outcome.Victim}: нажатий {outcome.Presses}, выстрелов {outcome.Shots}, " +
                        $"{F(outcome.Distance)} м, здоровье {F(outcome.HealthBefore)}→{F(outcome.HealthAfter)}" +
                        $"{(outcome.Killed ? ", убит" : "")}; ровность: подход ≥ {F(outcome.CarryUpright)}, выстрелы ≥ {F(outcome.Upright)}{seen}");
        }

        /// <summary>Свой игрок выбыл: сессия выбывшая, ввод перенесён на новый аватар (призрак).</summary>
        private IEnumerator ExpectEliminated(InputStep step, string label)
        {
            PlayerSession session = PlayerSession.LocalSession;
            if (session == null) Fail(label, "локальной сессии нет");

            // Живое тело на старте шага; если игрок уже выбыл — смену тела не проверить, только выбывание.
            bool eliminatedAtStart = session.IsEliminated;
            UxrAvatar atStart = E2EScriptedInput.Avatar;
            UxrAvatar alive = eliminatedAtStart ? null : atStart;
            string aliveName = Name(alive); // тело уничтожается при смерти — имя фиксируется сейчас
            GameLog.Debug.Info($"[E2E] {label}: на старте выбыл={eliminatedAtStart}, аватар ввода {Name(atStart)}");

            // Голова до смерти: шлем при смене тела не двигается — игрок остаётся там, где погиб.
            Vector3 headBefore = Head();
            float lastAlive = Now;
            E2EWaitOutcome eliminated = new E2EWaitOutcome();
            yield return E2EWait.Until(eliminated, "свой игрок выбыл, ввод на новом аватаре", step.timeoutS,
                () =>
                {
                    if (!session.IsEliminated && E2EScriptedInput.Avatar == alive) { headBefore = Head(); lastAlive = Now; }
                    return session.IsEliminated && E2EScriptedInput.Avatar != null && E2EScriptedInput.Avatar != alive &&
                           E2EScriptedInput.AppliedFrames > 10;
                },
                () => $"выбыл={session.IsEliminated}, аватар ввода {Name(E2EScriptedInput.Avatar)} (живое тело {Name(alive)}), " +
                      $"кадров {E2EScriptedInput.AppliedFrames}");
            if (!eliminated.Succeeded) Fail(label, eliminated.Diagnosis);

            Vector3 headAfter = Head();
            float jump = Vector3.ProjectOnPlane(headAfter - headBefore, Vector3.up).magnitude;
            if (jump > GhostJumpMax)
                Fail(label, $"при смене тела голова сдвинулась на {F(jump)} м ({headBefore} → {headAfter}) — шлем так не двигается");
            Pass(label, $"выбыл, ввод перенесён с {aliveName} на {Name(E2EScriptedInput.Avatar)}; голова на месте гибели " +
                        $"(сдвиг {F(jump)} м, смерть через {F(Now - lastAlive)} с после последнего живого кадра)");
        }

        private static Vector3 Head()
        {
            UxrAvatar avatar = E2EScriptedInput.Avatar;
            return avatar != null && avatar.CameraTransform != null ? avatar.CameraTransform.position : Vector3.zero;
        }

        private IEnumerator Release(InputStep step, string label)
        {
            UxrHandSide side = Side(step);
            _pilot.Press(side, UxrInputButtons.Grip, false);
            yield return null;
            yield return null;
            _pilot.Rest(side);
            Pass(label, $"Grip {side} отпущен");
        }

        private IEnumerator Hold(InputStep step, string label)
        {
            yield return E2EWait.Hold(step.seconds);
            Pass(label, $"{F(step.seconds)} с");
        }

        private void ExpectHeld(InputStep step, string label)
        {
            UxrHandSide side = Side(step);
            UxrAvatar avatar = E2EScriptedInput.Avatar;
            UxrGrabber grabber = avatar != null ? avatar.GetGrabber(side) : null;
            UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
            if (held == null) Fail(label, $"в руке {side} ничего нет");
            Pass(label, $"в руке {side}: {held.name}");
        }

        private void ExpectPeersHolding(string label)
        {
            PlayerSession local = PlayerSession.LocalSession;
            var report = new List<string>();
            int peers = 0;
            foreach (PlayerSession session in Object.FindObjectsByType<PlayerSession>(FindObjectsSortMode.None))
            {
                if (session == null || session == local || session.Role != GameRole.Player) continue;
                peers++;

                PlayerController controller = session.ActiveAvatar;
                UxrAvatar avatar = controller != null ? controller.GetComponent<UxrAvatar>() : null;
                if (avatar == null) Fail(label, $"у {session.PlayerName} нет аватара на этом клиенте");

                bool holding = UxrGrabManager.Instance.GetObjectBeingGrabbed(avatar, UxrHandSide.Right, out UxrGrabbableObject held) ||
                               UxrGrabManager.Instance.GetObjectBeingGrabbed(avatar, UxrHandSide.Left, out held);
                if (!holding || held == null) Fail(label, $"этот клиент не видит предмета в руках {session.PlayerName}");

                UxrGrabber hand = UxrGrabManager.Instance.GetGrabbingHand(held, 0, out UxrGrabber grabber) ? grabber : null;
                float gap = hand != null ? (held.transform.position - hand.transform.position).magnitude : float.NaN;
                if (hand == null || gap > 0.3f) Fail(label, $"{held.name} у {session.PlayerName}: рука {Name(hand)}, до предмета {F(gap)} м");
                report.Add($"{session.PlayerName}: {held.name} (до руки {F(gap)} м)");
            }

            if (peers == 0) Fail(label, "других игроков на клиенте нет");
            Pass(label, string.Join("; ", report));
        }

        // ── Админ ────────────────────────────────────────────────────────────

        /// <summary>
        /// Права админа скриптовому клиенту: режим отладки этого процесса (как меню редактора); права выдаёт
        /// штатный сервер (<c>DebugModeNetwork</c> по <c>DebugAdminPolicy</c>) — шаг ждёт реплицированный
        /// <c>PlayerSession.IsAdmin</c>. Решение пользователя 2026-10-22: админ — один из сетевых клиентов, права скриптом.
        /// </summary>
        private IEnumerator AdminRights(InputStep step, string label)
        {
            PlayerSession session = PlayerSession.LocalSession;
            if (session == null) Fail(label, "локальной сессии нет");

            DebugMode.Set(true, "e2e: скриптовый клиент-админ");
            E2EWaitOutcome granted = new E2EWaitOutcome();
            yield return E2EWait.Until(granted, "сервер выдал права админа", step.timeoutS,
                () => session.IsAdmin,
                () => $"DebugMode={DebugMode.Enabled}, IsAdmin={session.IsAdmin}");
            Require(label, granted, $"{session.PlayerName}: режим отладки включён, сервер выдал права");
        }

        /// <summary>
        /// Команда админа — тот же Command, что кнопка планшета (<c>MenuMatchManager</c>): дождаться, когда кнопка имеет
        /// смысл (<see cref="AdminMapCommands.IsAvailable(MapCommand)"/>), отправить и дождаться реплицированного итога.
        /// </summary>
        private IEnumerator AdminCommand(InputStep step, string label)
        {
            PlayerSession session = PlayerSession.LocalSession;
            MapCommand command = Command(step, label);
            if (session == null || !session.IsAdmin) Fail(label, $"{command}: у клиента нет прав админа");

            E2EWaitOutcome available = new E2EWaitOutcome();
            yield return E2EWait.Until(available, $"кнопка {command} имеет смысл", step.timeoutS,
                () => AdminMapCommands.IsAvailable(command),
                () => $"состояние карты {Describe()}");
            if (!available.Succeeded) Fail(label, available.Diagnosis);

            MapState before = MapReferee.Instance != null ? MapReferee.Instance.CurrentState : MapState.Warmup;
            int times = Mathf.Max(1, step.repeat);
            for (int i = 0; i < times; i++) session.CmdAdminMapCommand(command);
            string sent = times > 1 ? $"{command} ×{times} одним кадром" : command.ToString();

            if (!TryExpected(command, out MapState expected))
            {
                Pass(label, $"{sent} отправлена из состояния {before}");
                yield break;
            }

            E2EWaitOutcome applied = new E2EWaitOutcome();
            yield return E2EWait.Until(applied, $"{command}: карта в состоянии {expected}", step.timeoutS,
                () => MapReferee.Instance != null && MapReferee.Instance.CurrentState == expected,
                () => $"состояние карты {Describe()}");
            Require(label, applied, $"{command}: {before} → {expected}");
        }

        /// <summary>
        /// «Новая серия» планшета (<c>MenuSessionSetup</c>): тот же Command <c>CmdAdminStartSeries</c>; шаг ждёт
        /// реплицированную серию с этими картами. Загрузку первой карты проверяет следующий шаг <c>expect-map</c>.
        /// </summary>
        private IEnumerator AdminStartSeries(InputStep step, string label)
        {
            PlayerSession session = PlayerSession.LocalSession;
            if (session == null || !session.IsAdmin) Fail(label, "у клиента нет прав админа");

            session.CmdAdminStartSeries(step.target, step.maps);
            string maps = string.Join(" → ", step.maps);
            E2EWaitOutcome started = new E2EWaitOutcome();
            yield return E2EWait.Until(started, $"серия {maps} идёт", step.timeoutS,
                () => SeriesIs(step.maps, 0),
                () => $"серия {DescribeSeries()}");
            Require(label, started, $"режим {step.target}, карты {maps}");
        }

        /// <summary>
        /// Карта <c>target</c> загружена заново: новый оркестратор карты (та же сцена тоже считается — серия грузит её
        /// снова), разминка, ввод подан аватару. Оркестратор запоминается для следующей проверки карты.
        /// </summary>
        private IEnumerator ExpectMap(InputStep step, string label)
        {
            MapReferee previous = _knownMap;
            UxrAvatar avatarBefore = E2EScriptedInput.Avatar;
            E2EWaitOutcome loaded = new E2EWaitOutcome();
            yield return E2EWait.Until(loaded, $"карта {step.target} загружена, разминка, ввод на аватаре", step.timeoutS,
                () => MapReferee.Instance != null && MapReferee.Instance != previous &&
                      MapReferee.Instance.SceneName == step.target && MapReferee.Instance.CurrentState == MapState.Warmup &&
                      E2EScriptedInput.Avatar != null && E2EScriptedInput.AppliedFrames > 10,
                () => $"карта {(MapReferee.Instance != null ? MapReferee.Instance.SceneName : "нет")} " +
                      $"({(MapReferee.Instance != null && MapReferee.Instance == previous ? "прежняя" : "новая")}), " +
                      $"аватар {Name(E2EScriptedInput.Avatar)}, кадров {E2EScriptedInput.AppliedFrames}; {Describe()}");
            if (loaded.Succeeded) _knownMap = MapReferee.Instance;

            PlayerSession session = PlayerSession.LocalSession;
            bool rebound = E2EScriptedInput.Avatar != null && E2EScriptedInput.Avatar != avatarBefore;
            Require(label, loaded, $"{step.target}: {Describe()}, серия {DescribeSeries()}; аватар " +
                                   $"{(rebound ? "новый" : "прежний")} {Name(E2EScriptedInput.Avatar)}, команда " +
                                   $"{(session != null ? session.TeamIndex : -1)}, админ {(session != null && session.IsAdmin)}");
        }

        private static bool SeriesIs(string[] maps, int index)
        {
            Series series = Series.Instance;
            if (series == null || !series.IsRunning || series.CurrentIndex != index || series.Maps.Count != maps.Length)
                return false;
            for (int i = 0; i < maps.Length; i++)
                if (series.Maps[i] != maps[i]) return false;
            return true;
        }

        private static string DescribeSeries()
        {
            Series series = Series.Instance;
            if (series == null) return "нет";
            var maps = new List<string>(series.Maps);
            return $"{(series.IsRunning ? "идёт" : "не идёт")}, карта {series.CurrentIndex + 1}/{maps.Count} [{string.Join(", ", maps)}]";
        }

        /// <summary>Не-админ жмёт ту же кнопку: сервер отказывает, состояние карты не меняется.</summary>
        private IEnumerator AdminCommandDenied(InputStep step, string label)
        {
            PlayerSession session = PlayerSession.LocalSession;
            MapCommand command = Command(step, label);
            if (session == null) Fail(label, "локальной сессии нет");
            if (session.IsAdmin) Fail(label, $"{command}: клиент — админ, отказ проверить нельзя");

            MapState before = MapReferee.Instance != null ? MapReferee.Instance.CurrentState : MapState.Warmup;
            session.CmdAdminMapCommand(command);
            yield return E2EWait.Hold(step.seconds > 0f ? step.seconds : 2f);

            MapState after = MapReferee.Instance != null ? MapReferee.Instance.CurrentState : MapState.Warmup;
            if (after != before) Fail(label, $"{command} от не-админа изменила карту: {before} → {after}");
            Pass(label, $"{command} от не-админа {session.PlayerName}: состояние {after} не изменилось");
        }

        private MapCommand Command(InputStep step, string label)
        {
            if (!System.Enum.TryParse(step.target, false, out MapCommand command))
                Fail(label, $"неизвестная команда админа '{step.target}'");
            return command;
        }

        /// <summary>Состояние карты после команды, которое видно этому клиенту; false — проверяет следующий шаг.</summary>
        private static bool TryExpected(MapCommand command, out MapState state)
        {
            switch (command)
            {
                case MapCommand.GoLive:
                case MapCommand.Resume: state = MapState.Live; return true;
                case MapCommand.Pause: state = MapState.Paused; return true;
                default: state = MapState.Warmup; return false;
            }
        }

        private static string Describe()
        {
            MapReferee referee = MapReferee.Instance;
            Series series = Series.Instance;
            return $"{(referee != null ? referee.CurrentState.ToString() : "нет судьи")}, серия " +
                   $"{(series != null && series.IsRunning ? "идёт" : "не идёт")}";
        }

        // ── Барьер ───────────────────────────────────────────────────────────

        private void ReachStep(InputStep step, string label)
        {
            // grabbed, grabbed-2, … — покупка раунда: netId взятого предмета для кассы сервера.
            string data = step.name.StartsWith("grabbed", System.StringComparison.Ordinal)
                        ? "netId=" + GrabbedNetId.ToString(CultureInfo.InvariantCulture)
                        : step.name == "shot" ? (LastShooting != null ? LastShooting.StepData() : "role=target")
                        : null;
            if (!_context.Step(step.name, data)) Fail(label, "шаг не принят барьером (лимиты имени/данных)");
            Pass(label, data ?? "шаг отправлен");
        }

        private IEnumerator WaitServer(InputStep step, string label)
        {
            string data = null;
            yield return E2EBarrier.WaitServer(_context, step.name, step.timeoutS, d => data = d);
            Pass(label, data ?? "сервер дошёл");
        }

        // ── Мир игры (только чтение) ─────────────────────────────────────────

        private static TeamSpawnZone OwnZone(PlayerSession session)
        {
            if (session == null) return null;
            foreach (TeamSpawnZone zone in Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.None))
                if (zone.Team != null && zone.Team.teamIndex == session.TeamIndex) return zone;
            return null;
        }

        private static List<int> MapTeams()
        {
            var teams = new List<int>();
            foreach (TeamSpawnZone zone in Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.None))
                if (zone.Team != null && !teams.Contains(zone.Team.teamIndex)) teams.Add(zone.Team.teamIndex);
            teams.Sort();
            return teams;
        }

        private static ArsenalWallController OwnWall(PlayerSession session)
        {
            if (session == null || session.netId == 0) return null;
            foreach (ArsenalWallController wall in Object.FindObjectsByType<ArsenalWallController>(FindObjectsSortMode.None))
                if (wall.OwnerSessionNetId == session.netId) return wall;
            return null;
        }

        private static ArsenalSlotController CheapestAffordable(ArsenalWallController wall, PlayerSession session, string filter,
                                                                out int money)
        {
            MatchEconomy economy = MatchEconomy.Current;
            money = economy != null && session != null ? economy.GetMoney(session) : int.MaxValue;
            ArsenalSlotController best = null;
            foreach (ArsenalSlotController slot in wall.Slots)
            {
                if (slot == null || !slot.IsItemPresent || slot.Price > money) continue;
                if (slot.CurrentItem.GetComponent<UxrGrabbableObject>() == null) continue;
                if (filter == "pistol" && !IsSingleBulletPistol(slot)) continue;
                if (best == null || slot.Price < best.Price) best = slot;
            }

            return best;
        }

        /// <summary>Пистолет со стены: огнестрел, одна пуля на выстрел, не очередью.</summary>
        private static bool IsSingleBulletPistol(ArsenalSlotController slot)
        {
            WeaponInfo info = slot.WeaponData;
            return info != null && info.Category == WeaponCategory.Pistol && info.Pellets <= 1 && !info.FullAuto &&
                   slot.CurrentItem.GetComponentInParent<UltimateXR.Mechanics.Weapons.UxrFirearmWeapon>() != null;
        }

        // ── Вспомогательное ──────────────────────────────────────────────────

        private float Distance(Vector3 floorPoint) => Vector3.ProjectOnPlane(floorPoint - _pilot.FloorPosition, Vector3.up).magnitude;

        private static UxrHandSide Side(InputStep step) => step.hand == "left" ? UxrHandSide.Left : UxrHandSide.Right;

        private static IEnumerator Instant(System.Action action)
        {
            action();
            yield break;
        }

        private void Require(string label, E2EWaitOutcome outcome, string detail)
        {
            if (!outcome.Succeeded) Fail(label, outcome.Diagnosis);
            Pass(label, detail + " — " + outcome.Diagnosis);
        }

        private void Pass(string label, string detail) => _result.Set(label, true, detail);

        private void Fail(string label, string detail)
        {
            _result.Set(label, false, detail);
            E2EBarrier.ReportClientFailure(_context, label + ": " + detail);
            throw new E2EScenarioFailure(label, detail);
        }

        private static float Now => Time.realtimeSinceStartup;
        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Name(Object obj) => obj != null ? obj.name : "нет";
    }
}
#endif
