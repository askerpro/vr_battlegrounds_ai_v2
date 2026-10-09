using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Haptics
{
    /// <summary>Роль руки в вызове: вторая рука на том же предмете умножается на <see cref="UxrHapticClip.SecondaryHandGain" />.</summary>
    public enum HapticHandRole { Primary, Secondary }

    /// <summary>
    /// Единственный владелец вибромоторов контроллеров (<c>tasks/haptics-system/Details.md</c>). Остальной код и компоненты
    /// просят сыграть <see cref="UxrHapticClip" /> с формой (<see cref="UxrHapticWaveform" />, SDK-патч 66): форма — общий
    /// ассет, сила, приоритет, пауза повтора и кулдаун — в клипе точки интеграции. Смешивание — <see cref="HapticMixer" />,
    /// запись в мотор — <see cref="UnityXRHapticDevice" />.
    ///
    /// <para>
    /// Вибрация только на руке локального игрока: вызов с грабером чужого аватара или бота — no-op. Поэтому
    /// обработчики синхронизируемых событий (идут на каждой машине) могут звать сервис без своей проверки.
    /// <c>StateEventAuthority.IsAuthorOfItem</c> для этого не годится: на хосте он true для ничейных предметов и ботов.
    /// Авторскую проверку самого события это не заменяет.
    /// </para>
    ///
    /// <para>
    /// Ставит себя сам при старте игры (как <c>WatchNotificationRunner</c>); в batch mode (выделенный сервер, CI)
    /// не ставится — тогда все вызовы no-op.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HapticService : MonoBehaviour
    {
        private static HapticService s_instance;

        private readonly List<(int voice, Object owner)> _owned = new List<(int, Object)>();
        private UnityXRHapticDevice _device;
        private HapticMixer _mixer;
        private UxrAvatar _localAvatar;

        // ---------- API ----------

        /// <summary>
        /// Разовый клип на руке <paramref name="hand" />. Рука чужого аватара, бота или null — тихо игнорируется; клип без
        /// формы — тоже. <paramref name="role" /> = Secondary умножает силу на <see cref="UxrHapticClip.SecondaryHandGain" />,
        /// <paramref name="gain" /> — дополнительный множитель вызова.
        /// </summary>
        public static void Play(UxrHapticClip clip, UxrGrabber hand, HapticHandRole role = HapticHandRole.Primary, float gain = 1f)
        {
            if (s_instance == null || !IsLocalHand(hand)) return;
            s_instance.StartVoice(clip, hand.Side, gain, role, continuous: false, owner: null);
        }

        /// <summary>Разовый клип на руке локального игрока. <paramref name="role" /> — как у перегрузки с рукой.</summary>
        public static void Play(UxrHapticClip clip, UxrHandSide side, float gain = 1f, HapticHandRole role = HapticHandRole.Primary)
        {
            if (s_instance == null) return;
            s_instance.StartVoice(clip, side, gain, role, continuous: false, owner: null);
        }

        /// <summary>Разовый клип на обеих руках локального игрока — два независимых голоса.</summary>
        public static void PlayBoth(UxrHapticClip clip, float gain = 1f)
        {
            Play(clip, UxrHandSide.Left, gain);
            Play(clip, UxrHandSide.Right, gain);
        }

        /// <summary>
        /// Непрерывный клип на руке локального игрока: форма повторяется с паузой <see cref="UxrHapticClip.RepeatGapMs" />.
        /// Живёт, пока не вызван <see cref="HapticHandle.End" />, пока <paramref name="owner" /> не уничтожен и не выключен
        /// и пока не сменился локальный аватар. <paramref name="role" /> = Secondary умножает силу на
        /// <see cref="UxrHapticClip.SecondaryHandGain" />.
        /// </summary>
        public static HapticHandle Begin(UxrHapticClip clip, UxrHandSide side, Object owner, float gain = 1f,
                                         HapticHandRole role = HapticHandRole.Primary)
        {
            if (s_instance == null || owner == null) return default;
            return new HapticHandle(s_instance.StartVoice(clip, side, gain, role, continuous: true, owner));
        }

        internal static void End(int voice)
        {
            if (s_instance == null || voice == 0) return;
            s_instance._mixer.End(voice, Now);
            s_instance._owned.RemoveAll(o => o.voice == voice);
        }

        internal static bool IsActive(int voice) => s_instance != null && voice != 0 && s_instance._mixer.IsActive(voice);

        // ---------- Диагностика (окно подбора) ----------

        /// <summary>Сервис поставлен (Play Mode не в batch mode).</summary>
        public static bool IsInstalled => s_instance != null;

        /// <summary>Видит ли сервис физический контроллер руки.</summary>
        public static bool IsControllerConnected(UxrHandSide side) => s_instance != null && s_instance._device.IsConnected(side);

        /// <summary>Сколько голосов сейчас на руке и какая амплитуда уходит в мотор (до записи).</summary>
        public static void GetHandState(UxrHandSide side, out int voices, out float amplitude)
        {
            voices = 0;
            amplitude = 0f;
            if (s_instance == null) return;
            voices = s_instance._mixer.VoiceCount(side);
            amplitude = s_instance._mixer.Evaluate(side, Now);
        }

        /// <summary>Рука своего аватара: не чужой игрок и не бот.</summary>
        public static bool IsLocalHand(UxrGrabber hand)
        {
            if (hand == null) return false;
            UxrAvatar avatar = hand.Avatar;
            return avatar != null && avatar.AvatarMode == UxrAvatarMode.Local && avatar == UxrAvatar.LocalAvatar;
        }

        // ---------- Жизненный цикл ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isBatchMode) return;
            if (FindAnyObjectByType<HapticService>() != null) return;

            var host = new GameObject(nameof(HapticService));
            DontDestroyOnLoad(host);
            host.AddComponent<HapticService>();
        }

        private static float Now => Time.unscaledTime;

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;
            _device = new UnityXRHapticDevice();
            _mixer = new HapticMixer(_device);
        }

        private void OnEnable() => UxrHapticWaveform.Changed += OnWaveformChanged;

        private void OnDisable()
        {
            UxrHapticWaveform.Changed -= OnWaveformChanged;
            _mixer?.StopAll();
        }

        /// <summary>Правка формы в инспекторе во время Play: моторы сразу получают новые значения.</summary>
        private void OnWaveformChanged(UxrHapticWaveform waveform) => _mixer?.Refresh(Now);

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) StopAllVoices();
        }

        private void LateUpdate()
        {
            UxrAvatar local = UxrAvatar.LocalAvatar;
            if (local != _localAvatar)
            {
                // Смена аватара (спавн, смерть, смена скина): непрерывные сигналы прежнего тела не переносятся.
                if (_localAvatar != null) StopAllVoices();
                _localAvatar = local;
            }

            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                (int voice, Object owner) = _owned[i];
                bool alive = owner != null && (owner is not Behaviour behaviour || behaviour.isActiveAndEnabled);
                if (alive && _mixer.IsActive(voice)) continue;
                _mixer.End(voice, Now);
                _owned.RemoveAt(i);
            }

            _mixer.Tick(Now);
        }

        private int StartVoice(UxrHapticClip clip, UxrHandSide side, float gain, HapticHandRole role, bool continuous, Object owner)
        {
            if (clip == null || !clip.HasWaveform) return 0;
            if (role == HapticHandRole.Secondary) gain *= clip.SecondaryHandGain;

            int voice = _mixer.Play(clip, side, gain, continuous, Now);
            if (GameLog.Player.IsEnabled(LogLevel.Verbose))
                GameLog.Player.Verbose(voice != 0
                    ? $"[Haptics] {clip.Waveform.name} ({clip.Priority}, ×{clip.WaveformGain * gain:0.00}) → {side}{(continuous ? ", непрерывный" : "")}"
                    : $"[Haptics] {clip.Waveform.name} → {side} отброшен (кулдаун или нулевая сила)");

            if (voice != 0 && owner != null) _owned.Add((voice, owner));
            return voice;
        }

        private void StopAllVoices()
        {
            _mixer.StopAll();
            _owned.Clear();
        }
    }

    /// <summary>Дескриптор непрерывного клипа из <see cref="HapticService.Begin" />. default — пустой.</summary>
    public readonly struct HapticHandle
    {
        private readonly int _voice;

        internal HapticHandle(int voice) => _voice = voice;

        public bool IsActive => HapticService.IsActive(_voice);

        public void End() => HapticService.End(_voice);
    }
}
