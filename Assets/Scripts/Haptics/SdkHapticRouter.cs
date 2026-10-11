using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Перевод запросов вибрации UltimateXR (SDK-патч 65, <see cref="UxrHapticRouting" />) в голоса <see cref="HapticService" />
    /// (<c>tasks/haptics-system/Details.md</c> §5.3). Так у мотора один писатель: на Quest каждый прямой вызов SDK обрывает
    /// текущую вибрацию. Ставит и снимает <see cref="HapticService" />. Принимает <b>каждый</b> запрос (возвращает true),
    /// в том числе чтобы отбросить его: корутина SDK для принятого запроса не стартует, мотор SDK не трогает.
    /// <list type="number">
    /// <item>Аватар источника не локальный (бот, чужой игрок, нет локального) — отбросить.</item>
    /// <item>Отдача (Shot/ShotBig/ShotBigger), а рука держит оружие с <see cref="IWeaponRecoilHapticsOwner.OwnsRecoilHaptics" /> —
    /// отбросить: отдачу играет ствол.</item>
    /// <item>Клип с формой — сервису как есть.</item>
    /// <item>Клип без формы — запасной клип его <c>FallbackClipType</c> × <c>FallbackAmplitude</c>; тип SDK — запасной клип
    /// типа × сила запроса. Гул Rumble* держится срок запроса (по умолчанию 0,5 с, как в SDK). Нет запасного — тишина.</item>
    /// <item>Сырой запрос — голос клипа <see cref="SdkHapticFallbacks.Raw" /> с силой и сроком запроса; повтор до конца
    /// срока продлевает тот же голос.</item>
    /// <item>Остановка — гасит только голоса маршрутизатора на этой руке.</item>
    /// <item>Режим SDK Mix/Replace не учитывается — смешивание решает <see cref="HapticMixer" />.</item>
    /// </list>
    /// </summary>
    internal sealed class SdkHapticRouter
    {
        private readonly SdkHapticFallbacks _fallbacks;
        private readonly Func<UxrHapticRequest, bool> _route;
        private readonly Func<UxrControllerInput, bool> _isLocalSource;
        private readonly Func<UxrControllerInput, UxrHandSide, UxrGrabbableObject> _heldObject;

        /// <summary>Голоса маршрутизатора по руке — их и только их гасит остановка SDK.</summary>
        private readonly List<int>[] _voices = { new List<int>(), new List<int>() };

        /// <summary>Голоса со сроком (сырые и гул) по (рука, клип): повтор запроса продлевает голос, а не копит новые.</summary>
        private readonly Dictionary<(UxrHandSide, UxrHapticClip), int> _sustained = new Dictionary<(UxrHandSide, UxrHapticClip), int>();
        private readonly List<(UxrHandSide, UxrHapticClip)> _staleKeys = new List<(UxrHandSide, UxrHapticClip)>();

        public SdkHapticRouter(SdkHapticFallbacks fallbacks) : this(fallbacks, IsLocalPlayerInput, HeldByAvatarHand)
        {
        }

        /// <summary>
        /// Шов тестов EditMode: там нет локального аватара UltimateXR, поэтому локальность источника запроса и предмет в руке
        /// подменяются. <paramref name="heldObject" /> зовётся только для источника, признанного локальным.
        /// </summary>
        internal SdkHapticRouter(SdkHapticFallbacks fallbacks, Func<UxrControllerInput, bool> isLocalSource,
                                 Func<UxrControllerInput, UxrHandSide, UxrGrabbableObject> heldObject)
        {
            _fallbacks = fallbacks;
            _isLocalSource = isLocalSource;
            _heldObject = heldObject;
            _route = Route;
        }

        /// <summary>Сброс при Enter Play Mode без domain reload: статический перехват SDK не должен пережить сессию.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => UxrHapticRouting.Router = null;

        public void Install() => UxrHapticRouting.Router = _route;

        public void Uninstall()
        {
            if (UxrHapticRouting.Router == _route) UxrHapticRouting.Router = null;
        }

        /// <summary>Обработчик <see cref="UxrHapticRouting.Router" />. Всегда true.</summary>
        public bool Route(UxrHapticRequest request)
        {
            UxrControllerInput source = request.Source;
            if (!_isLocalSource(source)) return true;

            UxrHandSide side = request.Side;
            switch (request.Kind)
            {
                case UxrHapticRequestKind.Clip:
                    RouteClip(source, side, request.Clip);
                    break;

                case UxrHapticRequestKind.ClipType:
                    PlayType(source, side, request.ClipType, request.Amplitude, request.DurationSeconds);
                    break;

                case UxrHapticRequestKind.Raw:
                    Sustain(side, _fallbacks.Raw, request.Amplitude, request.DurationSeconds);
                    break;

                case UxrHapticRequestKind.Stop:
                    Stop(side);
                    break;
            }
            return true;
        }

        private void RouteClip(UxrControllerInput source, UxrHandSide side, UxrHapticClip clip)
        {
            if (clip == null) return;
            if (clip.HasWaveform)
            {
                // Тип выстрела у клипа с формой — тоже отдача: ствол, играющий её сам, не должен звучать дважды.
                if (SdkHapticFallbacks.IsShot(clip.FallbackClipType) && WeaponOwnsRecoil(source, side, clip.FallbackClipType)) return;
                Track(side, HapticService.PlayVoice(clip, side, 1f));
                return;
            }
            PlayType(source, side, clip.FallbackClipType, clip.FallbackAmplitude, clip.FallbackDurationSeconds);
        }

        private void PlayType(UxrControllerInput source, UxrHandSide side, UxrHapticClipType type, float amplitude, float durationSeconds)
        {
            if (SdkHapticFallbacks.IsShot(type) && WeaponOwnsRecoil(source, side, type)) return;

            UxrHapticClip fallback = _fallbacks.Get(type);
            if (fallback == null || !fallback.HasWaveform)
            {
                if (type != UxrHapticClipType.None && GameLog.Player.IsEnabled(LogLevel.Verbose))
                    GameLog.Player.Verbose($"[Haptics] SDK {type} → {side}: запасного клипа нет — тишина");
                return;
            }

            if (SdkHapticFallbacks.IsRumble(type))
                Sustain(side, fallback, amplitude, durationSeconds > 0f ? durationSeconds : SdkHapticFallbacks.DefaultRumbleSeconds);
            else
                Track(side, HapticService.PlayVoice(fallback, side, amplitude));
        }

        /// <summary>Голос со сроком: продлить уже звучащий голос (рука, клип) или начать новый.</summary>
        private void Sustain(UxrHandSide side, UxrHapticClip clip, float amplitude, float durationSeconds)
        {
            if (clip == null || !clip.HasWaveform) return;

            var key = (side, clip);
            bool known = _sustained.TryGetValue(key, out int voice);
            if (amplitude <= 0f || durationSeconds <= 0f)
            {
                // Нулевая сила или срок — гула больше не просят.
                if (known) HapticService.End(voice);
                _sustained.Remove(key);
                return;
            }

            if (known && HapticService.ExtendTimed(voice, amplitude, durationSeconds)) return;

            voice = HapticService.BeginTimed(clip, side, amplitude, durationSeconds);
            if (voice == 0)
            {
                _sustained.Remove(key);
                return;
            }
            _sustained[key] = voice;
            Track(side, voice);
        }

        private void Stop(UxrHandSide side)
        {
            List<int> voices = _voices[(int)side];
            foreach (int voice in voices) HapticService.End(voice);
            voices.Clear();

            _staleKeys.Clear();
            foreach (KeyValuePair<(UxrHandSide, UxrHapticClip), int> pair in _sustained)
                if (pair.Key.Item1 == side) _staleKeys.Add(pair.Key);
            foreach ((UxrHandSide, UxrHapticClip) key in _staleKeys) _sustained.Remove(key);
        }

        /// <summary>Запомнить голос руки; заодно убрать закончившиеся, чтобы список не рос.</summary>
        private void Track(UxrHandSide side, int voice)
        {
            if (voice == 0) return;
            List<int> voices = _voices[(int)side];
            voices.RemoveAll(v => !HapticService.IsActive(v));
            voices.Add(voice);
        }

        /// <summary>Рука держит оружие, которое само играет отдачу (контракт haptics-api).</summary>
        private bool WeaponOwnsRecoil(UxrControllerInput source, UxrHandSide side, UxrHapticClipType type)
        {
            UxrGrabbableObject grabbed = _heldObject(source, side);
            if (grabbed == null) return false;

            var owner = grabbed.GetComponentInParent<IWeaponRecoilHapticsOwner>(true);
            if (owner == null || !owner.OwnsRecoilHaptics) return false;

            if (GameLog.Player.IsEnabled(LogLevel.Verbose))
                GameLog.Player.Verbose($"[Haptics] SDK {type} → {side}: отдачу играет {grabbed.name} — отброшено");
            return true;
        }

        /// <summary>Контроллер локального игрока: не бот, не чужой аватар, аватар есть.</summary>
        private static bool IsLocalPlayerInput(UxrControllerInput source)
        {
            UxrAvatar avatar = source != null ? source.Avatar : null;
            return avatar != null && avatar.AvatarMode == UxrAvatarMode.Local && avatar == UxrAvatar.LocalAvatar;
        }

        /// <summary>Предмет в руке аватара источника (источник уже признан локальным — аватар есть).</summary>
        private static UxrGrabbableObject HeldByAvatarHand(UxrControllerInput source, UxrHandSide side)
        {
            UxrGrabber grabber = source.Avatar.GetGrabber(side);
            return grabber != null ? grabber.GrabbedObject : null;
        }
    }
}
