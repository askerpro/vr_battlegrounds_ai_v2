using System.Collections.Generic;
using System.Linq;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Собирает аватар выбывшего — призрака (T-35) — вариантом киборга и регистрирует его.
    /// Призрак — обычный аватар, отличающийся набором частей: своего типа у него нет, выбывший
    /// игрок определяется по сессии (<c>PlayerSession.IsEliminated</c>). Префаб не правится
    /// руками: всё, чем призрак отличается от киборга, задано здесь, и повторная сборка
    /// приводит его к этому виду.
    ///
    /// <list type="bullet">
    /// <item><b>Снимается снаряжение и игра живого:</b> <c>PlayerLoadoutManager</c>, <c>PocketHaptics</c>,
    ///       <c>MagazineEjectInput</c>, карманы и кобуры (якоря и их
    ///       ручки-прокси), любые хватаемые предметы на теле.</item>
    /// <item><b>Снимаются хитбоксы</b> — все сплошные коллайдеры. Триггер камеры остаётся: по нему
    ///       зона команды видит, что игрок пришёл (возрождение в Elimination).</item>
    /// <item><b>Тело — на материале призрака</b> (<c>Prefabs/Player/Ghost/GhostMaterial.mat</c>);
    ///       цвет команды ставит <see cref="TeamColorTint"/> в рантайме. Старая копия тела
    ///       <c>Cyborg/Ghost</c> удаляется.</item>
    /// <item><b>Вид выбывшего</b> у своего игрока — <see cref="GhostViewEffect"/>.</item>
    /// <item><b>Регистрация:</b> <c>Data/Player/Avatars/Ghost.asset</c> → <c>AvatarRegistry.ghost</c>
    ///       (не список скинов), <c>TeamAvatarStrategy.ghostPrefab</c>, <c>spawnPrefabs</c>.</item>
    /// </list>
    /// Проверка — <c>GhostAvatarTests</c>.
    /// </summary>
    public static class GhostAvatarBuilder
    {
        public const string GhostPath = "Assets/Prefabs/Player/Ghost/GhostAvatar.prefab";

        private const string SourcePath = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        private const string MaterialPath = "Assets/Prefabs/Player/Ghost/GhostMaterial.mat";
        private const string DataPath = "Assets/Data/Player/Avatars/Ghost.asset";
        private const string SourceDataPath = "Assets/Data/Player/Avatars/Cyborg.asset";
        private const string RegistryPath = "Assets/Data/Player/Avatars/AvatarsRegistry.asset";
        private const string StrategyPath = "Assets/Data/Player/Avatars/TeamAvatarStrategy.asset";
        private const string ManagersPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";

        /// <summary>Компоненты живого игрока, которых у призрака нет. Порядок — сначала зависимые.</summary>
        private static readonly string[] LivingOnlyComponents =
        {
            "MagazineEjectInput", "PocketHaptics", "PlayerLoadoutManager",
        };

        [MenuItem("Tools/VR Battlegrounds/Avatars/Build Ghost Avatar")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (source == null || material == null)
            {
                Debug.LogError($"[GhostAvatarBuilder] Нет {SourcePath} или {MaterialPath}.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            GameObject prefab;
            try
            {
                instance.name = "GhostAvatar";
                Strip(instance);
                Paint(instance, material);
                if (instance.GetComponent<TeamColorTint>() == null) instance.AddComponent<TeamColorTint>();
                if (instance.GetComponent<GhostViewEffect>() == null) instance.AddComponent<GhostViewEffect>();

                prefab = PrefabUtility.SaveAsPrefabAsset(instance, GhostPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            Register(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GhostAvatarBuilder] Призрак собран: {GhostPath}.");
        }

        private static void Strip(GameObject root)
        {
            foreach (string typeName in LivingOnlyComponents)
            {
                Component c = root.GetComponent(typeName);
                if (c != null) Object.DestroyImmediate(c);
            }

            // Карманы и кобуры — целиком, с ручками-прокси; прочие хватаемые предметы на теле.
            foreach (UxrGrabbableObjectAnchor anchor in root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                if (anchor != null) Object.DestroyImmediate(anchor.gameObject);
            }
            foreach (UxrGrabbableObject grabbable in root.GetComponentsInChildren<UxrGrabbableObject>(true))
            {
                if (grabbable != null) Object.DestroyImmediate(grabbable.gameObject);
            }

            // Хитбоксы (T-36) — объектами целиком, затем прочие сплошные коллайдеры.
            foreach (Hitbox hitbox in root.GetComponentsInChildren<Hitbox>(true))
            {
                if (hitbox != null) Object.DestroyImmediate(hitbox.gameObject);
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToList())
            {
                Object.DestroyImmediate(collider);
            }

            // Старая копия тела-призрака внутри киборга.
            Transform legacy = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Ghost" && t.parent != null && t.parent.name == "Cyborg");
            if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
        }

        private static void Paint(GameObject root, Material material)
        {
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.GetComponentInParent<UxrHandIntegration>(true) != null) continue;
                r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
            }
        }

        private static void Register(GameObject prefab)
        {
            var data = AssetDatabase.LoadAssetAtPath<AvatarData>(DataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<AvatarData>();
                AssetDatabase.CreateAsset(data, DataPath);
            }
            var sourceData = AssetDatabase.LoadAssetAtPath<AvatarData>(SourceDataPath);
            data.displayName = "Призрак";
            data.icon = sourceData != null ? sourceData.icon : null;
            data.prefab = prefab;
            EditorUtility.SetDirty(data);

            var registry = AssetDatabase.LoadAssetAtPath<AvatarRegistry>(RegistryPath);
            registry.ghost = data;
            EditorUtility.SetDirty(registry);

            var strategy = new SerializedObject(AssetDatabase.LoadAssetAtPath<TeamAvatarStrategy>(StrategyPath));
            strategy.FindProperty("ghostPrefab").objectReferenceValue = prefab;
            strategy.ApplyModifiedPropertiesWithoutUndo();

            GameObject managers = PrefabUtility.LoadPrefabContents(ManagersPath);
            try
            {
                NetworkManager manager = managers.GetComponentInChildren<NetworkManager>(true);
                if (manager != null && !manager.spawnPrefabs.Contains(prefab))
                {
                    manager.spawnPrefabs.Add(prefab);
                    PrefabUtility.SaveAsPrefabAsset(managers, ManagersPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(managers);
            }
        }
    }
}
