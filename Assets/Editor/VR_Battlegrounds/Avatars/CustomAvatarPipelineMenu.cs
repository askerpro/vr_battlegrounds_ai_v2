using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VRBattlegrounds.Editor
{
    public static class CustomAvatarPipelineMenu
    {
        private const string MenuRoot = "Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/";
        private const string BlenderPathPrefsKey = "VRBattlegrounds.CustomAvatarPipeline.BlenderPath";
        private const string LastFbxPrefsKey = "VRBattlegrounds.CustomAvatarPipeline.LastFbxAssetPath";
        private const string BlenderScriptsAssetFolder = "Assets/Editor/VR_Battlegrounds/Avatars/BlenderScripts";

        [MenuItem(MenuRoot + "Configure Blender Executable...", false, 1)]
        public static void ConfigureBlenderExecutable()
        {
            string current = EditorPrefs.GetString(BlenderPathPrefsKey, string.Empty);
            string startDirectory = File.Exists(current) ? Path.GetDirectoryName(current) : string.Empty;
            string selected = EditorUtility.OpenFilePanel("Select blender.exe", startDirectory, "exe");

            if (string.IsNullOrEmpty(selected))
                return;

            if (IsWindowsAppsPath(selected))
            {
                EditorUtility.DisplayDialog(
                    "Unsupported Blender Path",
                    "This Blender executable is inside C:\\Program Files\\WindowsApps, which Windows often blocks for direct process launches.\n\nInstall Blender from blender.org or Steam, then select its real blender.exe path.",
                    "OK");
                Debug.LogError($"[Custom Avatar Pipeline] Refusing WindowsApps Blender path: {selected}");
                return;
            }

            EditorPrefs.SetString(BlenderPathPrefsKey, selected);
            Debug.Log($"[Custom Avatar Pipeline] Blender executable set to: {selected}");
        }

        [MenuItem(MenuRoot + "1. Amputate Selected FBX Hands", false, 20)]
        public static void AmputateSelectedFbxHands()
        {
            RunSelectedFbxScript("amputate_avatar_hands.py", "amputate hands");
        }

        [MenuItem(MenuRoot + "2. Add Wrist Torsion Bones", false, 21)]
        public static void AddWristTorsionBones()
        {
            RunSelectedFbxScript("add_wrist_torsion_bones.py", "add wrist torsion bones");
        }

        [MenuItem(MenuRoot + "3. Add Eye Bones And Map Humanoid", false, 22)]
        public static void AddEyeBonesAndMapHumanoid()
        {
            if (!TryGetSelectedFbxAssetPath(out string assetPath, true))
                return;

            if (RunBlenderScriptForAsset(assetPath, "add_eye_bones.py", "add eye bones"))
                ApplyEyeMapping.MapEyes(assetPath);
        }

        [MenuItem(MenuRoot + "4. Create Scene Target From Selected FBX", false, 40)]
        public static void CreateSceneTargetFromSelectedFbx()
        {
            if (!TryGetSelectedFbxAssetPath(out string assetPath, true))
                return;

            CreateSceneTarget(assetPath);
        }

        [MenuItem(MenuRoot + "5. Run UXR Setup On Current Target", false, 41)]
        public static void RunUxrSetupOnCurrentTarget()
        {
            if (!EnsureAvatarTargetSelected())
                return;

            CoreAvatarSetup.Execute();
            HandsIntegrationSetup.Execute();
            ControllerAndCameraSetup.Execute();
            FinalizeRigMappingSetup.Execute();
            SetupUiFingertips();
            CreatePrefabSetup.Execute();
            HandPosesSetup.Execute();

            Debug.Log("[Custom Avatar Pipeline] UXR setup wizard completed on current target.");
        }

        /// <summary>
        ///     Кончики пальцев для нажатия UI — после разметки рига (кончик ставится на кисть, которую
        ///     двигает UltimateXR) и до сохранения префаба, чтобы попасть в него.
        /// </summary>
        private static void SetupUiFingertips()
        {
            GameObject target = Selection.activeGameObject;
            UltimateXR.Avatar.UxrAvatar avatar = target != null ? target.GetComponent<UltimateXR.Avatar.UxrAvatar>() : null;

            if (avatar == null)
            {
                Debug.LogWarning("[Custom Avatar Pipeline] Кончики пальцев для UI: на выделенном объекте нет UxrAvatar — шаг пропущен.");
                return;
            }

            VrBattlegrounds.EditorTools.AvatarFingertipSetup.Setup(avatar);
        }

        [MenuItem(MenuRoot + "Run Blender Preparation Only", false, 60)]
        public static void RunBlenderPreparationOnly()
        {
            if (!TryGetSelectedFbxAssetPath(out string assetPath, true))
                return;

            RunBlenderPreparation(assetPath);
        }

        [MenuItem(MenuRoot + "Run Full Selected FBX Pipeline", false, 61)]
        public static void RunFullSelectedFbxPipeline()
        {
            if (!TryGetSelectedFbxAssetPath(out string assetPath, true))
                return;

            try
            {
                EditorUtility.DisplayProgressBar("Custom Avatar Pipeline", "Preparing FBX in Blender...", 0.1f);
                if (!RunBlenderPreparation(assetPath))
                    return;

                EditorUtility.DisplayProgressBar("Custom Avatar Pipeline", "Creating scene target...", 0.6f);
                CreateSceneTarget(assetPath);

                EditorUtility.DisplayProgressBar("Custom Avatar Pipeline", "Running UXR setup wizard...", 0.75f);
                RunUxrSetupOnCurrentTarget();

                Debug.Log($"[Custom Avatar Pipeline] Full pipeline completed for {assetPath}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        internal static bool TryGetSelectedFbxAssetPath(out string assetPath, bool logErrors)
        {
            assetPath = null;

            UnityEngine.Object selected = Selection.activeObject;
            if (selected != null)
            {
                string selectedPath = AssetDatabase.GetAssetPath(selected);
                if (IsFbxAssetPath(selectedPath))
                {
                    assetPath = selectedPath;
                    EditorPrefs.SetString(LastFbxPrefsKey, assetPath);
                    return true;
                }
            }

            GameObject selectedGameObject = Selection.activeGameObject;
            if (selectedGameObject != null)
            {
                UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(selectedGameObject);
                string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
                if (IsFbxAssetPath(sourcePath))
                {
                    assetPath = sourcePath;
                    EditorPrefs.SetString(LastFbxPrefsKey, assetPath);
                    return true;
                }
            }

            if (logErrors)
            {
                string lastPath = EditorPrefs.GetString(LastFbxPrefsKey, string.Empty);
                string hint = string.IsNullOrEmpty(lastPath) ? string.Empty : $"\nLast prepared FBX was: {lastPath}";
                Debug.LogError("[Custom Avatar Pipeline] Select an FBX asset in the Project window, or select a scene instance created from an FBX." + hint);
            }

            return false;
        }

        private static bool RunBlenderPreparation(string assetPath)
        {
            if (!RunBlenderScriptForAsset(assetPath, "amputate_avatar_hands.py", "amputate hands"))
                return false;
            if (!RunBlenderScriptForAsset(assetPath, "add_wrist_torsion_bones.py", "add wrist torsion bones"))
                return false;
            if (!RunBlenderScriptForAsset(assetPath, "add_eye_bones.py", "add eye bones"))
                return false;

            ApplyEyeMapping.MapEyes(assetPath);
            return true;
        }

        private static void RunSelectedFbxScript(string scriptName, string actionName)
        {
            if (!TryGetSelectedFbxAssetPath(out string assetPath, true))
                return;

            RunBlenderScriptForAsset(assetPath, scriptName, actionName);
        }

        private static bool RunBlenderScriptForAsset(string assetPath, string scriptName, string actionName)
        {
            string fbxAbsolutePath = ToAbsoluteProjectPath(assetPath);
            string scriptAbsolutePath = ToAbsoluteProjectPath($"{BlenderScriptsAssetFolder}/{scriptName}");

            if (!File.Exists(scriptAbsolutePath))
            {
                Debug.LogError($"[Custom Avatar Pipeline] Blender script not found: {scriptAbsolutePath}");
                return false;
            }

            string blenderPath = ResolveBlenderExecutable();
            Debug.Log($"[Custom Avatar Pipeline] Running Blender step '{actionName}' on {assetPath}");

            int exitCode = RunProcess(
                blenderPath,
                $"--background --python {Quote(scriptAbsolutePath)} -- {Quote(fbxAbsolutePath)}",
                out string output);

            if (!string.IsNullOrWhiteSpace(output))
                Debug.Log($"[Custom Avatar Pipeline] Blender output ({actionName}):\n{output}");

            if (exitCode != 0)
            {
                Debug.LogError($"[Custom Avatar Pipeline] Blender step '{actionName}' failed with exit code {exitCode}.");
                return false;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            Debug.Log($"[Custom Avatar Pipeline] Blender step '{actionName}' completed and Unity reimported {assetPath}");
            return true;
        }

        private static GameObject CreateSceneTarget(string assetPath)
        {
            GameObject existing = GameObject.Find("AutoSetupAvatarTarget");
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                Debug.LogWarning("[Custom Avatar Pipeline] AutoSetupAvatarTarget already exists. Selected existing target instead of creating a duplicate.");
                return existing;
            }

            GameObject fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (fbxPrefab == null)
            {
                Debug.LogError($"[Custom Avatar Pipeline] Could not load FBX prefab at {assetPath}");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(fbxPrefab) as GameObject;
            if (instance == null)
                instance = UnityEngine.Object.Instantiate(fbxPrefab);

            instance.name = "AutoSetupAvatarTarget";
            Undo.RegisterCreatedObjectUndo(instance, "Create AutoSetupAvatarTarget");
            Selection.activeGameObject = instance;
            Debug.Log($"[Custom Avatar Pipeline] Created scene target from {assetPath}: {instance.name}");
            return instance;
        }

        private static bool EnsureAvatarTargetSelected()
        {
            if (Selection.activeGameObject != null)
                return true;

            GameObject existing = GameObject.Find("AutoSetupAvatarTarget");
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                return true;
            }

            Debug.LogError("[Custom Avatar Pipeline] Select the avatar root in the scene, or create AutoSetupAvatarTarget first.");
            return false;
        }

        private static string ResolveBlenderExecutable()
        {
            string configured = EditorPrefs.GetString(BlenderPathPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
            {
                if (IsWindowsAppsPath(configured))
                {
                    EditorPrefs.DeleteKey(BlenderPathPrefsKey);
                    Debug.LogWarning($"[Custom Avatar Pipeline] Ignoring WindowsApps Blender path because direct launch is blocked by Windows: {configured}");
                }
                else
                {
                    return configured;
                }
            }

            foreach (string candidate in GetDefaultBlenderPaths())
            {
                if (File.Exists(candidate))
                {
                    EditorPrefs.SetString(BlenderPathPrefsKey, candidate);
                    return candidate;
                }
            }

            return "blender";
        }

        private static string[] GetDefaultBlenderPaths()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            return new[]
            {
                Path.Combine(programFiles, "Blender Foundation", "Blender 4.5", "blender.exe"),
                Path.Combine(programFiles, "Blender Foundation", "Blender 5.1", "blender.exe"),
                Path.Combine(programFiles, "Blender Foundation", "Blender 5.0", "blender.exe"),
                Path.Combine(programFiles, "Blender Foundation", "Blender 4.4", "blender.exe"),
                Path.Combine(programFiles, "Blender Foundation", "Blender 4.3", "blender.exe"),
                Path.Combine(programFiles, "Blender Foundation", "Blender 4.2", "blender.exe"),
                Path.Combine(programFiles, "Blender Foundation", "Blender 4.1", "blender.exe"),
                Path.Combine(programFiles, "Blender Foundation", "Blender 4.0", "blender.exe"),
                Path.Combine(programFiles, "Blender Foundation", "Blender", "blender.exe"),
                Path.Combine(programFilesX86, "Blender Foundation", "Blender", "blender.exe")
            };
        }

        private static int RunProcess(string fileName, string arguments, out string output)
        {
            StringBuilder builder = new StringBuilder();

            using (var process = new System.Diagnostics.Process())
            {
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetParent(Application.dataPath).FullName
                };

                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                        builder.AppendLine(args.Data);
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                        builder.AppendLine(args.Data);
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    string extra = IsWindowsAppsPath(fileName)
                        ? "\nThe configured Blender path is inside C:\\Program Files\\WindowsApps and cannot be launched directly. Use Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/Configure Blender Executable... and select a blender.org/Steam installation instead."
                        : string.Empty;
                    output = $"Failed to start process '{fileName}'. {ex.Message}{extra}";
                    return -1;
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                output = builder.ToString();
                return process.ExitCode;
            }
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string relativePath = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        }

        private static bool IsFbxAssetPath(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) &&
                   assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsAppsPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   path.Replace('\\', '/').IndexOf("/WindowsApps/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
