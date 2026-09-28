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
        public string      Clip;      // клип: Modelas/<Folder>/Animations/<Folder>@<Clip>.FBX
        public float       Time;
        public UxrHandSide PackSide;  // рука пака; вторая рука позы — её зеркало
        public string      BodyMesh;  // корпус оружия в паке…
        public string      BodyBone;  // …его кость…
        public string      Weapon;
        public string      BodyPath;  // …и тот же меш в префабе оружия
        public int         GrabPoint;
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

        public static readonly HandsPackPoseRecipe[] Recipes =
        {
            new HandsPackPoseRecipe
            {
                Pose = "HandsPack_Gun_Grip", Folder = "Hands_Gun", Mesh = "Gun_Mesh", Clip = "Aiming_Idle", Time = 0f,
                PackSide = UxrHandSide.Right, BodyMesh = "Base_mesh", BodyBone = "Bn_Base",
                Weapon = "Assets/Prefabs/Weapons/GunReal/Gun_real.prefab", BodyPath = "MeshContainer/Base", GrabPoint = 0
            },
            new HandsPackPoseRecipe
            {
                Pose = "HandsPack_Gun_Support", Folder = "Hands_Gun", Mesh = "Gun_Mesh", Clip = "Aiming_Idle", Time = 0f,
                PackSide = UxrHandSide.Left, BodyMesh = "Base_mesh", BodyBone = "Bn_Base",
                Weapon = "Assets/Prefabs/Weapons/GunReal/Gun_real.prefab", BodyPath = "MeshContainer/Base", GrabPoint = 1
            }
        };

        [MenuItem("Tools/VR Battlegrounds/Avatars/Hand Poses/Import Hands Pack Poses")]
        public static void ImportAll()
        {
            var imported = new List<(HandsPackPoseRecipe Recipe, HandsPackPoseExtractor.Sample Sample, UxrHandPoseAsset Pose)>();
            foreach (HandsPackPoseRecipe recipe in Recipes)
            {
                HandsPackPoseExtractor.Sample sample = ExtractSample(recipe);
                imported.Add((recipe, sample, CreatePoseAsset(recipe, sample.Hand)));
            }

            AssetDatabase.SaveAssets();

            RegisterOnPoseBase(imported.Select(i => i.Pose));
            foreach (var (recipe, sample, pose) in imported)
            {
                AssignToGrabPoint(recipe, sample, pose);
            }

            AssetDatabase.SaveAssets();
            GameLog.Debug.Info($"[HandsPackPoseImporter] Поз из пака: {imported.Count} → {PoseFolder}");
        }

        private static HandsPackPoseExtractor.Sample ExtractSample(HandsPackPoseRecipe recipe)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}{recipe.Folder}/Mesh/{recipe.Mesh}.FBX");
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath($"{Pack}{recipe.Folder}/Animations/{recipe.Folder}@{recipe.Clip}.FBX")
                                              .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__"));
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

            Directory.CreateDirectory(PoseFolder);
            string path     = $"{PoseFolder}/{recipe.Pose}.asset";
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
                    grip.HandPose                    = pose;
                    grip.PoseBlendValue              = 0f;
                    grip.GripAlignTransformHandLeft  = left;
                    grip.GripAlignTransformHandRight = right;
                }

                PrefabUtility.SaveAsPrefabAsset(root, recipe.Weapon);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
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

            return parent;
        }
    }
}
