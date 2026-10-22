using System;
using System.Collections.Generic;
using System.Diagnostics;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UltimateXR.Editor.Manipulation.HandPoses;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEngine;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    /// <summary>Контекст панели автоснапа, который анализ читает без изменений.</summary>
    internal readonly struct GripAnalysisHost
    {
        public readonly UxrHandPoseEditorWindow Window;
        public readonly UxrAvatar Avatar;
        public readonly UxrHandPoseAsset Pose;
        public readonly UxrHandSide Side;
        public readonly UxrGrabbableObject Grabbable;
        public readonly int GrabPoint;

        public GripAnalysisHost(UxrHandPoseEditorWindow window, UxrAvatar avatar, UxrHandPoseAsset pose,
            UxrHandSide side, UxrGrabbableObject grabbable, int grabPoint)
        {
            Window = window;
            Avatar = avatar;
            Pose = pose;
            Side = side;
            Grabbable = grabbable;
            GrabPoint = grabPoint;
        }
    }

    internal enum GripProbeKind { Contact, Ring, Tip, Palm }

    internal enum GripProbeState { Contact, Near, Far, Penetration }

    /// <summary>Проба для SceneView; координаты — мировые, в пространстве превью у предмета.</summary>
    /// <summary>Настройки колец фаланг для расчёта (15 значений, индекс = палец·3 + фаланга); null — умолчания.</summary>
    internal sealed class GripRingSettings
    {
        public float[] T;
        /// <summary>Заданные радиусы, мм; 0 — по умолчанию 6,8 / 5,8 / 5,0.</summary>
        public float[] RadiusRawMm;
        public Vector2[] TiltDeg;
        public float[] AzimuthDeg;
        /// <summary>Смещение центра кольца, мм (x — LocalRight, y — LocalUp).</summary>
        public Vector2[] OffsetMm;
    }

    /// <summary>Ручная точка ладони для P1 (из профиля или черновика разметки), в рамке кисти <see cref="GripHandFrame"/>.</summary>
    internal struct GripPalmOverride
    {
        public bool Active;
        public Vector3 PositionHand;
        public Vector3 NormalHand;
        /// <summary>Ось «вперёд» рамки ладони (к основанию среднего пальца) в рамке кисти.</summary>
        public Vector3 ForwardHand;
        /// <summary>Источник для детализации, например «вручную (профиль X)».</summary>
        public string Source;
        /// <summary>Пометка, когда выбран режим «Вручную», но разметки нет.</summary>
        public string Note;
    }

    internal struct GripProbe
    {
        public Vector3 Position;
        public Vector3 Direction;
        public GripProbeKind Kind;
        public GripProbeState State;
        public float Signed;
        /// <summary>Точка ладони из ручной разметки (рисуется другим цветом).</summary>
        public bool Manual;
        /// <summary>Опорная проба кольца (ладонная точка), кольцо которого смещено вручную (t ≠ 0,5).</summary>
        public bool RingManual;
        /// <summary>Ось кости и радиус кольца для опорной пробы — центр кольца = Position − Direction·Radius.</summary>
        public Vector3 Axis;
        public float Radius;
    }

    internal struct GripProbeLine
    {
        public Vector3 From;
        public Vector3 To;
        public Color Color;
        /// <summary>Толщина линии в пикселях; 0 — обычная.</summary>
        public float Width;
    }

    internal sealed class GripDetailLine
    {
        public string Text;
        public GripCheckStatus Status;
        public int Indent;
    }

    internal sealed class GripCheckResult
    {
        public GripCheckDefinition Definition;
        public GripCheckStatus Status;
        public string Summary = "";
        public readonly List<GripDetailLine> Details = new List<GripDetailLine>();

        public void Add(int indent, GripCheckStatus status, string text)
        {
            Details.Add(new GripDetailLine { Indent = indent, Status = status, Text = text });
        }
    }

    internal sealed class GripAnalysisReport
    {
        /// <summary>Причина, по которой анализ не выполнен; null — результаты есть.</summary>
        public string Reason;
        public readonly Dictionary<string, GripCheckResult> Checks = new Dictionary<string, GripCheckResult>();
        public readonly List<GripProbe> Probes = new List<GripProbe>(256);
        public readonly List<GripProbeLine> Lines = new List<GripProbeLine>(64);
        public double ElapsedMs;
        public int TriangleCount;
        public bool CacheRebuilt;
        /// <summary>Почему кэш треугольников пересобран (null — взят готовый).</summary>
        public string RebuildReason;
        /// <summary>Время проверки ключа и перестройки кэша, мс.</summary>
        public double CacheMs;
        /// <summary>Время расчёта проверок (запросы к поверхности и прочее), мс.</summary>
        public double QueryMs;
        public int Queries;
        public int WindingSigns;
        public int NormalSigns;
        public int AmbiguousSigns;
    }

    /// <summary>
    /// Расчёт проверок качества хвата по скелетным пробам. Только чтение: позы, кости, префабы и ассеты не меняются.
    /// Геометрия предмета — тот же набор, что берёт «Обхватить», но закэширован в системе предмета.
    /// </summary>
    internal sealed class GripAnalysisEngine
    {
        public static readonly string[] FingerNamesRu = { "Большой", "Указательный", "Средний", "Безымянный", "Мизинец" };
        public static readonly string[] SegmentNamesRu = { "прокс.", "средн.", "дист." };
        public static readonly UxrFingerType[] FingerTypes =
        {
            UxrFingerType.Thumb,
            UxrFingerType.Index,
            UxrFingerType.Middle,
            UxrFingerType.Ring,
            UxrFingerType.Little
        };

        static readonly string[] FingerShortRu = { "Б", "У", "С", "Бз", "М" };
        const float Mm = 0.001f;
        const float RingStepDeg = 60f;
        // Нормаль поверхности предмета у точки касания показываем и сравниваем ближе этого расстояния (м)
        const float ContactNormalShowDistance = 0.015f;

        readonly GripSurfaceCache _cache = new GripSurfaceCache();
        ulong[] _cacheKeyParts;
        readonly GripPalmSkinPoint _palmSkin = new GripPalmSkinPoint();

        // Нейтральные углы (bind) не зависят от позы — кэш на аватар и сторону
        int _bindAvatarId;
        UxrHandSide _bindSide;
        BindPoseFingerState?[] _bindStates;
        string[] _bindReasons;

        struct PhalanxSample
        {
            public bool Valid;
            public string Error;
            public Vector3 Start;
            public Vector3 End;
            public Vector3 Axis;
            public Vector3 Palmar;
            public Vector3 Binormal;
            public float Radius;
            public Vector3 ContactPoint;
            public GripSurfaceHit ContactHit;
            public bool RingEvaluated;
            public float RingMinSigned;
            public float RingFraction;
            public float RingDeepestAngle;
            public GripSignMethod RingDeepestMethod;
        }

        public int CachedTriangleCount => _cache.Count;

        public void InvalidateGeometry()
        {
            _cache.Clear();
            _cacheKeyParts = null;
            _bindStates = null;
            _palmSkin.Invalidate();
        }

        public GripAnalysisReport Compute(GripAnalysisHost host, Transform snap, UxrGrabber grabber, GripPalmOverride palmOverride = default, GripRingSettings rings = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var report = new GripAnalysisReport();
            try
            {
                ComputeInto(report, host, snap, grabber, palmOverride, rings);
            }
            catch (Exception ex)
            {
                report.Reason = "Ошибка анализа: " + ex.Message;
                report.Probes.Clear();
                report.Lines.Clear();
            }

            report.ElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            report.QueryMs = Math.Max(0.0, report.ElapsedMs - report.CacheMs);
            report.TriangleCount = _cache.Count;
            return report;
        }

        void ComputeInto(GripAnalysisReport report, GripAnalysisHost host, Transform snap, UxrGrabber grabber, GripPalmOverride palmOverride, GripRingSettings rings)
        {
            string sideRu = host.Side == UxrHandSide.Left ? "левой" : "правой";
            if (host.Avatar == null || host.Pose == null)
            {
                report.Reason = "Откройте Target Avatar и позу в Hand Pose Editor.";
                return;
            }

            if (host.Grabbable == null)
            {
                report.Reason = "Не выбран UxrGrabbableObject: выберите предмет или его Grip Align в сцене.";
                return;
            }

            if (snap == null)
            {
                report.Reason = $"У «{host.Grabbable.name}» нет Grip Align (SnapTransform) для {sideRu} кисти в точке хвата {host.GrabPoint}.";
                return;
            }

            if (grabber == null)
            {
                report.Reason = $"У аватара «{host.Avatar.name}» не найден UxrGrabber для {sideRu} кисти.";
                return;
            }

            UxrAvatarHand hand = host.Avatar.GetHand(host.Side);
            UxrAvatarArmInfo armInfo = host.Avatar.AvatarRigInfo != null ? host.Avatar.AvatarRigInfo.GetArmInfo(host.Side) : null;
            if (hand == null || hand.Wrist == null || armInfo == null)
            {
                report.Reason = $"У аватара нет костей {sideRu} кисти или AvatarRigInfo.";
                return;
            }

            UxrUniversalLocalAxes fingerAxes = armInfo.FingerUniversalLocalAxes;
            UxrUniversalLocalAxes wristAxes = armInfo.HandUniversalLocalAxes;
            if (!AxesValid(fingerAxes) || !AxesValid(wristAxes))
            {
                report.Reason = "Оси кисти/пальцев аватара (Hand/FingerUniversalLocalAxes) не заданы; анализ их не исправляет.";
                return;
            }

            // Система предмета: позиция и поворот корня Grabbable без масштаба
            Transform root = host.Grabbable.transform;
            Matrix4x4 objectToWorld = Matrix4x4.TRS(root.position, root.rotation, Vector3.one);
            Matrix4x4 worldToObject = objectToWorld.inverse;

            var cacheWatch = Stopwatch.StartNew();
            report.RebuildReason = EnsureCache(host.Grabbable.gameObject, snap, worldToObject);
            report.CacheRebuilt = report.RebuildReason != null;
            report.CacheMs = cacheWatch.Elapsed.TotalMilliseconds;
            if (_cache.Count == 0)
            {
                report.Reason = $"У «{host.Grabbable.name}» нет видимых MeshFilter в радиусе {GripAuthoringGeometry.WeaponTriangleRadius:0.00} м от Grip Align.";
                return;
            }

            // Перевод кисти аватара в превью у предмета через пару align → grabber (как в DrawScene панели)
            Quaternion rotAvatarToPreview = snap.rotation * Quaternion.Inverse(grabber.transform.rotation);
            Vector3 snapPos = snap.position;
            Vector3 grabberPos = grabber.transform.position;

            Vector3 ToObject(Vector3 avatarWorld) => worldToObject.MultiplyPoint3x4(snapPos + rotAvatarToPreview * (avatarWorld - grabberPos));
            Vector3 DirToObject(Vector3 avatarDir) => worldToObject.MultiplyVector(rotAvatarToPreview * avatarDir);
            Vector3 ToWorld(Vector3 objectPoint) => objectToWorld.MultiplyPoint3x4(objectPoint);
            Vector3 DirToWorld(Vector3 objectDir) => objectToWorld.MultiplyVector(objectDir);

            float nearGap = GripAnalysisSettings.NearGapMm.Value * Mm;
            float windingNear = GripAnalysisSettings.WindingNearMm.Value * Mm;
            float windingRadius = GripAnalysisSettings.WindingRadiusMm.Value * Mm;
            float tolIn = GripAnalysisSettings.Get(GripAnalysisSettings.K1, 0) * Mm;
            float tolOut = GripAnalysisSettings.Get(GripAnalysisSettings.K1, 1) * Mm;

            GripSurfaceHit Query(Vector3 objectPoint)
            {
                GripSurfaceHit h = _cache.Query(objectPoint, windingNear, windingRadius);
                report.Queries++;
                switch (h.Method)
                {
                    case GripSignMethod.Winding: report.WindingSigns++; break;
                    case GripSignMethod.WindingAmbiguous: report.AmbiguousSigns++; break;
                    default: report.NormalSigns++; break;
                }

                return h;
            }

            GripProbeState Classify(float signed)
            {
                if (signed < -tolIn) return GripProbeState.Penetration;
                if (signed <= tolOut) return GripProbeState.Contact;
                if (signed <= nearGap) return GripProbeState.Near;
                return GripProbeState.Far;
            }

            bool doK1 = GripAnalysisSettings.K1.Enabled;
            bool doX1 = GripAnalysisSettings.X1.Enabled;
            bool doX2 = GripAnalysisSettings.X2.Enabled;
            bool doD1 = GripAnalysisSettings.D1.Enabled;
            bool doP1 = GripAnalysisSettings.P1.Enabled;
            bool doF1 = GripAnalysisSettings.F1.Enabled;
            bool needContactPoint = doK1 || doD1;

            var samples = new PhalanxSample[5, 3];
            var tipHits = new GripSurfaceHit[5];
            var tipValid = new bool[5];
            var fingers = new UxrAvatarFinger[5];

            for (int f = 0; f < 5; f++)
            {
                UxrAvatarFinger finger = hand.GetFinger(FingerTypes[f]);
                fingers[f] = finger;
                if (finger == null || finger.Proximal == null || finger.Intermediate == null || finger.Distal == null)
                {
                    for (int s = 0; s < 3; s++)
                    {
                        samples[f, s].Error = "нет костей пальца";
                    }

                    continue;
                }

                Vector3 tipWorld = GripAuthoringGeometry.GetDistalTipWorld(finger, fingerAxes);
                Transform[] bones = { finger.Proximal, finger.Intermediate, finger.Distal };
                Vector3[] starts = { finger.Proximal.position, finger.Intermediate.position, finger.Distal.position };
                Vector3[] ends = { finger.Intermediate.position, finger.Distal.position, tipWorld };

                for (int s = 0; s < 3; s++)
                {
                    ref PhalanxSample sample = ref samples[f, s];
                    // Настройки кольца из профиля кисти: t (по умолчанию середина), радиус (6,8 / 5,8 / 5,0 мм), наклон и азимут (0°)
                    int ringIndex = f * 3 + s;
                    float radiusRawMm = rings?.RadiusRawMm != null && rings.RadiusRawMm.Length == GripHandProbeSide.RingCount ? rings.RadiusRawMm[ringIndex] : 0f;
                    float ringFraction = GripHandProbeSide.ClampRingT(rings?.T != null && rings.T.Length == GripHandProbeSide.RingCount
                        ? rings.T[ringIndex]
                        : GripHandProbeSide.DefaultRingT);
                    Vector2 tiltDeg = rings?.TiltDeg != null && rings.TiltDeg.Length == GripHandProbeSide.RingCount
                        ? GripHandProbeSide.ClampRingTilt(rings.TiltDeg[ringIndex])
                        : Vector2.zero;
                    float azimuthDeg = rings?.AzimuthDeg != null && rings.AzimuthDeg.Length == GripHandProbeSide.RingCount
                        ? GripHandProbeSide.ClampRingAzimuth(rings.AzimuthDeg[ringIndex])
                        : 0f;

                    // Единая рамка кольца (та же, что у отрисовки и «Обхватить»): центр, нормаль плоскости, направление на точку касания
                    Vector2 offsetMm = rings?.OffsetMm != null && rings.OffsetMm.Length == GripHandProbeSide.RingCount
                        ? GripHandProbeSide.ClampRingOffset(rings.OffsetMm[ringIndex])
                        : Vector2.zero;
                    if (!GripHandProbeProfiles.ComputeRingFrame(bones[s], fingerAxes, starts[s], ends[s], ringFraction, tiltDeg, azimuthDeg, offsetMm,
                            out Vector3 centerWorld, out Vector3 normalWorld, out Vector3 contactWorld))
                    {
                        sample.Error = "вырожденная кость (нулевая длина или ладонное направление вдоль оси)";
                        continue;
                    }

                    Vector3 a = ToObject(starts[s]);
                    Vector3 b = ToObject(ends[s]);
                    Vector3 axis = DirToObject(normalWorld).normalized;
                    Vector3 radial = Vector3.ProjectOnPlane(DirToObject(contactWorld), axis).normalized;
                    sample.Valid = true;
                    sample.Start = a;
                    sample.End = b;
                    sample.Axis = axis;
                    sample.Palmar = radial;
                    sample.Binormal = Vector3.Cross(axis, radial);
                    sample.Radius = (radiusRawMm > 0f ? GripHandProbeSide.ClampRingRadiusMm(radiusRawMm) : GripHandProbeSide.DefaultRingRadiusMm(ringIndex)) * Mm;
                    bool ringManual = GripHandProbeSide.IsManualRingT(ringFraction) || GripHandProbeSide.IsManualRingRadius(radiusRawMm, ringIndex) ||
                                      GripHandProbeSide.IsManualRingOrientation(tiltDeg, azimuthDeg) || GripHandProbeSide.IsManualRingOffset(offsetMm);
                    sample.RingFraction = ringFraction;
                    Vector3 ringCenter = ToObject(centerWorld);
                    if (needContactPoint)
                    {
                        sample.ContactPoint = ringCenter + radial * sample.Radius;
                        sample.ContactHit = Query(sample.ContactPoint);
                        if (doK1)
                        {
                            report.Probes.Add(new GripProbe
                            {
                                Position = ToWorld(sample.ContactPoint),
                                Direction = DirToWorld(radial),
                                Kind = GripProbeKind.Contact,
                                State = Classify(sample.ContactHit.Signed),
                                Signed = sample.ContactHit.Signed,
                                RingManual = ringManual,
                                Axis = DirToWorld(axis),
                                Radius = sample.Radius
                            });

                            // Стрелка нормали точки касания (contactDir) и рядом — нормаль ближайшей поверхности предмета (внутрь)
                            Vector3 contactWorldPos = ToWorld(sample.ContactPoint);
                            report.Lines.Add(new GripProbeLine { From = contactWorldPos, To = contactWorldPos + DirToWorld(radial) * 0.007f, Color = new Color(0.3f, 0.85f, 1f, 1f), Width = 2f });
                            if (sample.ContactHit.Valid && sample.ContactHit.Distance < ContactNormalShowDistance)
                            {
                                report.Lines.Add(new GripProbeLine { From = contactWorldPos, To = contactWorldPos - DirToWorld(sample.ContactHit.Normal) * 0.007f, Color = new Color(1f, 0.6f, 0.15f, 1f), Width = 2f });
                            }
                        }
                    }

                    if (doX1)
                    {
                        sample.RingEvaluated = true;
                        sample.RingMinSigned = float.PositiveInfinity;
                        for (int k = 0; k < 6; k++)
                        {
                            float angle = k * RingStepDeg;
                            float rad = angle * Mathf.Deg2Rad;
                            Vector3 dir = radial * Mathf.Cos(rad) + sample.Binormal * Mathf.Sin(rad);
                            Vector3 p = ringCenter + dir * sample.Radius;
                            // Точка 0° совпадает с ладонной точкой K1 — повторно не считаем
                            GripSurfaceHit h = needContactPoint && k == 0 ? sample.ContactHit : Query(p);
                            if (h.Signed < sample.RingMinSigned)
                            {
                                sample.RingMinSigned = h.Signed;
                                sample.RingDeepestAngle = angle;
                                sample.RingDeepestMethod = h.Method;
                            }

                            report.Probes.Add(new GripProbe
                            {
                                Position = ToWorld(p),
                                Direction = DirToWorld(dir),
                                Kind = GripProbeKind.Ring,
                                State = Classify(h.Signed),
                                Signed = h.Signed,
                                RingManual = ringManual && k == 0 && !doK1,
                                Axis = DirToWorld(axis),
                                Radius = sample.Radius
                            });
                        }
                    }
                }

                if (doX2)
                {
                    Vector3 tipObject = ToObject(tipWorld);
                    tipHits[f] = Query(tipObject);
                    tipValid[f] = true;
                    report.Probes.Add(new GripProbe
                    {
                        Position = ToWorld(tipObject),
                        Direction = DirToWorld(samples[f, 2].Valid ? samples[f, 2].Axis : Vector3.forward),
                        Kind = GripProbeKind.Tip,
                        State = Classify(tipHits[f].Signed),
                        Signed = tipHits[f].Signed
                    });
                }
            }

            if (doK1) report.Checks["K1"] = EvaluateK1(samples, tolIn, tolOut);
            if (doX1) report.Checks["X1"] = EvaluateX1(samples);
            if (doX2) report.Checks["X2"] = EvaluateX2(tipHits, tipValid);
            if (doD1) report.Checks["D1"] = EvaluateD1(samples, report, ToWorld, DirToWorld);
            if (doP1) report.Checks["P1"] = EvaluateP1(host.Avatar, hand, host.Side, palmOverride, report, ToObject, DirToObject, ToWorld, DirToWorld, Query, Classify);
            if (doF1) report.Checks["F1"] = EvaluateF1(host.Avatar, host.Side, hand, fingers, wristAxes, fingerAxes);
        }

        static bool AxesValid(UxrUniversalLocalAxes axes)
        {
            return axes != null &&
                   axes.LocalRight.sqrMagnitude > 0.5f &&
                   axes.LocalUp.sqrMagnitude > 0.5f &&
                   axes.LocalForward.sqrMagnitude > 0.5f;
        }

        /// <summary>Пересобирает кэш, если изменились предмет, align или меши относительно корня предмета.</summary>
        string EnsureCache(GameObject weaponRoot, Transform snap, Matrix4x4 worldToObject)
        {
            // Ключ из четырёх частей (предмет, align, состав мешей, их положение), чтобы назвать причину перестройки. Матрицы относительно корня квантуются:
            // при движении предмета целиком они меняются только в пределах float-шума, и кэш не должен пересобираться.
            // Align квантуется грубо (5 мм / 0,005): он влияет лишь на отбор треугольников в радиусе 0,22 м.
            var identity = new GripHash();
            identity.Add(weaponRoot.GetHashCode());
            identity.Add(snap.GetHashCode());

            var align = new GripHash();
            AddQuantized(ref align, worldToObject * snap.localToWorldMatrix, 0.005f);

            var meshes = new GripHash();
            var layout = new GripHash();
            foreach (MeshFilter mf in weaponRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!GripAuthoringGeometry.IsWeaponSurfaceMesh(mf)) continue;
                meshes.Add(mf.GetHashCode());
                meshes.Add(mf.sharedMesh.GetHashCode());
                meshes.Add(mf.sharedMesh.vertexCount);
                AddQuantized(ref layout, worldToObject * mf.transform.localToWorldMatrix, 1e-4f);
            }

            ulong[] parts = { identity.Value, align.Value, meshes.Value, layout.Value };
            if (_cache.HasKey && _cacheKeyParts != null)
            {
                string reason = null;
                if (parts[0] != _cacheKeyParts[0]) reason = "другой предмет/align";
                else if (parts[2] != _cacheKeyParts[2]) reason = "изменился состав мешей";
                else if (parts[3] != _cacheKeyParts[3]) reason = "сдвинулись меши относительно корня";
                else if (parts[1] != _cacheKeyParts[1]) reason = "сдвинулся align относительно предмета";
                if (reason == null)
                {
                    return null;
                }

                Rebuild(weaponRoot, snap, worldToObject, parts);
                return reason;
            }

            Rebuild(weaponRoot, snap, worldToObject, parts);
            return "первый расчёт или «Пересчитать»";
        }

        void Rebuild(GameObject weaponRoot, Transform snap, Matrix4x4 worldToObject, ulong[] parts)
        {
            var key = new GripHash();
            foreach (ulong part in parts)
            {
                key.Add((int)part);
                key.Add((int)(part >> 32));
            }

            _cache.Rebuild(GripAuthoringGeometry.CollectWeaponTrianglesInObjectSpace(weaponRoot, snap, worldToObject), key.Value);
            _cacheKeyParts = parts;
        }

        static void AddQuantized(ref GripHash hash, Matrix4x4 m, float step)
        {
            for (int i = 0; i < 16; i++)
            {
                hash.Add(Mathf.RoundToInt(m[i] / step));
            }
        }

        static string Mm2(float meters) => (meters * 1000f).ToString("+0.00;-0.00;0.00");
        static string MmAbs(float meters) => (meters * 1000f).ToString("0.00");

        static string SignTag(GripSurfaceHit hit)
        {
            switch (hit.Method)
            {
                case GripSignMethod.Winding: return $"W {hit.Winding:0.00}";
                case GripSignMethod.WindingAmbiguous: return $"N (W {hit.Winding:0.00} неоднозначен)";
                default: return "N";
            }
        }

        /// <summary>Угол между нормалью точки касания кольца и нормалью ближайшей поверхности предмета (внутрь).</summary>
        static string NormalAngleText(PhalanxSample sample)
        {
            if (!sample.ContactHit.Valid || sample.ContactHit.Distance >= ContactNormalShowDistance)
            {
                return "";
            }

            return $" · ∠ к нормали предмета {Vector3.Angle(sample.Palmar, -sample.ContactHit.Normal):0}°";
        }

        static GripCheckStatus Worst(GripCheckStatus a, GripCheckStatus b)
        {
            return (int)a >= (int)b ? a : b;
        }

        GripCheckResult EvaluateK1(PhalanxSample[,] samples, float tolIn, float tolOut)
        {
            var result = new GripCheckResult { Definition = GripAnalysisSettings.K1 };
            int okMin = Mathf.RoundToInt(GripAnalysisSettings.Get(GripAnalysisSettings.K1, 2));
            int warnMin = Mathf.RoundToInt(GripAnalysisSettings.Get(GripAnalysisSettings.K1, 3));
            int total = 0;
            int valid = 0;
            var perFinger = new int[5];

            for (int f = 0; f < 5; f++)
            {
                var lines = new List<GripDetailLine>(3);
                for (int s = 0; s < 3; s++)
                {
                    PhalanxSample sample = samples[f, s];
                    if (!sample.Valid)
                    {
                        lines.Add(new GripDetailLine { Indent = 1, Status = GripCheckStatus.NotApplicable, Text = $"{SegmentNamesRu[s]}: n/a ({sample.Error ?? "нет данных"})" });
                        continue;
                    }

                    valid++;
                    float d = sample.ContactHit.Signed;
                    string state;
                    GripCheckStatus status;
                    if (d < -tolIn)
                    {
                        state = "проникновение";
                        status = GripCheckStatus.Fail;
                    }
                    else if (d <= tolOut)
                    {
                        state = "контакт";
                        status = GripCheckStatus.Ok;
                        perFinger[f]++;
                        total++;
                    }
                    else
                    {
                        state = "просвет";
                        status = GripCheckStatus.Warn;
                    }

                    lines.Add(new GripDetailLine { Indent = 1, Status = status, Text = $"{SegmentNamesRu[s]}: {Mm2(d)} мм · {state} · знак {SignTag(sample.ContactHit)}{NormalAngleText(sample)}" });
                }

                GripCheckStatus fingerStatus = perFinger[f] == 3 ? GripCheckStatus.Ok : perFinger[f] > 0 ? GripCheckStatus.Warn : GripCheckStatus.Fail;
                result.Add(0, fingerStatus, $"{FingerNamesRu[f]}: {perFinger[f]}/3");
                result.Details.AddRange(lines);
            }

            if (valid == 0)
            {
                result.Status = GripCheckStatus.NotApplicable;
                result.Summary = "нет фаланг для оценки";
                return result;
            }

            result.Status = total >= okMin ? GripCheckStatus.Ok : total >= warnMin ? GripCheckStatus.Warn : GripCheckStatus.Fail;
            var parts = new List<string>(5);
            for (int f = 0; f < 5; f++)
            {
                parts.Add($"{FingerShortRu[f]} {perFinger[f]}/3");
            }

            result.Summary = $"{total}/15 в контакте · " + string.Join(" ", parts);
            return result;
        }

        GripCheckResult EvaluateX1(PhalanxSample[,] samples)
        {
            var result = new GripCheckResult { Definition = GripAnalysisSettings.X1 };
            float tol = GripAnalysisSettings.Get(GripAnalysisSettings.X1, 0) * Mm;
            float worstDepth = 0f;
            string worstName = null;
            int failed = 0;
            int valid = 0;
            int points = 0;

            for (int f = 0; f < 5; f++)
            {
                float fingerDepth = 0f;
                var lines = new List<GripDetailLine>(3);
                for (int s = 0; s < 3; s++)
                {
                    PhalanxSample sample = samples[f, s];
                    if (!sample.Valid || !sample.RingEvaluated)
                    {
                        lines.Add(new GripDetailLine { Indent = 1, Status = GripCheckStatus.NotApplicable, Text = $"{SegmentNamesRu[s]}: n/a ({sample.Error ?? "нет данных"})" });
                        continue;
                    }

                    valid++;
                    points += 6;
                    float depth = Mathf.Max(0f, -sample.RingMinSigned);
                    fingerDepth = Mathf.Max(fingerDepth, depth);
                    GripCheckStatus status = depth > tol ? GripCheckStatus.Fail : GripCheckStatus.Ok;
                    if (status == GripCheckStatus.Fail)
                    {
                        failed++;
                    }

                    if (depth > worstDepth)
                    {
                        worstDepth = depth;
                        worstName = $"{FingerNamesRu[f]}, {SegmentNamesRu[s]}";
                    }

                    string where = depth > 0f
                        ? $" @t {sample.RingFraction:0.00}, {sample.RingDeepestAngle:0}° · знак {(sample.RingDeepestMethod == GripSignMethod.Winding ? "W" : "N")}"
                        : $" (мин. просвет {Mm2(sample.RingMinSigned)} мм)";
                    lines.Add(new GripDetailLine { Indent = 1, Status = status, Text = $"{SegmentNamesRu[s]}: глубина {MmAbs(depth)} мм{where}" });
                }

                result.Add(0, fingerDepth > tol ? GripCheckStatus.Fail : GripCheckStatus.Ok, $"{FingerNamesRu[f]}: макс. {MmAbs(fingerDepth)} мм");
                result.Details.AddRange(lines);
            }

            if (valid == 0)
            {
                result.Status = GripCheckStatus.NotApplicable;
                result.Summary = "нет фаланг для оценки";
                return result;
            }

            result.Status = failed > 0 ? GripCheckStatus.Fail : GripCheckStatus.Ok;
            result.Summary = worstName != null
                ? $"макс. {MmAbs(worstDepth)} мм ({worstName}) · глубже допуска {failed}/{valid} · точек {points}"
                : $"проникновений нет · точек {points}";
            return result;
        }

        GripCheckResult EvaluateX2(GripSurfaceHit[] tipHits, bool[] tipValid)
        {
            var result = new GripCheckResult { Definition = GripAnalysisSettings.X2 };
            float tol = GripAnalysisSettings.Get(GripAnalysisSettings.X2, 0) * Mm;
            int failed = 0;
            int valid = 0;
            var parts = new List<string>(5);

            for (int f = 0; f < 5; f++)
            {
                if (!tipValid[f] || !tipHits[f].Valid)
                {
                    result.Add(0, GripCheckStatus.NotApplicable, $"{FingerNamesRu[f]}: n/a");
                    continue;
                }

                valid++;
                float d = tipHits[f].Signed;
                float depth = Mathf.Max(0f, -d);
                GripCheckStatus status = depth > tol ? GripCheckStatus.Fail : GripCheckStatus.Ok;
                if (status == GripCheckStatus.Fail)
                {
                    failed++;
                    parts.Add($"{FingerShortRu[f]} {MmAbs(depth)}");
                }

                result.Add(0, status, $"{FingerNamesRu[f]}: {Mm2(d)} мм · знак {SignTag(tipHits[f])}");
            }

            if (valid == 0)
            {
                result.Status = GripCheckStatus.NotApplicable;
                result.Summary = "нет кончиков для оценки";
                return result;
            }

            result.Status = failed > 0 ? GripCheckStatus.Fail : GripCheckStatus.Ok;
            result.Summary = failed > 0
                ? $"глубже допуска {failed}/{valid}: " + string.Join(", ", parts) + " мм"
                : $"кончики не глубже {MmAbs(tol)} мм ({valid}/5)";
            return result;
        }

        GripCheckResult EvaluateD1(PhalanxSample[,] samples, GripAnalysisReport report,
            Func<Vector3, Vector3> toWorld, Func<Vector3, Vector3> dirToWorld)
        {
            var result = new GripCheckResult { Definition = GripAnalysisSettings.D1 };
            float maxDist = GripAnalysisSettings.Get(GripAnalysisSettings.D1, 0) * Mm;
            float okDeg = GripAnalysisSettings.Get(GripAnalysisSettings.D1, 1);
            float failDeg = GripAnalysisSettings.Get(GripAnalysisSettings.D1, 2);
            int evaluated = 0, ok = 0, warn = 0, fail = 0;
            float maxAngle = 0f;
            const float NormalTick = 0.006f;

            for (int f = 0; f < 5; f++)
            {
                result.Add(0, GripCheckStatus.NotApplicable, FingerNamesRu[f]);
                for (int s = 0; s < 3; s++)
                {
                    PhalanxSample sample = samples[f, s];
                    if (!sample.Valid || !sample.ContactHit.Valid)
                    {
                        result.Add(1, GripCheckStatus.NotApplicable, $"{SegmentNamesRu[s]}: n/a");
                        continue;
                    }

                    GripSurfaceHit hit = sample.ContactHit;
                    if (hit.Distance > maxDist)
                    {
                        result.Add(1, GripCheckStatus.NotApplicable, $"{SegmentNamesRu[s]}: дальше {MmAbs(maxDist)} мм ({MmAbs(hit.Distance)})");
                        continue;
                    }

                    // Снаружи — к ближайшей точке; внутри — от неё (куда «смотрит» поверхность для подушечки)
                    Vector3 toSurface = hit.Signed >= 0f ? hit.Closest - sample.ContactPoint : sample.ContactPoint - hit.Closest;
                    if (toSurface.sqrMagnitude < 1e-12f)
                    {
                        toSurface = -hit.Normal;
                    }

                    float angle = Vector3.Angle(sample.Palmar, toSurface);
                    GripCheckStatus status = angle <= okDeg ? GripCheckStatus.Ok : angle > failDeg ? GripCheckStatus.Fail : GripCheckStatus.Warn;
                    evaluated++;
                    maxAngle = Mathf.Max(maxAngle, angle);
                    switch (status)
                    {
                        case GripCheckStatus.Ok: ok++; break;
                        case GripCheckStatus.Warn: warn++; break;
                        default: fail++; break;
                    }

                    result.Add(1, status, $"{SegmentNamesRu[s]}: {angle:0.0}° · {Mm2(hit.Signed)} мм{NormalAngleText(sample)}");

                    Vector3 from = toWorld(sample.ContactPoint);
                    report.Lines.Add(new GripProbeLine { From = from, To = from + dirToWorld(sample.Palmar) * NormalTick, Color = new Color(1f, 1f, 1f, 0.7f) });
                    report.Lines.Add(new GripProbeLine { From = from, To = toWorld(hit.Closest), Color = StatusColor(status) });
                }
            }

            if (evaluated == 0)
            {
                result.Status = GripCheckStatus.NotApplicable;
                result.Summary = $"нет ладонных точек ближе {MmAbs(maxDist)} мм";
                return result;
            }

            result.Status = fail > 0 ? GripCheckStatus.Fail : warn > 0 ? GripCheckStatus.Warn : GripCheckStatus.Ok;
            result.Summary = $"оценено {evaluated}/15 · ok {ok} · warn {warn} · fail {fail} · макс. {maxAngle:0}°";
            return result;
        }

        GripCheckResult EvaluateP1(UxrAvatar avatar, UxrAvatarHand hand, UxrHandSide side, GripPalmOverride palmOverride, GripAnalysisReport report,
            Func<Vector3, Vector3> toObject, Func<Vector3, Vector3> dirToObject,
            Func<Vector3, Vector3> toWorld, Func<Vector3, Vector3> dirToWorld,
            Func<Vector3, GripSurfaceHit> query, Func<float, GripProbeState> classify)
        {
            var result = new GripCheckResult { Definition = GripAnalysisSettings.P1 };
            if (!hand.GetPalmCenter(out Vector3 centerWorld) || !hand.GetPalmOutDirection(side, out Vector3 outWorld))
            {
                result.Status = GripCheckStatus.NotApplicable;
                result.Summary = "SDK не вычислил центр ладони (нет Wrist/Index/Little Proximal)";
                return result;
            }

            // Ручная и авто точки ладони хранятся в рамке кисти (универсальные оси UltimateXR), а не в осях кости запястья
            if (!GripHandFrame.TryGet(avatar, side, out GripHandFrame frame, out string frameError))
            {
                result.Status = GripCheckStatus.NotApplicable;
                result.Summary = "нет рамки кисти: " + frameError;
                return result;
            }

            float warnDist = GripAnalysisSettings.Get(GripAnalysisSettings.P1, 0) * Mm;
            float failDist = GripAnalysisSettings.Get(GripAnalysisSettings.P1, 1) * Mm;
            float okDeg = GripAnalysisSettings.Get(GripAnalysisSettings.P1, 2);

            bool manual = palmOverride.Active;
            bool onSkin = false;
            Vector3 pointWorld;
            Vector3 normalWorld;
            if (manual)
            {
                // Ручная разметка: точка и нормаль из рамки кисти
                pointWorld = frame.PointToWorld(palmOverride.PositionHand);
                normalWorld = palmOverride.NormalHand.sqrMagnitude > 1e-12f ? frame.DirToWorld(palmOverride.NormalHand) : outWorld;
            }
            else
            {
                // Точка на коже ладони: сохранённое в рамке кисти смещение от центра SDK (BakeMesh не на каждом кадре)
                _palmSkin.Ensure(avatar, side, hand);
                onSkin = _palmSkin.HasSkin;
                pointWorld = onSkin ? centerWorld + frame.VectorToWorld(_palmSkin.OffsetHand) : centerWorld;
                normalWorld = onSkin && _palmSkin.HasNormal ? frame.DirToWorld(_palmSkin.NormalHand) : outWorld;
            }

            Vector3 centerSdk = toObject(centerWorld);
            Vector3 point = toObject(pointWorld);
            Vector3 palmNormal = dirToObject(normalWorld).normalized;
            GripSurfaceHit hit = query(point);
            string pointName = manual || onSkin ? "точка ладони" : "центр ладони";

            GripCheckStatus distStatus;
            string distText;
            if (hit.Signed < 0f)
            {
                distStatus = GripCheckStatus.Fail;
                distText = $"{pointName} внутри предмета на {MmAbs(hit.Distance)} мм";
            }
            else
            {
                distStatus = hit.Distance > failDist ? GripCheckStatus.Fail : hit.Distance > warnDist ? GripCheckStatus.Warn : GripCheckStatus.Ok;
                distText = $"расстояние {MmAbs(hit.Distance)} мм (warn > {MmAbs(warnDist)}, fail > {MmAbs(failDist)})";
            }

            Vector3 toSurface = hit.Signed >= 0f ? hit.Closest - point : point - hit.Closest;
            if (toSurface.sqrMagnitude < 1e-12f)
            {
                toSurface = -hit.Normal;
            }

            float angle = Vector3.Angle(palmNormal, toSurface);
            GripCheckStatus angleStatus = angle <= okDeg ? GripCheckStatus.Ok : GripCheckStatus.Warn;
            string normalSource = manual ? "нормаль разметки" : onSkin && _palmSkin.HasNormal ? "нормаль кожи" : "GetPalmOutDirection";
            float offsetMm = Vector3.Distance(centerSdk, point) * 1000f;

            if (manual)
            {
                result.Add(0, GripCheckStatus.NotApplicable, $"источник: {palmOverride.Source} · смещение от центра SDK {offsetMm:0.00} мм");
            }
            else
            {
                if (!string.IsNullOrEmpty(palmOverride.Note))
                {
                    result.Add(0, GripCheckStatus.NotApplicable, palmOverride.Note);
                }

                if (onSkin)
                {
                    result.Add(0, GripCheckStatus.NotApplicable,
                        $"источник: авто (кожа · рендерер «{_palmSkin.RendererName}» · внешнее попадание t = {_palmSkin.SelectedTMm:0.0} мм) · смещение от центра SDK {offsetMm:0.00} мм");
                }
                else
                {
                    result.Add(0, GripCheckStatus.Warn, $"источник: авто — центр SDK (кожа не найдена: {_palmSkin.FailReason})");
                }

                if (_palmSkin.UsedAllTriangles)
                {
                    result.Add(1, GripCheckStatus.Warn, "ладонная фильтрация (запястье/пястные) не дала треугольников — луч по всем треугольникам, внешнее попадание");
                }

                // Слои вдоль луча: кожа и перчатка поверх неё дают разные t; выбрано внешнее (максимальное)
                foreach (GripPalmSkinPoint.Layer layer in _palmSkin.Layers)
                {
                    string hits = layer.HitsMm.Count > 0
                        ? "t = " + string.Join(", ", layer.HitsMm.ConvertAll(t => t.ToString("0.0"))) + " мм"
                        : "нет попаданий";
                    bool selected = onSkin && layer.Name == _palmSkin.RendererName;
                    result.Add(1, GripCheckStatus.NotApplicable,
                        $"{(selected ? "▶ " : "")}«{layer.Name}»: ладонных треугольников {layer.PalmTriangles} · попаданий {layer.HitsMm.Count} · {hits}");
                }
            }

            result.Add(0, distStatus, distText + $" · знак {SignTag(hit)}");
            result.Add(0, angleStatus, $"угол нормали ладони ({normalSource}) к поверхности {angle:0.0}° (ok ≤ {okDeg:0}°)");
            result.Status = Worst(distStatus, angleStatus);
            result.Summary = $"{Mm2(hit.Signed)} мм · угол {angle:0}°" +
                             (manual ? " · ладонь: вручную" : onSkin ? "" : " · точка ладони: центр SDK (кожа не найдена)");

            Vector3 pointPreview = toWorld(point);
            if (manual || onSkin)
            {
                // Тонкая линия «центр SDK → точка ладони», чтобы было видно смещение
                report.Lines.Add(new GripProbeLine { From = toWorld(centerSdk), To = pointPreview, Color = new Color(0.85f, 0.85f, 0.85f, 0.8f), Width = 1f });
            }

            report.Probes.Add(new GripProbe
            {
                Position = pointPreview,
                Direction = dirToWorld(palmNormal),
                Kind = GripProbeKind.Palm,
                State = classify(hit.Signed),
                Signed = hit.Signed,
                Manual = manual
            });
            Color normalColor = manual ? new Color(1f, 0.55f, 0.1f, 1f) : new Color(1f, 0.92f, 0.4f, 0.9f);
            report.Lines.Add(new GripProbeLine { From = pointPreview, To = pointPreview + dirToWorld(palmNormal) * 0.025f, Color = normalColor });
            if (manual && palmOverride.ForwardHand.sqrMagnitude > 1e-12f)
            {
                // Ось «вперёд» ручной рамки ладони
                Vector3 forward = dirToObject(frame.DirToWorld(palmOverride.ForwardHand)).normalized;
                report.Lines.Add(new GripProbeLine { From = pointPreview, To = pointPreview + dirToWorld(forward) * 0.02f, Color = new Color(0.25f, 0.6f, 1f, 1f) });
            }
            report.Lines.Add(new GripProbeLine { From = pointPreview, To = toWorld(hit.Closest), Color = StatusColor(result.Status) });
            return result;
        }

        GripCheckResult EvaluateF1(UxrAvatar avatar, UxrHandSide side, UxrAvatarHand hand, UxrAvatarFinger[] fingers,
            UxrUniversalLocalAxes wristAxes, UxrUniversalLocalAxes fingerAxes)
        {
            var result = new GripCheckResult { Definition = GripAnalysisSettings.F1 };
            float tol = GripAnalysisSettings.Get(GripAnalysisSettings.F1, 0);
            float residualWarnDeg = GripAnalysisSettings.Get(GripAnalysisSettings.F1, 1);
            EnsureBindStates(avatar, side, wristAxes, fingerAxes);

            int violations = 0;
            int valid = 0;
            var parts = new List<string>();
            var naReasons = new List<string>();
            string[] jointNames = { "Spread", "Curl1", "Curl2", "Curl3" };
            string[] residualNames = { "средн.", "дист." };

            for (int f = 0; f < 5; f++)
            {
                UxrAvatarFinger finger = fingers[f];
                if (finger == null || finger.Proximal == null || finger.Intermediate == null || finger.Distal == null)
                {
                    result.Add(0, GripCheckStatus.NotApplicable, $"{FingerNamesRu[f]}: n/a (нет костей пальца)");
                    continue;
                }

                BindPoseFingerState? bind = _bindStates[f];
                if (!bind.HasValue)
                {
                    // Без bind-аватара углы IK не восстановить: текущая поза вместо bind дала бы ложные выходы
                    string reason = _bindReasons[f] ?? "bind-состояние недоступно";
                    result.Add(0, GripCheckStatus.NotApplicable, $"{FingerNamesRu[f]}: n/a ({reason})");
                    if (!naReasons.Contains(reason))
                    {
                        naReasons.Add(reason);
                    }

                    continue;
                }

                valid++;
                bool isThumb = FingerTypes[f] == UxrFingerType.Thumb;
                BindPoseFingerState bindState = bind.Value;
                GripAuthoringGeometry.GetFingerAngleBounds(side, isThumb, bindState.BindSpread,
                    out float minSpread, out float maxSpread,
                    out float minCurl1, out float maxCurl1,
                    out float minCurl2, out float maxCurl2,
                    out float minCurl3, out float maxCurl3);

                // Та же параметризация, что применяют IK и «Обхватить» и в которой заданы пределы:
                // bind-угол + приращение от bind-ориентации кости
                GripAuthoringGeometry.MeasureAnatomicalFingerAngles(finger, bindState, fingerAxes,
                    out float spread, out float curl1, out float curl2, out float curl3);

                float[] values = { spread, curl1, curl2, curl3 };
                float[] mins = { minSpread, minCurl1, minCurl2, minCurl3 };
                float[] maxs = { maxSpread, maxCurl1, maxCurl2, maxCurl3 };

                var lines = new List<GripDetailLine>();
                bool fingerViolated = false;
                for (int j = 0; j < 4; j++)
                {
                    float over = values[j] > maxs[j] + tol ? values[j] - maxs[j] : values[j] < mins[j] - tol ? values[j] - mins[j] : 0f;
                    string overText = over == 0f ? "" : $" — выход {over:+0.0;-0.0}°";
                    lines.Add(new GripDetailLine
                    {
                        Indent = 1,
                        Status = over == 0f ? GripCheckStatus.Ok : GripCheckStatus.Warn,
                        Text = $"{jointNames[j]} = {values[j]:0.0}°, предел {mins[j]:0.#}..{maxs[j]:0.#}°{overText}"
                    });
                    if (over == 0f)
                    {
                        continue;
                    }

                    fingerViolated = true;
                    violations++;
                    parts.Add($"{FingerShortRu[f]} {jointNames[j]} {over:+0.0;-0.0}°");
                }

                // Остаток вне шарнира средней и конечной фаланг: боковой (вокруг LocalUp) и скручивающий (вокруг LocalForward)
                Quaternion[] boneRotations = { finger.Intermediate.localRotation, finger.Distal.localRotation };
                Quaternion[] bindRotations = { bindState.IntermediateLocalRot, bindState.DistalLocalRot };
                for (int s = 0; s < 2; s++)
                {
                    GripAuthoringGeometry.GetHingeResidual(boneRotations[s], bindRotations[s], fingerAxes,
                        out float lateral, out float twist, out float total);
                    bool residualViolated = total > residualWarnDeg;
                    lines.Add(new GripDetailLine
                    {
                        Indent = 1,
                        Status = residualViolated ? GripCheckStatus.Warn : GripCheckStatus.Ok,
                        Text = $"{residualNames[s]}: вне шарнира {total:0.0}° (вбок {lateral:+0.0;-0.0}°, скручивание {twist:+0.0;-0.0}°), порог {residualWarnDeg:0.#}°"
                    });
                    if (!residualViolated)
                    {
                        continue;
                    }

                    fingerViolated = true;
                    violations++;
                    parts.Add($"{FingerShortRu[f]} {residualNames[s]} вне шарнира {total:0.0}°");
                }

                // Осевое скручивание проксимальной фаланги (корневой сустав допускает только Spread и Curl1).
                // У большого пальца «проксимальная» SDK — пястная кость с суставом CMC, где осевое вращение есть: только справка
                float proximalTwist = GripAuthoringGeometry.MeasureProximalTwist(finger, bindState, fingerAxes);
                bool twistViolated = !isThumb && Mathf.Abs(proximalTwist) > residualWarnDeg;
                lines.Add(new GripDetailLine
                {
                    Indent = 1,
                    Status = isThumb ? GripCheckStatus.NotApplicable : twistViolated ? GripCheckStatus.Warn : GripCheckStatus.Ok,
                    Text = isThumb
                        ? $"прокс.: скручивание {proximalTwist:+0.0;-0.0}° (справка: у большого пальца CMC допускает вращение)"
                        : $"прокс.: скручивание {proximalTwist:+0.0;-0.0}°, порог {residualWarnDeg:0.#}°"
                });
                if (twistViolated)
                {
                    fingerViolated = true;
                    violations++;
                    parts.Add($"{FingerShortRu[f]} прокс. скручивание {proximalTwist:+0.0;-0.0}°");
                }

                // Пястная кость: решатели её не трогают, поза должна совпадать с bind
                if (finger.Metacarpal != null && bindState.HasMetacarpal)
                {
                    float metacarpalDeviation = Quaternion.Angle(bindState.MetacarpalLocalRot, finger.Metacarpal.localRotation);
                    bool metacarpalViolated = metacarpalDeviation > residualWarnDeg;
                    lines.Add(new GripDetailLine
                    {
                        Indent = 1,
                        Status = metacarpalViolated ? GripCheckStatus.Warn : GripCheckStatus.Ok,
                        Text = $"пястная: отличие от bind {metacarpalDeviation:0.0}°, порог {residualWarnDeg:0.#}°"
                    });
                    if (metacarpalViolated)
                    {
                        fingerViolated = true;
                        violations++;
                        parts.Add($"{FingerShortRu[f]} пястная {metacarpalDeviation:0.0}°");
                    }
                }
                else if (finger.Metacarpal != null || bindState.HasMetacarpal)
                {
                    lines.Add(new GripDetailLine { Indent = 1, Status = GripCheckStatus.NotApplicable, Text = "пястная: n/a (есть только у позы или только у bind-аватара)" });
                }

                result.Add(0, fingerViolated ? GripCheckStatus.Warn : GripCheckStatus.Ok,
                    $"{FingerNamesRu[f]}: S {values[0]:0.0}° · C1 {values[1]:0.0}° · C2 {values[2]:0.0}° · C3 {values[3]:0.0}°");
                result.Details.AddRange(lines);
            }

            if (valid == 0)
            {
                result.Status = GripCheckStatus.NotApplicable;
                result.Summary = naReasons.Count > 0 ? string.Join("; ", naReasons) : "нет пальцев для оценки";
                return result;
            }

            result.Status = violations > 0 ? GripCheckStatus.Warn : GripCheckStatus.Ok;
            result.Summary = violations > 0
                ? $"нарушений: {violations} · " + string.Join(", ", parts)
                : "все суставы в пределах, средние и конечные фаланги — чистые шарниры";
            return result;
        }

        void EnsureBindStates(UxrAvatar avatar, UxrHandSide side, UxrUniversalLocalAxes wristAxes, UxrUniversalLocalAxes fingerAxes)
        {
            int avatarId = avatar.GetHashCode();
            if (_bindStates != null && _bindAvatarId == avatarId && _bindSide == side)
            {
                return;
            }

            _bindAvatarId = avatarId;
            _bindSide = side;
            _bindStates = new BindPoseFingerState?[5];
            _bindReasons = new string[5];
            for (int f = 0; f < 5; f++)
            {
                try
                {
                    // Bind-состояние как у IK, но без подмены текущей позой: без bind-аватара F1 для пальца n/a
                    if (GripAuthoringGeometry.TryCaptureBindPoseFingerState(avatar, side, FingerTypes[f], wristAxes, fingerAxes,
                            out BindPoseFingerState state, out string reason))
                    {
                        _bindStates[f] = state;
                    }
                    else
                    {
                        _bindReasons[f] = reason;
                    }
                }
                catch (Exception ex)
                {
                    _bindStates[f] = null;
                    _bindReasons[f] = "ошибка bind-состояния: " + ex.Message;
                }
            }
        }

        public static Color StatusColor(GripCheckStatus status)
        {
            switch (status)
            {
                case GripCheckStatus.Ok: return new Color(0.25f, 0.78f, 0.35f, 1f);
                case GripCheckStatus.Warn: return new Color(0.95f, 0.68f, 0.15f, 1f);
                case GripCheckStatus.Fail: return new Color(0.92f, 0.26f, 0.26f, 1f);
                default: return new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        }
    }

    /// <summary>FNV-1a по битам значений: сигнатура состояния для решения о пересчёте.</summary>
    internal struct GripHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        ulong _value;
        bool _started;

        public ulong Value => _started ? _value : Offset;

        public void Add(int value)
        {
            if (!_started)
            {
                _value = Offset;
                _started = true;
            }

            unchecked
            {
                uint v = (uint)value;
                for (int i = 0; i < 4; i++)
                {
                    _value ^= v & 0xFFu;
                    _value *= Prime;
                    v >>= 8;
                }
            }
        }

        public void Add(float value) => Add(BitConverter.SingleToInt32Bits(value));

        public void Add(Vector3 v)
        {
            Add(v.x);
            Add(v.y);
            Add(v.z);
        }

        public void Add(Quaternion q)
        {
            Add(q.x);
            Add(q.y);
            Add(q.z);
            Add(q.w);
        }

        public void Add(Matrix4x4 m)
        {
            for (int i = 0; i < 16; i++)
            {
                Add(m[i]);
            }
        }
    }
}
