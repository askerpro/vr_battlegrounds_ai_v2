using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    /// <summary>Чтение регистрации отдельно от явной синхронизации выбранного manager prefab.</summary>
    public static class AvatarRegistryTools
    {
        public static GameObject[] Prefabs(AvatarRegistry registry) => registry == null ? Array.Empty<GameObject>() :
            (registry.avatars ?? Array.Empty<AvatarData>()).Concat(new[] { registry.ghost })
                .Where(d => d && d.prefab).Select(d => d.prefab).Distinct().ToArray();

        public static string Validate(AvatarRegistry registry, NetworkManager manager)
        {
            if (!registry) return "Выберите AvatarRegistry.";
            var lines = new List<string>();
            foreach (var data in (registry.avatars ?? Array.Empty<AvatarData>()).Concat(new[] { registry.ghost }).Where(d => d))
                if (!data.prefab) lines.Add(data.name + ": отсутствует prefab.");
            foreach (var prefab in Prefabs(registry))
            {
                if (!prefab.GetComponent<NetworkIdentity>()) lines.Add(prefab.name + ": отсутствует NetworkIdentity на корне.");
                if (manager && (manager.spawnPrefabs == null || !manager.spawnPrefabs.Contains(prefab))) lines.Add(prefab.name + ": не зарегистрирован в " + manager.name + ".");
            }
            if (!manager) lines.Add("NetworkManager не выбран; сетевое соответствие не проверено.");
            if (manager && manager.spawnPrefabs != null && manager.spawnPrefabs.Any(p => !p)) lines.Add("NetworkManager содержит пустые spawnPrefabs.");
            return lines.Count == 0 ? "Выбранный реестр соответствует выбранному NetworkManager." : string.Join("\n", lines);
        }

        public static string Synchronize(AvatarRegistry registry, string managerPath)
        {
            var prefabs = Prefabs(registry);
            if (!registry || prefabs.Length == 0 || prefabs.Any(p => !p.GetComponent<NetworkIdentity>()))
                throw new InvalidOperationException("Реестр пуст либо у одного из аватаров отсутствует NetworkIdentity.");
            if (!managerPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Выберите manager prefab.");
            var root = PrefabUtility.LoadPrefabContents(managerPath);
            try
            {
                var managers = root.GetComponentsInChildren<NetworkManager>(true);
                if (managers.Length != 1) throw new InvalidOperationException("В выбранном prefab должен быть ровно один NetworkManager.");
                var manager = managers[0];
                if (manager.spawnPrefabs == null) manager.spawnPrefabs = new List<GameObject>();
                int added = 0;
                foreach (var prefab in prefabs) if (!manager.spawnPrefabs.Contains(prefab)) { manager.spawnPrefabs.Add(prefab); added++; }
                if (added > 0 && !PrefabUtility.SaveAsPrefabAsset(root, managerPath)) throw new InvalidOperationException("Manager prefab не сохранён.");
                return $"{managerPath}: добавлено аватаров {added}.";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
