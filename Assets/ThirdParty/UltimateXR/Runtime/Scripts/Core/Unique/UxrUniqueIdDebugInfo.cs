// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds: патч 8 (Docs/UltimateXR/sdk-patches.md). Файл добавлен проектом, в SDK его нет.
// --------------------------------------------------------------------------------------------------------------------
using System.Text;
using UnityEngine;

namespace UltimateXR.Core.Unique
{
    /// <summary>
    ///     Отладочное описание компонента в сериализованной ссылке на него.
    ///     <para>
    ///         Ссылка на <see cref="IUxrUniqueId" /> в событии состояния — только id. Когда принимающая сторона id
    ///         не находит, <c>UxrComponentNotFoundException</c> не может сказать, что это был за предмет: компонента
    ///         у неё нет. Поэтому отправитель при включённом флаге кладёт рядом с id путь в иерархии и тип.
    ///     </para>
    ///     <para>
    ///         Формат совместим в обе стороны внутри одной версии проекта: читатель понимает ссылку и со строкой,
    ///         и без неё, поэтому флаг можно включать на отдельных машинах (редактор, development-сборка).
    ///     </para>
    /// </summary>
    public static class UxrUniqueIdDebugInfo
    {
        /// <summary>Умолчание флага: в редакторе и development-сборках — да, в release — нет (трафик).</summary>
        public static bool DefaultIncludeInSerialization => Application.isEditor || Debug.isDebugBuild;

        /// <summary>Класть ли в сериализованную ссылку отладочное описание компонента.</summary>
        public static bool IncludeInSerialization { get; set; } = DefaultIncludeInSerialization;

        /// <summary>Потолок длины описания: оно едет в каждом событии, путь глубокой иерархии урезается с начала.</summary>
        public const int MaxLength = 160;

        /// <summary>
        ///     Описание компонента: путь в иерархии и тип, например <c>ArsenalWall (2)/DogTagPanel/DogTag [UxrGrabbableObject]</c>.
        ///     Для не-компонентов — только тип.
        /// </summary>
        public static string Describe(IUxrUniqueId unique)
        {
            if (unique == null) return string.Empty;

            string type = unique.GetType().Name;

            if (!(unique is Component component) || component == null)
            {
                return $"[{type}]";
            }

            var path = new StringBuilder(component.name);
            for (Transform t = component.transform.parent; t != null; t = t.parent)
            {
                path.Insert(0, '/').Insert(0, t.name);
            }

            string result = $"{path} [{type}]";
            return result.Length <= MaxLength ? result : "…" + result.Substring(result.Length - MaxLength + 1);
        }
    }
}
