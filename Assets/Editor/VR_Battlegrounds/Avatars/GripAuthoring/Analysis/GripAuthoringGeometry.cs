using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UltimateXR.Editor.Manipulation.HandPoses;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    /// <summary>Треугольник поверхности предмета в выбранной системе координат (кисти аватара или предмета).</summary>
    internal struct GripTriangle
    {
        public Vector3 A;
        public Vector3 B;
        public Vector3 C;
        public Vector3 Normal;
    }

    /// <summary>Нейтральная (bind) ориентация фаланг пальца и её углы по формуле <see cref="UxrFingerSpinner"/>.</summary>
    internal struct BindPoseFingerState
    {
        public Quaternion ProximalLocalRot;
        public Quaternion IntermediateLocalRot;
        public Quaternion DistalLocalRot;
        /// <summary>Есть ли у bind-пальца пястная кость; её локальный поворот — <see cref="MetacarpalLocalRot"/>.</summary>
        public bool HasMetacarpal;
        public Quaternion MetacarpalLocalRot;
        public float BindSpread;
        public float BindCurl1;
        public float BindCurl2;
        public float BindCurl3;
    }

    /// <summary>
    /// Общие геометрические хелперы «Обхватить», IK конечных фаланг и анализа хвата.
    /// Код перенесён из HandPoseSdkContactEditing без изменения чисел и порядка вычислений.
    /// </summary>
    internal static class GripAuthoringGeometry
    {
        // Радиусы подушечек фаланг (м), которыми «Обхватить» проверяет зазор
        public const float PadRadiusProximal = 0.0068f;
        public const float PadRadiusIntermediate = 0.0058f;
        public const float PadRadiusDistal = 0.0050f;

        // Доли длины сегмента, в которых «Обхватить» берёт сечения фаланги
        public const float SegmentMidFraction = 0.55f;
        public const float SegmentEndFraction = 0.90f;

        // Радиус сбора треугольников предмета вокруг точки хвата (м)
        public const float WeaponTriangleRadius = 0.22f;

        public static float GetPadRadius(int segmentIndex)
        {
            return segmentIndex == 0 ? PadRadiusProximal : segmentIndex == 1 ? PadRadiusIntermediate : PadRadiusDistal;
        }

        public static UxrAvatar GetBindPoseAvatar(UxrAvatar sceneAvatar)
        {
            if (sceneAvatar == null)
            {
                return null;
            }

            if (EditorUtility.IsPersistent(sceneAvatar.gameObject))
            {
                return sceneAvatar;
            }

            GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(sceneAvatar.gameObject);
            if (prefabSource != null)
            {
                UxrAvatar sourceAvatar = prefabSource.GetComponent<UxrAvatar>();
                if (sourceAvatar != null)
                {
                    return sourceAvatar;
                }
            }

            if (sceneAvatar.ParentAvatarPrefab != null)
            {
                return sceneAvatar.ParentAvatarPrefab;
            }

            return null;
        }

        public static BindPoseFingerState CaptureBindPoseFingerState(
            UxrAvatar avatar,
            UxrHandSide side,
            UxrFingerType fingerType,
            UxrUniversalLocalAxes wristAxes,
            UxrUniversalLocalAxes fingerAxes)
        {
            UxrAvatar bindAvatar = GetBindPoseAvatar(avatar) ?? avatar;
            UxrAvatarHand bindHand = bindAvatar != null ? bindAvatar.GetHand(side) : null;
            UxrAvatarFinger bindFinger = bindHand != null ? bindHand.GetFinger(fingerType) : null;

            if (bindFinger == null || bindFinger.Proximal == null || bindFinger.Intermediate == null || bindFinger.Distal == null)
            {
                UxrAvatarHand fallbackHand = avatar.GetHand(side);
                bindHand = fallbackHand;
                bindFinger = fallbackHand.GetFinger(fingerType);
            }

            bool isThumb = fingerType == UxrFingerType.Thumb;
            Transform wrist = bindHand.Wrist;

            return new BindPoseFingerState
            {
                ProximalLocalRot = bindFinger.Proximal.localRotation,
                IntermediateLocalRot = bindFinger.Intermediate.localRotation,
                DistalLocalRot = bindFinger.Distal.localRotation,
                HasMetacarpal = bindFinger.Metacarpal != null,
                MetacarpalLocalRot = bindFinger.Metacarpal != null ? bindFinger.Metacarpal.localRotation : Quaternion.identity,
                BindSpread = GetFingerAngle(wrist, bindFinger.Proximal, wristAxes, fingerAxes, side, isThumb, UxrFingerAngleType.Spread),
                BindCurl1 = GetFingerAngle(wrist, bindFinger.Proximal, wristAxes, fingerAxes, side, isThumb, UxrFingerAngleType.Curl),
                BindCurl2 = GetFingerAngle(bindFinger.Proximal, bindFinger.Intermediate, fingerAxes, fingerAxes, side, false, UxrFingerAngleType.Curl),
                BindCurl3 = GetFingerAngle(bindFinger.Intermediate, bindFinger.Distal, fingerAxes, fingerAxes, side, false, UxrFingerAngleType.Curl)
            };
        }

        /// <summary>
        /// То же bind-состояние, что даёт <see cref="CaptureBindPoseFingerState"/> для IK, но без подмены текущей позой:
        /// если bind-аватар или кости его пальца недоступны, возвращает false с причиной.
        /// </summary>
        internal static bool TryCaptureBindPoseFingerState(
            UxrAvatar avatar,
            UxrHandSide side,
            UxrFingerType fingerType,
            UxrUniversalLocalAxes wristAxes,
            UxrUniversalLocalAxes fingerAxes,
            out BindPoseFingerState state,
            out string reason)
        {
            state = default;
            UxrAvatar bindAvatar = GetBindPoseAvatar(avatar);
            if (bindAvatar == null)
            {
                reason = "bind-аватар недоступен (нет префаба-источника и ParentAvatarPrefab)";
                return false;
            }

            UxrAvatarHand bindHand = bindAvatar.GetHand(side);
            UxrAvatarFinger bindFinger = bindHand != null ? bindHand.GetFinger(fingerType) : null;
            if (bindHand == null || bindHand.Wrist == null ||
                bindFinger == null || bindFinger.Proximal == null || bindFinger.Intermediate == null || bindFinger.Distal == null)
            {
                reason = $"у bind-аватара {bindAvatar.name} нет костей пальца";
                return false;
            }

            // При найденных костях CaptureBindPoseFingerState берёт именно их, без подмены текущей позой
            state = CaptureBindPoseFingerState(avatar, side, fingerType, wristAxes, fingerAxes);
            reason = null;
            return true;
        }

        public static Vector3 GetDistalTipWorld(UxrAvatarFinger finger, UxrUniversalLocalAxes fingerAxes)
        {
            float midLen = Vector3.Distance(finger.Intermediate.position, finger.Distal.position);
            float distalLen = Mathf.Max(0.016f, midLen * 0.82f);
            Vector3 worldDir = finger.Distal.TransformDirection(fingerAxes.LocalForward).normalized;
            return finger.Distal.position + worldDir * distalLen;
        }

        public static void GetFingerAngleBounds(
            UxrHandSide side,
            bool isThumb,
            float bindSpread,
            out float minSpread,
            out float maxSpread,
            out float minCurl1,
            out float maxCurl1,
            out float minCurl2,
            out float maxCurl2,
            out float minCurl3,
            out float maxCurl3)
        {
            if (isThumb)
            {
                // Корневой сустав большого пальца: умеренное отведение и сгибание в пределах Hand Pose Editor
                float hardMinSpread = side == UxrHandSide.Left ? -50f : -5f;
                float hardMaxSpread = side == UxrHandSide.Left ? 5f : 50f;
                minSpread = Mathf.Max(hardMinSpread, bindSpread - 18f);
                maxSpread = Mathf.Min(hardMaxSpread, bindSpread + 18f);
                minCurl1 = -10f;
                maxCurl1 = 40f;
                minCurl2 = -15f;
                maxCurl2 = 85f;
                minCurl3 = -10f;
                maxCurl3 = 80f;
            }
            else
            {
                // Боковое отведение (Spread) разрешено ТОЛЬКО корневой фаланге и строго ограничено ±16° от нейтрали
                const float MaxLateralSpreadDeg = 16f;
                minSpread = Mathf.Clamp(bindSpread - MaxLateralSpreadDeg, -20f, 20f);
                maxSpread = Mathf.Clamp(bindSpread + MaxLateralSpreadDeg, -20f, 20f);
                minCurl1 = -10f;
                maxCurl1 = 90f;
                minCurl2 = 0f;
                maxCurl2 = 100f;
                minCurl3 = 0f;
                maxCurl3 = 85f;
            }
        }

        /// <summary>
        /// Точный расчёт угла Spread/Curl по формуле <see cref="UxrFingerSpinner"/>.
        /// Гарантирует 100% совпадение углов при переключении между IK и ручным режимом Hand Pose Editor.
        /// </summary>
        public static float GetFingerAngle(
            Transform parent,
            Transform target,
            UxrUniversalLocalAxes parentLocalAxes,
            UxrUniversalLocalAxes targetLocalAxes,
            UxrHandSide handSide,
            bool isThumbProximal,
            UxrFingerAngleType angleType)
        {
            Vector3 parentRight = isThumbProximal
                ? handSide == UxrHandSide.Left
                    ? parent.TransformDirection(-parentLocalAxes.LocalForward)
                    : parent.TransformDirection(parentLocalAxes.LocalForward)
                : parent.TransformDirection(parentLocalAxes.LocalRight);

            Vector3 parentUp = parent.TransformDirection(parentLocalAxes.LocalUp);

            Vector3 parentForward = isThumbProximal
                ? handSide == UxrHandSide.Left
                    ? parent.TransformDirection(parentLocalAxes.LocalRight)
                    : parent.TransformDirection(-parentLocalAxes.LocalRight)
                : parent.TransformDirection(parentLocalAxes.LocalForward);

            Vector3 targetRight = target.TransformDirection(targetLocalAxes.LocalRight);
            Vector3 targetUp = target.TransformDirection(targetLocalAxes.LocalUp);
            Vector3 targetForward = target.TransformDirection(targetLocalAxes.LocalForward);

            Quaternion toForward = Quaternion.FromToRotation(targetForward, parentForward);
            Vector3 rightOnlyRoll = toForward * targetRight;
            float roll = Vector3.SignedAngle(parentRight, rightOnlyRoll, parentForward);

            Quaternion removeRoll = Quaternion.AngleAxis(-roll, targetForward);

            if (angleType == UxrFingerAngleType.Spread)
            {
                Vector3 spreadAxis = removeRoll * targetUp;
                Vector3 planeRightNormal = parentRight;
                Vector3 spreadVectorOnPlane = Vector3.Cross(planeRightNormal, Vector3.ProjectOnPlane(spreadAxis, planeRightNormal));
                return Vector3.SignedAngle(spreadVectorOnPlane, targetForward, spreadAxis);
            }

            if (angleType == UxrFingerAngleType.Curl)
            {
                Vector3 curlAxis = removeRoll * targetRight;
                Vector3 planeUpNormal = parentUp;
                Vector3 curlVectorOnPlane = Vector3.Cross(Vector3.ProjectOnPlane(curlAxis, planeUpNormal), planeUpNormal);
                return Vector3.SignedAngle(curlVectorOnPlane, targetForward, curlAxis);
            }

            return 0f;
        }

        /// <summary>
        /// Точное обращение ApplyAnatomicalFingerAngles (HandPoseSdkContactEditing): углы текущей позы пальца в той же параметризации,
        /// что применяют IK и «Обхватить» и что задают пределы <see cref="GetFingerAngleBounds"/>:
        /// значение = bind-угол (<see cref="GetFingerAngle"/> в bind) + приращение от bind-ориентации кости
        /// (порядок спиннеров SDK и UxrAvatarRig.CurlFinger: Curl1 вокруг LocalRight, затем Spread вокруг уже согнутой LocalUp;
        /// Curl2/Curl3 — поворот вокруг собственной LocalRight).
        /// GetFingerAngle для текущей позы этого не даёт: у согнутого пальца его Spread расходится с применённым на 10–20°,
        /// и каждое событие ручки IK заново уводило палец вбок (накопление при перетаскивании).
        /// Боковой и скручивающий остатки средней/конечной фаланг (от старых коммитов или ручных правок) отбрасываются —
        /// их измеряет <see cref="GetHingeResidual"/>.
        /// </summary>
        internal static void MeasureAnatomicalFingerAngles(
            UxrAvatarFinger finger,
            BindPoseFingerState bindState,
            UxrUniversalLocalAxes fingerAxes,
            out float spread,
            out float curl1,
            out float curl2,
            out float curl3)
        {
            MeasureProximalSwing(finger, bindState, fingerAxes, out float deltaSpread, out float deltaCurl1, out _);

            spread = bindState.BindSpread + deltaSpread;
            curl1 = bindState.BindCurl1 + deltaCurl1;

            // Средняя и конечная: только составляющая поворота вокруг шарнирной оси LocalRight (swing-twist)
            curl2 = bindState.BindCurl2 + GetHingeAngle(Quaternion.Inverse(bindState.IntermediateLocalRot) * finger.Intermediate.localRotation, fingerAxes.LocalRight);
            curl3 = bindState.BindCurl3 + GetHingeAngle(Quaternion.Inverse(bindState.DistalLocalRot) * finger.Distal.localRotation, fingerAxes.LocalRight);
        }

        /// <summary>
        /// Осевое скручивание проксимальной фаланги относительно bind в модели корневого сустава:
        /// поворот от bind = Curl1(LocalRight) * Spread(LocalUp) * Twist(LocalForward), возвращается Twist в градусах.
        /// У позы, собранной ApplyAnatomicalFingerAngles без twist, и у развода спиннером SDK на согнутом пальце равно 0;
        /// импортированные позы бывают скручены на десятки градусов.
        /// </summary>
        internal static float MeasureProximalTwist(UxrAvatarFinger finger, BindPoseFingerState bindState, UxrUniversalLocalAxes fingerAxes)
        {
            MeasureProximalSwing(finger, bindState, fingerAxes, out _, out _, out float twist);
            return twist;
        }

        /// <summary>
        /// Разложение поворота проксимальной фаланги от bind: Curl1(LocalRight) * Spread(LocalUp) * Twist(LocalForward) —
        /// порядок спиннеров SDK (Rotate(LocalUp, Space.Self) на уже согнутой кости) и UxrAvatarRig.CurlFinger.
        /// Прежний порядок Spread * Curl1 (развод вокруг нормали ладони до сгиба) расходился со спиннером: развод спиннером
        /// на пальце, согнутом на 56°, читался как Twist до 40–45°; к тому же у сильно согнутого пальца такой Spread почти
        /// вращал кость вокруг её оси вместо сдвига вбок. Здесь Spread — боковой отвод кости при любом сгибе (|Spread| ≤ 90°),
        /// Curl1 — полный круг, без особенности при сгибе больше 90°.
        /// </summary>
        static void MeasureProximalSwing(
            UxrAvatarFinger finger,
            BindPoseFingerState bindState,
            UxrUniversalLocalAxes fingerAxes,
            out float deltaSpread,
            out float deltaCurl1,
            out float twist)
        {
            Quaternion proximalDelta = Quaternion.Inverse(bindState.ProximalLocalRot) * finger.Proximal.localRotation;
            Vector3 boneForward = proximalDelta * fingerAxes.LocalForward;

            // Curl1 — по проекции кости на плоскость сгиба (перпендикулярную LocalRight)
            Vector3 forwardOnCurlPlane = Vector3.ProjectOnPlane(boneForward, fingerAxes.LocalRight);
            deltaCurl1 = forwardOnCurlPlane.sqrMagnitude > 1e-8f
                ? Vector3.SignedAngle(fingerAxes.LocalForward, forwardOnCurlPlane, fingerAxes.LocalRight)
                : 0f;
            Quaternion curlRotation = Quaternion.AngleAxis(deltaCurl1, fingerAxes.LocalRight);

            // Spread — отвод кости от согнутого направления вокруг согнутой LocalUp
            deltaSpread = Vector3.SignedAngle(curlRotation * fingerAxes.LocalForward, boneForward, curlRotation * fingerAxes.LocalUp);

            // Остаток после Curl1 * Spread оставляет LocalForward на месте — это чистое скручивание вокруг оси кости
            Quaternion swing = curlRotation * Quaternion.AngleAxis(deltaSpread, fingerAxes.LocalUp);
            twist = GetHingeAngle(Quaternion.Inverse(swing) * proximalDelta, fingerAxes.LocalForward);
        }

        /// <summary>Угол поворота <paramref name="rotation"/> вокруг оси <paramref name="axis"/> (twist из swing-twist разложения), градусы в (-180, 180].</summary>
        internal static float GetHingeAngle(Quaternion rotation, Vector3 axis)
        {
            float alongAxis = Vector3.Dot(new Vector3(rotation.x, rotation.y, rotation.z), axis.normalized);
            return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(alongAxis, rotation.w) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Отклонение фаланги от чистого шарнира bind * AngleAxis(Δ, LocalRight): поворот, оставшийся после снятия сгиба
        /// вокруг LocalRight (swing из swing-twist). <paramref name="lateral"/> — составляющая вокруг LocalUp (вбок),
        /// <paramref name="twist"/> — вокруг LocalForward (скручивание), <paramref name="total"/> — полный угол остатка. Градусы.
        /// </summary>
        internal static void GetHingeResidual(
            Quaternion boneLocalRotation,
            Quaternion bindLocalRotation,
            UxrUniversalLocalAxes fingerAxes,
            out float lateral,
            out float twist,
            out float total)
        {
            Quaternion delta = Quaternion.Inverse(bindLocalRotation) * boneLocalRotation;
            Quaternion hinge = Quaternion.AngleAxis(GetHingeAngle(delta, fingerAxes.LocalRight), fingerAxes.LocalRight);
            Quaternion swing = delta * Quaternion.Inverse(hinge);
            if (swing.w < 0f)
            {
                swing = new Quaternion(-swing.x, -swing.y, -swing.z, -swing.w);
            }

            var swingVector = new Vector3(swing.x, swing.y, swing.z);
            lateral = 2f * Mathf.Atan2(Vector3.Dot(swingVector, fingerAxes.LocalUp.normalized), swing.w) * Mathf.Rad2Deg;
            twist = 2f * Mathf.Atan2(Vector3.Dot(swingVector, fingerAxes.LocalForward.normalized), swing.w) * Mathf.Rad2Deg;
            total = 2f * Mathf.Atan2(swingVector.magnitude, swing.w) * Mathf.Rad2Deg;
        }

        /// <summary>Треугольники предмета вокруг snapTransform, переведённые в мировое пространство кисти аватара (пара align → grabber).</summary>
        public static List<GripTriangle> CollectWeaponTrianglesInAvatarSpace(
            GameObject weaponRoot,
            Transform snapTransform,
            Transform grabberTransform)
        {
            var result = new List<GripTriangle>(1024);
            if (weaponRoot == null || snapTransform == null || grabberTransform == null)
            {
                return result;
            }

            Quaternion rotPreviewToAvatar = grabberTransform.rotation * Quaternion.Inverse(snapTransform.rotation);
            Vector3 snapPos = snapTransform.position;
            Vector3 grabberPos = grabberTransform.position;

            Vector3 ToAvatar(Vector3 weaponWorldPt) => grabberPos + rotPreviewToAvatar * (weaponWorldPt - snapPos);

            AppendWeaponTriangles(weaponRoot, snapPos, ToAvatar, result);
            return result;
        }

        /// <summary>Тот же набор треугольников, что и для «Обхватить», но в системе предмета (worldToObject без масштаба).</summary>
        public static List<GripTriangle> CollectWeaponTrianglesInObjectSpace(
            GameObject weaponRoot,
            Transform snapTransform,
            Matrix4x4 worldToObject)
        {
            var result = new List<GripTriangle>(1024);
            if (weaponRoot == null || snapTransform == null)
            {
                return result;
            }

            Vector3 ToObject(Vector3 weaponWorldPt) => worldToObject.MultiplyPoint3x4(weaponWorldPt);

            AppendWeaponTriangles(weaponRoot, snapTransform.position, ToObject, result);
            return result;
        }

        /// <summary>Отбор мешей предмета для геометрии хвата: активные, видимые, без превью-мешей кисти.</summary>
        public static bool IsWeaponSurfaceMesh(MeshFilter mf)
        {
            if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy) return false;
            if (mf.GetComponent<UxrGrabbableObjectPreviewMesh>() != null ||
                mf.GetComponentInParent<UxrGrabbableObjectPreviewMesh>() != null)
            {
                return false;
            }

            var renderer = mf.GetComponent<MeshRenderer>();
            if (renderer != null && !renderer.enabled) return false;
            return true;
        }

        static void AppendWeaponTriangles(
            GameObject weaponRoot,
            Vector3 snapPos,
            Func<Vector3, Vector3> toSpace,
            List<GripTriangle> result)
        {
            const float MaxRadiusSq = WeaponTriangleRadius * WeaponTriangleRadius;

            foreach (MeshFilter mf in weaponRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!IsWeaponSurfaceMesh(mf)) continue;

                AppendMeshTriangles(mf.sharedMesh, mf.transform.localToWorldMatrix, snapPos, MaxRadiusSq, toSpace, result);
            }
        }

        static void AppendMeshTriangles(
            Mesh mesh,
            Matrix4x4 localToWorld,
            Vector3 snapWorldPos,
            float maxRadiusSq,
            Func<Vector3, Vector3> toAvatar,
            List<GripTriangle> output)
        {
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            if (vertices == null || indices == null || indices.Length < 3)
            {
                return;
            }

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 wA = localToWorld.MultiplyPoint3x4(vertices[indices[i]]);
                Vector3 wB = localToWorld.MultiplyPoint3x4(vertices[indices[i + 1]]);
                Vector3 wC = localToWorld.MultiplyPoint3x4(vertices[indices[i + 2]]);

                Vector3 center = (wA + wB + wC) * (1f / 3f);
                if ((center - snapWorldPos).sqrMagnitude > maxRadiusSq)
                {
                    continue;
                }

                Vector3 a = toAvatar(wA);
                Vector3 b = toAvatar(wB);
                Vector3 c = toAvatar(wC);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float areaSq = cross.sqrMagnitude;
                if (areaSq < 1e-12f)
                {
                    continue;
                }

                output.Add(new GripTriangle
                {
                    A = a,
                    B = b,
                    C = c,
                    Normal = cross / Mathf.Sqrt(areaSq)
                });
            }
        }

        public static float EvaluateSegmentClearance(Vector3 segA, Vector3 segB, float padRadius, List<GripTriangle> weaponTriangles)
        {
            Vector3 pMid = Vector3.Lerp(segA, segB, SegmentMidFraction);
            Vector3 pEnd = Vector3.Lerp(segA, segB, SegmentEndFraction);
            float dMid = SignedDistanceToWeaponSurface(pMid, weaponTriangles);
            float dEnd = SignedDistanceToWeaponSurface(pEnd, weaponTriangles);
            return Mathf.Min(dMid, dEnd) - padRadius;
        }

        public static float DistanceToWeaponSurface(Vector3 point, List<GripTriangle> triangles, out Vector3 closestNormal)
        {
            float minSq = float.PositiveInfinity;
            closestNormal = Vector3.up;

            for (int i = 0; i < triangles.Count; i++)
            {
                GripTriangle tri = triangles[i];
                Vector3 closest = ClosestPointOnTriangle(point, tri.A, tri.B, tri.C);
                Vector3 diff = point - closest;
                float sq = diff.sqrMagnitude;
                if (sq < minSq)
                {
                    minSq = sq;
                    closestNormal = tri.Normal;
                }
            }

            return Mathf.Sqrt(minSq);
        }

        public static float SignedDistanceToWeaponSurface(Vector3 point, List<GripTriangle> triangles)
        {
            float minSq = float.PositiveInfinity;
            Vector3 bestDiff = Vector3.zero;
            Vector3 bestNormal = Vector3.up;

            for (int i = 0; i < triangles.Count; i++)
            {
                GripTriangle tri = triangles[i];
                Vector3 closest = ClosestPointOnTriangle(point, tri.A, tri.B, tri.C);
                Vector3 diff = point - closest;
                float sq = diff.sqrMagnitude;
                if (sq < minSq)
                {
                    minSq = sq;
                    bestDiff = diff;
                    bestNormal = tri.Normal;
                }
            }

            float dist = Mathf.Sqrt(minSq);
            float sign = Vector3.Dot(bestDiff, bestNormal) < -1e-5f ? -1f : 1f;
            return dist * sign;
        }

        public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denom = 1f / (va + vb + vc);
            float v2 = vb * denom;
            float w2 = vc * denom;
            return a + ab * v2 + ac * w2;
        }
    }
}
