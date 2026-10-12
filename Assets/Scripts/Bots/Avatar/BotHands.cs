using Mirror;
using Unity.Profiling;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>Кадр хвата (для стенда): предмет и зазоры до вызова GrabObject.</summary>
    public struct BotGrabMoment
    {
        public int Frame;
        public UxrGrabbableObject Item;
        /// <summary>Фактический граббер после всех стадий кадра ↔ снап SDK, мм.</summary>
        public float GrabberToSnapMm;
        /// <summary>Заданный драйвером граббер ↔ снап SDK, мм и градусы (≤ GrabMm/GrabDegrees — иначе хвата нет).</summary>
        public float CommandedToSnapMm;
        public float CommandedToSnapDegrees;
    }

    /// <summary>
    /// Режим <see cref="BotBodyMode.PlayerFlow"/>: хват и отпускание оружия бота штатным SDK, обеими руками.
    /// Только сервер под <see cref="StateEventAuthority.IsWorldAuthority"/>; вызывается из LateUpdate верхнего уровня
    /// (<see cref="BotRuntimeController"/>), никогда из обработчика события UXR.
    ///
    /// <para>
    /// <c>UxrGrabManager.GrabObject</c> правил не проверяет, поэтому перед ним — <c>CanBeGrabbedByGrabber</c>
    /// (расстояние до снапа + <c>CanGrabDelegate</c> = <c>GrabRules</c>). Порядок: основная рукоять правой рукой,
    /// затем поддержка левой — только когда основной хват устоялся: SDK закончил плавный переход
    /// (<c>IsInSmoothTransition</c>) и снап основной рукояти совпал с граббером, <b>заданным драйвером</b> в этом кадре
    /// (<see cref="PrimarySettleMm"/>). Сравнение с командой, а не с фактической костью: <c>KeepGripsInPlace</c> подтягивает
    /// кисть к отстающему оружию (held-motion у не-Local аватара), и фактический граббер всегда «на снапе», а
    /// <c>MainGripAimLock</c> снимает позу в <c>ConstraintsApplied</c> до этого подтягивания — то есть вместе с отставанием
    /// (pf-round3, PPK на ходу: 33,9 мм / 2,2° навсегда).
    /// Иначе <c>MainGripAimLock</c> пистолета запоминает позу «оружие↔основная рука» посреди перехода и держит её
    /// при двух руках навсегда (pf-round2, PPK: обе кисти стабильно в 125/122 мм от команды, ствол 6,5° мимо). Берётся только предмет,
    /// который никто не держит (<c>IsBeingGrabbed</c>); выхвата нет. Отпускание — <c>ReleaseObject(…, true)</c> только
    /// от автора предмета; уборка в кобуру — штатное отпускание человека <c>TryRelease</c> (размещение решает SDK).
    /// После смерти (<c>!IsAlive</c>) компонент просто перестаёт хватать: сброс рук при смерти делает штатный путь
    /// (AvatarTeardown), здесь он не дублируется.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotHands : MonoBehaviour
    {
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("BotHands.Tick");
        /// <summary>Вложенные маркеры вызовов SDK (хват, отпускание/размещение) — их стоимость отдельно от нашей логики.</summary>
        private static readonly ProfilerMarker GrabMarker = new ProfilerMarker("BotHands.GrabObject");
        private static readonly ProfilerMarker ReleaseMarker = new ProfilerMarker("BotHands.Release");

        /// <summary>
        /// Кисть «прибыла» к снапу (eq-round1): заданный драйвером граббер (команда кисти × граббер в системе запястья —
        /// константа рига) от снапа SDK, прочитанного в этом кадре, не дальше этого, мм и градусы. Хват — только так:
        /// без хвата издалека (SDK иначе дотягивает предмет к руке скачком).
        /// </summary>
        public const float ArriveMm = 5f;
        public const float ArriveDegrees = 5f;

        /// <summary>
        /// Финальный допуск хвата в LateUpdate (снап перечитан после всех стадий кадра): заданный граббер ↔ снап SDK, мм и
        /// градусы, плюс штатная <c>CanBeGrabbedByGrabber</c>. Шире прибытия: на ходу снап кобуры между Update и концом кадра
        /// сдвигается покачиванием бедра (перенос корня цель уже учитывает), eq-round2.
        /// </summary>
        public const float GrabMm = 10f;
        public const float GrabDegrees = 8f;

        /// <summary>Допуск «основной хват устоялся»: снап основной рукояти от граббера, миллиметры.</summary>
        public const float PrimarySettleMm = 2f;

        /// <summary>Допуск «основной хват устоялся» по углу снапа к грабберу, градусы.</summary>
        public const float PrimarySettleDegrees = 1f;

        private UxrAvatar _avatar;
        private PlayerController _player;

        /// <summary>Сколько раз за жизнь тела SDK принял хват (основная/поддержка) — для стенда.</summary>
        public int PrimaryGrabs { get; private set; }
        public int SupportGrabs { get; private set; }

        /// <summary>Сколько раз отпущена поддержка перед уборкой, сколько раз оружие отпущено при уборке и сколько из них SDK положил в якорь.</summary>
        public int SupportReleases { get; private set; }
        public int Stows { get; private set; }
        public int StowsPlaced { get; private set; }

        /// <summary>Последний хват основной рукояти и поддержки: кадр, предмет, зазоры в кадре хвата (до GrabObject).</summary>
        public BotGrabMoment LastPrimaryGrab { get; private set; }
        public BotGrabMoment LastSupportGrab { get; private set; }

        /// <summary>Что отпущено последней уборкой и в какой якорь SDK его положил (null — не положил).</summary>
        public UxrGrabbableObject LastStowedItem { get; private set; }
        public string LastStowAnchor { get; private set; }

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
            _player = GetComponent<PlayerController>();
        }

        /// <summary>
        /// Рукоять спуска 0 (основная) и первая другая точка того же предмета, доступная левой руке (поддержка).
        /// Геометрию не кеширует — только индексы точек, которые даёт SDK.
        /// </summary>
        public static bool TryResolveGrips(UxrFirearmWeapon firearm, out UxrGrabbableObject grip, out int primary, out int support)
        {
            grip = null;
            primary = 0;
            support = -1;
            if (firearm == null || !firearm.TryGetTriggerGrip(0, out grip, out primary) || grip == null) return false;

            int count = grip.GrabPointCount;
            for (int i = 0; i < count; i++)
            {
                if (i == primary) continue;
                UxrGrabPointInfo info = grip.GetGrabPoint(i);
                if (info == null || (!info.BothHandsCompatible && info.HandSide != UxrHandSide.Left)) continue;
                support = i;
                break;
            }
            return true;
        }

        /// <summary>Держит ли рука <paramref name="side"/> точку <paramref name="point"/> предмета (без перебора грабберов сцены).</summary>
        public bool Holds(UxrGrabbableObject grip, int point, UxrHandSide side)
        {
            if (grip == null || point < 0 || _avatar == null || !UxrGrabManager.HasInstance) return false;
            UxrGrabber grabber = _avatar.GetGrabber(side);
            return grabber != null && grabber.GrabbedObject == grip && UxrGrabManager.Instance.GetGrabbedPoint(grabber) == point;
        }

        /// <summary>Исполняет хват/сброс кадра. LateUpdate верхнего уровня, после всех стадий UXR.</summary>
        public void Tick(in BotInputFrame frame, UxrFirearmWeapon firearm)
        {
            using (TickMarker.Auto())
            {
                if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority || _avatar == null || _player == null) return;
                if (!UxrGrabManager.HasInstance || !UxrGrabManager.Instance.isActiveAndEnabled) return;
                if (UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync) return;
                // После смерти просто перестаём хватать; сброс при смерти — не наш.
                if (!_player.IsAlive) return;

                UxrGrabManager manager = UxrGrabManager.Instance;
                UxrGrabber right = _avatar.GetGrabber(UxrHandSide.Right);
                UxrGrabber left = _avatar.GetGrabber(UxrHandSide.Left);
                if (right == null) return;

                bool resolved = TryResolveGrips(firearm, out UxrGrabbableObject grip, out int primary, out int support);

                if (resolved && (frame.ReleaseSupport || frame.Stow))
                {
                    Stow(manager, grip, right, left, frame.Stow);
                    return;
                }

                if (frame.Release)
                {
                    ReleaseForeign(manager, left, resolved ? grip : null);
                    ReleaseForeign(manager, right, resolved ? grip : null);
                    return;
                }

                if (!resolved) return;

                if (!Holds(grip, primary, UxrHandSide.Right))
                {
                    if (!frame.GrabPrimary || !frame.HasRightHand || manager.IsBeingGrabbed(grip) || right.GrabbedObject != null) return;
                    // Прибытие по факту: снап перечитан у SDK после всех стадий этого кадра.
                    if (!WithinGrab(_avatar, grip, primary, UxrHandSide.Right, frame.RightHand, out float mm, out float degrees)) return;
                    if (!grip.CanBeGrabbedByGrabber(right, primary)) return;

                    var moment = Moment(grip, primary, right, mm, degrees);
                    using (GrabMarker.Auto())
                    {
                        manager.GrabObject(right, grip, primary, true);
                    }
                    if (Holds(grip, primary, UxrHandSide.Right))
                    {
                        PrimaryGrabs++;
                        LastPrimaryGrab = moment;
                    }
                    // Поддержка — со следующего кадра: кисть встанет на её снап по позе оружия в руке.
                    return;
                }

                if (!frame.GrabSupport || support < 0 || left == null || left.GrabbedObject != null) return;
                // Поддержка — только после того, как основной хват устоялся (поза для MainGripAimLock снимается верно).
                if (!PrimarySettled(grip, primary, right, frame)) return;
                if (!frame.HasLeftHand ||
                    !WithinGrab(_avatar, grip, support, UxrHandSide.Left, frame.LeftHand, out float supportMm, out float supportDegrees)) return;
                if (!grip.CanBeGrabbedByGrabber(left, support)) return;
                // Точку поддержки держит чужая рука — не выхватываем.
                if (manager.GetGrabbingHand(grip, support, out UxrGrabber _)) return;

                var supportMoment = Moment(grip, support, left, supportMm, supportDegrees);
                using (GrabMarker.Auto())
                {
                    manager.GrabObject(left, grip, support, true);
                }
                if (Holds(grip, support, UxrHandSide.Left))
                {
                    SupportGrabs++;
                    LastSupportGrab = supportMoment;
                }
            }
        }

        /// <summary>
        /// Уборка оружия (этап pf-equipment): сначала отпустить поддержку (<c>ReleaseObject</c> от автора), основную — со
        /// следующего кадра: SDK ищет якорь для размещения, только когда предмет держит одна рука. Основная отпускается
        /// штатным путём человека — <c>UxrGrabManager.TryRelease</c> (как отпускание кнопки хвата в
        /// <c>UxrStandardAvatarController</c>): SDK кладёт предмет в ближайший совместимый якорь в радиусе
        /// (<c>CanBePlacedOnAnchor</c> → <c>PlaceObject(…, Smooth, true)</c>), иначе роняет. Поза предмета не пишется.
        /// </summary>
        private void Stow(UxrGrabManager manager, UxrGrabbableObject grip, UxrGrabber right, UxrGrabber left, bool stow)
        {
            if (!StateEventAuthority.IsAuthorOfItem(grip)) return;

            if (left != null && left.GrabbedObject == grip)
            {
                using (ReleaseMarker.Auto())
                {
                    manager.ReleaseObject(left, grip, true);
                }
                if (left.GrabbedObject == null) SupportReleases++;
                return;
            }

            if (!stow || right.GrabbedObject != grip) return;
            using (ReleaseMarker.Auto())
            {
                manager.TryRelease(_avatar, UxrHandSide.Right);
            }
            if (right.GrabbedObject != null) return;
            Stows++;
            LastStowedItem = grip;
            LastStowAnchor = grip.CurrentAnchor != null ? grip.CurrentAnchor.name : null;
            if (grip.CurrentAnchor != null) StowsPlaced++;
        }

        /// <summary>
        /// Заданный драйвером граббер стороны <paramref name="side"/> (кисть <paramref name="hand"/> × граббер в системе
        /// запястья — константа рига) против снапа точки <paramref name="point"/>, который SDK даёт сейчас: мм и градусы.
        /// false — SDK не дал снап.
        /// </summary>
        public static bool CommandedGap(UxrAvatar avatar, UxrGrabbableObject grip, int point, UxrHandSide side, in Pose hand,
                                        out float mm, out float degrees)
        {
            mm = degrees = float.PositiveInfinity;
            UxrGrabber grabber = avatar != null ? avatar.GetGrabber(side) : null;
            Transform bone = avatar != null ? avatar.GetHandBone(side) : null;
            if (grabber == null || bone == null || grip == null || point < 0) return false;
            if (!grip.ComputeRequiredGrabberTransform(grabber, point, out Vector3 snap, out Quaternion snapRotation, false)) return false;
            Quaternion boneInverse = Quaternion.Inverse(bone.rotation);
            Vector3 offset = boneInverse * (grabber.transform.position - bone.position);
            Quaternion offsetRotation = boneInverse * grabber.transform.rotation;
            mm = (snap - (hand.position + hand.rotation * offset)).magnitude * 1000f;
            degrees = Quaternion.Angle(snapRotation, hand.rotation * offsetRotation);
            return true;
        }

        /// <summary>Заданный граббер на снапе: не дальше <see cref="ArriveMm"/> и <see cref="ArriveDegrees"/>.</summary>
        public static bool AtSnap(UxrAvatar avatar, UxrGrabbableObject grip, int point, UxrHandSide side, in Pose hand,
                                  out float mm, out float degrees) =>
            CommandedGap(avatar, grip, point, side, hand, out mm, out degrees) && mm <= ArriveMm && degrees <= ArriveDegrees;

        /// <summary>Заданный граббер в допуске хвата: не дальше <see cref="GrabMm"/> и <see cref="GrabDegrees"/>.</summary>
        public static bool WithinGrab(UxrAvatar avatar, UxrGrabbableObject grip, int point, UxrHandSide side, in Pose hand,
                                      out float mm, out float degrees) =>
            CommandedGap(avatar, grip, point, side, hand, out mm, out degrees) && mm <= GrabMm && degrees <= GrabDegrees;

        /// <summary>Замер кадра хвата до GrabObject: фактический граббер после всех стадий и заданный — против снапа SDK.</summary>
        private static BotGrabMoment Moment(UxrGrabbableObject grip, int point, UxrGrabber grabber, float commandedMm, float commandedDegrees)
        {
            float grabberMm = float.NaN;
            if (grip.ComputeRequiredGrabberTransform(grabber, point, out Vector3 snap, out Quaternion _, false))
                grabberMm = (grabber.transform.position - snap).magnitude * 1000f;
            return new BotGrabMoment
            {
                Frame = Time.frameCount, Item = grip, GrabberToSnapMm = grabberMm,
                CommandedToSnapMm = commandedMm, CommandedToSnapDegrees = commandedDegrees
            };
        }

        /// <summary>Основной хват устоялся: SDK не в плавном переходе и снап основной рукояти стоит на граббере.</summary>
        private bool PrimarySettled(UxrGrabbableObject grip, int primary, UxrGrabber right, in BotInputFrame frame)
        {
            if (grip.IsInSmoothTransition || !frame.HasRightHand) return false;
            if (!grip.ComputeRequiredGrabberTransform(right, primary, out Vector3 snap, out Quaternion snapRotation, false)) return false;

            // Граббер, который задал драйвер: команда запястья × (граббер в системе запястья — константа рига).
            Transform bone = _avatar.GetHandBone(UxrHandSide.Right);
            if (bone == null) return false;
            Quaternion boneInverse = Quaternion.Inverse(bone.rotation);
            Vector3 offset = boneInverse * (right.transform.position - bone.position);
            Quaternion offsetRotation = boneInverse * right.transform.rotation;
            Vector3 commanded = frame.RightHand.position + frame.RightHand.rotation * offset;
            Quaternion commandedRotation = frame.RightHand.rotation * offsetRotation;

            return (snap - commanded).sqrMagnitude <= PrimarySettleMm * PrimarySettleMm * 1e-6f &&
                   Quaternion.Angle(snapRotation, commandedRotation) <= PrimarySettleDegrees;
        }

        /// <summary>Отпустить предмет в руке, если это не выбранное оружие; только автор предмета.</summary>
        private static void ReleaseForeign(UxrGrabManager manager, UxrGrabber grabber, UxrGrabbableObject keep)
        {
            UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
            if (held == null || held == keep || !StateEventAuthority.IsAuthorOfItem(held)) return;
            manager.ReleaseObject(grabber, held, true);
        }
    }
}
