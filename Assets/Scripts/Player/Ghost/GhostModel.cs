using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Призрак выбывшего — префаб <c>Prefabs/Player/Ghost/GhostBody</c>: шлем, корпус и две кисти
    /// (<see cref="GhostHand"/>). Смотреть и править — в самом префабе: масштаб и смещения частей
    /// задаются там, не в коде.
    ///
    /// <para>
    /// <b>Начало координат — глаза.</b> Корень ставится в камеру аватара и поворачивается только по
    /// курсу — корпус висит под глазами и не наклоняется с головой. <see cref="Head"/> берёт полный
    /// поворот камеры. Кисти повторяют позу кистей аватара (<see cref="GhostHand.Follow"/>).
    /// </para>
    ///
    /// Создаёт и ставит в позу <see cref="GhostBody"/>. Префаб собирает
    /// <c>Tools/VR Battlegrounds/Avatars/Build Ghost Parts</c>, если его нет.
    /// </summary>
    public sealed class GhostModel : MonoBehaviour
    {
        [Tooltip("Шлем — поворачивается вместе с камерой. Вращение вокруг глаз (начало префаба).")]
        public Transform Head;

        [Tooltip("Корпус — только по курсу головы.")]
        public Transform Torso;

        public GhostHand LeftHand;
        public GhostHand RightHand;

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private Quaternion _headLocalRotation = Quaternion.identity;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            if (Head != null) _headLocalRotation = Head.localRotation;
        }

        /// <summary>Цвет команды (альфа — из материала призрака).</summary>
        public void SetColor(Color color, float alpha)
        {
            _renderers ??= GetComponentsInChildren<Renderer>(true);
            _block ??= new MaterialPropertyBlock();
            color.a = alpha;
            _block.SetColor("_BaseColor", color);
            foreach (Renderer r in _renderers) r.SetPropertyBlock(_block);
        }

        /// <summary>Своя голова изнутри камеры не рисуется.</summary>
        public void SetHeadVisible(bool visible)
        {
            if (Head != null) Head.gameObject.SetActive(visible);
        }

        /// <summary>Встать в позу аватара: глаза — в камеру, кисти — по кистям.</summary>
        public void Pose(UxrAvatar avatar, Transform fallbackForward)
        {
            Transform camera = avatar != null ? avatar.CameraTransform : null;
            if (camera == null) return;

            Vector3 forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = fallbackForward.forward;

            transform.SetPositionAndRotation(camera.position, Quaternion.LookRotation(forward, Vector3.up));
            if (Head != null) Head.rotation = camera.rotation * _headLocalRotation;

            UxrAvatarRigInfo rigInfo = avatar.AvatarRigInfo;
            if (LeftHand != null) LeftHand.Follow(avatar.GetHand(UxrHandSide.Left), rigInfo?.GetArmInfo(UxrHandSide.Left));
            if (RightHand != null) RightHand.Follow(avatar.GetHand(UxrHandSide.Right), rigInfo?.GetArmInfo(UxrHandSide.Right));
        }
    }
}
