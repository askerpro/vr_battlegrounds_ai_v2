using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.LegsCompare;

namespace VrBattlegrounds.Editor.PuppetStand
{
    /// <summary>
    /// <c>Tools/VR Battlegrounds/Debug/Clip Scrub Stand</c> — сцена стенда перемотки клипов (<see cref="ClipScrubStand"/>):
    /// манекены MEF в ряд, у каждого свой клип и ползунок кадра (инспектор стенда), отличие позы от эталона в подписях.
    /// Список с кадрами (<see cref="ListPath"/>) переживает пересборку сцены. Работает в Edit Mode.
    /// </summary>
    public static class ClipScrubStandMenu
    {
        public const string ScenePath = "Assets/Scenes/Dev/ClipScrub.unity";
        public const string ListPath = "Assets/Scenes/Dev/ClipScrub.asset";
        private const string PuppetPrefab = "Assets/Prefabs/Player/MEF_Base_Avatar.prefab";
        private const float HeadHeight = 1.1f; // камера (глаза) около колена — середина переходов «присед ↔ сидение»
        private const string LivePrefab = "Assets/Prefabs/Player/Optimized_MEF_Player.prefab";
        private const string FloorMaterial = "Assets/ThirdParty/UnityStarter_Robot/Environment/Materials/GridBlue_01_Mat.mat";

        /// <summary>Цепочка поз аватара по умолчанию — уровни Legs_Crouch 0 и 1 контроллера ног (статичные клипы покоя).</summary>
        private static readonly (string path, string label)[] DefaultChain =
        {
            ("Assets/Art/Animations/Locomotion/MixamoRifle/Rifle_Idle_Static.anim", "стоя"),
            ("Assets/Art/Animations/Locomotion/MixamoRifle/Rifle_IdleCrouching_Static.anim", "присед (колено)"),
        };

        /// <summary>Кандидаты по умолчанию: текущее сидение (Legs_Crouch 2) и все клипы папки кандидатов.</summary>
        private const string CurrentSit = "Assets/Art/Animations/Locomotion/Sit/Sit_SittingOnTheFloor.fbx";
        private const string CandidatesFolder = "Assets/Art/Animations/Locomotion/SitCandidates";

        [MenuItem("Tools/VR Battlegrounds/Debug/Clip Scrub Stand")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                GameLog.Debug.Warning("[ClipScrubStandMenu] Выйди из Play Mode.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureList();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (Camera cam in Object.FindObjectsByType<Camera>()) Object.DestroyImmediate(cam.gameObject);
            // Список — после NewScene: новая сцена выгружает из памяти только что созданный ассет, ссылка стенда умерла бы.
            var list = AssetDatabase.LoadAssetAtPath<ClipScrubList>(ListPath);

            var root = new GameObject("[DevStand] ClipScrub");
            var stand = root.AddComponent<ClipScrubStand>();
            stand.list = list;
            stand.puppetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PuppetPrefab);
            // На сцене одна инспектируемая строка (onlyInspected) — пол и камера у манипулятора.
            float width = stand.onlyInspected ? 0f : stand.spacing * Mathf.Max(list.entries.Count - 1, 2);

            // Манипулятор как у стенда-кукловода: тело — место и поворот ряда, высота головы — кадр каждой строки (followHead).
            StandHandles.Create(root.transform, HeadHeight, "PuppetHead (камера: вверх-вниз — кадр по высоте)", out Transform body, out Transform head);
            stand.body = body;
            stand.head = head;
            // Play: живые аватары UltimateXR — кисти по ручкам контроллеров (поза винтовки), ноги — клип строки.
            StandHandles.CreateHands(body, head, PuppetHandPose.Rifle, out Transform leftHand, out Transform rightHand);
            stand.leftHand = leftHand;
            stand.rightHand = rightHand;
            stand.livePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LivePrefab);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3(-width * 0.5f, 0f, 0f);
            floor.transform.localScale = new Vector3(Mathf.Max(0.6f, width * 0.2f + 0.6f), 1f, 0.6f);
            var material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial);
            if (material != null) floor.GetComponent<Renderer>().sharedMaterial = material;
            stand.floor = floor.transform; // режим ClipPose опускает пол под подошвы

            var camGo = new GameObject("Camera");
            var camera = camGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camGo.transform.position = new Vector3(-width * 0.5f, 1.2f, 2.4f + width * 0.4f);
            camGo.transform.LookAt(new Vector3(-width * 0.5f, 0.7f, 0f));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeTransform = head;
            GameLog.Debug.Info($"[ClipScrubStandMenu] Стенд: {list.chain.Count} поз цепочки, {list.entries.Count} кандидатов. Высота PuppetHead — кадр строк (followHead), иначе ползунки инспектора [DevStand] ClipScrub; список — {ListPath}.");
        }

        private static ClipScrubList EnsureList()
        {
            var list = AssetDatabase.LoadAssetAtPath<ClipScrubList>(ListPath);
            if (list != null) return list;

            Directory.CreateDirectory(Path.GetDirectoryName(ListPath));
            list = ScriptableObject.CreateInstance<ClipScrubList>();
            foreach ((string path, string label) in DefaultChain)
            {
                AnimationClip clip = Clip(path);
                if (clip != null) list.chain.Add(new ClipScrubList.KeyPose { label = label, clip = clip });
            }

            AnimationClip sit = Clip(CurrentSit);
            if (sit != null) list.entries.Add(new ClipScrubList.Entry { label = "Sit_SittingOnTheFloor (текущее сидение)", clip = sit });
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CandidatesFolder }))
            {
                AnimationClip clip = Clip(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null) list.entries.Add(new ClipScrubList.Entry { label = clip.name, clip = clip });
            }

            AssetDatabase.CreateAsset(list, ListPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<ClipScrubList>(ListPath);
        }

        private static AnimationClip Clip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }
}
