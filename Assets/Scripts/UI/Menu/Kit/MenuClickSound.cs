using System;
using UnityEngine;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>
    /// Звук клика кнопок меню планшета: клип и громкость — из <see cref="MenuTheme"/>, играет источник на корневом
    /// канвасе кнопки (один на канвас, создаётся при первом клике) — звук идёт от планшета, а не из головы.
    /// Нажатие лазером иначе ничем, кроме цвета, не подтверждено. Проверка — <c>MenuSoundTests</c>.
    /// </summary>
    public static class MenuClickSound
    {
        /// <summary>Сыгран клик: кнопка и клип. Для тестов и отладки.</summary>
        public static event Action<KitButton, AudioClip> Played;

        public static void Play(KitButton button)
        {
            MenuTheme theme = MenuTheme.Instance;
            AudioClip clip = theme != null ? theme.ClickSound : null;
            if (button == null || clip == null) return;

            AudioSource source = SourceFor(button);
            if (source != null) source.PlayOneShot(clip, theme.ClickVolume);
            Played?.Invoke(button, clip);
        }

        private static AudioSource SourceFor(KitButton button)
        {
            Canvas canvas = button.GetComponentInParent<Canvas>();
            GameObject host = canvas != null ? canvas.rootCanvas.gameObject : button.gameObject;

            var source = host.GetComponent<AudioSource>();
            if (source != null) return source;

            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 0.3f;
            source.maxDistance = 5f;
            source.rolloffMode = AudioRolloffMode.Linear;
            return source;
        }
    }
}
