using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

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
    /// Рецепты поз хвата пака Hands Weapons Animations (<see cref="Recipes" />: клип, кадр, рука пака, точка хвата
    /// оружия) и общие помощники записи хвата: кадр пака (<see cref="ExtractSample" />), регистрация поз в базе
    /// (<see cref="RegisterOnPoseBase" />), трансформы выравнивания (<see cref="AlignTransform" />, <see cref="RemoveUnused" />).
    ///
    /// <para>
    /// Рецепты читают экспорт исходных поз <c>_orig</c> (<c>PackOrigPoses</c>), источник паков Grip Extraction
    /// (<see cref="PackClipGripSource" />) и preflight арсенала. Прежний импорт поз в префабы (записи хвата
    /// MEF_Base_Avatar на оружии) выведен вместе с этим аватаром хвата (решение пользователя 2026-10-23): хваты
    /// аватаров регистрирует <c>AvatarGripRegistration</c> из поз <c>_orig</c>.
    /// </para>
    ///
    /// <para>Проверка поз пака — <c>HandsPackHandPoseTests</c>. Новая поза — рецепт в <see cref="Recipes" /> и кейс в тесте.</para>
    /// </summary>
    public static class HandsPackPoseImporter
    {
        public const string PoseFolder = "Assets/Art/HandPoses/HandsPack";

        /// <summary>Папка align-трансформов поз паков под <c>Grabs</c> (общая у Hands и KINEMATION).</summary>
        internal const string AlignSource = "HandsPack";

        private const string Pack = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/";

        /// <summary>
        /// GUID <c>MEF_Base_Avatar</c>. Аватар хвата выведен (2026-10-23): записи хвата по нему на оружии не пишутся и
        /// снимаются (<see cref="RetiredGripAvatarCleanup" />, сборщики — <c>HandsPackWeaponBuilder.DroppedGripAvatars</c>).
        /// Сам префаб нужен стендам и сборщику ног. Путь ищется по GUID: 2026-10-17 префаб переехал в <c>Player/Unused</c>.
        /// </summary>
        internal const string MefBaseAvatarGuid = "b6fe59db941fa944696ece5e1aabc032";

        /// <summary>Путь <c>MEF_Base_Avatar</c> по GUID (стенды, сборщик ног, локомоция Mixamo); нет префаба — исключение.</summary>
        internal static string MefBaseAvatarPath
        {
            get
            {
                string path = AssetDatabase.GUIDToAssetPath(MefBaseAvatarGuid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    throw new FileNotFoundException($"Нет префаба MEF_Base_Avatar с GUID {MefBaseAvatarGuid}.");
                }

                return path;
            }
        }

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

        internal static HandsPackPoseExtractor.Sample ExtractSample(HandsPackPoseRecipe recipe)
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

        /// <summary>Дописывает позы в <c>_handPoses</c> базы поз (по ссылке); общее с Grip Extraction.</summary>
        internal static void RegisterOnPoseBase(string poseBase, IEnumerable<UxrHandPoseAsset> poses)
        {
            GameObject root   = AssetDatabase.LoadAssetAtPath<GameObject>(poseBase);
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

        /// <summary>
        /// Удаляет трансформ выравнивания, если на него больше не ссылается ни одна запись хвата префаба,
        /// и опустевшие папки над ним (до корня префаба).
        /// </summary>
        internal static void RemoveUnused(GameObject root, Transform old, Transform current)
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

        /// <summary>
        /// <c>MeshContainer/Grabs/&lt;источник&gt;/&lt;поза&gt;/&lt;аватар&gt;/&lt;сторона&gt;</c> (или от корня, если <c>MeshContainer/Grabs</c> нет);
        /// создаётся при отсутствии. Источник паков — <see cref="AlignSource" />.
        /// </summary>
        internal static Transform AlignTransform(Transform root, string pose, string avatar, UxrHandSide side, string source = AlignSource)
        {
            Transform parent = root.Find("MeshContainer/Grabs") ?? root;
            foreach (string name in new[] { source, pose, avatar, side.ToString() })
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
