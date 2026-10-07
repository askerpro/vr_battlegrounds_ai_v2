using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.PhysicalSpaceUtils
{
    /// <summary>Наблюдает разрешённые SDK-переносы связанного тела, не применяет позы и не владеет копией placement.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerPlacementTracker : MonoBehaviour
    {
        private PlayerSession _session;
        private PhysicalSpaceAnchorFrame _frame;
        private int _sceneHandle;
        private float _retryFrameAt;

        private void Awake() => _session = GetComponent<PlayerSession>();
        private void OnEnable() => UxrAvatar.GlobalAvatarMoved += AvatarMoved;
        private void OnDisable() => UxrAvatar.GlobalAvatarMoved -= AvatarMoved;

        private void AvatarMoved(object sender, UxrAvatarMoveEventArgs args)
        {
            RecordAvatar(sender as UxrAvatar);
        }

        /// <summary>Явная точка для SDK-переноса текущего аватара; старый аватар смены скина игнорируется.</summary>
        public void RecordAvatar(UxrAvatar avatar)
        {
            if (_session == null) _session = GetComponent<PlayerSession>();
            if (_session == null || _session.ActiveAvatar == null || avatar == null ||
                _session.ActiveAvatar.GetComponent<UxrAvatar>() != avatar) return;
            AvatarCalibrationApplier applier = avatar.GetComponent<AvatarCalibrationApplier>();
            if (applier != null && applier.IsApplyingPlacement) return;
            if (!_session.isServer && !_session.isLocalPlayer) return;

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (_sceneHandle != scene.handle)
            {
                _sceneHandle = scene.handle;
                _frame = default;
                _retryFrameAt = 0f;
            }
            if (_session.IsCalibrated && !_frame.IsValid && Time.realtimeSinceStartup >= _retryFrameAt)
            {
                PhysicalSpaceAnchorFrame.TryBuildFromScene(out _frame, out _);
                _retryFrameAt = Time.realtimeSinceStartup + 1f;
            }
            if (!PlayerPlacement.TryCapture(avatar.transform.position, avatar.transform.rotation, _session.IsCalibrated,
                                            scene.name, _frame, out PlayerPlacement place, out _)) return;
            if (_session.isServer) _session.ServerRememberPlacement(place);
            if (_session.isLocalPlayer) LocalPlayerCalibration.RecordPlacement(place, _session.EffectiveCalibration);
        }
    }
}
