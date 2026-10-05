using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Чтение каталога и области операции без создания превью и записи ассетов.</summary>
    internal static class ArsenalEditorStatus
    {
        public static T[] Assets<T>(string folder) where T : UnityEngine.Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<T>).Where(a => a != null).ToArray();

        public static string[] Prefabs(IEnumerable<WeaponInfo> weapons) => weapons.Where(w => w != null)
            .SelectMany(w => new[] { w.WeaponPrefab, w.MagazinePrefab }).Where(p => p != null)
            .Select(AssetDatabase.GetAssetPath).Distinct().ToArray();

        public static string Capacity(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var wall = root != null ? root.GetComponent<ArsenalWallController>() : null;
            if (wall == null) return "Станция отсутствует";
            return $"{wall.Slots.Count} слотов: панель {wall.Slots.Count(s => s.PresentationZone == ArsenalPresentationZone.Pegboard)}, полка {wall.Slots.Count(s => s.PresentationZone == ArsenalPresentationZone.Shelf)}";
        }

        public static string ValidateEntries(IList<ArsenalPreset.Entry> entries)
        {
            if (entries.Count == 0) return "Ассортимент пуст.";
            if (entries.Any(e => e.Weapon == null || e.Weapon.WeaponPrefab == null || string.IsNullOrWhiteSpace(e.Weapon.WeaponId)))
                return "Каждой строке нужны данные, ID и префаб оружия.";
            if (entries.Any(e => !Enum.IsDefined(typeof(ArsenalPresentationZone), e.Zone))) return "В ассортименте есть неизвестная зона.";
            if (entries.Any(e => e.Weapon.MagazinePrefab == null)) return "Каждому оружию нужен префаб магазина.";
            if (entries.Select(e => e.Weapon.WeaponId).Distinct().Count() != entries.Count) return "ID оружия повторяются.";
            if (entries.Select(e => e.Weapon.WeaponPrefab).Distinct().Count() != entries.Count) return "Префабы оружия повторяются.";
            return null;
        }

        public static string ValidatePreset(ArsenalPreset preset, IList<ArsenalPreset.Entry> entries = null)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.PresetId)) return "Ассортименту нужен постоянный ID.";
            var values = entries ?? preset.Entries.ToArray();
            string invalid = ValidateEntries(values);
            if (invalid != null || preset.PresentationStyle == null) return invalid;
            try
            {
                foreach (var entry in values) ArsenalPresentationResolver.Resolve(entry.Weapon, entry.Zone, preset.PresentationStyle);
                return null;
            }
            catch (InvalidOperationException exception) { return "Недопустимый presentation style: " + exception.Message; }
        }

        public static string CapacityProblem(IEnumerable<ArsenalPreset.Entry> entries, MapData map)
        {
            if (map == null) return "Выберите карту.";
            string path = map.sceneName == "Lobby" ? EditorTools.ArsenalPresetAssetBuilder.DemoPath : EditorTools.ArsenalPresetAssetBuilder.CommonPath;
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var wall = root != null ? root.GetComponent<ArsenalWallController>() : null;
            if (wall == null) return "Нет станции для проверки вместимости: " + path;
            if (wall.Slots.Any(s => s == null) || wall.Slots.Distinct().Count() != wall.Slots.Count)
                return "У станции пустые или повторные ссылки слотов: " + path;
            foreach (ArsenalPresentationZone zone in Enum.GetValues(typeof(ArsenalPresentationZone)))
                if (entries.Count(e => e.Zone == zone) > wall.Slots.Count(s => s.PresentationZone == zone))
                    return "Не хватает слотов зоны " + zone + " для карты " + map.displayName;
            return null;
        }

        public static string Report(IEnumerable<WeaponInfo> weapons)
        {
            var rows = new List<string>();
            var ids = new HashSet<string>();
            var networkIds = new Dictionary<uint, GameObject>();
            var managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Managers/--- MANAGERS ---.prefab");
            var manager = managerPrefab != null ? managerPrefab.GetComponentInChildren<NetworkManager>(true) : null;
            foreach (var w in weapons)
            {
                var problems = new List<string>();
                if (string.IsNullOrWhiteSpace(w.WeaponId) || !ids.Add(w.WeaponId)) problems.Add("пустой/повторный ID");
                foreach (var prefab in new[] { w.WeaponPrefab, w.MagazinePrefab })
                {
                    if (prefab == null) { problems.Add("отсутствует префаб"); continue; }
                    var identity = prefab.GetComponent<NetworkIdentity>();
                    if (identity == null || identity.assetId == 0) problems.Add(prefab.name + ": нет сетевого ID");
                    else if (networkIds.TryGetValue(identity.assetId, out var previous) && previous != prefab) problems.Add(prefab.name + ": повторный сетевой ID");
                    else networkIds[identity.assetId] = prefab;
                    if (manager == null || !manager.spawnPrefabs.Contains(prefab)) problems.Add(prefab.name + ": отсутствует в каноническом spawnPrefabs");
                    var body = prefab.GetComponent<Rigidbody>();
                    if (body == null || body.collisionDetectionMode == CollisionDetectionMode.Discrete) problems.Add(prefab.name + ": физика требует проверки");
                    if (!prefab.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger)) problems.Add(prefab.name + ": нет твёрдого коллайдера");
                    if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c == null)) problems.Add(prefab.name + ": отсутствующий скрипт");
                }
                rows.Add(w.DisplayName + " [" + w.WeaponId + "]: " + (problems.Count == 0 ? "ссылки/ID/коллайдеры OK" : string.Join("; ", problems)));
            }
            return string.Join("\n", rows) + "\nЭто структурный отчёт; поведение в шлеме и все требования оружейных тестов он не проверяет.";
        }

        public static Dictionary<string, string> Snapshot(IEnumerable<string> paths)
        {
            return ArsenalFileSnapshot.Read(Directory.GetParent(Application.dataPath).FullName, paths.ToArray(), CancellationToken.None);
        }

        // Один native-вызов может быть длинным; окно распределяет отдельные входы по Editor update.
        public static string[] DependenciesForInput(string input) => Dependencies(new[] { input });

        public static string[] Dependencies(IEnumerable<string> inputs)
        {
            var paths = inputs.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            var assets = paths.SelectMany(p => AssetDatabase.IsValidFolder(p)
                ? AssetDatabase.FindAssets("", new[] { p }).Select(AssetDatabase.GUIDToAssetPath) : new[] { p })
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p))).Distinct().ToArray();
            return paths.Concat(assets.Length == 0 ? Array.Empty<string>() : AssetDatabase.GetDependencies(assets, true)).Distinct().ToArray();
        }

        public static void RequireScene(Scene scene, string path)
        {
            var active = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != path || active.handle != scene.handle || active.path != path)
                throw new InvalidOperationException("Контекст сцены изменился после подготовки плана. Подготовьте план снова.");
        }
        public static string Diff(Dictionary<string, string> before, Dictionary<string, string> after) =>
            string.Join("\n", before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Where(p => !before.TryGetValue(p, out var b) || !after.TryGetValue(p, out var a) || a != b).OrderBy(p => p, StringComparer.Ordinal));

        public static bool DirtyScene => Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty);
    }
}
