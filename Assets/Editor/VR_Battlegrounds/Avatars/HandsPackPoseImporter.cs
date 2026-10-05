using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Поза хвата из кадра клипа пака и точка хвата оружия, которой она нужна.</summary>
    public sealed class HandsPackPoseRecipe
    {
        public string      Pose;      // имя ассета и позы — по нему SDK ищет позу у аватара
        public string      Folder;    // папка пака в Modelas/
        public string      Mesh;      // меш рук в Modelas/<Folder>/Mesh/
        public string      Clip;      // имя клипа в Modelas/<Folder>/Animations (файлы названы не единообразно)
        public float       Time;
        public UxrHandSide PackSide;  // рука пака; вторая рука позы — её зеркало
        public string      BodyMesh;  // корпус оружия в паке…
        public string      BodyBone;  // …его кость…
        public string      Weapon;
        public string      BodyPath;  // …и тот же меш в префабе оружия
        public int         GrabPoint;
        public string      PoseFolder; // папка ассета позы; null — HandsPackPoseImporter.PoseFolder
    }

    /// <summary>
    /// Позы хвата из пака Hands Weapons Animations: снимает кадр (<see cref="HandsPackPoseExtractor" />),
    /// кладёт позу в <see cref="PoseFolder" />, регистрирует на <c>PlayerBase_NonSdkHands</c> — аватары с кистью не от SDK
    /// наследуют позы по цепочке префабов — и назначает точке хвата оружия вместе с трансформами
    /// выравнивания, которые кладут оружие в ладонь как в паке (<see cref="HandsPackGripAligner" />).
    ///
    /// <para>
    /// Трансформы выравнивания — свои для позы и аватара (<c>Grabs/HandsPack/&lt;поза&gt;/&lt;аватар&gt;/Left|Right</c>):
    /// общие <c>Grabs/default</c> использует запись по умолчанию, то есть остальные аватары.
    /// </para>
    ///
    /// <para>Проверка — <c>HandsPackHandPoseTests</c>. Новая поза — рецепт в <see cref="Recipes" /> и кейс в тесте.</para>
    /// </summary>
    public static class HandsPackPoseImporter
    {
        public const string PoseFolder = "Assets/Art/HandPoses/HandsPack";

        private const string Pack       = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/";
        private const string PoseBase   = "Assets/Prefabs/Player/PlayerBase_NonSdkHands.prefab";

        /// <summary>Аватары, чьим записям хвата на оружии назначаются позы пака.</summary>
        private static readonly string[] GripAvatars = { "Assets/Prefabs/Player/MEF_Base_Avatar.prefab" };

        public static readonly HandsPackPoseRecipe[] Recipes = new[]
        {
            new HandsPackPoseRecipe
            {
                Pose = "HandsPack_Gun_Grip", Folder = "Hands_Gun", Mesh = "Gun_Mesh", Clip = "Aiming_Idle", Time = 0f,
                PackSide = UxrHandSide.Right, BodyMesh = "Base_mesh", BodyBone = "Bn_Base",
                Weapon = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower.prefab", BodyPath = "MeshContainer/Base", GrabPoint = 0
            },
            new HandsPackPoseRecipe
            {
                Pose = "HandsPack_Gun_Support", Folder = "Hands_Gun", Mesh = "Gun_Mesh", Clip = "Aiming_Idle", Time = 0f,
                PackSide = UxrHandSide.Left, BodyMesh = "Base_mesh", BodyBone = "Bn_Base",
                Weapon = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower.prefab", BodyPath = "MeshContainer/Base", GrabPoint = 1
            }
        }.Concat(WeaponPair("Scar", "Hands_Automatic_Rifle01", "Hands_Automatic_Rifle01", "Aiming_Idle", "Scar_Base_mesh", "Bn_Scar_Base"))
         // Uzi — одной рукой: левая рука пака не на оружии, позы поддержки нет.
         .Concat(WeaponPair("Uzi", "Hands_Tommy_gun", "Tommy-Gun_Mesh", "Aiming_Idle", "body_Mesh", "Bn_body", support: false))
         .Concat(WeaponPair("MP5K", "Hands_Automatic_Rifle04", "Hands_Automatic_Rifle04_Mesh", "Idle_Aim", "Rifle04_Body_Mesh", "B_Rifle04_Body"))
         .Concat(WeaponPair("PPK", "Hands_Gun02", "Hands_Gun_02_Mesh", "Aim_Idle", "Gun02_Body_Mesh", "B_Gun02_Body"))
         .Concat(WeaponPair("Revolver", "Hands_Gun_03", "Hands_Gun_03", "Idle_Aiming", "Gun_03_Body_Mesh", "B_Gun_03_Body"))
         .Concat(WeaponPair("SniperRifle", "Hands_Sniper_Rifle", "Sniper_Rifle_Mesh", "Aiming_Idle", "Sniper_Rifle_Base_Mesh", "Bn_Sniper_Rifle_Base_Mesh"))
         .ToArray();

        /// <summary>
        /// Позы оружия T-38 (<c>HandsPackWeaponBuilder</c>): рукоять — правая рука пака, точка 0; вторая рука
        /// (цевьё или поддержка пистолета) — левая, точка 1. Префаб — <c>Assets/Prefabs/Weapons/&lt;name&gt;/&lt;name&gt;.prefab</c>.
        /// </summary>
        private static IEnumerable<HandsPackPoseRecipe> WeaponPair(string name, string folder, string mesh, string clip, string bodyMesh, string bodyBone, bool support = true)
        {
            string weapon = $"Assets/Prefabs/Weapons/{name}/{name}.prefab";
            yield return new HandsPackPoseRecipe
            {
                Pose = $"HandsPack_{name}_Grip", Folder = folder, Mesh = mesh, Clip = clip, Time = 0f, PackSide = UxrHandSide.Right,
                BodyMesh = bodyMesh, BodyBone = bodyBone, Weapon = weapon, BodyPath = "MeshContainer/Base", GrabPoint = 0
            };
            if (!support) yield break;
            yield return new HandsPackPoseRecipe
            {
                Pose = $"HandsPack_{name}_Support", Folder = folder, Mesh = mesh, Clip = clip, Time = 0f, PackSide = UxrHandSide.Left,
                BodyMesh = bodyMesh, BodyBone = bodyBone, Weapon = weapon, BodyPath = "MeshContainer/Base", GrabPoint = 1
            };
        }

        public static void ImportAll() => Import(Recipes);

        /// <summary>Позы одного оружия — после его пересборки <c>HandsPackWeaponBuilder</c> (сборщик кладёт калибровочный хват).</summary>
        public static void ImportFor(string weaponPrefab) => Import(Recipes.Where(r => r.Weapon == weaponPrefab));

        public static void Import(IEnumerable<HandsPackPoseRecipe> recipes) =>
            Import(recipes.Select(r => (r, ExtractSample(r))));

        /// <summary>
        /// Позы из готовых кадров — для источников с другим ригом рук (KINEMATION, <see cref="KinemationPoseExtractor" />):
        /// кадр в универсальных осях ладони, дальше всё как у пака Hands.
        /// </summary>
        public static void Import(IEnumerable<(HandsPackPoseRecipe Recipe, HandsPackPoseExtractor.Sample Sample)> samples)
        {
            var imported = new List<(HandsPackPoseRecipe Recipe, HandsPackPoseExtractor.Sample Sample, UxrHandPoseAsset Pose)>();
            foreach (var (recipe, sample) in samples)
            {
                imported.Add((recipe, sample, CreatePoseAsset(recipe, sample.Hand)));
            }

            AssetDatabase.SaveAssets();

            RegisterOnPoseBase(imported.Select(i => i.Pose));
            foreach (var (recipe, sample, pose) in imported)
            {
                AssignToGrabPoint(recipe, sample, pose);
            }

            AssetDatabase.SaveAssets();
            GameLog.Debug.Info($"[HandsPackPoseImporter] Поз: {imported.Count} → {string.Join(", ", imported.Select(i => i.Recipe.PoseFolder ?? PoseFolder).Distinct())}");
        }

        private static HandsPackPoseExtractor.Sample ExtractSample(HandsPackPoseRecipe recipe)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}{recipe.Folder}/Mesh/{recipe.Mesh}.FBX");
            AnimationClip clip = AssetDatabase.FindAssets("t:AnimationClip", new[] { $"{Pack}{recipe.Folder}/Animations" })
                                              .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                                              .OfType<AnimationClip>().FirstOrDefault(c => c.name == recipe.Clip);
            if (model == null || clip == null)
            {
                throw new FileNotFoundException($"Пак: нет меша {recipe.Mesh} или клипа {recipe.Clip} в {recipe.Folder}");
            }

            return HandsPackPoseExtractor.Extract(model, clip, recipe.Time, recipe.PackSide, recipe.BodyMesh, recipe.BodyBone);
        }

        private static UxrHandPoseAsset CreatePoseAsset(HandsPackPoseRecipe recipe, UxrHandDescriptor packHand)
        {
            var asset = ScriptableObject.CreateInstance<UxrHandPoseAsset>();
            asset.Version             = UxrHandPoseAsset.CurrentVersion;
            asset.PoseType            = UxrHandPoseType.Fixed;
            asset.HandDescriptorLeft  = recipe.PackSide == UxrHandSide.Left ? packHand : packHand.Mirrored();
            asset.HandDescriptorRight = recipe.PackSide == UxrHandSide.Right ? packHand : packHand.Mirrored();

            string folder = recipe.PoseFolder ?? PoseFolder;
            Directory.CreateDirectory(folder);
            string path     = $"{folder}/{recipe.Pose}.asset";
            var    existing = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(path);
            if (existing != null)
            {
                // Перезапись на месте: GUID сохраняется, ссылки из аватаров и оружия не рвутся.
                EditorUtility.CopySerialized(asset, existing);
                existing.name = recipe.Pose;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(asset);
                return existing;
            }

            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void RegisterOnPoseBase(IEnumerable<UxrHandPoseAsset> poses)
        {
            GameObject root   = AssetDatabase.LoadAssetAtPath<GameObject>(PoseBase);
            var        avatar = new SerializedObject(root.GetComponent<UxrAvatar>());
            SerializedProperty list = avatar.FindProperty("_handPoses");

            foreach (UxrHandPoseAsset pose in poses)
            {
                bool present = Enumerable.Range(0, list.arraySize).Any(i => list.GetArrayElementAtIndex(i).objectReferenceValue == pose);
                if (!present)
                {
                    list.InsertArrayElementAtIndex(list.arraySize);
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = pose;
                }
            }

            avatar.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(root);
        }

        private static void AssignToGrabPoint(HandsPackPoseRecipe recipe, HandsPackPoseExtractor.Sample sample, UxrHandPoseAsset pose)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(recipe.Weapon);
            try
            {
                Transform body = root.transform.Find(recipe.BodyPath)
                                 ?? throw new KeyNotFoundException($"{recipe.Weapon}: нет корпуса {recipe.BodyPath}");
                UxrGrabPointInfo grabPoint = root.GetComponent<UxrGrabbableObject>().GetGrabPoint(recipe.GrabPoint);

                foreach (string avatarPath in GripAvatars)
                {
                    // Граббер и размер кисти у аватаров разные — трансформы выравнивания свои у каждого.
                    var       avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath);
                    Transform left         = AlignTransform(root.transform, recipe.Pose, avatarPrefab.name, UxrHandSide.Left);
                    Transform right        = AlignTransform(root.transform, recipe.Pose, avatarPrefab.name, UxrHandSide.Right);
                    HandsPackGripAligner.Place(root.transform, body, sample, recipe.PackSide, avatarPrefab, UxrHandSide.Left, left);
                    HandsPackGripAligner.Place(root.transform, body, sample, recipe.PackSide, avatarPrefab, UxrHandSide.Right, right);

                    string guid = AssetDatabase.AssetPathToGUID(avatarPath);
                    grabPoint.CheckAddGripPoseInfo(guid);

                    UxrGripPoseInfo grip = grabPoint.GetGripPoseInfo(guid);
                    Transform oldLeft  = grip.GripAlignTransformHandLeft;
                    Transform oldRight = grip.GripAlignTransformHandRight;
                    grip.HandPose                    = pose;
                    grip.PoseBlendValue              = 0f;
                    grip.GripAlignTransformHandLeft  = left;
                    grip.GripAlignTransformHandRight = right;

                    // Точки, которые сборщик оружия поставил калибровкой, после позы пака никем не
                    // используются — убираются, чтобы в префабе не было двух мест одного хвата.
                    RemoveUnused(root, oldLeft, left);
                    RemoveUnused(root, oldRight, right);
                }

                PrefabUtility.SaveAsPrefabAsset(root, recipe.Weapon);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Удаляет трансформ выравнивания, если на него больше не ссылается ни одна запись хвата префаба,
        /// и опустевшие папки над ним (до корня префаба).
        /// </summary>
        private static void RemoveUnused(GameObject root, Transform old, Transform current)
        {
            if (old == null || old == current || !old.IsChildOf(root.transform) || old.name == "Grabs") return;

            bool used = root.GetComponentsInChildren<UxrGrabbableObject>(true)
                            .SelectMany(g => Enumerable.Range(0, g.GrabPointCount).Select(i => g.GetGrabPoint(i)))
                            .SelectMany(p => p.AvatarGripPoseEntries)
                            .Any(e => e.GripAlignTransformHandLeft == old || e.GripAlignTransformHandRight == old);
            if (used || old.GetComponentsInChildren<Transform>(true).Length > 1) return;

            Transform parent = old.parent;
            Object.DestroyImmediate(old.gameObject);
            while (parent != null && parent != root.transform && parent.childCount == 0 &&
                   parent.GetComponents<Component>().Length == 1)
            {
                Transform next = parent.parent;
                Object.DestroyImmediate(parent.gameObject);
                parent = next;
            }
        }

        /// <summary><c>MeshContainer/Grabs/HandsPack/&lt;поза&gt;/&lt;аватар&gt;/&lt;сторона&gt;</c> (или от корня, если <c>MeshContainer/Grabs</c> нет); создаётся при отсутствии.</summary>
        private static Transform AlignTransform(Transform root, string pose, string avatar, UxrHandSide side)
        {
            Transform parent = root.Find("MeshContainer/Grabs") ?? root;
            foreach (string name in new[] { "HandsPack", pose, avatar, side.ToString() })
            {
                Transform child = parent.Find(name);
                if (child == null)
                {
                    child = new GameObject(name).transform;
                    child.SetParent(parent, false);
                }

                parent = child;
            }

            // Как у остальных точек хвата: превью руки в инспекторе ищет трансформы по этому компоненту.
            if (parent.GetComponent<UxrGrabbableObjectSnapTransform>() == null) parent.gameObject.AddComponent<UxrGrabbableObjectSnapTransform>();
            return parent;
        }
    }
}
