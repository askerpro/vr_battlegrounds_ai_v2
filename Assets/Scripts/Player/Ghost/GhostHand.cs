using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UltimateXR.Extensions.Unity.Math;
using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Кисть призрака — кисть SmallHands из UltimateXR (скин-меш на 20 костей). Повторяет позу
    /// кисти любого аватара: кости разных ригов смотрят разными осями, поэтому поворот переносится
    /// через «универсальные» оси UltimateXR (<see cref="UxrUniversalLocalAxes"/>) — у аватара они
    /// посчитаны в <see cref="UxrAvatarArmInfo"/>, у этой кисти считаются тем же способом при старте.
    ///
    /// <para>
    /// Префабы <c>Prefabs/Player/Ghost/GhostHand{Left,Right}</c> собирает
    /// <c>Tools/VR Battlegrounds/Avatars/Build Ghost Parts</c>.
    /// </para>
    /// </summary>
    public sealed class GhostHand : MonoBehaviour
    {
        /// <summary>Кости одного пальца. У большого пальца нет пястной (<see cref="Metacarpal"/> = null).</summary>
        [System.Serializable]
        public sealed class Finger
        {
            public Transform Metacarpal;
            public Transform Proximal;
            public Transform Intermediate;
            public Transform Distal;
        }

        public UxrHandSide Side;
        public Transform Wrist;
        public Finger Thumb = new Finger();
        public Finger Index = new Finger();
        public Finger Middle = new Finger();
        public Finger Ring = new Finger();
        public Finger Little = new Finger();
        public SkinnedMeshRenderer Renderer;

        private UxrUniversalLocalAxes _handAxes;
        private UxrUniversalLocalAxes _fingerAxes;
        private float _palmLength;

        private void Awake() => SolveAxes();

        /// <summary>Универсальные оси в позе привязки — тем же расчётом, что <c>UxrAvatarArmInfo.SolveHandAndFingerAxes</c>.</summary>
        private void SolveAxes()
        {
            if (_handAxes != null || Wrist == null || Index.Proximal == null || Index.Distal == null || Middle.Proximal == null || Ring.Proximal == null) return;

            float sign = Side == UxrHandSide.Left ? 1f : -1f;

            Vector3 handRight = Wrist.InverseTransformDirection(Index.Proximal.position - Middle.Proximal.position).GetClosestAxis() * sign;
            Vector3 handForward = Wrist.InverseTransformDirection((Vector3.Lerp(Ring.Proximal.position, Middle.Proximal.position, 0.5f) - Wrist.position).normalized);
            Vector3 handUp = Vector3.Cross(handForward, handRight).normalized;
            handRight = Vector3.Cross(handUp, handForward).normalized;
            _handAxes = UxrUniversalLocalAxes.FromAxes(Wrist, handRight, handUp, handForward);

            Vector3 fingerRight = Index.Proximal.InverseTransformDirection(Index.Proximal.position - Middle.Proximal.position).GetClosestAxis() * sign;
            Vector3 fingerForward = Index.Proximal.InverseTransformDirection(Index.Distal.position - Index.Proximal.position).GetClosestAxis();
            _fingerAxes = UxrUniversalLocalAxes.FromAxes(Index.Proximal, fingerRight, Vector3.Cross(fingerForward, fingerRight), fingerForward);

            _palmLength = Vector3.Distance(Wrist.position, Middle.Proximal.position) / Mathf.Max(transform.lossyScale.x, 1e-4f);
        }

        /// <summary>
        /// Встать в позу кисти аватара. Размер подгоняется под его ладонь: у SmallHands она меньше,
        /// чем у взрослого аватара.
        /// </summary>
        public void Follow(UxrAvatarHand source, UxrAvatarArmInfo sourceInfo)
        {
            SolveAxes();
            if (_handAxes == null || source?.Wrist == null || sourceInfo?.HandUniversalLocalAxes == null || sourceInfo.FingerUniversalLocalAxes == null) return;

            Transform sourceMiddle = source.Middle?.Proximal;
            if (sourceMiddle != null && _palmLength > 1e-4f)
            {
                float scale = Vector3.Distance(source.Wrist.position, sourceMiddle.position) / _palmLength;
                transform.localScale = Vector3.one * scale / ParentScale();
            }

            Wrist.SetPositionAndRotation(source.Wrist.position, Retarget(source.Wrist.rotation, sourceInfo.HandUniversalLocalAxes, _handAxes));

            FollowFinger(Thumb, source.Thumb, sourceInfo.FingerUniversalLocalAxes);
            FollowFinger(Index, source.Index, sourceInfo.FingerUniversalLocalAxes);
            FollowFinger(Middle, source.Middle, sourceInfo.FingerUniversalLocalAxes);
            FollowFinger(Ring, source.Ring, sourceInfo.FingerUniversalLocalAxes);
            FollowFinger(Little, source.Little, sourceInfo.FingerUniversalLocalAxes);
        }

        private float ParentScale() => transform.parent != null ? Mathf.Max(transform.parent.lossyScale.x, 1e-4f) : 1f;

        private void FollowFinger(Finger target, UxrAvatarFinger source, UxrUniversalLocalAxes sourceAxes)
        {
            if (source == null) return;
            // Порядок — от корня к кончику: поворот родителя двигает детей.
            Bone(target.Metacarpal, source.Metacarpal, sourceAxes);
            Bone(target.Proximal, source.Proximal, sourceAxes);
            Bone(target.Intermediate, source.Intermediate, sourceAxes);
            Bone(target.Distal, source.Distal, sourceAxes);
        }

        private void Bone(Transform target, Transform source, UxrUniversalLocalAxes sourceAxes)
        {
            if (target == null || source == null) return;
            target.rotation = Retarget(source.rotation, sourceAxes, _fingerAxes);
        }

        /// <summary>Поворот кости источника → поворот кости с другими осями, та же поза в мире.</summary>
        private static Quaternion Retarget(Quaternion sourceRotation, UxrUniversalLocalAxes sourceAxes, UxrUniversalLocalAxes targetAxes)
        {
            return sourceRotation * Quaternion.Inverse(sourceAxes.UniversalToActualAxesRotation) * targetAxes.UniversalToActualAxesRotation;
        }
    }
}
