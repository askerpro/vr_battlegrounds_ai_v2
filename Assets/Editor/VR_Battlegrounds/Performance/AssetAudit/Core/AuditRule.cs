using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>Серьёзность находки. Порядок значений = порядок сортировки (выше — серьёзнее).</summary>
    public enum AuditSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2,
    }

    /// <summary>
    /// Правило аудита. Новое правило — новый класс-наследник с конструктором без параметров:
    /// ядро находит его через <see cref="TypeCache"/>, правка ядра не нужна.
    /// Что правило проверяет, задают интерфейсы <c>I*AuditCheck</c> ниже (можно несколько).
    /// Пороги — публичные поля с <see cref="AuditThresholdAttribute"/>: попадают в отчёт и
    /// переопределяются запросом (<see cref="AuditRequest.ThresholdOverrides"/>).
    /// Экземпляр правила живёт один прогон: накопленное состояние можно держать в полях.
    /// </summary>
    public abstract class AuditRule
    {
        /// <summary>Стабильный идентификатор вида «область.суть», например <c>shader.non-mobile</c>.</summary>
        public abstract string Id { get; }

        /// <summary>Короткое название для окна.</summary>
        public abstract string Title { get; }

        /// <summary>Серьёзность по умолчанию; находка может её поднять или понизить по порогам.</summary>
        public abstract AuditSeverity DefaultSeverity { get; }

        /// <summary>Почему это плохо именно на Quest 2/3 (тайловый мобильный GPU, multiview, 72–90 Гц).</summary>
        public abstract string WhyBadOnQuest { get; }

        /// <summary>Откуда известно: замер, отчёт, путь к данным. Правило без доказательства — гипотеза, так и писать.</summary>
        public abstract string Evidence { get; }

        /// <summary>Подсказка исправления (или имя будущего фиксера); null — нет общего рецепта.</summary>
        public virtual string FixHint => null;

        /// <summary>Группа в окне: shader, material, texture, mesh, lighting, scene.</summary>
        public virtual string Category => "general";
    }

    /// <summary>Порог правила: публичное поле int/float/bool/string/string[] с описанием.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AuditThresholdAttribute : Attribute
    {
        public AuditThresholdAttribute(string description) => Description = description;
        public string Description { get; }
    }

    /// <summary>Проверка материала (.mat или встроенного в модель).</summary>
    public interface IMaterialAuditCheck
    {
        void CheckMaterial(Material material, string assetPath, AuditContext context);
    }

    /// <summary>Проверка текстуры с её импортёром.</summary>
    public interface ITextureAuditCheck
    {
        void CheckTexture(TextureImporter importer, Texture texture, string assetPath, AuditContext context);
    }

    /// <summary>Проверка шейдера (.shader, .shadergraph). Материалы охвата уже пройдены — см. <see cref="AuditContext.MaterialsUsing"/>.</summary>
    public interface IShaderAuditCheck
    {
        void CheckShader(Shader shader, string assetPath, AuditContext context);
    }

    /// <summary>Проверка объекта иерархии: объект сцены или узел префаба/модели.</summary>
    public interface IGameObjectAuditCheck
    {
        void CheckGameObject(GameObject gameObject, AuditObjectSource source, AuditContext context);
    }

    /// <summary>Проверка сцены целиком (настройки окружения, свет). Вызывается только в охвате «сцена».</summary>
    public interface ISceneAuditCheck
    {
        void CheckScene(Scene scene, string scenePath, AuditContext context);
    }

    /// <summary>Итог по накопленному за прогон (суммарные бюджеты охвата).</summary>
    public interface IAuditFinishCheck
    {
        void Finish(AuditContext context);
    }

    /// <summary>Откуда объект: ассет (префаб/модель или сцена), путь в иерархии, ближайший префаб.</summary>
    public readonly struct AuditObjectSource
    {
        public AuditObjectSource(string assetPath, string hierarchyPath, bool inScene, string prefabPath)
        {
            AssetPath = assetPath;
            HierarchyPath = hierarchyPath;
            InScene = inScene;
            PrefabPath = prefabPath;
        }

        /// <summary>Префаб/модель, где лежит объект, или путь сцены.</summary>
        public string AssetPath { get; }

        /// <summary>Путь в иерархии от корня: <c>Environment/Tower/LOD3</c>.</summary>
        public string HierarchyPath { get; }

        public bool InScene { get; }

        /// <summary>Ближайший префаб-источник (для объекта сцены — его префаб, если есть), иначе <see cref="AssetPath"/>.</summary>
        public string PrefabPath { get; }

        /// <summary>Ключ группировки одинаковых экземпляров: префаб, а для объектов без префаба — сцена + путь.</summary>
        public string GroupAsset => string.IsNullOrEmpty(PrefabPath) ? AssetPath : PrefabPath;
    }
}
