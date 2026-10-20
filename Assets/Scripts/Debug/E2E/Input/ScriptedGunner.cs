// Ярус C (несколько процессов) — стрелок скриптового клиента: наведение взятого ствола и курок вводом
// (контракт tasks/bots-control-modes/contracts/xr-input.md, этап 2 эпика e2e-regression). Отключить из сборки: VRBG_NO_E2E.
#if !VRBG_NO_E2E
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>
    /// Стрелок: «подойди к противнику, наведи ствол, нажми курок» → кадры устройства через <see cref="ScriptedPilot"/>.
    /// Выстрел делает штатный путь SDK — <c>UxrFirearmWeapon</c> читает курок с руки, держащей спуск; стрелок только
    /// читает состояние оружия и мира (поза дула, готовность ствола, здоровье цели) и ничего в SDK не вызывает.
    ///
    /// <para>
    /// <b>Наведение — замкнутым циклом.</b> Пока ствол в руке, поза дула (<c>ShotSource</c>) относительно грабера
    /// постоянна; каждый кадр грабер поворачивается вокруг точки удержания так, чтобы дуло смотрело в центр торса цели
    /// (хитбоксы <see cref="Hitbox"/>, копия цели у этого клиента). Отдачу компенсирует следующий кадр.
    /// </para>
    ///
    /// <para>
    /// <b>Нажатие</b> — как у человека: курок отпущен ≥ 2 кадров (SDK стреляет по фронту и требует отпустить после
    /// хвата), нажат 3 кадра, отпущен; между попытками пауза больше темпа ствола.
    /// </para>
    /// </summary>
    public sealed class ScriptedGunner
    {
        /// <summary>Наведено: угол между дулом и направлением на цель, градусы.</summary>
        public const float AimToleranceDeg = 0.5f;

        private const int AimStableFrames = 3;
        private const int ReleaseFrames = 2;
        private const int PressFrames = 3;
        private const float RetryPause = 0.6f;
        private const float ShotConfirmSeconds = 1f;
        private const float HitConfirmSeconds = 2f;
        private const float HoldForward = 0.40f;
        private const float HoldDown = 0.12f;
        private const float HoldSide = 0.12f;
        private const float BodyRadius = 0.2f;
        private const float ObstacleLookAhead = 0.6f;
        private const float ApproachSlack = 0.2f;
        private const float MinRange = 2f;
        private const float AimGain = 0.7f;

        private readonly ScriptedPilot _pilot;
        private readonly List<Collider> _torso = new List<Collider>();
        private BarrelObstruction _obstruction;
        private Quaternion _aimRotation;
        private bool _aiming;

        /// <summary>Итог стрельбы: попадание, сколько нажатий и выстрелов, что видел клиент.</summary>
        public sealed class Outcome
        {
            public bool Hit;
            public bool Killed;
            public string Failure;
            public int Presses;
            public int Shots;
            public float HealthBefore;
            public float HealthAfter;
            public float Distance;

            /// <summary>Наименьшая ровность оружия (<see cref="Level"/>) на нажатиях: 1 — ровно, 0 — на боку.</summary>
            public float Upright = 1f;

            /// <summary>Наименьшая ровность оружия (<see cref="Level"/>) при подходе к цели (оружие несут).</summary>
            public float CarryUpright = 1f;
            public string Weapon;
            public string Victim;
            public readonly List<string> Seen = new List<string>();

            /// <summary>Сводка для шага барьера (≤ 256 символов).</summary>
            public string StepData() =>
                $"hit={(Hit ? 1 : 0)} kill={(Killed ? 1 : 0)} presses={Presses} shots={Shots} hp={F(HealthBefore)}->{F(HealthAfter)} d={F(Distance)}";
        }

        public ScriptedGunner(ScriptedPilot pilot) => _pilot = pilot;

        /// <summary>
        /// Подойти к противнику на <paramref name="range"/> м (по свободному пути), навести ствол руки
        /// <paramref name="side"/> на его торс и жать курок до попадания (<paramref name="untilKill"/> — до его смерти),
        /// не больше <paramref name="maxPresses"/> раз. Провал — в <see cref="Outcome.Failure"/>.
        /// </summary>
        public IEnumerator ShootEnemy(UxrHandSide side, float range, float speed, int maxPresses, bool untilKill, float timeoutS,
                                      Outcome outcome)
        {
            float deadline = Now + timeoutS;
            UxrAvatar avatar = E2EScriptedInput.Avatar;
            UxrGrabber grabber = avatar != null ? avatar.GetGrabber(side) : null;
            if (!TryHeldFirearm(grabber, out UxrFirearmWeapon firearm, out string why))
            {
                outcome.Failure = why;
                yield break;
            }

            outcome.Weapon = firearm.name;
            UxrProjectileSource source = firearm.GetComponent<UxrProjectileSource>();
            UxrShotDescriptor shot = ShotOf(firearm, source);
            if (shot == null || shot.ShotSource == null)
            {
                outcome.Failure = $"{firearm.name}: у спуска 0 нет ShotSource";
                yield break;
            }

            PlayerController victim = Enemy(_pilot.FloorPosition);
            if (victim == null)
            {
                outcome.Failure = "живого противника на этом клиенте нет";
                yield break;
            }

            outcome.Victim = victim.Session != null ? victim.Session.PlayerName : victim.name;
            PlayerSession victimSession = victim.Session;
            UxrActor victimActor = victim.GetComponent<UxrActor>();
            outcome.HealthBefore = victim.Health;
            CacheTarget(victim, firearm);

            int shotsFired = 0;
            System.Action<int> onShot = _ => shotsFired++;
            System.EventHandler<UxrDamageEventArgs> onDamage = (_, e) =>
                Note(outcome, $"урон {F(e.Damage)} по {Name(e.ActorTarget)} от {Name(e.ActorSource)}{(e.IsCanceled ? " (отменён)" : "")}");
            System.EventHandler<UxrNonDamagingImpactEventArgs> onImpact = (_, e) =>
                Note(outcome, $"снаряд в {Name(e.RaycastHit.collider)} ({LayerMask.LayerToName(e.RaycastHit.collider != null ? e.RaycastHit.collider.gameObject.layer : 0)})");

            if (source != null) source.ShotFired += onShot;
            UxrWeaponManager.Instance.DamageReceiving += onDamage;
            UxrWeaponManager.Instance.NonActorImpacted += onImpact;
            try
            {
                // ── Подход ────────────────────────────────────────────────
                // Ближе дистанции и с открытой линией огня; линия закрыта — подходить дальше, до MinRange.
                float stopAt = range;
                while (true)
                {
                    // Ориентир — тело цели (торс), не корень: при физической ходьбе корень стоит на месте.
                    Vector3 victimFloor = Target(victim);
                    victimFloor.y = _pilot.FloorPosition.y;
                    Vector3 toVictim = Vector3.ProjectOnPlane(victimFloor - _pilot.FloorPosition, Vector3.up);
                    _pilot.TurnTowards(toVictim, Time.deltaTime, 2f);
                    // Цель шага чуть ближе дистанции: пилот останавливается в допуске до точки.
                    if (toVictim.magnitude <= stopAt + ApproachSlack)
                    {
                        if (LineOfFire(victim, out string blocker)) break;
                        if (stopAt <= MinRange)
                        {
                            outcome.Failure = $"линия огня до {outcome.Victim} закрыта даже с {F(toVictim.magnitude)} м: {blocker}";
                            yield break;
                        }

                        stopAt = Mathf.Max(MinRange, stopAt - 1f);
                        Note(outcome, $"линия огня закрыта ({blocker}) — подхожу до {F(stopAt)} м");
                    }

                    outcome.CarryUpright = Mathf.Min(outcome.CarryUpright, Level(shot.ShotSource));
                    if (Blocked(avatar, toVictim.normalized, out string obstacle))
                    {
                        outcome.Failure = $"на пути к {outcome.Victim} препятствие {obstacle} (до цели {F(toVictim.magnitude)} м) — " +
                                          "сквозь геометрию в бою идти нельзя (WallPass)";
                        yield break;
                    }

                    _pilot.StepTowards(victimFloor - toVictim.normalized * (stopAt - ApproachSlack), speed,
                                       Time.deltaTime, 0.05f);
                    if (Now > deadline)
                    {
                        outcome.Failure = $"не подошёл к {outcome.Victim}: {F(toVictim.magnitude)} м";
                        yield break;
                    }

                    yield return null;
                }

                // ── Ствол готов ───────────────────────────────────────────
                _aiming = false;
                while (!firearm.IsReadyToFire(0) || !firearm.CanUse)
                {
                    Aim(side, grabber, shot, Target(victim));
                    if (Now > deadline)
                    {
                        UxrFirearmTriggerDecision decision = firearm.QueryReadinessDecision(0);
                        outcome.Failure = $"{firearm.name} не готов к выстрелу: IsReadyToFire={firearm.IsReadyToFire(0)}, CanUse={firearm.CanUse}, " +
                                          $"решение {decision.Kind}/{decision.Reason}, патронов {firearm.GetTotalAmmoLeft(0)}, " +
                                          $"оружие включено={UxrWeaponManager.Instance.WeaponSystemEnabled}";
                        yield break;
                    }

                    yield return null;
                }

                // ── Наведение и курок ─────────────────────────────────────
                while (outcome.Presses < maxPresses && !Killed(victim, victimActor, victimSession))
                {
                    int stable = 0;
                    string ray = null;
                    while (stable < AimStableFrames)
                    {
                        Vector3 target = Target(victim);
                        float error = Aim(side, grabber, shot, target);
                        bool clear = RayHits(shot, victim, out ray);
                        bool free = !Obstructed();
                        stable = error <= AimToleranceDeg && clear && free ? stable + 1 : 0;
                        if (Now > deadline)
                        {
                            outcome.Failure = $"не навёлся на {outcome.Victim}: ошибка {F(error)}°, луч дула — {ray}, " +
                                              $"ствол упёрт={!free}, {AimSlip(grabber, side)}";
                            yield break;
                        }

                        yield return null;
                    }

                    int shotsBefore = shotsFired;
                    float healthBefore = Health(victim);
                    outcome.Presses++;
                    outcome.Distance = (shot.ShotSource.position - Target(victim)).magnitude;
                    float upright = Level(shot.ShotSource);
                    outcome.Upright = Mathf.Min(outcome.Upright, upright);
                    Note(outcome, $"нажатие {outcome.Presses}: {F(outcome.Distance)} м, ровность {F(upright)}, луч — {ray}");

                    for (int i = 0; i < ReleaseFrames; i++)
                    {
                        _pilot.Press(side, UxrInputButtons.Trigger, false);
                        Aim(side, grabber, shot, Target(victim));
                        yield return null;
                    }

                    for (int i = 0; i < PressFrames; i++)
                    {
                        _pilot.Press(side, UxrInputButtons.Trigger, true);
                        Aim(side, grabber, shot, Target(victim));
                        yield return null;
                    }

                    _pilot.Press(side, UxrInputButtons.Trigger, false);

                    float confirm = Now + ShotConfirmSeconds;
                    while (shotsFired == shotsBefore && Now < confirm) yield return null;
                    outcome.Shots = shotsFired;
                    if (shotsFired == shotsBefore)
                    {
                        UxrFirearmTriggerDecision decision = firearm.QueryReadinessDecision(0);
                        Note(outcome, $"нажатие {outcome.Presses} без выстрела: решение {decision.Kind}/{decision.Reason}, CanUse={firearm.CanUse}");
                    }
                    else
                    {
                        float hitDeadline = Now + HitConfirmSeconds;
                        while (!Damaged(victim, victimActor, victimSession, healthBefore) && Now < hitDeadline) yield return null;
                        if (Damaged(victim, victimActor, victimSession, healthBefore))
                        {
                            outcome.Hit = true;
                            outcome.HealthAfter = Health(victim);
                            outcome.Killed = Killed(victim, victimActor, victimSession);
                            if (!untilKill || outcome.Killed) yield break;
                        }
                    }

                    yield return E2EWait.Hold(RetryPause);
                }

                outcome.HealthAfter = Health(victim);
                outcome.Killed = Killed(victim, victimActor, victimSession);
                if (outcome.Hit && (!untilKill || outcome.Killed)) yield break;
                outcome.Failure = outcome.Hit
                    ? $"{outcome.Presses} нажатий, выстрелов {outcome.Shots}: {outcome.Victim} ранен, но жив " +
                      $"(здоровье {F(outcome.HealthBefore)}→{F(outcome.HealthAfter)})"
                    : $"{outcome.Presses} нажатий, выстрелов {outcome.Shots}, попадания по {outcome.Victim} нет " +
                      $"(здоровье {F(outcome.HealthBefore)}→{F(outcome.HealthAfter)})";
            }
            finally
            {
                _pilot.Press(side, UxrInputButtons.Trigger, false);
                if (source != null) source.ShotFired -= onShot;
                if (UxrWeaponManager.Instance != null)
                {
                    UxrWeaponManager.Instance.DamageReceiving -= onDamage;
                    UxrWeaponManager.Instance.NonActorImpacted -= onImpact;
                }
            }
        }

        // ── Наведение ────────────────────────────────────────────────────────

        /// <summary>
        /// Кадр наведения: точка удержания перед головой, поворот грабера, при котором дуло смотрит в
        /// <paramref name="target"/>. Возвращает текущую ошибку дула, градусы.
        /// </summary>
        private float Aim(UxrHandSide side, UxrGrabber grabber, UxrShotDescriptor shot, Vector3 target)
        {
            Transform muzzle = shot.ShotSource;
            Transform hand = grabber.transform;
            Vector3 head = _pilot.FloorPosition + Vector3.up * _pilot.EyeHeight;
            Quaternion yaw = Quaternion.AngleAxis(_pilot.Yaw, Vector3.up);
            float sideSign = side == UxrHandSide.Left ? -1f : 1f;
            Vector3 hold = head + yaw * new Vector3(HoldSide * sideSign, -HoldDown, HoldForward);

            // Дуло в пространстве грабера постоянно, пока ствол в руке.
            Quaternion handInverse = Quaternion.Inverse(hand.rotation);
            Vector3 muzzleInHand = handInverse * (muzzle.position - hand.position);
            Quaternion muzzleInHandRotation = handInverse * muzzle.rotation;

            if (!_aiming)
            {
                // Первый кадр: решение по модели «дуло жёстко в руке» из точки удержания — ствол на цель, верх вверх
                // (как держит человек; минимальный поворот из позы переноски оставил бы ствол на боку).
                _aimRotation = hand.rotation;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 muzzlePosition = hold + _aimRotation * muzzleInHand;
                    _aimRotation = LookUpright(target - muzzlePosition) * Quaternion.Inverse(muzzleInHandRotation);
                }

                _aiming = true;
            }
            else
            {
                // Дальше — по фактическому дулу: отклонение направления и крена, которое не объясняет модель
                // (ограничения кисти, отдача), накапливается в команде. Коэффициент < 1 — команда применяется кадром позже.
                Quaternion correction = LookUpright(target - muzzle.position) * Quaternion.Inverse(muzzle.rotation);
                _aimRotation = Quaternion.Slerp(Quaternion.identity, correction, AimGain) * _aimRotation;
            }

            _pilot.ReachGrabber(side, hold, _aimRotation);
            return Vector3.Angle(muzzle.forward, target - muzzle.position);
        }

        /// <summary>
        /// Ровность оружия — крен вокруг ствола: 1 — верх оружия смотрит в «вертикальный верх» для текущего направления
        /// ствола, 0 — оружие на боку, −1 — вверх ногами. Наклон ствола вниз или вверх на меру не влияет.
        /// </summary>
        public static float Level(Transform frame)
        {
            Vector3 upright = Vector3.ProjectOnPlane(Vector3.up, frame.forward);
            return upright.sqrMagnitude < 1e-6f ? 1f : Vector3.Dot(frame.up, upright.normalized);
        }

        /// <summary>Поза дула «смотрит по направлению, верх вверх».</summary>
        private static Quaternion LookUpright(Vector3 direction) => Quaternion.LookRotation(direction, Vector3.up);

        /// <summary>
        /// Ось предмета для наведения и переноски: у огнестрела — дуло спуска 0 (вперёд — ствол, вверх — верх оружия),
        /// у остальных предметов — их корень.
        /// </summary>
        public static Transform AimFrame(UxrGrabbableObject held)
        {
            UxrFirearmWeapon firearm = held != null ? held.GetComponentInParent<UxrFirearmWeapon>() : null;
            UxrShotDescriptor shot = firearm != null ? ShotOf(firearm, firearm.GetComponent<UxrProjectileSource>()) : null;
            return shot != null && shot.ShotSource != null ? shot.ShotSource : held.transform;
        }

        /// <summary>Расхождение фактического грабера с командой наведения: метры и градусы.</summary>
        private string AimSlip(UxrGrabber grabber, UxrHandSide side)
        {
            Vector3 head = _pilot.FloorPosition + Vector3.up * _pilot.EyeHeight;
            Vector3 hold = head + Quaternion.AngleAxis(_pilot.Yaw, Vector3.up) *
                           new Vector3(HoldSide * (side == UxrHandSide.Left ? -1f : 1f), -HoldDown, HoldForward);
            return $"грабер от команды {F((grabber.transform.position - hold).magnitude)} м / " +
                   $"{F(Quaternion.Angle(grabber.transform.rotation, _aimRotation))}°";
        }

        /// <summary>Хитбоксы торса цели и упор ствола — на время шага, а не поиском каждый кадр.</summary>
        private void CacheTarget(PlayerController victim, UxrFirearmWeapon firearm)
        {
            _torso.Clear();
            foreach (Hitbox hitbox in victim.GetComponentsInChildren<Hitbox>())
            {
                Collider collider = hitbox.Part == HitZone.Torso ? hitbox.GetComponent<Collider>() : null;
                if (collider != null) _torso.Add(collider);
            }

            _obstruction = firearm.GetComponentInChildren<BarrelObstruction>();
        }

        /// <summary>Центр торса цели (кеш хитбоксов шага); без хитбоксов — грудь над корнем.</summary>
        private Vector3 Target(PlayerController victim) => TorsoCenter(victim, _torso);

        /// <summary>Центр торса по коллайдерам <paramref name="torso"/>; null — найти хитбоксы торса у цели.</summary>
        private static Vector3 TorsoCenter(PlayerController victim, List<Collider> torso = null)
        {
            if (victim == null) return Vector3.zero;
            Bounds bounds = default;
            bool any = false;
            if (torso == null)
            {
                torso = new List<Collider>();
                foreach (Hitbox hitbox in victim.GetComponentsInChildren<Hitbox>())
                    if (hitbox.Part == HitZone.Torso && hitbox.TryGetComponent(out Collider found)) torso.Add(found);
            }

            foreach (Collider collider in torso)
            {
                if (collider == null || !collider.enabled) continue;
                if (!any) bounds = collider.bounds;
                else bounds.Encapsulate(collider.bounds);
                any = true;
            }

            return any ? bounds.center : victim.transform.position + Vector3.up * 1.3f;
        }

        /// <summary>Луч из дула маской выстрела первым попадает в хитбокс цели.</summary>
        private static bool RayHits(UxrShotDescriptor shot, PlayerController victim, out string what)
        {
            Transform muzzle = shot.ShotSource;
            if (!Physics.Raycast(muzzle.position, muzzle.forward, out RaycastHit hit, shot.ProjectileMaxDistance,
                                 shot.CollisionLayerMask, QueryTriggerInteraction.Ignore))
            {
                what = "ни во что";
                return false;
            }

            bool hitbox = Hitbox.TryGetPart(hit.collider, out HitZone part);
            PlayerController owner = hit.collider.GetComponentInParent<PlayerController>();
            what = hitbox
                ? $"хитбокс {part} {(owner != null ? owner.name : "?")} в {F(hit.distance)} м"
                : $"{hit.collider.name} ({LayerMask.LayerToName(hit.collider.gameObject.layer)}) в {F(hit.distance)} м";
            return hitbox && owner == victim;
        }

        /// <summary>От точки удержания ствола до торса цели нет геометрии (слои пули без хитбоксов).</summary>
        private bool LineOfFire(PlayerController victim, out string blocker)
        {
            Vector3 head = _pilot.FloorPosition + Vector3.up * (_pilot.EyeHeight - HoldDown);
            int mask = HitLayers.ProjectileMask & ~(1 << HitLayers.HitboxLayer);
            foreach (RaycastHit hit in Physics.RaycastAll(head, Target(victim) - head, (Target(victim) - head).magnitude, mask,
                                                          QueryTriggerInteraction.Ignore))
            {
                Collider collider = hit.collider;
                if (collider == null || collider.GetComponentInParent<UxrAvatar>() != null ||
                    collider.GetComponentInParent<UxrGrabbableObject>() != null) continue;

                blocker = $"{collider.name} ({LayerMask.LayerToName(collider.gameObject.layer)}) в {F(hit.distance)} м";
                return false;
            }

            blocker = null;
            return true;
        }

        private bool Obstructed() => _obstruction != null && _obstruction.IsObstructed;

        // ── Мир (только чтение) ──────────────────────────────────────────────

        /// <summary>Ствол в руке грабера, который стреляет спуском 0 с той точки хвата, за которую держат.</summary>
        public static bool TryHeldFirearm(UxrGrabber grabber, out UxrFirearmWeapon firearm, out string why)
        {
            firearm = null;
            UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
            if (held == null)
            {
                why = "в руке ничего нет";
                return false;
            }

            firearm = held.GetComponentInParent<UxrFirearmWeapon>();
            if (firearm == null)
            {
                why = $"в руке {held.name} — не огнестрел";
                return false;
            }

            if (!firearm.TryGetTriggerGrip(0, out UxrGrabbableObject trigger, out int point) || trigger != held)
            {
                why = $"{firearm.name}: спуск 0 держат не за {held.name} (спуск на {(trigger != null ? trigger.name : "нет")})";
                return false;
            }

            if (!UxrGrabManager.Instance.GetGrabbingHand(held, point, out UxrGrabber hand) || hand != grabber)
            {
                why = $"{firearm.name}: точку спуска {point} держит не рука {grabber.Side}";
                return false;
            }

            why = null;
            return true;
        }

        /// <summary>Ближайший живой игрок другой команды (копия на этом клиенте).</summary>
        private static PlayerController Enemy(Vector3 from)
        {
            PlayerSession local = PlayerSession.LocalSession;
            if (local == null) return null;

            PlayerController best = null;
            float bestDistance = float.MaxValue;
            foreach (PlayerSession session in Object.FindObjectsByType<PlayerSession>(FindObjectsSortMode.None))
            {
                if (session == null || session == local || session.Role != GameRole.Player) continue;
                if (session.TeamIndex == local.TeamIndex || session.IsEliminated) continue;

                PlayerController controller = session.ActiveAvatar;
                if (controller == null || !controller.IsAlive) continue;
                float distance = Vector3.ProjectOnPlane(TorsoCenter(controller) - from, Vector3.up).magnitude;
                if (distance < bestDistance)
                {
                    best = controller;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static UxrShotDescriptor ShotOf(UxrFirearmWeapon firearm, UxrProjectileSource source)
        {
            if (source == null || source.ShotTypes == null) return null;
            int index = firearm.GetTriggerShotIndex(0);
            return index >= 0 && index < source.ShotTypes.Count ? source.ShotTypes[index] : null;
        }

        /// <summary>
        /// Капсула тела на шаг вперёд упирается в геометрию (слои пули без хитбоксов): идти дальше — пройти сквозь
        /// стену. Аватары и предметы в руках не считаются.
        /// </summary>
        private bool Blocked(UxrAvatar avatar, Vector3 direction, out string obstacle)
        {
            obstacle = null;
            Vector3 floor = _pilot.FloorPosition;
            Vector3 bottom = floor + Vector3.up * (BodyRadius + 0.3f);
            // Верх капсулы — на уровне глаз: голова ниже макушки, проём выше неё не препятствие.
            Vector3 top = floor + Vector3.up * Mathf.Max(BodyRadius + 0.31f, _pilot.EyeHeight - BodyRadius);
            int mask = HitLayers.ProjectileMask & ~(1 << HitLayers.HitboxLayer);
            foreach (RaycastHit hit in Physics.CapsuleCastAll(bottom, top, BodyRadius, direction, ObstacleLookAhead, mask,
                                                             QueryTriggerInteraction.Ignore))
            {
                Collider collider = hit.collider;
                if (collider == null || collider.GetComponentInParent<UxrAvatar>() != null ||
                    collider.GetComponentInParent<UxrGrabbableObject>() != null) continue;

                obstacle = $"{collider.name} ({LayerMask.LayerToName(collider.gameObject.layer)}) в {F(hit.distance)} м";
                return true;
            }

            return false;
        }

        private static bool Damaged(PlayerController victim, UxrActor actor, PlayerSession session, float healthBefore) =>
            Killed(victim, actor, session) || victim.Health < healthBefore;

        /// <summary>Цель убита: актор мёртв, сессия выбыла или тело уже заменено призраком (уничтожено).</summary>
        private static bool Killed(PlayerController victim, UxrActor actor, PlayerSession session) =>
            victim == null || actor == null || actor.IsDead || (session != null && session.IsEliminated);

        private static float Health(PlayerController victim) => victim != null ? victim.Health : 0f;

        private static void Note(Outcome outcome, string line)
        {
            if (outcome.Seen.Count < 24) outcome.Seen.Add(line);
            GameLog.Debug.Info($"[ScriptedGunner] {line}");
        }

        private static float Now => Time.realtimeSinceStartup;
        private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Name(Object obj) => obj != null ? obj.name : "нет";
    }
}
#endif
