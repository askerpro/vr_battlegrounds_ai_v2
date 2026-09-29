using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Выбывший должен это заметить. Только своему игроку (на его машине):
    /// <list type="bullet">
    /// <item><b>Гибель</b> — контроллеры вибрируют 3 с (звук и «Вы погибли» даёт HUD).</item>
    /// <item><b>Пока выбывший не в своей зоне</b> — мир светлее и чёрно-белый. В своей зоне он уже
    ///       знает, что выбыл (ждёт раунд, работает с планшетом), и вид возвращается в норму.</item>
    /// </list>
    ///
    /// <para>
    /// Эффект — пост-обработка URP (<see cref="ColorAdjustments"/>: насыщенность −100, экспозиция
    /// вверх) на глобальном <see cref="Volume"/>, созданном в рантайме. Пост-обработка камеры
    /// включается только на время эффекта: полноэкранный проход на Quest стоит кадра, а выбывший
    /// не стреляет. Переход плавный — резкая смена картинки в шлеме неприятна.
    /// </para>
    ///
    /// Добавляет <see cref="SpectatorController"/> своему аватару.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostViewEffect : MonoBehaviour
    {
        private const float HapticSeconds = 3f;
        private const float HapticAmplitude = 0.8f;
        private const float FadeSeconds = 0.6f;
        private const float Saturation = -100f;
        private const float Exposure = 0.6f;

        private SpectatorController _spectator;
        private PlayerController _player;
        private UxrAvatar _avatar;

        private Volume _volume;
        private VolumeProfile _profile;
        private UniversalAdditionalCameraData _cameraData;
        private bool _cameraHadPostProcessing;

        private void Awake()
        {
            _spectator = GetComponent<SpectatorController>();
            _player = GetComponent<PlayerController>();
            _avatar = GetComponent<UxrAvatar>();
        }

        private void OnEnable()
        {
            if (_player != null) _player.PlayerDied += OnDied;
        }

        private void OnDisable()
        {
            if (_player != null) _player.PlayerDied -= OnDied;
            SetWeight(0f);
        }

        private void OnDestroy()
        {
            if (_volume != null) Destroy(_volume.gameObject);
            if (_profile != null) Destroy(_profile);
        }

        private bool IsLocal => _avatar != null && _avatar.AvatarMode == UxrAvatarMode.Local;

        /// <summary>
        /// Гибель своего игрока: вибрация обоих контроллеров. <see cref="PlayerController.PlayerDied"/>
        /// поднимается на каждой машине — у чужих аватаров контроллеров нет, их пропускаем.
        /// Mix, а не Replace: Replace глушит остальные хаптики руки (как в <c>PocketHaptics</c>).
        /// </summary>
        private void OnDied(PlayerController player)
        {
            if (!IsLocal) return;

            UxrAvatar.LocalAvatarInput.SendHapticFeedback(UxrHandSide.Left, UxrHapticClipType.RumbleFreqNormal, HapticAmplitude, HapticSeconds, UxrHapticMode.Mix);
            UxrAvatar.LocalAvatarInput.SendHapticFeedback(UxrHandSide.Right, UxrHapticClipType.RumbleFreqNormal, HapticAmplitude, HapticSeconds, UxrHapticMode.Mix);
        }

        private void Update()
        {
            // Сетевой аватар становится локальным уже после спавна — режим проверяется каждый кадр.
            if (!IsLocal)
            {
                if (_volume != null && _volume.weight > 0f) SetWeight(0f);
                return;
            }

            bool wanted = GhostViewRule.Wanted(_spectator != null && _spectator.IsSpectating(),
                                              _player != null && _player.Session != null && _player.Session.IsInSpawnZone);

            float current = _volume != null ? _volume.weight : 0f;
            float target = wanted ? 1f : 0f;
            if (Mathf.Approximately(current, target)) return;

            SetWeight(Mathf.MoveTowards(current, target, Time.deltaTime / FadeSeconds));
        }

        private void SetWeight(float weight)
        {
            if (weight > 0f) EnsureVolume();
            if (_volume == null) return;

            _volume.weight = weight;
            SetCameraPostProcessing(weight > 0f);
        }

        private void EnsureVolume()
        {
            if (_volume != null) return;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            ColorAdjustments color = _profile.Add<ColorAdjustments>(true);
            color.saturation.Override(Saturation);
            color.postExposure.Override(Exposure);

            var go = new GameObject("GhostViewVolume");
            DontDestroyOnLoad(go);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.sharedProfile = _profile;
            _volume.weight = 0f;
        }

        /// <summary>Пост-обработка камеры — только пока эффект виден; исходная настройка возвращается.</summary>
        private void SetCameraPostProcessing(bool on)
        {
            if (_cameraData == null)
            {
                Camera camera = _avatar != null && _avatar.CameraTransform != null ? _avatar.CameraTransform.GetComponent<Camera>() : null;
                if (camera == null) return;
                _cameraData = camera.GetUniversalAdditionalCameraData();
                _cameraHadPostProcessing = _cameraData.renderPostProcessing;
            }

            _cameraData.renderPostProcessing = on || _cameraHadPostProcessing;
        }
    }

    /// <summary>Когда показывать эффект выбывшего. Чистое правило — проверяется тестом.</summary>
    public static class GhostViewRule
    {
        /// <summary>Выбыл и ещё не в своей зоне.</summary>
        public static bool Wanted(bool spectating, bool inOwnZone) => spectating && !inOwnZone;
    }
}
