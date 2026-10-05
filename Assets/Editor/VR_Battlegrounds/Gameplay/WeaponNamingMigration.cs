using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Перенос трёх Hands-моделей без изменения сетевой идентичности, баланса и совместимости карманов.</summary>
    public static class WeaponNamingMigration
    {
        private sealed class Rename
        {
            public readonly string OldFolder, Folder, OldName, Name, Guid;
            public Rename(string oldFolder, string folder, string oldName, string name, string guid)
            { OldFolder = oldFolder; Folder = folder; OldName = oldName; Name = name; Guid = guid; }
            public string Old => $"Assets/Prefabs/Weapons/{OldFolder}/{OldName}.prefab";
            public string Intermediate => $"Assets/Prefabs/Weapons/{Folder}/{OldName}.prefab";
            public string Target => $"Assets/Prefabs/Weapons/{Folder}/{Name}.prefab";
        }

        private sealed class Snapshot
        {
            public Rename Rename;
            public long LocalId;
            public uint NetworkId;
            public string[] Objects;
        }

        private static readonly Rename[] Renames = {
            new Rename("GunReal", "BrowningHiPower", "Gun_real", "BrowningHiPower", "a89abddac5809dc47a6715d7ebeab24e"),
            new Rename("GunReal", "BrowningHiPower", "Gun_real_mag", "BrowningHiPower_Magazine", "7cc1b24032fe06543b2b7edf8c18c8b4"),
            new Rename("M16", "AR15", "M16_Rifle_prefab", "AR15", "cc00fcab78d7cee4faad59a02b2b7183"),
            new Rename("M16", "AR15", "M16_Magazine", "AR15_Magazine", "b36fa7fe2791fba44aa22d7da2dfd50b"),
            new Rename("ShotgunReal", "FabarmSDASS", "Shotgun_real", "FabarmSDASS", "d586c5e550bf0ee4a9619d4bd368ca54"),
            new Rename("ShotgunReal", "FabarmSDASS", "Shotgun_real_mag", "FabarmSDASS_Ammo", "4c0598d66f467ea4690a960b022f58da")
        };

        private static void ApplyMenu() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenMaintenance(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Catalog);

        /// <summary>Область поддержанного переноса: прежние/новые пути и шесть отображаемых названий.</summary>
        public static string[] AffectedPaths => Renames.Where((r, i) => i % 2 == 0)
            .SelectMany(r => new[] { $"Assets/Prefabs/Weapons/{r.OldFolder}", $"Assets/Prefabs/Weapons/{r.Folder}",
                $"Assets/Art/Weapons/HandsPack/{r.OldName}", $"Assets/Art/Weapons/HandsPack/{r.Name}",
                $"Assets/Art/Weapons/Interaction/{r.OldName}", $"Assets/Art/Weapons/Interaction/{r.Name}" })
            .Concat(new[] { "Assets/Data/Weapons/Gun_Weapon.asset", "Assets/Data/Weapons/M16_Weapon.asset",
                "Assets/Data/Weapons/ShotgunReal_Weapon.asset", "Assets/Data/Weapons/Herrington_Weapon.asset",
                "Assets/Data/Weapons/SRM12_Weapon.asset", "Assets/Data/Weapons/TR15_Weapon.asset" }).Distinct().ToArray();

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Имена оружия нельзя переносить в Play Mode.");

            // Проверяем все исходные/целевые пути до первого MoveAsset. Частичный перенос имеет ровно один путь каждого GUID.
            var folderMoves = Renames.Where((r, i) => i % 2 == 0)
                .Select(r => ($"Assets/Prefabs/Weapons/{r.OldFolder}", $"Assets/Prefabs/Weapons/{r.Folder}"))
                .Concat(Renames.Where((r, i) => i % 2 == 0).SelectMany(r => new[] {
                    ($"Assets/Art/Weapons/HandsPack/{r.OldName}", $"Assets/Art/Weapons/HandsPack/{r.Name}"),
                    ($"Assets/Art/Weapons/Interaction/{r.OldName}", $"Assets/Art/Weapons/Interaction/{r.Name}")
                })).ToArray();
            foreach (var move in folderMoves) PreflightFolder(move.Item1, move.Item2);
            foreach (var r in Renames)
            {
                string path = Resolve(r.Old, r.Intermediate, r.Target);
                if (AssetDatabase.AssetPathToGUID(path) != r.Guid)
                    throw new InvalidOperationException("Неожиданный GUID: " + path);
                Required<GameObject>(path);
                // Immutable baseline остаётся на старом пути; новый builder использует явную таблицу.
                Required<GameObject>("Assets/Art/Weapons/HandsPack/LegacyBuildDonors/" + r.OldName + ".prefab");
            }
            var before = Renames.Select(r => Capture(r, Resolve(r.Old, r.Intermediate, r.Target))).ToArray();
            var infoAssets = AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" })
                .Select(g => Required<WeaponInfo>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            var infoLinks = infoAssets.Select(InfoLinks).ToArray();
            string mapLinks = MapLinks();
            string spawnLinks = SpawnLinks();
            var display = new[] {
                (Required<WeaponInfo>("Assets/Data/Weapons/Gun_Weapon.asset"), "Browning Hi-Power"),
                (Required<WeaponInfo>("Assets/Data/Weapons/M16_Weapon.asset"), "AR-15"),
                (Required<WeaponInfo>("Assets/Data/Weapons/ShotgunReal_Weapon.asset"), "FABARM SDASS"),
                (Required<WeaponInfo>("Assets/Data/Weapons/Herrington_Weapon.asset"), "Remington 11-87"),
                (Required<WeaponInfo>("Assets/Data/Weapons/SRM12_Weapon.asset"), "Desert Tech SRS"),
                (Required<WeaponInfo>("Assets/Data/Weapons/TR15_Weapon.asset"), "AR-15 с глушителем")
            };
            // Только первые три WeaponInfo участвуют в переносе prefab; остальные меняют лишь отображаемое имя.
            for (int i = 0; i < 3; i++)
                if (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(display[i].Item1.WeaponPrefab)) != Renames[i * 2].Guid)
                    throw new InvalidOperationException("WeaponInfo ссылается на другую модель: " + display[i].Item1.name);

            foreach (var move in folderMoves) MoveFolder(move.Item1, move.Item2);
            foreach (var r in Renames)
            {
                if (AssetDatabase.AssetPathToGUID(r.Target) != r.Guid) Move(Resolve(r.Intermediate, r.Target), r.Target);
                RenameRoot(r.Target, r.Name);
            }
            foreach (var item in display)
            {
                var serialized = new SerializedObject(item.Item1);
                serialized.FindProperty("_displayName").stringValue = item.Item2;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item.Item1);
            }
            AssetDatabase.SaveAssets();
            foreach (var expected in before)
            {
                var actual = Capture(expected.Rename, expected.Rename.Target);
                if (actual.LocalId != expected.LocalId || actual.NetworkId != expected.NetworkId || !actual.Objects.SequenceEqual(expected.Objects))
                    throw new InvalidOperationException("Изменена идентичность ассета: " + expected.Rename.Target);
            }
            if (!infoAssets.Select(InfoLinks).SequenceEqual(infoLinks) || MapLinks() != mapLinks || SpawnLinks() != spawnLinks)
                throw new InvalidOperationException("Перенос изменил WeaponInfo/MapData/preset/spawnPrefabs ссылки.");
            GameLog.WeaponSystem.Info("Имена Hands: Browning Hi-Power, AR-15, FABARM SDASS; GUID/SDK/network IDs, WeaponId, баланс и ссылки сохранены.");
            return "Шесть префабов и motion/interaction папки перенесены; идентичность и ссылки проверены.";
        }

        private static Snapshot Capture(Rename r, string path)
        {
            var prefab = Required<GameObject>(path);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab, out string guid, out long localId) || guid != r.Guid)
                throw new InvalidOperationException("Не подтверждён GUID/localFileID: " + path);
            var identity = prefab.GetComponent<NetworkIdentity>();
            if (identity == null) throw new InvalidOperationException("Нет NetworkIdentity: " + path);
            uint networkId = (uint)new SerializedObject(identity).FindProperty("_assetId").longValue;
            if (networkId == 0) throw new InvalidOperationException("Нет network assetId: " + path);
            // Проверяем все вложенные GO/components, а не только root localFileID. SDK ID/tag также не должны поменяться.
            var objects = new List<string>();
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                foreach (var obj in new UnityEngine.Object[] { transform.gameObject }.Concat(transform.GetComponents<Component>()))
                {
                    if (obj == null) throw new InvalidOperationException("Missing script: " + path);
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string objectGuid, out long objectId))
                        throw new InvalidOperationException("Нет localFileID: " + obj.name);
                    var serialized = new SerializedObject(obj);
                    var id = serialized.FindProperty("_uxrUniqueId");
                    var tag = serialized.FindProperty("_tag");
                    objects.Add(objectGuid + "/" + objectId + "/" + obj.GetType().FullName + "/" + (id?.stringValue ?? "") + "/" + (tag?.stringValue ?? ""));
                }
            return new Snapshot { Rename = r, LocalId = localId, NetworkId = networkId, Objects = objects.OrderBy(x => x, StringComparer.Ordinal).ToArray() };
        }

        private static string InfoLinks(WeaponInfo info) => Reference(info) + ":" + info.WeaponId + ":" + Reference(info.WeaponPrefab) + ":" + Reference(info.MagazinePrefab);

        private static string MapLinks()
        {
            var registry = Required<MapRegistry>("Assets/Data/Maps/MapRegistry.asset");
            var links = new List<string> { Reference(registry.lobby) };
            foreach (var map in registry.maps)
            {
                links.Add(Reference(map));
                if (map == null) continue;
                links.Add(map.sceneName + ":" + Reference(map.arsenalPreset));
                if (map.arsenalPreset != null)
                    links.AddRange(map.arsenalPreset.Entries.Select(e => Reference(e.Weapon) + ":" + (int)e.Zone));
            }
            return string.Join("|", links);
        }

        private static string SpawnLinks()
        {
            var root = Required<GameObject>("Assets/Prefabs/Managers/--- MANAGERS ---.prefab");
            var manager = root.GetComponentsInChildren<NetworkManager>(true).Single();
            return string.Join("|", manager.spawnPrefabs.Select(Reference));
        }

        private static string Reference(UnityEngine.Object obj)
        {
            if (obj == null) return "null";
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long id))
                throw new InvalidOperationException("Ссылка без GUID/localFileID: " + obj.name);
            return guid + "/" + id;
        }

        private static string Resolve(params string[] candidates)
        {
            foreach (string path in candidates.Distinct())
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) && (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta")))
                    throw new InvalidOperationException("Неимпортированный конфликт пути: " + path);
            var existing = candidates.Distinct().Where(p => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p))).ToArray();
            if (existing.Length != 1) throw new InvalidOperationException("Ожидался один путь: " + string.Join(", ", candidates));
            return existing[0];
        }

        private static void PreflightFolder(string source, string target)
        {
            string path = Resolve(source, target);
            if (!AssetDatabase.IsValidFolder(path)) throw new InvalidOperationException("Не папка: " + path);
            string absent = path == source ? target : source;
            if (Directory.Exists(absent) || File.Exists(absent) || File.Exists(absent + ".meta"))
                throw new InvalidOperationException("Неимпортированный конфликт пути: " + absent);
        }

        private static void MoveFolder(string source, string target)
        {
            if (AssetDatabase.IsValidFolder(target)) return;
            string guid = AssetDatabase.AssetPathToGUID(source);
            Move(source, target);
            if (AssetDatabase.AssetPathToGUID(target) != guid) throw new InvalidOperationException("Изменён GUID папки: " + target);
        }

        private static void Move(string source, string target)
        {
            if (source == target) return;
            string guid = AssetDatabase.AssetPathToGUID(source);
            string error = AssetDatabase.MoveAsset(source, target);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(source + " → " + target + ": " + error);
            if (AssetDatabase.AssetPathToGUID(target) != guid) throw new InvalidOperationException("Изменён GUID: " + target);
        }

        private static void RenameRoot(string path, string name)
        {
            var asset = Required<GameObject>(path);
            if (asset.name == name) return;
            // Сохраняем существующий persistent prefab, не пересоздавая его по совпадению имён дочерних объектов.
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_Name").stringValue = name;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            if (PrefabUtility.SavePrefabAsset(asset) == null)
                throw new InvalidOperationException("Не сохранено новое имя корня: " + path);
        }

        private static T Required<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path)
            ?? throw new InvalidOperationException("Нет ассета: " + path);
    }
}
