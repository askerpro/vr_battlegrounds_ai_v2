using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Перевод старых моделей на Embedded с сохранением назначенных материалов.</summary>
    public static class ModelMaterialLocationMigration
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Assets/Migrate Legacy Model Materials";
        private static readonly Queue<string> Pending = new Queue<string>();
        private static readonly List<string> Results = new List<string>();
        private static int total;
        private static int migrated;
        private static string failure;

        public static bool IsRunning => Pending.Count > 0 && failure == null;
        public static string Status => $"Обработано {Results.Count}/{total}, исправлено {migrated}, ошибка: {failure ?? "нет"}";

        [MenuItem(MenuPath)]
        public static void BeginAll()
        {
            Begin(AssetDatabase.FindAssets("t:Model", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => (AssetImporter.GetAtPath(path) is ModelImporter importer) && IsLegacy(importer)));
        }

        [MenuItem(MenuPath, true)]
        private static bool CanBegin() => !EditorApplication.isPlayingOrWillChangePlaymode && !IsRunning;

        public static void Begin(IEnumerable<string> paths)
        {
            if (!CanBegin()) throw new InvalidOperationException("Редактор занят или миграция уже выполняется.");
            Pending.Clear();
            Results.Clear();
            migrated = 0;
            failure = null;
            foreach (string path in paths.Distinct()) Pending.Enqueue(path);
            total = Pending.Count;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        /// <summary>External имеет значение 0; имя устаревшего enum не используется, чтобы не создавать CS0618.</summary>
        public static bool IsLegacy(ModelImporter importer) => (int)importer.materialLocation == 0;

        public static string MigrateOne(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Миграция недоступна в Play Mode.");
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new ArgumentException("Не ModelImporter: " + path);
            if (!IsLegacy(importer)) return "SKIP " + path;

            string before = CaptureBindings(path);
            var originalRemaps = importer.GetExternalObjectMap();
            // Legacy искал .mat неявно. Сначала превращаем этот поиск в явные remap-ссылки.
            importer.SearchAndRemapMaterials(importer.materialName, importer.materialSearch);
            // Битый GUID в legacy-remap давал поиск по имени. Не перекрываем найденный .mat пустой ссылкой.
            foreach (var entry in originalRemaps)
                if (entry.Value != null) importer.AddRemap(entry.Key, entry.Value);
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();

            string after = CaptureBindings(path);
            if (before != after)
            {
                // Не оставляем модель с изменёнными материалами при неразрешённом сопоставлении.
                importer = (ModelImporter)AssetImporter.GetAtPath(path);
                foreach (var key in importer.GetExternalObjectMap().Keys.ToArray()) importer.RemoveRemap(key);
                foreach (var entry in originalRemaps) importer.AddRemap(entry.Key, entry.Value);
                importer.materialLocation = (ModelImporterMaterialLocation)0;
                importer.SaveAndReimport();
                throw new InvalidOperationException("Материалы изменились; настройки восстановлены: " + path
                    + "\nДо:\n" + before + "\nПосле:\n" + after);
            }
            return "PASS " + path;
        }

        /// <summary>Слоты каждого Renderer сравниваются по GUID и local file ID, включая явные remap.</summary>
        public static string CaptureBindings(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) throw new InvalidOperationException("Модель не загрузилась: " + path);
            var bindings = new StringBuilder();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bindings.Append(AnimationUtility.CalculateTransformPath(renderer.transform, root.transform));
                bindings.Append('|').Append(renderer.GetType().Name);
                foreach (var material in renderer.sharedMaterials)
                    bindings.Append('|').Append(Identity(material));
                bindings.AppendLine();
            }
            return bindings.ToString();
        }

        private static string Identity(Object obj)
        {
            if (obj == null) return "NULL";
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
                throw new InvalidOperationException("Материал без постоянного идентификатора: " + obj.name);
            return guid + ":" + localId;
        }

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                failure = "Миграция остановлена при входе в Play Mode.";
                Finish();
                return;
            }
            var timer = Stopwatch.StartNew();
            try
            {
                // Ограничиваем работу одного Update, чтобы MCP и окно редактора оставались доступны.
                while (Pending.Count > 0 && timer.ElapsedMilliseconds < 250)
                {
                    string result = MigrateOne(Pending.Dequeue());
                    Results.Add(result);
                    if (result.StartsWith("PASS ", StringComparison.Ordinal)) migrated++;
                }
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
                Finish();
                return;
            }
            if (Pending.Count == 0) Finish();
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            Directory.CreateDirectory("Temp/ModelMaterialMigration");
            File.WriteAllText("Temp/ModelMaterialMigration/result.txt",
                Status + "\n" + string.Join("\n", Results) + "\n" + failure, new UTF8Encoding(false));
            Pending.Clear();
            if (failure == null) GameLog.Debug.Info("[ModelMaterialMigration] " + Status);
            else GameLog.Debug.Error("[ModelMaterialMigration] " + Status);
        }
    }
}
