using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Общая вибрация взаимодействий своего игрока — одна система вместо скрипта под каждый сценарий (заменяет
    /// <c>PocketHaptics</c>). Клипы — из <see cref="HapticRoles" />, для отдельного якоря или предмета — из
    /// <see cref="HapticOverride" />; слот без формы — без вибрации.
    /// <list type="bullet">
    /// <item><b>Готовность якоря</b> (состояние): пока якорь готов принять предмет из руки или отдать своё содержимое этой
    /// руке, на ней звучит непрерывный клип роли якоря (<see cref="AnchorReadiness" />). Карманы находят на ощупь.</item>
    /// <item><b>Хват, укладка, отпускание</b> (события <see cref="UxrGrabManager" />): разовый клип на руке своего игрока.</item>
    /// </list>
    /// Только локальный аватар; чужие руки сервис и так отбрасывает. Ставится вместе с <see cref="HapticService" />.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionHaptics : MonoBehaviour
    {
        private HapticRoles _roles;
        private UxrGrabManager _manager;
        private UxrAvatar _avatar;
        private AnchorReadiness _readiness;

        private readonly UxrGrabbableObjectAnchor[] _readyAnchor = new UxrGrabbableObjectAnchor[2];
        private readonly UxrHapticClip[] _readyClip = new UxrHapticClip[2];
        private readonly HapticHandle[] _readyVoice = new HapticHandle[2];
        private readonly Dictionary<UxrGrabbableObjectAnchor, UxrHapticClip> _anchorClip = new Dictionary<UxrGrabbableObjectAnchor, UxrHapticClip>();

        private void Awake() => _roles = HapticRoles.Instance;

        private void OnEnable() => HapticRoles.Changed += OnRolesChanged;

        private void OnDisable()
        {
            HapticRoles.Changed -= OnRolesChanged;
            Subscribe(null);
            ReleaseReadiness();
        }

        private void Update()
        {
            Subscribe(UxrGrabManager.HasInstance ? UxrGrabManager.Instance : null);

            UxrAvatar local = UxrAvatar.LocalAvatar;
            if (local == null || local.AvatarMode != UxrAvatarMode.Local)
            {
                if (_readiness != null) ReleaseReadiness();
                return;
            }

            if (local != _avatar || _readiness == null)
            {
                ReleaseReadiness();
                _avatar = local;
                _readiness = new AnchorReadiness(local, IsTracked, () => _roles.AnchorReady(AnchorRoleKind.World).HasWaveform);
            }

            UpdateHand(UxrHandSide.Left);
            UpdateHand(UxrHandSide.Right);
        }

        // ---------- Готовность якоря ----------

        private void UpdateHand(UxrHandSide side)
        {
            int i = (int)side;
            UxrGrabbableObjectAnchor anchor = _readiness.GetReadyAnchor(side);
            UxrHapticClip clip = anchor != null ? ReadyClip(anchor) : null;

            if (anchor != _readyAnchor[i])
            {
                GameLog.Player.Verbose(anchor != null
                    ? $"[InteractionHaptics] {side}: якорь '{anchor.name}' готов — вибрация"
                    : $"[InteractionHaptics] {side}: якорь не готов — вибрация гаснет", this);
                _readyAnchor[i] = anchor;
            }

            // Один голос на руку, пока она у готового якоря с тем же клипом: переход между соседними карманами его не рвёт.
            // Сервис может завершить голос сам (смена локального аватара) — тогда он запускается заново.
            if (clip == _readyClip[i] && (clip == null || _readyVoice[i].IsActive)) return;
            _readyVoice[i].End();
            _readyClip[i] = clip;
            _readyVoice[i] = clip != null ? HapticService.Begin(clip, side, this) : default;
        }

        private bool IsTracked(UxrGrabbableObjectAnchor anchor) => ReadyClip(anchor) != null;

        /// <summary>Клип готовности якоря (переопределение или роль) или null, если вибрации нет. Кэш — роль якоря не меняется.</summary>
        private UxrHapticClip ReadyClip(UxrGrabbableObjectAnchor anchor)
        {
            if (!_anchorClip.TryGetValue(anchor, out UxrHapticClip clip))
            {
                var own = anchor.GetComponent<HapticOverride>();
                clip = HapticOverride.Pick(own != null ? own.AnchorReady : null, _roles.AnchorReady(AnchorRole.Get(anchor)));
                if (clip != null && !clip.HasWaveform) clip = null;
                _anchorClip[anchor] = clip;
            }
            return clip;
        }

        private void OnRolesChanged(HapticRoles roles)
        {
            if (roles != _roles) return;
            // Правка ролей в Play: пересобрать кэш и перезапустить голоса с новыми параметрами клипа.
            _anchorClip.Clear();
            for (int i = 0; i < 2; i++)
            {
                _readyVoice[i].End();
                _readyVoice[i] = default;
                _readyClip[i] = null;
            }
        }

        private void ReleaseReadiness()
        {
            _readiness?.Dispose();
            _readiness = null;
            _avatar = null;
            _anchorClip.Clear();
            for (int i = 0; i < 2; i++)
            {
                _readyVoice[i].End();
                _readyVoice[i] = default;
                _readyClip[i] = null;
                _readyAnchor[i] = null;
            }
        }

        // ---------- События предмета ----------

        private void Subscribe(UxrGrabManager manager)
        {
            if (manager == _manager) return;
            if (_manager != null)
            {
                _manager.ObjectGrabbed -= OnGrabbed;
                _manager.ObjectPlaced -= OnPlaced;
                _manager.ObjectReleased -= OnReleased;
            }
            _manager = manager;
            if (_manager != null)
            {
                _manager.ObjectGrabbed += OnGrabbed;
                _manager.ObjectPlaced += OnPlaced;
                _manager.ObjectReleased += OnReleased;
            }
        }

        private void OnGrabbed(object sender, UxrManipulationEventArgs e) => PlayItem(e, o => o.Grab, _roles.ItemGrab);

        private void OnPlaced(object sender, UxrManipulationEventArgs e) => PlayItem(e, o => o.Place, _roles.ItemPlace);

        private void OnReleased(object sender, UxrManipulationEventArgs e) => PlayItem(e, o => o.Release, _roles.ItemRelease);

        private static void PlayItem(UxrManipulationEventArgs e, System.Func<HapticOverride, UxrHapticClip> own, UxrHapticClip role)
        {
            // Перехват второй рукой и смена руки — не новое взятие предмета.
            if (e == null || !e.IsGrabbedStateChanged || !HapticService.IsLocalHand(e.Grabber)) return;
            HapticOverride overrides = e.GrabbableObject != null ? e.GrabbableObject.GetComponent<HapticOverride>() : null;
            UxrHapticClip clip = HapticOverride.Pick(overrides != null ? own(overrides) : null, role);
            HapticService.Play(clip, e.Grabber);
        }
    }
}
