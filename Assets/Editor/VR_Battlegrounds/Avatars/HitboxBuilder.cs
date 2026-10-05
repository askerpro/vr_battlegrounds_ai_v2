using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Хитбоксы игроков и маска пуль (T-36). Генерация, а не ручная настройка: всё задано здесь,
    /// повторная сборка приводит аватары и оружие к этому виду.
    ///
    /// <list type="bullet">
    /// <item><b>Хитбоксы</b> — по скелету UltimateXR (<c>UxrAvatar.AvatarRig</c>), поэтому подходят любому
    ///       аватару: голова (сфера), торс (низ и верх), плечо и предплечье обеих рук, бедро и голень
    ///       обеих ног (если ноги в скелете есть). Дочерние объекты костей <c>Hitbox_*</c>, компонент
    ///       <see cref="Hitbox"/> с частью тела, слой <see cref="HitLayers.HitboxLayerName"/>. Прежние
    ///       сплошные коллайдеры аватара снимаются.</item>
    /// <item><b>Где правится.</b> В базе варианта внутри <c>Prefabs/Player</c> (не <c>PlayerBase*</c>):
    ///       цветные и прочие варианты наследуют хитбоксы.</item>
    /// <item><b>Маска пуль</b> <see cref="HitLayers.ProjectileMask"/> — во все <c>UxrShotDescriptor</c> оружия
    ///       <c>Prefabs/Weapons</c>.</item>
    /// <item><b>Физика:</b> слой хитбоксов ни с чем не сталкивается — он только для луча пули
    ///       (<see cref="HitLayers.ConfigureLayerCollisions"/>).</item>
    /// <item>Матрица слоя трупа пересчитывается, призрак и трупы пересобираются — они снимают или
    ///       повторяют хитбоксы.</item>
    /// </list>
    /// Проверка — <c>HitboxTests</c>.
    /// </summary>
    public static class HitboxBuilder
    {
        private const string PlayerPrefabs = "Assets/Prefabs/Player/";
        private const string WeaponsFolder = "Assets/Prefabs/Weapons";
        private const string RegistryPath = "Assets/Data/Player/Avatars/AvatarsRegistry.asset";

        public static void Build()
        {
            EnsureLayer(HitLayers.HitboxLayerName);

            foreach (string path in OwnerPrefabs()) BuildFor(path);
            ApplyProjectileMask();
            AssetDatabase.SaveAssets();

            // Призрак снимает хитбоксы, трупы повторяют иерархию модели — пересобрать.
            GhostAvatarBuilder.Build();
            CorpseBuilder.Build();
            CorpsePhysics.ConfigureLayerCollisions();
            HitLayers.ConfigureLayerCollisions();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Добавляет пользовательский слой, если его нет. Общий для сборщиков.</summary>
        public static void EnsureLayer(string name)
        {
            if (LayerMask.NameToLayer(name) >= 0) return;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;
                layer.stringValue = name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                VrBattlegrounds.Core.GameLog.Player.Info($"[HitboxBuilder] Добавлен слой '{name}' ({i}).");
                return;
            }
            VrBattlegrounds.Core.GameLog.Player.Error($"[HitboxBuilder] Нет свободного слоя для '{name}'.");
        }

        /// <summary>Базы вариантов аватаров реестра внутри Prefabs/Player (не PlayerBase*).</summary>
        private static List<string> OwnerPrefabs()
        {
            var registry = AssetDatabase.LoadAssetAtPath<AvatarRegistry>(RegistryPath);
            var owners = new List<string>();
            foreach (AvatarData data in registry.avatars)
            {
                if (data == null || data.prefab == null) continue;

                string owner = AssetDatabase.GetAssetPath(data.prefab);
                for (GameObject p = PrefabUtility.GetCorrespondingObjectFromSource(data.prefab); p != null; p = PrefabUtility.GetCorrespondingObjectFromSource(p))
                {
                    string path = AssetDatabase.GetAssetPath(p);
                    if (!path.StartsWith(PlayerPrefabs) || System.IO.Path.GetFileName(path).StartsWith("PlayerBase")) break;
                    owner = path;
                }
                if (!owners.Contains(owner)) owners.Add(owner);
            }
            return owners;
        }

        /// <summary>Только хитбоксы переданного аватара, без projectile masks, ghost и corpses.</summary>
        public static void BuildFor(string path)
        {
            if (LayerMask.NameToLayer("Hitbox") < 0) throw new System.InvalidOperationException("Слой Hitbox отсутствует; сначала выполните явную системную настройку слоёв.");
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                UxrAvatar avatar = root.GetComponent<UxrAvatar>();
                if (avatar == null || avatar.AvatarRig?.Head?.Head == null)
                    throw new System.InvalidOperationException("Не найден UxrAvatar с головной костью.");

                RemoveOld(root);

                var rig = avatar.AvatarRig;
                Transform neck = rig.Head.Neck != null ? rig.Head.Neck : rig.Head.Head;
                Transform chest = rig.UpperChest != null ? rig.UpperChest : rig.Chest != null ? rig.Chest : rig.Spine;
                Transform hips = rig.Hips != null ? rig.Hips : rig.Spine;
                Transform lowerTarget = rig.Chest != null ? rig.Chest : chest;

                Sphere(rig.Head.Head, HitZone.Head, "Head", 0.12f, 0.07f);
                Capsule(hips, lowerTarget, HitZone.Torso, "Torso_Lower", 0.15f);
                Capsule(chest, neck, HitZone.Torso, "Torso_Upper", 0.16f);

                Capsule(rig.LeftArm.UpperArm, rig.LeftArm.Forearm, HitZone.Arm, "UpperArm_L", 0.055f);
                Capsule(rig.RightArm.UpperArm, rig.RightArm.Forearm, HitZone.Arm, "UpperArm_R", 0.055f);
                Capsule(rig.LeftArm.Forearm, rig.LeftArm.Hand.Wrist, HitZone.Arm, "Forearm_L", 0.05f);
                Capsule(rig.RightArm.Forearm, rig.RightArm.Hand.Wrist, HitZone.Arm, "Forearm_R", 0.05f);

                Capsule(rig.LeftLeg.UpperLeg, rig.LeftLeg.LowerLeg, HitZone.Leg, "Thigh_L", 0.08f);
                Capsule(rig.RightLeg.UpperLeg, rig.RightLeg.LowerLeg, HitZone.Leg, "Thigh_R", 0.08f);
                Capsule(rig.LeftLeg.LowerLeg, rig.LeftLeg.Foot, HitZone.Leg, "Calf_L", 0.065f);
                Capsule(rig.RightLeg.LowerLeg, rig.RightLeg.Foot, HitZone.Leg, "Calf_R", 0.065f);

                Workbench.AvatarScopedActions.SetCanonicalAssetId(root, path);
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new System.InvalidOperationException("Хитбоксы не сохранены: " + path);
                VrBattlegrounds.Core.GameLog.Player.Info($"[HitboxBuilder] {path}: хитбоксов {root.GetComponentsInChildren<Hitbox>(true).Length}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Прежние хитбоксы: сгенерированные и ручные (объекты Hitbox* из одного коллайдера) и сплошные коллайдеры мешей.</summary>
        private static void RemoveOld(GameObject root)
        {
            foreach (Hitbox h in root.GetComponentsInChildren<Hitbox>(true))
                if (h != null) Object.DestroyImmediate(h.gameObject);

            foreach (Collider c in root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToList())
            {
                if (c == null) continue;
                GameObject go = c.gameObject;
                bool bareHitbox = go.name.StartsWith("Hitbox") && go.GetComponents<Component>().Length == 2 && go.transform.childCount == 0;
                if (bareHitbox) Object.DestroyImmediate(go);
                else Object.DestroyImmediate(c);
            }
        }

        private static GameObject Part(Transform bone, HitZone part, string name)
        {
            var go = new GameObject($"Hitbox_{name}");
            go.transform.SetParent(bone, false);
            go.layer = HitLayers.HitboxLayer;

            Hitbox hitbox = go.AddComponent<Hitbox>();
            var so = new SerializedObject(hitbox);
            so.FindProperty("_part").enumValueIndex = (int)part;
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        private static void Sphere(Transform bone, HitZone part, string name, float radius, float up)
        {
            if (bone == null) return;
            float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
            var sphere = Part(bone, part, name).AddComponent<SphereCollider>();
            sphere.radius = radius / scale;
            sphere.center = bone.InverseTransformDirection(Vector3.up) * (up / scale);
        }

        private static void Capsule(Transform bone, Transform toward, HitZone part, string name, float radius)
        {
            if (bone == null || toward == null) return;
            float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
            Vector3 local = bone.InverseTransformPoint(toward.position);
            Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            int axis = abs.x >= abs.y && abs.x >= abs.z ? 0 : abs.y >= abs.z ? 1 : 2;

            var capsule = Part(bone, part, name).AddComponent<CapsuleCollider>();
            capsule.direction = axis;
            capsule.radius = radius / scale;
            capsule.height = abs[axis] + 2f * capsule.radius;
            capsule.center = local * 0.5f;
        }

        private static void ApplyProjectileMask()
        {
            int mask = HitLayers.ProjectileMask;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { WeaponsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool changed = false;

                foreach (UxrProjectileSource source in root.GetComponentsInChildren<UxrProjectileSource>(true))
                {
                    var so = new SerializedObject(source);
                    SerializedProperty shots = so.FindProperty("_shotTypes");
                    for (int i = 0; i < shots.arraySize; i++)
                    {
                        SerializedProperty m = shots.GetArrayElementAtIndex(i).FindPropertyRelative("_collisionLayerMask");
                        if (m.intValue == mask) continue;
                        m.intValue = mask;
                        changed = true;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                if (changed)
                {
                    PrefabUtility.SavePrefabAsset(root);
                    VrBattlegrounds.Core.GameLog.Player.Info($"[HitboxBuilder] {path}: маска пуль → 0x{mask:X}.");
                }
            }
        }
    }
}
