using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    /// <summary>
    /// Кольца фаланг (K1/X1): сдвиг вдоль кости (t), радиус, наклон плоскости (pitch/yaw) и азимут точки касания.
    /// Ручки в SceneView, черновик на время перетаскивания, запись в профиль кисти (Undo) при отпускании ручки;
    /// подгонка радиусов и наклона по коже по кнопке.
    /// </summary>
    internal static class GripRingEditing
    {
        static readonly int SliderHint = "VrBattlegrounds.GripRingEditing.T".GetHashCode();
        static readonly int RadiusHint = "VrBattlegrounds.GripRingEditing.R".GetHashCode();
        static readonly int AzimuthHint = "VrBattlegrounds.GripRingEditing.A".GetHashCode();
        static readonly int PitchHint = "VrBattlegrounds.GripRingEditing.P".GetHashCode();
        static readonly int YawHint = "VrBattlegrounds.GripRingEditing.Y".GetHashCode();
        static readonly int OffsetHint = "VrBattlegrounds.GripRingEditing.O".GetHashCode();
        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        static readonly Color DefaultRingColor = new Color(0.85f, 0.85f, 0.95f, 0.9f);
        static readonly Color ManualRingColor = new Color(1f, 0.35f, 0.9f, 1f);
        static readonly Color ContactColor = new Color(0.3f, 0.85f, 1f, 1f);

        // Подгонка по коже: лучи в плоскости кольца
        const int FitRays = 12;
        const float FitMaxDistance = 0.025f;
        const int FitMinHits = 6;

        static float[] _draftT;
        static float[] _draftRadiusMm;
        static Vector2[] _draftTilt;
        static float[] _draftAzimuth;
        static Vector2[] _draftOffset;
        static int _draftAvatarId;
        static UxrHandSide _draftSide;
        static int _activeIndex = -1;
        static int _discDragId;
        static Vector2 _discStartTilt;

        /// <summary>Растёт при любом изменении черновика; входит в сигнатуру пересчёта.</summary>
        public static int Version { get; private set; }
        public static string Status { get; set; } = "";

        static GripRingEditing()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                _draftT = null;
                _draftRadiusMm = null;
                _draftTilt = null;
                _draftAzimuth = null;
                _draftOffset = null;
            };
        }

        static bool DraftFor(GripAnalysisHost host) =>
            host.Avatar != null && host.Avatar.GetHashCode() == _draftAvatarId && host.Side == _draftSide;

        static void BeginDraft(GripAnalysisHost host)
        {
            if (DraftFor(host))
            {
                return;
            }

            _draftAvatarId = host.Avatar.GetHashCode();
            _draftSide = host.Side;
            _draftT = null;
            _draftRadiusMm = null;
            _draftTilt = null;
            _draftAzimuth = null;
            _draftOffset = null;
        }

        /// <summary>Действующие t колец руки: черновик перетаскивания, иначе профиль, иначе середина.</summary>
        public static float[] GetEffective(GripAnalysisHost host, GripHandProbeProfile profile)
        {
            if (DraftFor(host) && _draftT != null)
            {
                return (float[])_draftT.Clone();
            }

            return profile != null ? profile.Get(host.Side).GetRingsCopy() : GripHandProbeSide.DefaultRings();
        }

        /// <summary>Заданные радиусы колец, мм (0 — по умолчанию): черновик, иначе профиль.</summary>
        public static float[] GetRawRadiiMm(GripAnalysisHost host, GripHandProbeProfile profile)
        {
            if (DraftFor(host) && _draftRadiusMm != null)
            {
                return (float[])_draftRadiusMm.Clone();
            }

            return profile != null ? profile.Get(host.Side).GetRingRadiiRawCopyMm() : new float[GripHandProbeSide.RingCount];
        }

        /// <summary>Наклон колец, ° (x — pitch, y — yaw): черновик, иначе профиль.</summary>
        public static Vector2[] GetTiltsDeg(GripAnalysisHost host, GripHandProbeProfile profile)
        {
            if (DraftFor(host) && _draftTilt != null)
            {
                return (Vector2[])_draftTilt.Clone();
            }

            return profile != null ? profile.Get(host.Side).GetRingTiltsCopyDeg() : new Vector2[GripHandProbeSide.RingCount];
        }

        /// <summary>Азимут точки касания колец, °: черновик, иначе профиль.</summary>
        public static float[] GetAzimuthsDeg(GripAnalysisHost host, GripHandProbeProfile profile)
        {
            if (DraftFor(host) && _draftAzimuth != null)
            {
                return (float[])_draftAzimuth.Clone();
            }

            return profile != null ? profile.Get(host.Side).GetRingAzimuthsCopyDeg() : new float[GripHandProbeSide.RingCount];
        }

        /// <summary>Смещение центров колец, мм (x — LocalRight, y — LocalUp): черновик, иначе профиль.</summary>
        public static Vector2[] GetOffsetsMm(GripAnalysisHost host, GripHandProbeProfile profile)
        {
            if (DraftFor(host) && _draftOffset != null)
            {
                return (Vector2[])_draftOffset.Clone();
            }

            return profile != null ? profile.Get(host.Side).GetRingOffsetsCopyMm() : new Vector2[GripHandProbeSide.RingCount];
        }

        /// <summary>Все настройки колец для расчёта анализа (с учётом черновика).</summary>
        public static GripRingSettings GetSettings(GripAnalysisHost host, GripHandProbeProfile profile)
        {
            return new GripRingSettings
            {
                T = GetEffective(host, profile),
                RadiusRawMm = GetRawRadiiMm(host, profile),
                TiltDeg = GetTiltsDeg(host, profile),
                AzimuthDeg = GetAzimuthsDeg(host, profile),
                OffsetMm = GetOffsetsMm(host, profile)
            };
        }

        /// <summary>Пишет t колец в профиль (Undo) и сбрасывает черновик t.</summary>
        public static void Save(GripAnalysisHost host, float[] rings, string undoName)
        {
            GripHandProbeProfile written = GripHandProbeProfiles.SaveRings(host.Avatar, host.Side, rings, undoName, out string error);
            Status = written != null ? $"t колец записаны в {AssetDatabase.GetAssetPath(written)}." : "Не записано: " + error;
            _draftT = null;
            Version++;
        }

        /// <summary>Пишет радиусы колец, мм (0 — по умолчанию), в профиль (Undo) и сбрасывает черновик радиусов.</summary>
        public static void SaveRadii(GripAnalysisHost host, float[] radiiMm, string undoName, string status = null)
        {
            GripHandProbeProfile written = GripHandProbeProfiles.SaveRingRadii(host.Avatar, host.Side, radiiMm, undoName, out string error);
            Status = written != null ? status ?? $"Радиусы колец записаны в {AssetDatabase.GetAssetPath(written)}." : "Не записано: " + error;
            _draftRadiusMm = null;
            Version++;
        }

        /// <summary>Пишет наклон и азимут колец в профиль (Undo) и сбрасывает их черновик.</summary>
        public static void SaveOrientation(GripAnalysisHost host, Vector2[] tiltDeg, float[] azimuthDeg, string undoName, string status = null)
        {
            GripHandProbeProfile written = GripHandProbeProfiles.SaveRingOrientation(host.Avatar, host.Side, tiltDeg, azimuthDeg, undoName, out string error);
            Status = written != null ? status ?? $"Наклон и азимут колец записаны в {AssetDatabase.GetAssetPath(written)}." : "Не записано: " + error;
            _draftTilt = null;
            _draftAzimuth = null;
            Version++;
        }

        /// <summary>Пишет смещения центров колец в профиль (Undo) и сбрасывает их черновик.</summary>
        public static void SaveOffsets(GripAnalysisHost host, Vector2[] offsetsMm, string undoName, string status = null)
        {
            GripHandProbeProfile written = GripHandProbeProfiles.SaveRingOffsets(host.Avatar, host.Side, offsetsMm, undoName, out string error);
            Status = written != null ? status ?? $"Смещения колец записаны в {AssetDatabase.GetAssetPath(written)}." : "Не записано: " + error;
            _draftOffset = null;
            Version++;
        }

        struct RingBone
        {
            public bool Valid;
            public Transform Bone;
            public Vector3 Start;
            public Vector3 End;
        }

        /// <summary>Кости и сегменты 15 фаланг в мире аватара (дистальная — до кончика пальца).</summary>
        static RingBone[] RingBones(UxrAvatarHand hand, UxrUniversalLocalAxes fingerAxes)
        {
            var result = new RingBone[GripHandProbeSide.RingCount];
            for (int f = 0; f < GripAnalysisEngine.FingerTypes.Length; f++)
            {
                UxrAvatarFinger finger = hand.GetFinger(GripAnalysisEngine.FingerTypes[f]);
                if (finger == null || finger.Proximal == null || finger.Intermediate == null || finger.Distal == null)
                {
                    continue;
                }

                Vector3 tip = GripAuthoringGeometry.GetDistalTipWorld(finger, fingerAxes);
                Transform[] bones = { finger.Proximal, finger.Intermediate, finger.Distal };
                Vector3[] starts = { finger.Proximal.position, finger.Intermediate.position, finger.Distal.position };
                Vector3[] ends = { finger.Intermediate.position, finger.Distal.position, tip };
                for (int s = 0; s < 3; s++)
                {
                    result[f * 3 + s] = new RingBone { Valid = (ends[s] - starts[s]).sqrMagnitude > 1e-10f, Bone = bones[s], Start = starts[s], End = ends[s] };
                }
            }

            return result;
        }

        static bool TryGetHand(GripAnalysisHost host, out UxrAvatarHand hand, out UxrUniversalLocalAxes fingerAxes)
        {
            hand = host.Avatar != null ? host.Avatar.GetHand(host.Side) : null;
            UxrAvatarArmInfo arm = host.Avatar != null && host.Avatar.AvatarRigInfo != null ? host.Avatar.AvatarRigInfo.GetArmInfo(host.Side) : null;
            fingerAxes = arm != null ? arm.FingerUniversalLocalAxes : null;
            return hand != null && hand.Wrist != null && fingerAxes != null;
        }

        /// <summary>Наклон кольца на начало перетаскивания диска (Disc отдаёт поворот от начала перетаскивания).</summary>
        static Vector2 DiscStart(int controlId, Vector2 current)
        {
            if (_discDragId != controlId)
            {
                _discDragId = controlId;
                _discStartTilt = current;
            }

            return _discStartTilt;
        }

        /// <summary>Знаковый угол поворота <paramref name="rotation"/> вокруг <paramref name="axis"/>, °.</summary>
        static float AngleAround(Quaternion rotation, Vector3 axis)
        {
            rotation.ToAngleAxis(out float angle, out Vector3 rotationAxis);
            if (float.IsNaN(rotationAxis.x) || float.IsInfinity(rotationAxis.x))
            {
                return 0f;
            }

            angle = Mathf.DeltaAngle(0f, angle);
            return Vector3.Dot(rotationAxis, axis) >= 0f ? angle : -angle;
        }

        /// <summary>Тип ручек колец в сцене — совпадает с вкладкой таблицы блока «Кольца фаланг».</summary>
        internal enum HandleMode { T = 0, Radius = 1, Orientation = 2, Offset = 3 }

        public static string HandleModeName(HandleMode mode)
        {
            switch (mode)
            {
                case HandleMode.Radius: return "радиус";
                case HandleMode.Orientation: return "наклон/азимут";
                case HandleMode.Offset: return "смещение центра";
                default: return "t (сдвиг вдоль кости)";
            }
        }

        /// <summary>
        /// Ручки колец только выбранного типа и только для выбранного пальца (<paramref name="fingerFilter"/>: −1 — все, иначе 0..4):
        /// t — шар на оси (слайдер вдоль кости); радиус — 4 крупные точки на окружности, любая тянет радиус радиально;
        /// наклон/азимут — куб в точке касания (азимут по окружности) и у выбранного кольца диски pitch/yaw.
        /// Окружности всех колец и стрелка точки касания рисуются всегда, только как отображение.
        /// Координаты — в пространстве превью у предмета, как пробы анализа.
        /// </summary>
        public static void OnSceneGUI(GripAnalysisHost host, GripPreviewMapping map, GripHandProbeProfile profile, HandleMode mode, int fingerFilter)
        {
            if (!TryGetHand(host, out UxrAvatarHand hand, out UxrUniversalLocalAxes fingerAxes))
            {
                return;
            }

            Event e = Event.current;
            float[] rings = GetEffective(host, profile);
            float[] rawRadii = GetRawRadiiMm(host, profile);
            float[] radii = GripHandProbeSide.EffectiveRadiiMm(rawRadii);
            Vector2[] tilts = GetTiltsDeg(host, profile);
            float[] azimuths = GetAzimuthsDeg(host, profile);
            Vector2[] offsets = GetOffsetsMm(host, profile);
            RingBone[] ringBones = RingBones(hand, fingerAxes);
            Color oldColor = Handles.color;
            try
            {
                for (int index = 0; index < ringBones.Length; index++)
                {
                    RingBone rb = ringBones[index];
                    if (!rb.Valid ||
                        !GripHandProbeProfiles.ComputeRingFrame(rb.Bone, fingerAxes, rb.Start, rb.End, rings[index], tilts[index], azimuths[index], offsets[index],
                            out Vector3 centerW, out Vector3 normalW, out Vector3 contactW))
                    {
                        continue;
                    }

                    Vector3 a = map.ToPreview(rb.Start);
                    Vector3 b = map.ToPreview(rb.End);
                    Vector3 axis = b - a;
                    float lengthSq = axis.sqrMagnitude;
                    Vector3 axisDir = axis.normalized;
                    Vector3 center = map.ToPreview(centerW);
                    Vector3 normal = map.DirToPreview(normalW).normalized;
                    Vector3 contact = map.DirToPreview(contactW).normalized;
                    float radius = radii[index] * 0.001f;
                    bool manual = GripHandProbeSide.IsManualRingT(rings[index]) || GripHandProbeSide.IsManualRingRadius(rawRadii[index], index) ||
                                  GripHandProbeSide.IsManualRingOrientation(tilts[index], azimuths[index]) || GripHandProbeSide.IsManualRingOffset(offsets[index]);
                    Color color = manual ? ManualRingColor : DefaultRingColor;
                    float handleSize = HandleUtility.GetHandleSize(center);

                    // Отображение: окружность кольца и стрелка нормали точки касания (contactDir) — без ввода
                    if (e.type == EventType.Repaint)
                    {
                        Handles.color = color;
                        Handles.DrawWireDisc(center, normal, radius, manual ? 2f : 1f);
                        Vector3 contactPoint = center + contact * radius;
                        Handles.color = ContactColor;
                        Handles.DrawAAPolyLine(2.5f, contactPoint, contactPoint + contact * HandleUtility.GetHandleSize(contactPoint) * 0.25f);
                    }

                    // Ручки — только у выбранного пальца
                    if (fingerFilter >= 0 && index / 3 != fingerFilter)
                    {
                        continue;
                    }

                    bool hot = false;
                    bool near = false;
                    switch (mode)
                    {
                        case HandleMode.T:
                        {
                            int sliderId = GUIUtility.GetControlID(SliderHint + index, FocusType.Passive);
                            Handles.color = color;
                            EditorGUI.BeginChangeCheck();
                            Vector3 moved = Handles.Slider(sliderId, center, axisDir, handleSize * 0.08f, Handles.SphereHandleCap, 0f);
                            if (EditorGUI.EndChangeCheck())
                            {
                                BeginDraft(host);
                                _draftT = _draftT ?? (float[])rings.Clone();
                                _draftT[index] = GripHandProbeSide.ClampRingT(Vector3.Dot(moved - a, axis) / lengthSq);
                                _activeIndex = index;
                                Version++;
                            }

                            hot = GUIUtility.hotControl == sliderId;
                            near = HandleUtility.nearestControl == sliderId;
                            break;
                        }
                        case HandleMode.Radius:
                        {
                            // 4 крупные точки на окружности (касание, напротив, по бокам); любая тянет радиус радиально
                            Vector3 side = Vector3.Cross(normal, contact).normalized;
                            Vector3[] dirs = { contact, -contact, side, -side };
                            for (int k = 0; k < dirs.Length; k++)
                            {
                                int radiusId = GUIUtility.GetControlID(RadiusHint + index * 4 + k, FocusType.Passive);
                                Vector3 rim = center + dirs[k] * radius;
                                Handles.color = k == 0 ? ContactColor : color;
                                EditorGUI.BeginChangeCheck();
                                Vector3 rimMoved = Handles.Slider(radiusId, rim, dirs[k], HandleUtility.GetHandleSize(rim) * 0.11f, Handles.SphereHandleCap, 0f);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    BeginDraft(host);
                                    _draftRadiusMm = _draftRadiusMm ?? (float[])rawRadii.Clone();
                                    _draftRadiusMm[index] = GripHandProbeSide.ClampRingRadiusMm(Vector3.Dot(rimMoved - center, dirs[k]) * 1000f);
                                    _activeIndex = index;
                                    Version++;
                                }

                                hot |= GUIUtility.hotControl == radiusId;
                                near |= HandleUtility.nearestControl == radiusId;
                            }

                            break;
                        }
                        case HandleMode.Orientation:
                        {
                            // Азимут: куб в точке касания, перетаскивание по окружности (проекция на плоскость кольца)
                            int azimuthId = GUIUtility.GetControlID(AzimuthHint + index, FocusType.Passive);
                            Vector3 contactPos = center + contact * radius;
                            Handles.color = ContactColor;
                            EditorGUI.BeginChangeCheck();
                            Vector3 contactMoved = Handles.FreeMoveHandle(azimuthId, contactPos, HandleUtility.GetHandleSize(contactPos) * 0.06f, Vector3.zero, Handles.CubeHandleCap);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Vector3 inPlane = Vector3.ProjectOnPlane(contactMoved - center, normal);
                                if (inPlane.sqrMagnitude > 1e-12f)
                                {
                                    BeginDraft(host);
                                    _draftAzimuth = _draftAzimuth ?? (float[])azimuths.Clone();
                                    _draftAzimuth[index] = GripHandProbeSide.ClampRingAzimuth(azimuths[index] + Vector3.SignedAngle(contact, inPlane, normal));
                                    _activeIndex = index;
                                    Version++;
                                }
                            }

                            hot = GUIUtility.hotControl == azimuthId;
                            near = HandleUtility.nearestControl == azimuthId;

                            // Диски pitch (LocalRight) и yaw (LocalUp) — только у выбранного кольца (последнего тронутого кубом)
                            if (_activeIndex == index && rb.Bone != null)
                            {
                                Vector3 rightW = map.DirToPreview(rb.Bone.TransformDirection(fingerAxes.LocalRight)).normalized;
                                Vector3 upW = map.DirToPreview(rb.Bone.TransformDirection(fingerAxes.LocalUp)).normalized;
                                float discSize = Mathf.Max(radius * 1.8f, handleSize * 0.3f);

                                Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
                                int pitchId = GUIUtility.GetControlID(PitchHint + index, FocusType.Passive);
                                EditorGUI.BeginChangeCheck();
                                Quaternion pitchRot = Handles.Disc(pitchId, Quaternion.identity, center, rightW, discSize, false, 0f);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    // Disc возвращает поворот от начала перетаскивания — прибавляем к наклону на старте
                                    Vector2 start = DiscStart(pitchId, tilts[index]);
                                    BeginDraft(host);
                                    _draftTilt = _draftTilt ?? (Vector2[])tilts.Clone();
                                    _draftTilt[index] = GripHandProbeSide.ClampRingTilt(start + new Vector2(AngleAround(pitchRot, rightW), 0f));
                                    Version++;
                                }

                                Handles.color = new Color(0.3f, 1f, 0.3f, 0.9f);
                                int yawId = GUIUtility.GetControlID(YawHint + index, FocusType.Passive);
                                EditorGUI.BeginChangeCheck();
                                Quaternion yawRot = Handles.Disc(yawId, Quaternion.identity, center, upW, discSize * 0.85f, false, 0f);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    Vector2 start = DiscStart(yawId, tilts[index]);
                                    BeginDraft(host);
                                    _draftTilt = _draftTilt ?? (Vector2[])tilts.Clone();
                                    _draftTilt[index] = GripHandProbeSide.ClampRingTilt(start + new Vector2(0f, AngleAround(yawRot, upW)));
                                    Version++;
                                }

                                hot |= GUIUtility.hotControl == pitchId || GUIUtility.hotControl == yawId;
                            }

                            break;
                        }
                    }

                    if (mode == HandleMode.Offset &&
                        GripHandProbeProfiles.GetRingOffsetAxes(rb.Bone, fingerAxes, rb.Start, rb.End, out Vector3 offsetXW, out Vector3 offsetYW))
                    {
                        // Центр кольца тянется в плоскости поперёк кости; положение вдоль кости (t) заблокировано проекцией
                        Vector3 offsetX = map.DirToPreview(offsetXW).normalized;
                        Vector3 offsetY = map.DirToPreview(offsetYW).normalized;
                        Vector3 pointA = a + axis * rings[index];
                        int offsetId = GUIUtility.GetControlID(OffsetHint + index, FocusType.Passive);
                        Handles.color = color;
                        EditorGUI.BeginChangeCheck();
                        Vector3 movedCenter = Handles.FreeMoveHandle(offsetId, center, handleSize * 0.09f, Vector3.zero, Handles.SphereHandleCap);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Vector3 delta = Vector3.ProjectOnPlane(movedCenter - pointA, axisDir);
                            BeginDraft(host);
                            _draftOffset = _draftOffset ?? (Vector2[])offsets.Clone();
                            _draftOffset[index] = GripHandProbeSide.ClampRingOffset(new Vector2(Vector3.Dot(delta, offsetX), Vector3.Dot(delta, offsetY)) * 1000f);
                            _activeIndex = index;
                            Version++;
                        }

                        if (e.type == EventType.Repaint)
                        {
                            // Линия от точки A на кости к смещённому центру
                            Handles.DrawDottedLine(pointA, center, 2f);
                        }

                        hot |= GUIUtility.hotControl == offsetId;
                        near |= HandleUtility.nearestControl == offsetId;
                    }

                    if (hot && _activeIndex != index)
                    {
                        _activeIndex = index;
                    }

                    if ((hot || near || _activeIndex == index) && e.type == EventType.Repaint)
                    {
                        float t = _draftT != null && DraftFor(host) ? _draftT[index] : rings[index];
                        float r = _draftRadiusMm != null && DraftFor(host) ? GripHandProbeSide.EffectiveRadiiMm(_draftRadiusMm)[index] : radii[index];
                        Vector2 tilt = _draftTilt != null && DraftFor(host) ? _draftTilt[index] : tilts[index];
                        float azimuth = _draftAzimuth != null && DraftFor(host) ? _draftAzimuth[index] : azimuths[index];
                        Vector2 offset = _draftOffset != null && DraftFor(host) ? _draftOffset[index] : offsets[index];
                        string text = mode == HandleMode.Offset
                            ? string.Format(Ru, "off ({0:0.0}, {1:0.0}) мм", offset.x, offset.y)
                            : mode == HandleMode.Radius
                            ? string.Format(Ru, "r = {0:0.0} мм", r)
                            : mode == HandleMode.Orientation
                                ? string.Format(Ru, "наклон {0:0}° / {1:0}° · азимут {2:0}°", tilt.x, tilt.y, azimuth)
                                : string.Format(Ru, "t = {0:0.00}", t);
                        Handles.Label(center + handleSize * 0.15f * Vector3.up, text, EditorStyles.whiteMiniLabel);
                    }
                }
            }
            finally
            {
                Handles.color = oldColor;
            }

            // Отпустили ручку — записываем черновик в профиль (по одной Undo-операции на группу полей)
            if (GUIUtility.hotControl == 0)
            {
                _discDragId = 0;
            }

            if (DraftFor(host) && GUIUtility.hotControl == 0 && e.type != EventType.Layout)
            {
                if (_draftT != null)
                {
                    Save(host, _draftT, "Сдвиг кольца фаланги");
                }

                if (_draftRadiusMm != null)
                {
                    SaveRadii(host, _draftRadiusMm, "Радиус кольца фаланги");
                }

                if (_draftTilt != null || _draftAzimuth != null)
                {
                    SaveOrientation(host, _draftTilt ?? tilts, _draftAzimuth ?? azimuths, "Наклон/азимут кольца фаланги");
                }

                if (_draftOffset != null)
                {
                    SaveOffsets(host, _draftOffset, "Смещение центра кольца фаланги");
                }
            }
        }

        static HashSet<Transform> FingerBones(UxrAvatarHand hand, int f)
        {
            UxrAvatarFinger finger = hand.GetFinger(GripAnalysisEngine.FingerTypes[f]);
            var bones = new HashSet<Transform>();
            if (finger != null)
            {
                foreach (Transform bone in new[] { finger.Metacarpal, finger.Proximal, finger.Intermediate, finger.Distal })
                {
                    if (bone != null) bones.Add(bone);
                }
            }

            return bones;
        }

        /// <summary>
        /// Окружность наименьших квадратов (алгебраическая, Kåsa) по точкам в плоскости: x² + y² = 2a·x + 2b·y + c.
        /// false — вырожденная система (точки на прямой).
        /// </summary>
        static bool FitCircle(List<Vector2> points, out Vector2 center, out float radius, out float rms)
        {
            center = Vector2.zero;
            radius = rms = 0f;
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, n = points.Count, sxz = 0, syz = 0, sz = 0;
            foreach (Vector2 p in points)
            {
                double x = p.x, y = p.y, z = x * x + y * y;
                sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y;
                sxz += x * z; syz += y * z; sz += z;
            }

            // Нормальные уравнения для [2a, 2b, c]
            double[,] m = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
            double[] r = { sxz, syz, sz };
            double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            if (System.Math.Abs(det) < 1e-24)
            {
                return false;
            }

            double Det(int column)
            {
                double[,] c = (double[,])m.Clone();
                for (int i = 0; i < 3; i++) c[i, column] = r[i];
                return c[0, 0] * (c[1, 1] * c[2, 2] - c[1, 2] * c[2, 1]) - c[0, 1] * (c[1, 0] * c[2, 2] - c[1, 2] * c[2, 0]) + c[0, 2] * (c[1, 0] * c[2, 1] - c[1, 1] * c[2, 0]);
            }

            double twoA = Det(0) / det, twoB = Det(1) / det, cc = Det(2) / det;
            double a = twoA / 2, b = twoB / 2;
            double r2 = cc + a * a + b * b;
            if (r2 <= 0)
            {
                return false;
            }

            center = new Vector2((float)a, (float)b);
            radius = (float)System.Math.Sqrt(r2);
            double sum = 0;
            foreach (Vector2 p in points)
            {
                double d = (p - center).magnitude - radius;
                sum += d * d;
            }

            rms = (float)System.Math.Sqrt(sum / points.Count);
            return true;
        }

        /// <summary>
        /// Подгонка колец по коже — центр (смещение от линии фаланги) и радиус вместе: из текущего центра 12 лучей в плоскости
        /// кольца против запечённых SMR руки (кожа и перчатка, внешнее попадание на коже своего пальца) → точки на коже →
        /// окружность наименьших квадратов → новый центр и радиус; 3 итерации из нового центра (если кость лежит на коже,
        /// первые лучи наполовину промахиваются). Меньше 6 попаданий — кольцо не меняется.
        /// <paramref name="fingerFilter"/>: −1 — все пальцы, иначе 0..4 (большой…мизинец).
        /// </summary>
        public static void FitRingsToSkin(GripAnalysisHost host, GripHandProbeProfile profile, int fingerFilter)
        {
            const int Iterations = 3;
            if (!TryGetHand(host, out UxrAvatarHand hand, out UxrUniversalLocalAxes fingerAxes))
            {
                Status = "Подгонка невозможна: нет костей кисти или FingerUniversalLocalAxes.";
                return;
            }

            List<GripPalmSkinPoint.BakedSkin> skins = GripPalmSkinPoint.BakeHandSkins(host.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true), hand);
            if (skins.Count == 0)
            {
                Status = "Подгонка невозможна: нет видимых SMR этой руки.";
                return;
            }

            float[] rings = GetEffective(host, profile);
            float[] raw = GetRawRadiiMm(host, profile);
            Vector2[] tilts = GetTiltsDeg(host, profile);
            float[] azimuths = GetAzimuthsDeg(host, profile);
            Vector2[] offsets = GetOffsetsMm(host, profile);
            RingBone[] ringBones = RingBones(hand, fingerAxes);
            var report = new StringBuilder();
            int fitted = 0, skipped = 0;
            for (int f = 0; f < GripAnalysisEngine.FingerTypes.Length; f++)
            {
                if (fingerFilter >= 0 && f != fingerFilter)
                {
                    continue;
                }

                // Кожа своего пальца: все его кости (включая пястную); соседние пальцы отбрасываются
                HashSet<Transform> bones = FingerBones(hand, f);
                for (int s = 0; s < 3; s++)
                {
                    int index = f * 3 + s;
                    RingBone rb = ringBones[index];
                    string name = $"{GripAnalysisEngine.FingerNamesRu[f]} {GripAnalysisEngine.SegmentNamesRu[s]}";
                    if (!rb.Valid || bones.Count == 0 ||
                        !GripHandProbeProfiles.GetRingOffsetAxes(rb.Bone, fingerAxes, rb.Start, rb.End, out Vector3 offsetX, out Vector3 offsetY))
                    {
                        skipped++;
                        report.AppendLine($"{name}: нет кости/оси");
                        continue;
                    }

                    Vector3 boneAxis = (rb.End - rb.Start).normalized;
                    Vector3 pointA = Vector3.Lerp(rb.Start, rb.End, rings[index]);
                    Vector2 offset = offsets[index];
                    float radiusMm = 0f, rmsMm = 0f;
                    int hitsUsed = 0;
                    bool any = false;
                    string stop = null;
                    for (int iteration = 0; iteration < Iterations; iteration++)
                    {
                        if (!GripHandProbeProfiles.ComputeRingFrame(rb.Bone, fingerAxes, rb.Start, rb.End, rings[index], tilts[index], azimuths[index], offset,
                                out Vector3 center, out Vector3 normal, out Vector3 contact))
                        {
                            stop = "вырожденная рамка";
                            break;
                        }

                        Vector3 binormal = Vector3.Cross(normal, contact);
                        var points = new List<Vector2>(FitRays);
                        for (int k = 0; k < FitRays; k++)
                        {
                            float angle = 2f * Mathf.PI * k / FitRays;
                            Vector3 dir = contact * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                            if (GripPalmSkinPoint.RaycastFarthest(skins, bones, center, dir, FitMaxDistance, out float distance))
                            {
                                // Точка на коже в координатах плоскости кольца (мм)
                                points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance * 1000f);
                            }
                        }

                        if (points.Count < FitMinHits || !FitCircle(points, out Vector2 circleCenter, out float circleRadius, out float circleRms))
                        {
                            stop = $"попаданий {points.Count}/{FitRays}";
                            break;
                        }

                        // Новый центр в мире → смещение от точки A поперёк кости (t не меняется)
                        Vector3 newCenter = center + (contact * circleCenter.x + binormal * circleCenter.y) * 0.001f;
                        Vector3 delta = Vector3.ProjectOnPlane(newCenter - pointA, boneAxis);
                        offset = GripHandProbeSide.ClampRingOffset(new Vector2(Vector3.Dot(delta, offsetX), Vector3.Dot(delta, offsetY)) * 1000f);
                        radiusMm = GripHandProbeSide.ClampRingRadiusMm(circleRadius);
                        rmsMm = circleRms;
                        hitsUsed = points.Count;
                        any = true;
                    }

                    if (!any)
                    {
                        skipped++;
                        report.AppendLine($"{name}: {stop ?? "нет попаданий"} — не изменено");
                        continue;
                    }

                    offsets[index] = offset;
                    raw[index] = radiusMm;
                    fitted++;
                    report.AppendLine(string.Format(Ru, "{0}: смещение ({1:0.0}, {2:0.0}) мм · радиус {3:0.0} мм · RMS {4:0.00} мм · лучей {5}/{6}{7}",
                        name, offset.x, offset.y, radiusMm, rmsMm, hitsUsed, FitRays, stop != null ? $" (последняя итерация: {stop})" : ""));
                }
            }

            if (fitted == 0)
            {
                Status = "Кольца не подогнаны.\n" + report.ToString().TrimEnd();
                return;
            }

            // Центр и радиус — одна Undo-операция
            int group = Undo.GetCurrentGroup();
            SaveOffsets(host, offsets, "Подгонка колец по коже");
            SaveRadii(host, raw, "Подгонка колец по коже",
                $"Подогнано колец: {fitted}, пропущено: {skipped}.\n" + report.ToString().TrimEnd());
            Undo.CollapseUndoOperations(group);
        }

        /// <summary>
        /// Подгонка наклона по коже: из центра кольца по ладонному направлению (без наклона) ищется внешнее попадание на коже
        /// своего пальца; наклон и азимут ставятся так, чтобы направление на точку касания совпало с нормалью кожи в этой точке.
        /// <paramref name="fingerFilter"/>: −1 — все пальцы, иначе 0..4.
        /// </summary>
        public static void FitTiltToSkin(GripAnalysisHost host, GripHandProbeProfile profile, int fingerFilter)
        {
            if (!TryGetHand(host, out UxrAvatarHand hand, out UxrUniversalLocalAxes fingerAxes))
            {
                Status = "Подгонка невозможна: нет костей кисти или FingerUniversalLocalAxes.";
                return;
            }

            List<GripPalmSkinPoint.BakedSkin> skins = GripPalmSkinPoint.BakeHandSkins(host.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true), hand);
            if (skins.Count == 0)
            {
                Status = "Подгонка невозможна: нет видимых SMR этой руки.";
                return;
            }

            float[] rings = GetEffective(host, profile);
            Vector2[] tilts = GetTiltsDeg(host, profile);
            float[] azimuths = GetAzimuthsDeg(host, profile);
            Vector2[] offsets = GetOffsetsMm(host, profile);
            RingBone[] ringBones = RingBones(hand, fingerAxes);
            var report = new StringBuilder();
            int fitted = 0, skipped = 0;
            for (int f = 0; f < GripAnalysisEngine.FingerTypes.Length; f++)
            {
                if (fingerFilter >= 0 && f != fingerFilter)
                {
                    continue;
                }

                HashSet<Transform> bones = FingerBones(hand, f);
                for (int s = 0; s < 3; s++)
                {
                    int index = f * 3 + s;
                    RingBone rb = ringBones[index];
                    string name = $"{GripAnalysisEngine.FingerNamesRu[f]} {GripAnalysisEngine.SegmentNamesRu[s]}";
                    if (!rb.Valid || bones.Count == 0 ||
                        !GripHandProbeProfiles.ComputeRingFrame(rb.Bone, fingerAxes, rb.Start, rb.End, rings[index], Vector2.zero, 0f, offsets[index],
                            out Vector3 center, out _, out Vector3 palmar))
                    {
                        skipped++;
                        report.AppendLine($"{name}: нет кости/оси");
                        continue;
                    }

                    if (!GripPalmSkinPoint.RaycastFarthest(skins, bones, center, palmar, FitMaxDistance, out float distance, out Vector3 skinNormal) ||
                        !GripHandProbeProfiles.SolveRingOrientation(rb.Bone, fingerAxes, rb.Start, rb.End, skinNormal, out Vector2 tilt, out float azimuth))
                    {
                        skipped++;
                        report.AppendLine($"{name}: ладонный луч не попал в кожу пальца — не изменён");
                        continue;
                    }

                    float before = Vector3.Angle(palmar, skinNormal);
                    tilts[index] = tilt;
                    azimuths[index] = azimuth;
                    fitted++;
                    report.AppendLine(string.Format(Ru, "{0}: наклон {1:0}°, азимут {2:0}° (расхождение было {3:0}°, кожа на {4:0.0} мм)",
                        name, tilt.x, azimuth, before, distance * 1000f));
                }
            }

            if (fitted == 0)
            {
                Status = "Наклон не подогнан.\n" + report.ToString().TrimEnd();
                return;
            }

            SaveOrientation(host, tilts, azimuths, "Подгонка наклона колец по коже",
                $"Подогнано колец: {fitted}, пропущено: {skipped}.\n" + report.ToString().TrimEnd());
        }
    }
}
