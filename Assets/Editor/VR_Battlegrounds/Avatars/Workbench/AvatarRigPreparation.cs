using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.EditorTools;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    internal enum AvatarHandsMode { Native, Sdk }

    /// <summary>Риг создаётся отдельно от игрового варианта; исходный model asset остаётся входом.</summary>
    internal static class AvatarRigPreparation
    {
        internal const string IntegrationPath = "Assets/ThirdParty/UltimateXR/Runtime/Prefabs/HandIntegrations/BigHandsIntegration.prefab";
        internal const string LeftPath = "Assets/ThirdParty/UltimateXR/Runtime/Prefabs/Internal/Hands/IK/BigIKHandLeft.prefab";
        internal const string RightPath = "Assets/ThirdParty/UltimateXR/Runtime/Prefabs/Internal/Hands/IK/BigIKHandRight.prefab";
        internal static string RigPath(string name) => "Assets/Prefabs/Avatars/" + ValidName(name) + ".prefab";
        internal static string PoseFolder(string name) => "Assets/Art/Avatars/" + ValidName(name) + "/HandPoses";
        internal static string ValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains("..") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("/") || name.Contains("\\"))
                throw new InvalidOperationException("Введите имя нового рига без пути и специальных символов.");
            return name.Trim();
        }

        internal static string Inspect(GameObject source)
        {
            var animators = source ? source.GetComponentsInChildren<Animator>(true).Where(a => a.avatar && a.avatar.isHuman && a.avatar.isValid).ToArray() : Array.Empty<Animator>();
            if (animators.Length != 1) return "Нужен ровно один валидный Humanoid Animator; найдено: " + animators.Length;
            var human = animators[0].avatar.humanDescription.human.Select(h => h.humanName).ToArray();
            var required = Enum.GetNames(typeof(HumanBodyBones)).Where(n => n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little")).ToArray();
            var missing = required.Except(human).ToArray();
            return missing.Length == 0 ? "Humanoid и 30 пальцевых костей доступны для родных кистей." : "Humanoid найден. Пальцы без mapping: " + string.Join(", ", missing);
        }

        internal static string CopyFbx(string source, string output)
        {
            if (!source.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || !output.StartsWith("Assets/Models/Avatars/", StringComparison.Ordinal)
                || !output.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || output.Contains("..")) throw new InvalidOperationException("Путь копии: Assets/Models/Avatars/*.fbx.");
            if (File.Exists(output) || File.Exists(output + ".meta")) throw new InvalidOperationException("Копия уже существует. Исходники не перезаписываются.");
            EnsureFolder(Path.GetDirectoryName(output).Replace('\\', '/'));
            if (!AssetDatabase.CopyAsset(source, output)) throw new InvalidOperationException("FBX не скопирован.");
            return "Создана копия FBX с отдельным GUID: " + output;
        }

        internal static string Build(string sourcePath, string name, AvatarHandsMode mode, bool sdkSourcePrepared, bool generatePoses)
        {
            string output = RigPath(name), poseFolder = PoseFolder(name);
            if (File.Exists(output) || File.Exists(output + ".meta")) throw new InvalidOperationException("Rig уже существует. Создание не перезаписывает готовые риги.");
            if (generatePoses && Directory.Exists(poseFolder) && Directory.EnumerateFiles(poseFolder).Any()) throw new InvalidOperationException("Папка поз уже существует.");
            if (mode == AvatarHandsMode.Sdk && !sdkSourcePrepared) throw new InvalidOperationException("Для SDK нужен заранее подготовленный источник без родной геометрии кистей.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source) throw new InvalidOperationException("Model asset недоступен.");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = ValidName(name);
                var animators = root.GetComponentsInChildren<Animator>(true).Where(a => a.avatar && a.avatar.isValid && a.avatar.isHuman).ToArray();
                if (animators.Length != 1) throw new InvalidOperationException("Источник должен содержать один валидный Humanoid Animator.");
                if (root.GetComponentsInChildren<UxrAvatar>(true).Length > 0) throw new InvalidOperationException("Этот источник уже является ригом. Выберите исходную модель.");
                var avatar = root.AddComponent<UxrAvatar>();
                UxrAvatarRig.SetupRigElementsFromAnimator(avatar.AvatarRig, animators[0]);
                avatar.TryToInferMissingRigElements();
                var avatarSettings = new SerializedObject(avatar);
                avatarSettings.FindProperty("_rigType").intValue = (int)UxrAvatarRigType.HalfOrFullBody;
                avatarSettings.ApplyModifiedPropertiesWithoutUndo();
                foreach (var solver in root.GetComponentsInChildren<UltimateXR.Animation.IK.UxrWristTorsionIKSolver>(true))
                    if (solver.transform.childCount == 0 && solver.name.EndsWith("_end", StringComparison.OrdinalIgnoreCase)) UnityEngine.Object.DestroyImmediate(solver);
                if (mode == AvatarHandsMode.Native)
                {
                    if (!avatar.AvatarRig.LeftArm.Hand.HasFullHandData() || !avatar.AvatarRig.RightArm.Hand.HasFullHandData())
                        throw new InvalidOperationException("Родные кисти требуют полный finger mapping обеих рук.");
                    var integration = AssetDatabase.LoadAssetAtPath<GameObject>(IntegrationPath);
                    if (!integration) throw new InvalidOperationException("Не найден BigHandsIntegration.");
                    PrefabUtility.InstantiatePrefab(integration, root.transform);
                }
                else
                {
                    VRBattlegrounds.Editor.HandsIntegrationSetup.Setup(root);
                    VRBattlegrounds.Editor.FinalizeRigMappingSetup.Setup(avatar);
                }
                foreach (var hand in root.GetComponentsInChildren<UxrHandIntegration>(true)) hand.TryToMatchHand();
                foreach (var grabber in root.GetComponentsInChildren<UxrGrabber>(true))
                    grabber.HandRenderer = UxrAvatarRig.TryToGetHandRenderer(avatar, grabber.Side);
                FixAvatarRenderers.Setup(avatar);
                VRBattlegrounds.Editor.ControllerAndCameraSetup.Setup(root);
                if (!root.GetComponent<UltimateXR.Devices.Integrations.UxrDummyControllerInput>()) root.AddComponent<UltimateXR.Devices.Integrations.UxrDummyControllerInput>();
                if (!AvatarFingertipSetup.CanSetup(avatar)) throw new InvalidOperationException("Не удалось подготовить UI fingertips.");
                AvatarFingertipSetup.Setup(avatar);
                var createRigInfo = typeof(UxrAvatar).GetMethod("CreateRigInfo", BindingFlags.Instance | BindingFlags.NonPublic);
                if (createRigInfo == null) throw new InvalidOperationException("SDK не предоставляет CreateRigInfo; риг не сохраняется.");
                createRigInfo.Invoke(avatar, null);
                EnsureFolder("Assets/Prefabs/Avatars");
                if (!PrefabUtility.SaveAsPrefabAssetAndConnect(root, output, InteractionMode.AutomatedAction)) throw new InvalidOperationException("Риг не сохранён.");
                string result = "Создан риг " + output + ". Игровой вариант и регистрация — отдельные действия.";
                if (generatePoses)
                {
                    result += "\n" + VRBattlegrounds.Editor.HandPosesSetup.Setup(root, poseFolder);
                    AvatarMaintenanceTools.SavePrefab(root, output);
                }
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path)))) throw new IOException("Не создана папка " + path);
        }
    }
}
