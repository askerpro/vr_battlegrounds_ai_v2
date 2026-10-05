using System;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Проверяемое отображение реального Action в captured body-space rest/rear.</summary>
    [Serializable]
    public sealed class ChamberActionBinding
    {
        public Transform Target;
        public Vector3 RestPosition;
        public Quaternion RestRotation = Quaternion.identity;
        public Vector3 RearPosition;
        public Quaternion RearRotation = Quaternion.identity;
        public bool AnimateRotation;
        [NonSerialized] internal Transform CapturedParent;

        public bool TryGetProgress(Transform body, float epsilon, out float progress)
        {
            progress = 0f;
            if (Target == null || body == null) return false;
            return TryGetBodyPose(Target, body, out Pose pose) && TryGetPoseProgress(pose, epsilon, out progress);
        }

        internal bool TryGetPoseProgress(Pose pose, float epsilon, out float progress)
        {
            progress = 0f;
            Vector3 travel = RearPosition - RestPosition;
            Vector3 current = pose.position - RestPosition;
            float distance = travel.magnitude;
            bool translates = distance > epsilon;
            float angle = AnimateRotation ? RotationDistance(RestRotation, RearRotation) : 0f;
            bool rotates = AnimateRotation && !RotationNear(RestRotation, RearRotation);
            if (!AnimateRotation && !RotationNear(pose.rotation, RestRotation)) return false;
            if (AnimateRotation && !rotates && !RotationNear(pose.rotation, RestRotation)) return false;
            if (!translates && !rotates) return false;
            progress = translates ? Vector3.Dot(current, travel / distance) / distance : 1f;
            if (translates && (current - travel * progress).sqrMagnitude > epsilon * epsilon) return false;
            if (!translates && current.sqrMagnitude > epsilon * epsilon) return false;
            if (rotates)
            {
                Quaternion currentRotation = pose.rotation;
                float rotationProgress = RotationDistance(RestRotation, currentRotation) / angle;
                // Угол сам по себе не доказывает направление. Сравниваем фактический Quaternion
                // с единственным согласованным captured shortest-arc mapping.
                Quaternion expected = Quaternion.SlerpUnclamped(RestRotation, RearRotation, rotationProgress);
                if (!RotationNear(currentRotation, expected)) return false;
                progress = Mathf.Min(progress, rotationProgress);
            }
            if (float.IsNaN(progress) || float.IsInfinity(progress)) return false;
            return true;
        }

        public bool IsAtRest(Transform body, float epsilon)
        {
            if (Target == null || body == null) return false;
            if (!TryGetBodyPose(Target, body, out Pose pose)) return false;
            Vector3 actual = pose.position;
            if (!Finite(actual.x) || !Finite(actual.y) || !Finite(actual.z) ||
                (actual - RestPosition).sqrMagnitude > epsilon * epsilon) return false;
            Quaternion rotation = pose.rotation;
            if (!Finite(rotation.x) || !Finite(rotation.y) || !Finite(rotation.z) || !Finite(rotation.w)) return false;
            return RotationNear(rotation, RestRotation);
        }

        // Dot около 1 теряет чувствительность к малому углу. Сравниваем компоненты
        // unit quaternion с учётом q == -q; это численный, не угловой физический порог.
        internal static bool RotationNear(Quaternion actual, Quaternion expected)
        {
            if (!Finite(actual.x) || !Finite(actual.y) || !Finite(actual.z) || !Finite(actual.w) ||
                !Finite(expected.x) || !Finite(expected.y) || !Finite(expected.z) || !Finite(expected.w)) return false;
            if (Quaternion.Dot(actual, actual) <= 0f || Quaternion.Dot(expected, expected) <= 0f) return false;
            actual = actual.normalized; expected = expected.normalized;
            float sign = Quaternion.Dot(actual, expected) < 0f ? -1f : 1f;
            const float allowance = 8f * 1.192092896e-7f; // Эвристический запас component rounding, не восьмиоперационный bound.
            return Mathf.Max(Mathf.Abs(actual.x - sign * expected.x), Mathf.Abs(actual.y - sign * expected.y),
                Mathf.Abs(actual.z - sign * expected.z), Mathf.Abs(actual.w - sign * expected.w)) <= allowance;
        }

        internal static float RotationDistance(Quaternion from, Quaternion to)
        {
            Quaternion relative = (Quaternion.Inverse(from.normalized) * to.normalized).normalized;
            float sine = Mathf.Sqrt(relative.x * relative.x + relative.y * relative.y + relative.z * relative.z);
            return 2f * Mathf.Atan2(sine, Mathf.Abs(relative.w)) * Mathf.Rad2Deg;
        }

        internal static bool TryGetBodyPose(Transform target, Transform body, out Pose pose)
        {
            pose = Pose.identity;
            if (!TryGetRelativeMatrix(target, body, out Matrix4x4 matrix)) return false;
            pose = new Pose(matrix.MultiplyPoint3x4(Vector3.zero), matrix.rotation);
            return true;
        }

        internal static bool TrySetBodyPose(Transform target, Transform body, Pose pose, bool rotate)
        {
            if (target == null || target.parent == null || !TryGetRelativeMatrix(target.parent, body, out Matrix4x4 parent)) return false;
            target.localPosition = parent.inverse.MultiplyPoint3x4(pose.position);
            if (rotate) target.localRotation = Quaternion.Inverse(parent.rotation) * pose.rotation;
            return true;
        }

        private static bool TryGetRelativeMatrix(Transform target, Transform body, out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            if (target == null || body == null) return false;
            Transform common = body;
            while (common != null && !target.IsChildOf(common)) common = common.parent;
            if (common == null) return false;
            matrix = ToAncestor(body, common).inverse * ToAncestor(target, common);
            for (int index = 0; index < 16; index++) if (!Finite(matrix[index])) return false;
            if (!Finite(matrix.determinant) || matrix.determinant <= 0f) return false;
            return true;
        }

        private static Matrix4x4 ToAncestor(Transform target, Transform ancestor)
        {
            Matrix4x4 result = Matrix4x4.identity;
            for (Transform current = target; current != ancestor; current = current.parent)
                result = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * result;
            return result;
        }

        internal static bool HasSupportedLocalFrames(Transform target, Transform body)
        {
            if (target == null || body == null) return false;
            Transform common = body;
            while (common != null && !target.IsChildOf(common)) common = common.parent;
            return common != null && ValidPath(target, common) && ValidPath(body, common);
        }

        private static bool ValidPath(Transform target, Transform common)
        {
            for (Transform current = target; current != common; current = current.parent)
            {
                Vector3 scale = current.localScale;
                float largest = Mathf.Max(scale.x, scale.y, scale.z);
                float smallest = Mathf.Min(scale.x, scale.y, scale.z);
                float determinant = scale.x * scale.y * scale.z;
                // Mapping Quaternion не представляет shear или отражение. Допускается только
                // численная разница компонентов равномерного положительного масштаба.
                if (!Finite(largest) || !Finite(smallest) || smallest <= 0f || !Finite(determinant) || determinant <= 0f ||
                    largest - smallest > largest * (8f * 1.192092896e-7f)) return false;
            }
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
