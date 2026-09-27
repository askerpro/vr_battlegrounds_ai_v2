using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>Роль якоря для отладочной отрисовки.</summary>
    public enum AnchorRoleKind
    {
        /// <summary>Карман магазинов (<see cref="UxrMagazinePocket" />).</summary>
        Magazine,
        /// <summary>Основное оружие — <c>Anchor_Back</c>.</summary>
        Primary,
        /// <summary>Второе оружие — <c>Anchor_Hip_R</c>.</summary>
        Secondary,
        /// <summary>Прочий якорь на аватаре.</summary>
        AvatarOther,
        /// <summary>Якорь вне аватара: слоты оружия, стены арсенала.</summary>
        World
    }

    /// <summary>
    /// Роль и цвет якоря — общие для редакторских гизмо и вида в шлеме, чтобы один карман не
    /// выглядел в двух местах по-разному. Имена <c>Anchor_Back</c> / <c>Anchor_Hip</c> — контракт
    /// карманов (их же ищет <c>PlayerLoadoutManager</c>).
    /// </summary>
    public static class AnchorRole
    {
        /// <summary>
        /// Карман аватара — якорь внутри аватара, но не внутри хватаемого предмета. Гнездо
        /// магазина оружия, лежащего в кармане, тоже висит в иерархии аватара (UltimateXR
        /// подвешивает предмет к якорю), но карманом не является.
        /// </summary>
        public static bool IsAvatarPocket(UxrGrabbableObjectAnchor anchor)
        {
            return anchor.GetComponentInParent<UxrAvatar>(true) != null &&
                   anchor.GetComponentInParent<UxrGrabbableObject>(true) == null;
        }

        public static AnchorRoleKind Get(UxrGrabbableObjectAnchor anchor)
        {
            if (anchor.GetComponent<UxrMagazinePocket>() != null) return AnchorRoleKind.Magazine;
            if (!IsAvatarPocket(anchor)) return AnchorRoleKind.World;
            if (anchor.name.Contains("Anchor_Back")) return AnchorRoleKind.Primary;
            if (anchor.name.Contains("Anchor_Hip")) return AnchorRoleKind.Secondary;
            return AnchorRoleKind.AvatarOther;
        }

        public static Color GetColor(AnchorRoleKind role)
        {
            switch (role)
            {
                case AnchorRoleKind.Magazine:    return new Color(0.2f, 1f, 0.3f);
                case AnchorRoleKind.Primary:     return new Color(1f, 0.55f, 0.1f);
                case AnchorRoleKind.Secondary:   return new Color(0.2f, 0.8f, 1f);
                case AnchorRoleKind.AvatarOther: return new Color(1f, 0.9f, 0.2f);
                default:                         return new Color(0.75f, 0.75f, 0.75f);
            }
        }
    }
}
