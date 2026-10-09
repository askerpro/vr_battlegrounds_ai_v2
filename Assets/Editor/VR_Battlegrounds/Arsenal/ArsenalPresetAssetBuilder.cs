using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Пути ассетов станций и пресетов арсенала и регистрация сетевых префабов каталога оружия.</summary>
    public static class ArsenalPresetAssetBuilder
    {
        public const string CommonPath = "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab";
        public const string DemoPath = "Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab";
        public const string GameplayPath = "Assets/Data/Weapons/CurrentGameplayArsenal.asset";
        public const string FullPath = "Assets/Data/Weapons/FullDemoArsenal.asset";

        public static string RegisterNetworkPrefabs(WeaponInfo[] weapons)
        {
            const string path = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";
            CheckEditor();
            if (weapons == null || weapons.Length == 0 || weapons.Any(w => w == null || w.WeaponPrefab == null || w.MagazinePrefab == null))
                throw new InvalidOperationException("Сетевой каталог требует WeaponInfo, оружие и магазин каждой записи.");
            var prefabs = weapons.SelectMany(w => new[] { w.WeaponPrefab, w.MagazinePrefab }).Distinct().ToArray();
            var ids = new HashSet<uint>();
            var guids = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            void Validate(GameObject prefab)
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
                if (string.IsNullOrEmpty(guid) || guids.TryGetValue(guid, out var previous) && previous != prefab)
                    throw new InvalidOperationException("Пустой или повторный GUID сетевого префаба: " + prefab.name);
                guids[guid] = prefab;
                var identity = prefab.GetComponent<NetworkIdentity>();
                if (identity == null || identity.assetId == 0 || !ids.Add(identity.assetId))
                    throw new InvalidOperationException("Отсутствующий NetworkIdentity, пустой или повторный Mirror assetId: " + prefab.name);
            }
            foreach (var prefab in prefabs)
                Validate(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var manager = root.GetComponentsInChildren<NetworkManager>(true).Single();
                ids.Clear();
                guids.Clear();
                if (manager.spawnPrefabs.Any(p => p == null)) throw new InvalidOperationException("Пустая ссылка в каноническом spawnPrefabs.");
                foreach (var prefab in manager.spawnPrefabs.Concat(prefabs).Where(p => p != null).Distinct())
                    Validate(prefab);
                int added = 0;
                foreach (var prefab in prefabs)
                    if (!manager.spawnPrefabs.Contains(prefab)) { manager.spawnPrefabs.Add(prefab); added++; }
                if (added > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"Сетевая регистрация: проверено {prefabs.Length} префабов выбранного каталога и {ids.Count} уникальных сетевых префабов менеджера; добавлено {added}; {path}.";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void CheckEditor() { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Редактор в Play Mode."); }
    }
}
