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
    /// <c>Tools/VR Battlegrounds/Debug/Sit Candidates Stand</c> — сцена подбора позы сидения (<see cref="SitCandidateStand"/>):
    /// манекены MEF в ряд, по одному на кандидата из <see cref="ListPath"/>, манипулятор тело/голова как у стенда-кукловода
    /// (высота головы — присед), над манекенами — клипы с весами. Работает в Edit Mode.
    /// Список кандидатов переживает пересборку сцены; новый кандидат — строка в списке, манекен появится сам.
    /// </summary>
    public static class SitCandidatesStandMenu
    {
        public const string ScenePath = "Assets/Scenes/Dev/SitCandidates.unity";
        public const string ListPath = "Assets/Scenes/Dev/SitCandidates.asset";
        private const string PuppetPrefab = "Assets/Prefabs/Player/MEF_Base_Avatar.prefab";
        private const string StandClip = "Assets/Art/Animations/Locomotion/MixamoRifle/Rifle_Idle_Static.anim";
        private const string KneelClip = "Assets/Art/Animations/Locomotion/MixamoRifle/Rifle_IdleCrouching_Static.anim";
        private const string FirstCandidate = "Assets/Art/Animations/Locomotion/Sit/Sit_SittingOnTheFloor.fbx";
        private const float HeadHeight = 1.75f; // выше кости головы стоя — старт в позе «стоя»
        private const string FloorMaterial = "Assets/ThirdParty/UnityStarter_Robot/Environment/Materials/GridBlue_01_Mat.mat";

        [MenuItem("Tools/VR Battlegrounds/Debug/Sit Candidates Stand")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                GameLog.Debug.Warning("[SitCandidatesStandMenu] Выйди из Play Mode.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureList();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (Camera cam in Object.FindObjectsByType<Camera>()) Object.DestroyImmediate(cam.gameObject);
            // Список — после NewScene: новая сцена выгружает из памяти только что созданный ассет, ссылка стенда умерла бы.
            var list = AssetDatabase.LoadAssetAtPath<SitCandidateList>(ListPath);

            var root = new GameObject("[DevStand] SitCandidates");
            var stand = root.AddComponent<SitCandidateStand>();
            stand.list = list;
            stand.puppetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PuppetPrefab);
            float width = stand.spacing * Mathf.Max(list.candidates.Count - 1, 0);

            // Манипулятор как у стенда-кукловода: тело на полу (место и поворот ряда), голова — ребёнок тела; её высота — присед.
            StandHandles.Create(root.transform, HeadHeight, "PuppetHead (голова: двигай вниз — присед)", out Transform body, out Transform head);
            stand.body = body;
            stand.head = head;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3(width * 0.5f, 0f, 0f);
            floor.transform.localScale = new Vector3(Mathf.Max(1f, width * 0.2f + 0.6f), 1f, 0.6f);
            var material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial);
            if (material != null) floor.GetComponent<Renderer>().sharedMaterial = material;

            var camGo = new GameObject("Camera");
            var camera = camGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camGo.transform.position = new Vector3(width * 0.5f, 1.2f, 2.4f + width * 0.4f);
            camGo.transform.LookAt(new Vector3(width * 0.5f, 0.7f, 0f));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeTransform = head;
            GameLog.Debug.Info($"[SitCandidatesStandMenu] Стенд: {list.candidates.Count} кандидатов. Двигай PuppetBody (ряд) и PuppetHead вниз (присед), кандидаты — {ListPath}.");
        }

        private static SitCandidateList EnsureList()
        {
            var list = AssetDatabase.LoadAssetAtPath<SitCandidateList>(ListPath);
            if (list != null) return list;

            Directory.CreateDirectory(Path.GetDirectoryName(ListPath));
            list = ScriptableObject.CreateInstance<SitCandidateList>();
            list.stand = AssetDatabase.LoadAssetAtPath<AnimationClip>(StandClip);
            list.kneel = AssetDatabase.LoadAssetAtPath<AnimationClip>(KneelClip);
            AnimationClip sit = AssetDatabase.LoadAllAssetsAtPath(FirstCandidate).OfType<AnimationClip>()
                                             .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (sit != null)
            {
                list.candidates.Add(new SitCandidateList.Candidate { label = "Sit_SittingOnTheFloor @0 (текущая)", clip = sit, frame = 0f });
            }

            AssetDatabase.CreateAsset(list, ListPath);
            AssetDatabase.SaveAssets();
            return list;
        }
    }
}
