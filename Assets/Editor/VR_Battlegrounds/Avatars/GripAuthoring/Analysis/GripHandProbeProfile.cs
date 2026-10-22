using System;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    /// <summary>
    /// Ручная рамка ладони в системе запястья (wrist.localToWorld): позиция и поворот.
    /// Оси рамки (соглашение Quaternion.LookRotation): +Y — нормаль ладони наружу, +Z — «вперёд» от центра ладони
    /// к основанию среднего пальца (ортогонально нормали), +X = Y × Z.
    /// </summary>
    [Serializable]
    public sealed class GripPalmProbe
    {
        [Tooltip("Рамка задана вручную; иначе анализ берёт автоматическую точку на коже")]
        public bool ManuallySet;
        public Vector3 PositionWrist;
        public Quaternion RotationWrist = Quaternion.identity;

        /// <summary>Нормаль ладони наружу в системе запястья.</summary>
        public Vector3 NormalWrist => Normalized(RotationWrist) * Vector3.up;

        /// <summary>Направление «вперёд» (к основанию среднего пальца) в системе запястья.</summary>
        public Vector3 ForwardWrist => Normalized(RotationWrist) * Vector3.forward;

        /// <summary>Рамка из нормали и направления «вперёд»; «вперёд» ортогонализуется к нормали.</summary>
        public static Quaternion FrameFrom(Vector3 normal, Vector3 forward)
        {
            if (normal.sqrMagnitude < 1e-12f)
            {
                normal = Vector3.up;
            }

            normal.Normalize();
            Vector3 f = Vector3.ProjectOnPlane(forward, normal);
            if (f.sqrMagnitude < 1e-10f)
            {
                // «Вперёд» вдоль нормали — берём любую ортогональную ось
                f = Vector3.ProjectOnPlane(Mathf.Abs(normal.z) < 0.9f ? Vector3.forward : Vector3.right, normal);
            }

            return Quaternion.LookRotation(f.normalized, normal);
        }

        static Quaternion Normalized(Quaternion q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return n < 1e-6f ? Quaternion.identity : new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
        }

        /// <summary>
        /// Поворот UxrGrabber относительно запястья, при котором оси grabber совпадают с рамкой ладони:
        /// его ось к пальцам (<see cref="UxrGrabber.LocalFingerDirection"/>) идёт в «вперёд» рамки,
        /// ось из ладони (<see cref="UxrGrabber.LocalPalmOutDirection"/>) — в нормаль. Grabber в префабах — дочерний
        /// объект кости запястья, поэтому результат — его localRotation, а <see cref="PositionWrist"/> — localPosition.
        /// </summary>
        public Quaternion ToGrabberLocalRotation(Vector3 grabberLocalFingerAxis, Vector3 grabberLocalPalmOutAxis)
        {
            Quaternion grabberAxes = Quaternion.LookRotation(grabberLocalFingerAxis, grabberLocalPalmOutAxis);
            return Normalized(RotationWrist) * Quaternion.Inverse(grabberAxes);
        }

        /// <summary>То же по осям конкретного grabber (SDK выбирает их как ближайшие к HandUniversalLocalAxes).</summary>
        public Quaternion ToGrabberLocalRotation(UxrGrabber grabber)
        {
            return ToGrabberLocalRotation(grabber.LocalFingerDirection, grabber.LocalPalmOutDirection);
        }

        /// <summary>То же по соглашению SDK без аватара: +Z grabber — к пальцам, −Y — из ладони.</summary>
        public Quaternion ToGrabberLocalRotationDefault()
        {
            return ToGrabberLocalRotation(Vector3.forward, Vector3.down);
        }

        public void CopyFrom(GripPalmProbe other)
        {
            ManuallySet = other.ManuallySet;
            PositionWrist = other.PositionWrist;
            RotationWrist = other.RotationWrist;
        }
    }

    /// <summary>
    /// Разметка одной руки. Расширяется новыми полями: точки контакта фаланг, радиусы и т.п.
    /// (новые поля добавлять с безопасными значениями по умолчанию — старые ассеты читаются без миграции).
    /// </summary>
    [Serializable]
    public sealed class GripHandProbeSide
    {
        /// <summary>Колец на руку: 5 пальцев × 3 фаланги.</summary>
        public const int RingCount = 15;
        /// <summary>Значение по умолчанию («не задано») — середина сегмента.</summary>
        public const float DefaultRingT = 0.5f;
        public const float MinRingT = 0.05f;
        public const float MaxRingT = 0.95f;

        public GripPalmProbe Palm = new GripPalmProbe();

        [Tooltip("Доля длины сегмента для кольца фаланги, индекс = палец·3 + фаланга; пальцы: большой, указательный, средний, безымянный, мизинец; фаланги: прокс., средн., дист.")]
        public float[] RingT = DefaultRings();

        public static float[] DefaultRings()
        {
            var rings = new float[RingCount];
            for (int i = 0; i < rings.Length; i++)
            {
                rings[i] = DefaultRingT;
            }

            return rings;
        }

        public static float ClampRingT(float t)
        {
            return float.IsNaN(t) || float.IsInfinity(t) ? DefaultRingT : Mathf.Clamp(t, MinRingT, MaxRingT);
        }

        public static bool IsManualRingT(float t) => Mathf.Abs(t - DefaultRingT) > 1e-4f;

        public const float MinRingRadiusMm = 2f;
        public const float MaxRingRadiusMm = 20f;

        [Tooltip("Радиус кольца фаланги, мм; индекс как у RingT; 0 — по умолчанию (6,8 / 5,8 / 5,0 мм для прокс./средн./дист.)")]
        public float[] RingRadiusMm = new float[RingCount];

        /// <summary>Радиус по умолчанию, мм: 6,8 / 5,8 / 5,0 для прокс./средн./дист. (как радиусы фаланг «Обхватить»).</summary>
        public static float DefaultRingRadiusMm(int index)
        {
            int phalanx = ((index % 3) + 3) % 3;
            return phalanx == 0 ? 6.8f : phalanx == 1 ? 5.8f : 5.0f;
        }

        public static float ClampRingRadiusMm(float radiusMm)
        {
            return float.IsNaN(radiusMm) || float.IsInfinity(radiusMm) ? 0f : Mathf.Clamp(radiusMm, MinRingRadiusMm, MaxRingRadiusMm);
        }

        /// <summary>Заданное значение (0 — по умолчанию); старые ассеты без массива читаются как 0.</summary>
        public float GetRingRadiusRawMm(int index)
        {
            if (RingRadiusMm == null || RingRadiusMm.Length != RingCount || index < 0 || index >= RingCount)
            {
                return 0f;
            }

            float raw = RingRadiusMm[index];
            return raw > 0f && !float.IsNaN(raw) && !float.IsInfinity(raw) ? ClampRingRadiusMm(raw) : 0f;
        }

        /// <summary>Действующий радиус кольца, мм: заданный или по умолчанию.</summary>
        public float GetRingRadiusMm(int index)
        {
            float raw = GetRingRadiusRawMm(index);
            return raw > 0f ? raw : DefaultRingRadiusMm(index);
        }

        /// <summary>Копия заданных радиусов, мм (0 — по умолчанию).</summary>
        public float[] GetRingRadiiRawCopyMm()
        {
            var radii = new float[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                radii[i] = GetRingRadiusRawMm(i);
            }

            return radii;
        }

        /// <summary>Действующие радиусы, мм, из массива заданных (0 — по умолчанию).</summary>
        public static float[] EffectiveRadiiMm(float[] rawMm)
        {
            var radii = new float[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                float raw = rawMm != null && rawMm.Length == RingCount ? rawMm[i] : 0f;
                radii[i] = raw > 0f ? ClampRingRadiusMm(raw) : DefaultRingRadiusMm(i);
            }

            return radii;
        }

        /// <summary>Радиус задан и отличается от умолчания.</summary>
        public static bool IsManualRingRadius(float rawMm, int index) => rawMm > 0f && Mathf.Abs(rawMm - DefaultRingRadiusMm(index)) > 1e-3f;

        public const float MaxRingTiltDeg = 75f;

        [Tooltip("Наклон плоскости кольца от перпендикуляра к кости, °: x — вокруг LocalRight кости (pitch), y — вокруг LocalUp (yaw); индекс как у RingT")]
        public Vector2[] RingTiltDeg = new Vector2[RingCount];

        [Tooltip("Угол точки касания по окружности кольца от ладонной стороны (−LocalUp), ° вокруг нормали кольца; индекс как у RingT")]
        public float[] RingContactAzimuthDeg = new float[RingCount];

        public static Vector2 ClampRingTilt(Vector2 tilt)
        {
            float Clamp(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : Mathf.Clamp(v, -MaxRingTiltDeg, MaxRingTiltDeg);
            return new Vector2(Clamp(tilt.x), Clamp(tilt.y));
        }

        public static float ClampRingAzimuth(float azimuth)
        {
            return float.IsNaN(azimuth) || float.IsInfinity(azimuth) ? 0f : Mathf.DeltaAngle(0f, azimuth);
        }

        /// <summary>Наклон кольца, °; старые ассеты без массива читаются как 0.</summary>
        public Vector2 GetRingTiltDeg(int index) =>
            RingTiltDeg != null && RingTiltDeg.Length == RingCount && index >= 0 && index < RingCount ? ClampRingTilt(RingTiltDeg[index]) : Vector2.zero;

        /// <summary>Азимут точки касания, °; старые ассеты без массива читаются как 0.</summary>
        public float GetRingAzimuthDeg(int index) =>
            RingContactAzimuthDeg != null && RingContactAzimuthDeg.Length == RingCount && index >= 0 && index < RingCount ? ClampRingAzimuth(RingContactAzimuthDeg[index]) : 0f;

        public Vector2[] GetRingTiltsCopyDeg()
        {
            var tilts = new Vector2[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                tilts[i] = GetRingTiltDeg(i);
            }

            return tilts;
        }

        public float[] GetRingAzimuthsCopyDeg()
        {
            var azimuths = new float[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                azimuths[i] = GetRingAzimuthDeg(i);
            }

            return azimuths;
        }

        public const float MaxRingOffsetMm = 15f;

        [Tooltip("Смещение центра кольца от точки на линии фаланги, мм, в плоскости поперёк кости: x — вдоль LocalRight, y — вдоль LocalUp (тыл); индекс как у RingT")]
        public Vector2[] RingOffsetMm = new Vector2[RingCount];

        public static Vector2 ClampRingOffset(Vector2 offsetMm)
        {
            float Clamp(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : Mathf.Clamp(v, -MaxRingOffsetMm, MaxRingOffsetMm);
            return new Vector2(Clamp(offsetMm.x), Clamp(offsetMm.y));
        }

        /// <summary>Смещение центра кольца, мм; старые ассеты без массива читаются как 0.</summary>
        public Vector2 GetRingOffsetMm(int index) =>
            RingOffsetMm != null && RingOffsetMm.Length == RingCount && index >= 0 && index < RingCount ? ClampRingOffset(RingOffsetMm[index]) : Vector2.zero;

        public Vector2[] GetRingOffsetsCopyMm()
        {
            var offsets = new Vector2[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                offsets[i] = GetRingOffsetMm(i);
            }

            return offsets;
        }

        public static bool IsManualRingOffset(Vector2 offsetMm) => Mathf.Abs(offsetMm.x) > 1e-3f || Mathf.Abs(offsetMm.y) > 1e-3f;

        /// <summary>Наклон или азимут кольца заданы (не 0).</summary>
        public static bool IsManualRingOrientation(Vector2 tiltDeg, float azimuthDeg) =>
            Mathf.Abs(tiltDeg.x) > 1e-3f || Mathf.Abs(tiltDeg.y) > 1e-3f || Mathf.Abs(azimuthDeg) > 1e-3f;

        /// <summary>t кольца; старые ассеты без массива читаются как середина.</summary>
        public float GetRingT(int index)
        {
            return RingT != null && RingT.Length == RingCount && index >= 0 && index < RingCount ? ClampRingT(RingT[index]) : DefaultRingT;
        }

        /// <summary>Копия всех 15 значений t (с ограничением диапазона).</summary>
        public float[] GetRingsCopy()
        {
            var rings = new float[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                rings[i] = GetRingT(i);
            }

            return rings;
        }
    }

    /// <summary>
    /// Профиль скелетных проб кисти для анализа хвата. Ключ — GUID базового аватара цепочки prefab:
    /// разметка действует для всех потомков (поиск идёт вверх по ParentAvatarPrefab).
    /// Хранится в Git: GripAuthoring/Analysis/Profiles/&lt;аватар&gt;_HandProbes.asset.
    /// </summary>
    public sealed class GripHandProbeProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField] int _schemaVersion = CurrentSchemaVersion;
        [SerializeField] string _avatarPrefabGuid = "";
        [SerializeField] string _avatarName = "";
        [SerializeField] int _revision;
        [SerializeField] GripHandProbeSide _left = new GripHandProbeSide();
        [SerializeField] GripHandProbeSide _right = new GripHandProbeSide();

        public int SchemaVersion => _schemaVersion;
        public string AvatarPrefabGuid => _avatarPrefabGuid;
        public string AvatarName => _avatarName;
        /// <summary>Растёт при каждом сохранении; входит в сигнатуру пересчёта анализа.</summary>
        public int Revision => _revision;

        public GripHandProbeSide Get(UxrHandSide side)
        {
            if (side == UxrHandSide.Left)
            {
                return _left ?? (_left = new GripHandProbeSide());
            }

            return _right ?? (_right = new GripHandProbeSide());
        }

        public void Initialize(string avatarPrefabGuid, string avatarName)
        {
            _schemaVersion = CurrentSchemaVersion;
            _avatarPrefabGuid = avatarPrefabGuid ?? "";
            _avatarName = avatarName ?? "";
        }

        public void BumpRevision()
        {
            _revision++;
        }
    }
}
