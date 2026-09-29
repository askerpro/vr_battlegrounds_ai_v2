using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Покадровый кэш того, что правила хвата (<c>GrabRules</c>) выводят из иерархии предмета:
    /// хозяин якоря (<see cref="AnchoredItemGrabRule.GetHost" />) и правило детали
    /// (<see cref="GrabOnlyWhenParentHeld" /> и её предмет-родитель).
    ///
    /// <para>
    /// Зачем. UltimateXR (<c>UxrGrabManager.GetClosestGrabbableObject</c>) зовёт
    /// <c>CanGrabDelegate</c> для каждой точки каждого хватаемого предмета сцены, для каждой
    /// свободной руки, каждый кадр — ещё до проверки расстояния. Без кэша это сотни
    /// <c>GetComponentInParent</c> за кадр (≈126 предметов в лобби вместе с прокси карманов
    /// чужих аватаров). С кэшем — один подъём по иерархии на предмет за кадр.
    /// </para>
    ///
    /// <para>
    /// Почему покадровый, а не навсегда. Предметы переезжают между якорями, руками и полом —
    /// ответ зависит от предков, а они меняются. Кэш сбрасывается целиком в начале каждого
    /// кадра; внутри кадра запись перепроверяется по прямому родителю
    /// (<c>transform.parent</c>): вставка и выброс магазина меняют именно его, и такое изменение
    /// видно сразу. Смену только дальних предков запись увидит со следующего кадра.
    /// Словарь сравнивает ключи по ссылке — без перегруженного <c>UnityEngine.Object.Equals</c>.
    /// </para>
    /// </summary>
    public static class GrabbableHierarchyCache
    {
        private struct Entry
        {
            public Transform              Parent;
            public UxrGrabbableObject     AnchorHost;
            public GrabOnlyWhenParentHeld PartRule;
            public UxrGrabbableObject     PartParent;
            public SupportGripRequiresMain SupportRule;
        }

        private sealed class ReferenceComparer : IEqualityComparer<UxrGrabbableObject>
        {
            public bool Equals(UxrGrabbableObject a, UxrGrabbableObject b) => ReferenceEquals(a, b);
            public int GetHashCode(UxrGrabbableObject obj) => RuntimeHelpers.GetHashCode(obj);
        }

        // Страховка на случай, когда кадр не идёт (EditMode-тесты): уничтоженные ключи не копятся.
        private const int MaxEntries = 4096;

        private static readonly Dictionary<UxrGrabbableObject, Entry> s_entries = new Dictionary<UxrGrabbableObject, Entry>(256, new ReferenceComparer());
        private static int s_frame = -1;

        /// <summary>То же, что <see cref="AnchoredItemGrabRule.GetHost" />, но из кэша кадра.</summary>
        public static UxrGrabbableObject GetAnchorHost(UxrGrabbableObject grabbable)
        {
            return grabbable == null ? null : GetEntry(grabbable).AnchorHost;
        }

        /// <summary>
        /// <see cref="GrabOnlyWhenParentHeld" /> на самом предмете (null — нет) и его предмет-родитель
        /// (<see cref="GrabOnlyWhenParentHeld.Parent" />), из кэша кадра.
        /// </summary>
        public static GrabOnlyWhenParentHeld GetPartRule(UxrGrabbableObject grabbable, out UxrGrabbableObject partParent)
        {
            if (grabbable == null)
            {
                partParent = null;
                return null;
            }

            Entry entry = GetEntry(grabbable);
            partParent = entry.PartParent;
            return entry.PartRule;
        }

        /// <summary><see cref="SupportGripRequiresMain" /> на самом предмете (null — нет), из кэша кадра.</summary>
        public static SupportGripRequiresMain GetSupportRule(UxrGrabbableObject grabbable)
        {
            return grabbable == null ? null : GetEntry(grabbable).SupportRule;
        }

        private static Entry GetEntry(UxrGrabbableObject grabbable)
        {
            int frame = Time.frameCount;

            if (frame != s_frame || s_entries.Count >= MaxEntries)
            {
                s_entries.Clear();
                s_frame = frame;
            }

            Transform parent = grabbable.transform.parent;

            if (s_entries.TryGetValue(grabbable, out Entry entry) && ReferenceEquals(entry.Parent, parent))
            {
                return entry;
            }

            entry.Parent     = parent;
            entry.AnchorHost = AnchoredItemGrabRule.GetHost(grabbable);
            entry.PartRule   = grabbable.TryGetComponent(out GrabOnlyWhenParentHeld rule) ? rule : null;
            entry.PartParent = entry.PartRule != null ? entry.PartRule.Parent : null;
            entry.SupportRule = grabbable.TryGetComponent(out SupportGripRequiresMain supportRule) ? supportRule : null;

            s_entries[grabbable] = entry;
            return entry;
        }
    }
}
