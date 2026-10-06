using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// <c>Tools/VR Battlegrounds/Debug/Avatar Puppet Stand</c> — сцена стенда-кукловода (<c>AvatarPuppetStand</c>):
    /// стенд-плейграунд: аватары из <see cref="StandAvatars"/> в ряд — три версии MEF (ноги UltimateXR на клипах Mixamo, на клипах
    /// Final IK, эталон VRIK), манипулятор «тело» с детьми «голова» и две «кисти», скрытый риг сценария из клипа,
    /// оружие для поз рук, просмотр клипа без IK (дочерний <c>ClipPreview</c>), пол слоя Ground, обзорная камера.
    /// Повторный запуск пересобирает сцену. Корень помечен
    /// Play стенда — <c>Tools/VR Battlegrounds/Debug/Avatar Puppet Stand Play</c> (временная стартовая сцена у единственного writer).
    /// </summary>
    public static class AvatarPuppetStandMenu
    {
        public const string ScenePath = "Assets/Scenes/Dev/AvatarPuppetStand.unity";
        /// <summary>
        /// Аватары стенда — три версии MEF: ноги UltimateXR (решатель ноги и клипы Mixamo, раздел «Ноги» контроллера), те же
        /// ноги на клипах Final IK и эталон VRIK (Final IK целиком). Heavy и киборг из ряда убраны (2026-10-01).
        /// </summary>
        private static readonly string[] StandAvatars =
        {
            "Assets/Prefabs/Player/Optimized_MEF_Player.prefab",
            "Assets/Prefabs/Player/Experimental/Optimized_MEF_Player_VRIK.prefab",
        };

        // Два MEF (2026-10-06, указание пользователя): UxrIK на клипах Mixamo и эталон VRIK на родных клипах.
        private static readonly string[] StandLabels = { "MEF · UxrIK, клипы Mixamo", "MEF · VRIK, родные клипы (эталон)" };

        private static readonly string[] StandOverrides = { "", "" };
        private const string StandType = "VrBattlegrounds.DevTools.LegsCompare.AvatarPuppetStand, VrBattlegrounds.LegsCompare";
        private const string FloorMaterial = "Assets/ThirdParty/UnityStarter_Robot/Environment/Materials/GridBlue_01_Mat.mat";
        private const float EyeHeight = 1.65f;
        private const string LegsPanelType = "VrBattlegrounds.DevTools.LegsCompare.LegsDebugPanel, VrBattlegrounds.LegsCompare";
        private const string PreviewType ="VrBattlegrounds.DevTools.LegsCompare.ClipFeetPreview, VrBattlegrounds.LegsCompare";
        private const string MefRigPath = "Assets/Art/Avatars/Locomotion/MEF_Base_Avatar_LocomotionRig.prefab";
        private const string MefMeshPath = "Assets/Prefabs/Player/MEF_Base_Avatar.prefab";
        private const string MannequinPath = "Assets/ThirdParty/FImpossible Creations/Plugins - Animating/Legs Animator/Demos - Legs Animator/Demos Resources/Prefabs/FAnnequin_IdleGlue.prefab";
        private const string RiflePath = "Assets/Prefabs/Weapons/AK105/AK105.prefab";
        private const string PistolPath = "Assets/Prefabs/Weapons/PPK/PPK.prefab";
        private const string RifleClips = "Assets/Art/Animations/Locomotion/MixamoRifle/";

        /// <summary>Сценарий из клипа по умолчанию: шаг с винтовкой вперёд, вправо, назад — медленно (×0,4 ≈ 0,7 м/с), покой.</summary>
        private static readonly (string clip, float duration, float speed)[] DefaultScenario =
        {
            ("Rifle_Idle", 2f, 1f), ("Rifle_WalkForward", 5f, 0.4f), ("Rifle_Idle", 2f, 1f), ("Rifle_WalkRight", 5f, 0.4f),
            ("Rifle_Idle", 2f, 1f), ("Rifle_WalkBackward", 5f, 0.4f), ("Rifle_WalkForward", 4f, 0.7f), ("Rifle_Idle", 2f, 1f),
        };

        [MenuItem("Tools/VR Battlegrounds/Debug/Avatar Puppet Stand")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                GameLog.Debug.Warning("[AvatarPuppetStandMenu] Выйди из Play Mode.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            System.Type standType = System.Type.GetType(StandType);
            if (standType == null)
            {
                GameLog.Error($"[AvatarPuppetStandMenu] Нет типа {StandType}.");
                return;
            }

            var prefabs = new List<GameObject>();
            var labelsUsed = new List<string>();
            var overridesUsed = new List<string>();
            for (int i = 0; i < StandAvatars.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StandAvatars[i]);
                if (prefab == null)
                {
                    GameLog.Debug.Warning($"[AvatarPuppetStandMenu] Нет префаба {StandAvatars[i]} (вариант VRIK — Tools/VR Battlegrounds/Avatars/Experimental/Setup VRIK Variant).");
                    continue;
                }

                prefabs.Add(prefab);
                labelsUsed.Add(StandLabels[i]);
                overridesUsed.Add(StandOverrides[i]);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.DestroyImmediate(cam.gameObject);

            var root = new GameObject("[DevStand] AvatarPuppetStand");
            var stand = (MonoBehaviour)root.AddComponent(standType);
            float spacing = 4f;   // боковая камера снимков не должна видеть соседа
            float width = spacing * (prefabs.Count - 1);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.layer = LayerMask.NameToLayer("Ground");
            floor.transform.position = new Vector3(width * 0.5f, 0f, 0f);
            // Пол большой: программы уводят голову (и тела) на десятки метров.
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            var material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial);
            if (material != null) floor.GetComponent<Renderer>().sharedMaterial = material;

            // Манипулятор стоит у первого аватара (ряд начинается в корне стенда); смещение от этой точки — всем. Тело — на
            // полу (место и поворот корпуса), голова и кисти — его дети: поворот головы кисти не трогает.
            var bodyHandle = new GameObject("PuppetBody (тело: двигай и крути)").transform;
            bodyHandle.position = Vector3.zero;
            var bodyGizmo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bodyGizmo.name = "Gizmo";
            Object.DestroyImmediate(bodyGizmo.GetComponent<Collider>());
            bodyGizmo.transform.SetParent(bodyHandle, false);
            bodyGizmo.transform.localScale = new Vector3(0.35f, 0.01f, 0.35f);
            var bodyArrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bodyArrow.name = "Forward";
            Object.DestroyImmediate(bodyArrow.GetComponent<Collider>());
            bodyArrow.transform.SetParent(bodyHandle, false);
            bodyArrow.transform.localPosition = new Vector3(0f, 0.01f, 0.25f);
            bodyArrow.transform.localScale = new Vector3(0.05f, 0.02f, 0.2f);

            var head = new GameObject("PuppetHead (голова: крути отдельно)").transform;
            head.SetParent(bodyHandle, false);
            head.localPosition = new Vector3(0f, EyeHeight, 0f);
            var headGizmo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headGizmo.name = "Gizmo";
            Object.DestroyImmediate(headGizmo.GetComponent<Collider>());
            headGizmo.transform.SetParent(head, false);
            headGizmo.transform.localScale = new Vector3(0.12f, 0.08f, 0.16f);
            headGizmo.transform.localPosition = new Vector3(0f, 0f, 0.3f);

            Transform left = Handle("PuppetLeftHand", bodyHandle, new Vector3(-0.25f, EyeHeight - 0.8f, 0.1f));
            Transform right = Handle("PuppetRightHand", bodyHandle, new Vector3(0.25f, EyeHeight - 0.8f, 0.1f));

            var so = new SerializedObject(stand);
            SerializedProperty list = so.FindProperty("avatarPrefabs");
            list.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            so.FindProperty("spacing").floatValue = spacing;
            SerializedProperty enabled = so.FindProperty("avatarEnabled");
            enabled.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++) enabled.GetArrayElementAtIndex(i).boolValue = true;
            SerializedProperty labels = so.FindProperty("slotLabels"), overrides = so.FindProperty("slotOverrides");
            labels.arraySize = overrides.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++)
            {
                labels.GetArrayElementAtIndex(i).stringValue = labelsUsed[i];
                overrides.GetArrayElementAtIndex(i).stringValue = overridesUsed[i];
            }
            so.FindProperty("handPose").enumValueIndex = 2;   // руки «Винтовка»: набор клипов винтовки, оружие в руках
            so.FindProperty("eyeHeight").floatValue = EyeHeight;
            so.FindProperty("clipRigPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(MefRigPath);
            so.FindProperty("rifleWeapon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
            so.FindProperty("pistolWeapon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PistolPath);
            SerializedProperty scenario = so.FindProperty("clipScenario");
            scenario.arraySize = DefaultScenario.Length;
            for (int i = 0; i < DefaultScenario.Length; i++)
            {
                SerializedProperty e = scenario.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("clip").objectReferenceValue = Clip(RifleClips + DefaultScenario[i].clip + ".fbx");
                e.FindPropertyRelative("duration").floatValue = DefaultScenario[i].duration;
                e.FindPropertyRelative("speed").floatValue = DefaultScenario[i].speed;
            }
            so.FindProperty("clipPreview").objectReferenceValue = BuildClipPreview(root.transform);
            so.FindProperty("body").objectReferenceValue = bodyHandle;
            so.FindProperty("head").objectReferenceValue = head;
            so.FindProperty("leftHand").objectReferenceValue = left;
            so.FindProperty("rightHand").objectReferenceValue = right;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Отладка ног в Play: над аватарами — Legs_Crouch, шаги, клипы с весами; в инспекторе панели — график и пороги.
            System.Type panelType = System.Type.GetType(LegsPanelType);
            if (panelType != null) new GameObject("LegsDebugPanel (отладка ног)").AddComponent(panelType);

            var overview = new GameObject("Overview Camera").AddComponent<Camera>();
            overview.gameObject.AddComponent<AudioListener>();
            overview.transform.position = new Vector3(width * 0.5f, 1.6f, 3.2f + width * 0.35f);
            overview.transform.LookAt(new Vector3(width * 0.5f, 1.0f, 0f));

            // Теги — по правилам GameTagRules (пол — Environment), как у Apply Game Tags: руками не ставятся.
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                foreach (Transform t in sceneRoot.GetComponentsInChildren<Transform>(true))
                    t.gameObject.tag = GameTagRules.ExpectedTag(t.gameObject);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeTransform = head;
            GameLog.Debug.Info($"[AvatarPuppetStandMenu] Стенд: {prefabs.Count} аватаров. Play — ручной режим (двигай/крути PuppetBody, крути PuppetHead, двигай кисти) или кнопки программ в инспекторе стенда; агенту — AvatarPuppetStand.RunAll(папка).");
        }

        /// <summary>
        /// Просмотр клипа без IK (бывшая сцена Clip Feet Preview): манекен Legs Animator и MEF на копии рига, позади ряда.
        /// Выключен; включает режим стенда ClipPreview (и в Edit Mode).
        /// </summary>
        private const string PlayOwner = "puppet-stand";

        /// <summary>Play в сцене стенда: временная стартовая сцена у единственного writer, снимается в Edit Mode.</summary>
        [MenuItem("Tools/VR Battlegrounds/Debug/Avatar Puppet Stand Play")]
        public static void Play()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null) { GameLog.Debug.Warning("[AvatarPuppetStandMenu] Сначала соберите стенд: Avatar Puppet Stand."); return; }
            if (!PlayModeStartFromOffline.TrySetTemporaryStartScene(scene, PlayOwner))
            {
                GameLog.Debug.Warning("[AvatarPuppetStandMenu] Стартовую сцену уже занял другой стенд.");
                return;
            }
            EditorApplication.playModeStateChanged -= ReleaseStartScene;
            EditorApplication.playModeStateChanged += ReleaseStartScene;
            EditorApplication.EnterPlaymode();
        }

        private static void ReleaseStartScene(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= ReleaseStartScene;
            PlayModeStartFromOffline.ClearTemporaryStartScene(PlayOwner);
        }

        private static GameObject BuildClipPreview(Transform parent)
        {
            System.Type previewType = System.Type.GetType(PreviewType);
            if (previewType == null) return null;
            var go = new GameObject("ClipPreview (клип, Foot IK, фаза — здесь)");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -2.5f);
            var preview = (MonoBehaviour)go.AddComponent(previewType);
            var so = new SerializedObject(preview);
            so.FindProperty("mannequinPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(MannequinPath);
            so.FindProperty("mefRigPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(MefRigPath);
            so.FindProperty("mefMeshPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(MefMeshPath);
            so.FindProperty("clip").objectReferenceValue = Clip(RifleClips + "Rifle_WalkForward.fbx");
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            return go;
        }

        private static AnimationClip Clip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        private static Transform Handle(string name, Transform parent, Vector3 localPosition)
        {
            var handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = name;
            Object.DestroyImmediate(handle.GetComponent<Collider>());
            handle.transform.SetParent(parent, false);
            handle.transform.localPosition = localPosition;
            handle.transform.localScale = Vector3.one * 0.07f;
            return handle.transform;
        }
    }
}
