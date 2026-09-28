using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Что именно упрощается в отрисовке чужого (удалённого) аватара. Только решения и
    /// данные — применяет их <see cref="RemoteAvatarRenderSnapshot"/>, а к аватарам
    /// цепляет <see cref="RemoteAvatarRenderOptimizer"/>.
    ///
    /// <para>
    /// <b>Зачем.</b> Каждый аватар — десятки <c>SkinnedMeshRenderer</c> (MEF — 49 шт., 106k
    /// треугольников, 103 кости), и все они по умолчанию отбрасывают тень, скинятся даже вне
    /// кадра (<c>UxrAvatar.Awake</c> ставит <c>updateWhenOffscreen = true</c>) и пишут
    /// motion vectors. На Quest каждый такой аватар за спиной игрока стоит полного скиннинга
    /// и прохода в shadow map. Свой аватар (<see cref="UxrAvatarMode.Local"/>) не трогается:
    /// UltimateXR двигает его кости вручную, и для него флаг SDK осмыслен.
    /// </para>
    /// </summary>
    public static class RemoteAvatarRenderPolicy
    {
        /// <summary>
        /// Тень чужих аватаров выключается целиком: на шлеме она почти не читается, а
        /// shadow pass повторяет весь скиннинг. <c>receiveShadows</c> не меняется —
        /// тени окружения на аватаре остаются.
        /// </summary>
        public const ShadowCastingMode RemoteShadowCasting = ShadowCastingMode.Off;

        /// <summary>
        /// Запас к <c>localBounds</c> скина с каждой стороны, метры. С
        /// <c>updateWhenOffscreen = false</c> отсечение идёт по bounds из импорта (поза
        /// привязки), а IK UltimateXR вытягивает руки дальше неё — без запаса вытянутая
        /// рука на краю поля зрения могла бы исчезать («popping»).
        /// </summary>
        public const float BoundsPaddingMeters = 0.3f;

        /// <summary>
        /// Рендереры, которых под снаряжением не видно никогда. У Heavy_Soldier
        /// (<c>Heavy_Soldier_Rig_Mask_Winter</c>) лицо целиком закрыто маской, но модель
        /// BusinessLady под ней продолжает рисовать зубы и оба слоя глаз. Имена — точные
        /// имена объектов в риге; переименовали в модели — правьте здесь, иначе
        /// <c>RemoteAvatarRenderOptimizerTests</c> покраснеет.
        /// </summary>
        public static readonly IReadOnlyList<string> HiddenUnderGearRendererNames = new[]
        {
            "SK_BusinessLady_teeth_upper",
            "SK_BusinessLady_teeth_lower",
            "SK_BusinessLady_Eyes_Inside",
            "SK_BusinessLady_Eyes_Outside",
        };

        /// <summary>Упрощать ли отрисовку аватара в этом режиме. Только удалённые.</summary>
        public static bool ShouldOptimize(UxrAvatarMode mode) => mode == UxrAvatarMode.UpdateExternally;

        /// <summary>Скрыт ли рендерер с этим именем под снаряжением (точное совпадение).</summary>
        public static bool IsHiddenUnderGear(string rendererName)
        {
            if (string.IsNullOrEmpty(rendererName)) return false;

            foreach (string hidden in HiddenUnderGearRendererNames)
            {
                if (string.Equals(hidden, rendererName, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>Bounds, расширенные на <see cref="BoundsPaddingMeters"/> с каждой стороны.</summary>
        public static Bounds PadBounds(Bounds bounds)
        {
            // Expand увеличивает полный размер, поэтому запас на сторону удваивается.
            bounds.Expand(BoundsPaddingMeters * 2f);
            return bounds;
        }

        /// <summary>
        /// Рендереры тела аватара. Всё, что лежит под <see cref="UxrGrabbableObject"/>
        /// (оружие в руке или в кобуре), пропускается: у предметов свой жизненный цикл, они
        /// уходят из иерархии аватара, и их состояние здесь не запоминается.
        /// </summary>
        public static List<Renderer> CollectBodyRenderers(Transform avatarRoot)
        {
            var result = new List<Renderer>();
            if (avatarRoot == null) return result;

            foreach (Renderer renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<UxrGrabbableObject>(true) != null) continue;
                result.Add(renderer);
            }

            return result;
        }
    }
}
