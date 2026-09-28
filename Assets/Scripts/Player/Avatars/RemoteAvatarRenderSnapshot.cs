using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Исходные настройки рендереров аватара и переход «удалённый ↔ как в префабе».
    /// Снимок делается один раз, до первого кадра, поэтому <see cref="Restore"/> возвращает
    /// именно префабные значения, а повторное <see cref="ApplyRemote"/> не накапливает запас
    /// у bounds. Решения берутся из <see cref="RemoteAvatarRenderPolicy"/>.
    ///
    /// <para>
    /// Скрытые детали гасятся через <see cref="Renderer.forceRenderingOff"/>, а не
    /// <c>enabled</c>: <c>enabled</c> всем рендерерам аватара переписывает
    /// <c>UxrAvatar.RenderMode</c> (в том числе в <c>UxrAvatar.Start</c>), и деталь
    /// вернулась бы.
    /// </para>
    /// </summary>
    public sealed class RemoteAvatarRenderSnapshot
    {
        private struct Entry
        {
            public Renderer Renderer;
            public ShadowCastingMode ShadowCasting;
            public bool ForceRenderingOff;
            public bool HiddenUnderGear;
            public SkinnedMeshRenderer Skin;
            public bool UpdateWhenOffscreen;
            public bool SkinnedMotionVectors;
            public Bounds LocalBounds;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        /// <summary>Применены ли сейчас удалённые настройки.</summary>
        public bool IsApplied { get; private set; }

        /// <summary>Сколько рендереров под управлением снимка.</summary>
        public int RendererCount => _entries.Count;

        /// <summary>Сколько из них скин.</summary>
        public int SkinCount { get; private set; }

        /// <summary>Сколько гасится как скрытые под снаряжением.</summary>
        public int HiddenCount { get; private set; }

        /// <summary>Запоминает текущие (префабные) настройки рендереров.</summary>
        public static RemoteAvatarRenderSnapshot Capture(IEnumerable<Renderer> renderers)
        {
            var snapshot = new RemoteAvatarRenderSnapshot();
            if (renderers == null) return snapshot;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                var entry = new Entry
                {
                    Renderer = renderer,
                    ShadowCasting = renderer.shadowCastingMode,
                    ForceRenderingOff = renderer.forceRenderingOff,
                    HiddenUnderGear = RemoteAvatarRenderPolicy.IsHiddenUnderGear(renderer.name),
                    Skin = renderer as SkinnedMeshRenderer,
                };

                if (entry.Skin != null)
                {
                    entry.UpdateWhenOffscreen = entry.Skin.updateWhenOffscreen;
                    entry.SkinnedMotionVectors = entry.Skin.skinnedMotionVectors;
                    entry.LocalBounds = entry.Skin.localBounds;
                    snapshot.SkinCount++;
                }

                if (entry.HiddenUnderGear) snapshot.HiddenCount++;
                snapshot._entries.Add(entry);
            }

            return snapshot;
        }

        /// <summary>Облегчает отрисовку. Повторный вызов ничего не делает.</summary>
        public void ApplyRemote()
        {
            if (IsApplied) return;

            foreach (Entry entry in _entries)
            {
                if (entry.Renderer == null) continue;

                entry.Renderer.shadowCastingMode = RemoteAvatarRenderPolicy.RemoteShadowCasting;
                if (entry.HiddenUnderGear) entry.Renderer.forceRenderingOff = true;

                if (entry.Skin != null)
                {
                    // Сначала bounds, потом флаг: без updateWhenOffscreen отсечение идёт
                    // только по ним.
                    entry.Skin.localBounds = RemoteAvatarRenderPolicy.PadBounds(entry.LocalBounds);
                    entry.Skin.updateWhenOffscreen = false;
                    entry.Skin.skinnedMotionVectors = false;
                }
            }

            IsApplied = true;
        }

        /// <summary>Возвращает значения, снятые в <see cref="Capture"/>.</summary>
        public void Restore()
        {
            if (!IsApplied) return;

            foreach (Entry entry in _entries)
            {
                if (entry.Renderer == null) continue;

                entry.Renderer.shadowCastingMode = entry.ShadowCasting;
                entry.Renderer.forceRenderingOff = entry.ForceRenderingOff;

                if (entry.Skin != null)
                {
                    entry.Skin.localBounds = entry.LocalBounds;
                    entry.Skin.updateWhenOffscreen = entry.UpdateWhenOffscreen;
                    entry.Skin.skinnedMotionVectors = entry.SkinnedMotionVectors;
                }
            }

            IsApplied = false;
        }
    }
}
