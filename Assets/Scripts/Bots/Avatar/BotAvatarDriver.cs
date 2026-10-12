using Mirror;
using Unity.Profiling;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Режим <see cref="BotBodyMode.PlayerFlow"/>: <b>единственный писатель</b> корня, камеры и костей кистей бота
    /// (<c>UxrAvatar.Left/RightHandBone</c>) — тех же трансформов, что у удалённого игрока пишет <c>NetworkTransform</c>.
    /// Только у аватара без владельца и только на сервере/хосте; клиенты получают позу штатным <c>NetworkTransform</c>
    /// (<c>ServerAuthoredAvatar</c>), бот-компонентов у них нет.
    ///
    /// <para>
    /// <b>Точка записи — <c>UxrManager.StageUpdated(AvatarUsingTracking)</c></b>, то есть в <c>UxrManager.PostUpdate</c>
    /// (LateUpdate, порядок 0) непосредственно перед Manipulation. Почему не MonoBehaviour.Update: (1) порядок
    /// относительно Manipulation не зависит от DefaultExecutionOrder Blaze/UxrManager и режима PostUpdate;
    /// (2) запись идёт после проходов Animator/NavMesh этого кадра — между ней и Manipulation никто эти трансформы
    /// не трогает. Это запись трансформов, а не действие SDK: хват/выстрел/automation отсюда не вызываются.
    /// После Manipulation драйвер ничего не пишет и кости кистей обратно не читает.
    /// </para>
    ///
    /// <para>
    /// Не пишет кости скелета, пивот камеры, Dummy Forward, масштаб, поля BodyIK; не использует UxrLocomotion и
    /// UxrControllerInput. Камера пишется локально к пивоту (как HMD в пространстве трекинга) — движется вместе с
    /// корнем, без сглаживания. Корень ставится одним кадром: скачок больше teleportDistance UXR сам переносит ноги.
    /// <see cref="BotBody"/> в этом режиме — только источник ног/курса/взгляда (<see cref="BotBody.PoseDrivenExternally"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BotBody))]
    public sealed class BotAvatarDriver : MonoBehaviour
    {
        private static readonly ProfilerMarker ApplyMarker = new ProfilerMarker("BotAvatarDriver.Apply");

        private UxrAvatar _avatar;
        private BotBody _body;
        private NetworkIdentity _identity;
        private Transform _camera;
        private BotInputFrame _frame;
        private bool _hasFrame;

        /// <summary>Последние записанные мировые позы кистей (для замера стенда; управление их не читает).</summary>
        public Pose AppliedLeftHand { get; private set; }
        public Pose AppliedRightHand { get; private set; }
        public bool AppliedLeft { get; private set; }
        public bool AppliedRight { get; private set; }

        /// <summary>Кадр Unity последней записи.</summary>
        public int AppliedFrame { get; private set; } = -1;

        /// <summary>Пишет ли драйвер на этой машине: сервер/хост, автор мира, аватар без владельца.</summary>
        public bool CanDrive => NetworkServer.active && StateEventAuthority.IsWorldAuthority && _avatar != null &&
                                _identity != null && _identity.connectionToClient == null;

        /// <summary>Намерения этого кадра. Применяются до Manipulation; без нового кадра держится прежний.</summary>
        public void Submit(in BotInputFrame frame)
        {
            _frame = frame;
            _hasFrame = true;
        }

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
            _body = GetComponent<BotBody>();
            _identity = GetComponent<NetworkIdentity>();
        }

        private void OnEnable()
        {
            if (!CanDrive)
            {
                // Бот-компоненты живут только на сервере/хосте у аватара без владельца.
                GameLog.Player.Warning($"[BotAvatarDriver] {name}: не сервер или у аватара есть владелец — драйвер выключен.", this);
                enabled = false;
                return;
            }

            // Один писатель: BotBody с этого момента только источник ног/курса/взгляда.
            _body.SetPoseDrivenExternally(true);
            UxrManager.StageUpdated += OnStageUpdated;
        }

        private void OnDisable()
        {
            UxrManager.StageUpdated -= OnStageUpdated;
            if (_body != null) _body.SetPoseDrivenExternally(false);
            _hasFrame = false;
        }

        private void OnStageUpdated(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.AvatarUsingTracking || !_hasFrame || !CanDrive) return;

            using (ApplyMarker.Auto())
            {
                Apply();
            }
        }

        private void Apply()
        {
            Transform root = _avatar.transform;
            root.SetPositionAndRotation(_frame.RootPosition, _frame.RootRotation);

            // Камера аватара без владельца выключена — UxrAvatar.CameraTransform искал бы её каждый вызов; снимаем один раз.
            if (_camera == null) _camera = _avatar.CameraTransform;
            Transform camera = _camera;
            Transform pivot = _avatar.CameraController;
            if (camera != null)
            {
                if (pivot != null && camera.parent == pivot)
                    camera.SetLocalPositionAndRotation(_frame.HeadPosition, _frame.HeadRotation);
                else if (pivot != null)
                    camera.SetPositionAndRotation(pivot.TransformPoint(_frame.HeadPosition), pivot.rotation * _frame.HeadRotation);
                else
                    camera.SetPositionAndRotation(root.TransformPoint(_frame.HeadPosition), root.rotation * _frame.HeadRotation);
            }

            AppliedLeft = _frame.HasLeftHand && Write(_avatar.LeftHandBone, _frame.LeftHand);
            AppliedRight = _frame.HasRightHand && Write(_avatar.RightHandBone, _frame.RightHand);
            if (AppliedLeft) AppliedLeftHand = _frame.LeftHand;
            if (AppliedRight) AppliedRightHand = _frame.RightHand;
            AppliedFrame = Time.frameCount;
        }

        private static bool Write(Transform bone, in Pose pose)
        {
            if (bone == null) return false;
            bone.SetPositionAndRotation(pose.position, pose.rotation);
            return true;
        }
    }
}
