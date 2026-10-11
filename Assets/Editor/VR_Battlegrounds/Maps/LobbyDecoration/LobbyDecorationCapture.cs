using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Фиксированные ракурсы ревью без постоянных камер и AudioListener.</summary>
    public static class LobbyDecorationCapture
    {
        private const string DirectoryPath = "tasks/lobby-decoration/reports/implementation/visual-review/pass-10/";
        private const string ReferenceRoot = "tasks/lobby-decoration/reports/references/";
        private sealed class View
        {
            public string name, reference;
            public Vector3 eye, target;
            public float fov;
            public int width = 1024, height = 576;
        }

        [MenuItem("Tools/VR Battlegrounds/Lobby Decoration/6. Снять ракурсы арт-ревью")]
        public static void Capture()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(root, "F:/UnityProjects/Vr_Battlegrounds_ai", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Capture только в основном checkout.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/Lobby.unity" || SceneManager.sceneCount != 1 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Нужна свободная Lobby вне Play/компиляции/prefab stage.");
            var definition = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PhysicalArenaDefinition>(true)).Single();
            var floor = definition.FloorBounds;
            var tent = LobbyMilitaryCampSources.TentBounds(floor);
            Vector3 origin = new Vector3(floor.center.x, floor.max.y, floor.center.z);
            var views = new List<View>
            {
                Ref("ref-forest", "exec-fe85be3b-c236-4948-8c0c-cc1700567d16.png", new Vector3(0,7.5f,-23),new Vector3(0,2.2f,0),54),
                Ref("ref-forest-low", "exec-fe85be3b-c236-4948-8c0c-cc1700567d16.png", new Vector3(0,4.2f,-20),new Vector3(0,2.2f,0),54),
                Ref("ref-forest-match", "exec-fe85be3b-c236-4948-8c0c-cc1700567d16.png", new Vector3(0,3.2f,tent.min.z-8.62f),new Vector3(0,2.2f,tent.center.z),54),
                Ref("ref-interior", "exec-700f8a54-dd3a-4841-a83e-f3b0ac41e580.png", new Vector3(0,1.65f,-7.2f),new Vector3(0,1.65f,26),58),
                Ref("ref-desert-composition", "exec-bd5609d3-9dc7-4252-9898-e01918f85387.png", new Vector3(1.4f,6.8f,-20),new Vector3(0,2,1),54),
                Ref("ref-mountain-composition", "exec-4d899d54-d590-4e86-9266-13b5de3c0417.png", new Vector3(-1.5f,4.6f,-24),new Vector3(0,2.4f,0),57)
            };
            views.Add(Eye("player-center-north", new Vector3(0,1.65f,0), 0));
            views.Add(Eye("player-center-south", new Vector3(0,1.65f,0), 180));
            foreach (float end in new[] { -1f, 1f })
            {
                string label = end > 0 ? "north" : "south";
                var eye = new Vector3(0,1.65f,end * (tent.extents.z - .78f));
                float outward = end > 0 ? 0 : 180;
                views.Add(Eye("player-" + label + "-edge-outward", eye, outward));
                views.Add(Eye("player-" + label + "-edge-back", eye, outward + 180));
                views.Add(Eye("player-" + label + "-edge-left", eye, outward - 75));
                views.Add(Eye("player-" + label + "-edge-right", eye, outward + 75));
            }
            views.Add(Eye("player-east-window", new Vector3(floor.extents.x - .45f,1.65f,6), 90));
            views.Add(Eye("player-west-window", new Vector3(-floor.extents.x + .45f,1.65f,LobbyMilitaryCampSources.SideWindowOffset(-1)), -90));
            Directory.CreateDirectory(DirectoryPath);
            var records = new List<object>();
            foreach (var view in views)
            {
                GameObject cameraObject = null;
                RenderTexture renderTarget = null;
                Texture2D texture = null;
                var priorTarget = RenderTexture.active;
                try
                {
                    cameraObject = new GameObject("LobbyReviewTemporaryCamera") { hideFlags = HideFlags.HideAndDontSave };
                    var camera = cameraObject.AddComponent<Camera>();camera.enabled = false;
                    cameraObject.transform.position = origin + view.eye;
                    cameraObject.transform.rotation = Quaternion.LookRotation(view.target - view.eye, Vector3.up);
                    camera.fieldOfView = view.fov;camera.aspect = (float)view.width / view.height;camera.nearClipPlane = .05f;camera.farClipPlane = 800;
                    camera.clearFlags = CameraClearFlags.Skybox;
                    renderTarget = RenderTexture.GetTemporary(view.width, view.height, 24, RenderTextureFormat.ARGB32);
                    camera.targetTexture = renderTarget;camera.Render();RenderTexture.active = renderTarget;
                    texture = new Texture2D(view.width, view.height, TextureFormat.RGB24, false);
                    texture.ReadPixels(new Rect(0,0,view.width,view.height),0,0);texture.Apply();
                    string file = DirectoryPath + view.name + ".png";
                    File.WriteAllBytes(file, texture.EncodeToPNG());
                    records.Add(new { name = view.name, path = file, reference = view.reference, width = view.width, height = view.height,
                        verticalFov = view.fov, eye = new[] { origin.x + view.eye.x, origin.y + view.eye.y, origin.z + view.eye.z },
                        lookAt = new[] { origin.x + view.target.x, origin.y + view.target.y, origin.z + view.target.z },
                        variant = "Forest", nominalPlayerEye = view.name.StartsWith("player-", StringComparison.Ordinal) });
                }
                finally
                {
                    RenderTexture.active = priorTarget;
                    if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                    if (renderTarget != null) RenderTexture.ReleaseTemporary(renderTarget);
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            File.WriteAllText(DirectoryPath + "capture-metadata.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
                capturedUtc = DateTime.UtcNow.ToString("O"), scene = scene.path, views = records,
                limits = "Forest один; Desert/Mountain обозначают композиционные ракурсы, не смену биома. Nominal eye1.65м, не Quest run. Gameplay не перемещён ради pixel match. Камеры временные, AudioListener не добавлен."
            }, Newtonsoft.Json.Formatting.Indented));
            GameLog.Debug.Info("[LobbyDecoration] Арт-ракурсы: " + records.Count + "; " + DirectoryPath);
        }

        private static View Ref(string name, string reference, Vector3 eye, Vector3 target, float fov)
        {
            var png = File.ReadAllBytes(ReferenceRoot + reference);
            Func<int,int> bigEndian = offset => (png[offset] << 24) | (png[offset+1] << 16) | (png[offset+2] << 8) | png[offset+3];
            int width = bigEndian(16), height = bigEndian(20);
            return new View { name = name, reference = ReferenceRoot + reference, eye = eye, target = target, fov = fov, width = 1024, height = Mathf.RoundToInt(1024f * height / width) };
        }
        private static View Eye(string name, Vector3 eye, float yaw) => new View { name = name, eye = eye, target = eye + Quaternion.Euler(0,yaw,0) * Vector3.forward * 30, fov = 70 };

        /// <summary>Диагностический кадр настоящей камеры аватара; её поза/проекция восстанавливаются в том же вызове.</summary>
        public static string CaptureRuntimeEye(string label, Vector3 eye, Vector3 target)
        {
            if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().name != "Lobby")
                throw new InvalidOperationException("Нужен собственный диагностический Play Lobby.");
            var camera = UltimateXR.Avatar.UxrAvatar.LocalAvatarCamera;
            if (!camera) throw new InvalidOperationException("Нет камеры локального аватара.");
            var position = camera.transform.position; var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView, aspect = camera.aspect, near = camera.nearClipPlane, far = camera.farClipPlane;
            var clear = camera.clearFlags; var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var renderTarget = RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);
            Texture2D texture = null;
            try
            {
                camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
                camera.fieldOfView = 70; camera.aspect = 1280f/720; camera.nearClipPlane = .05f; camera.farClipPlane = 800;
                camera.clearFlags = CameraClearFlags.Skybox; camera.targetTexture = renderTarget;
                camera.Render(); RenderTexture.active = renderTarget;
                texture = new Texture2D(1280,720,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,1280,720),0,0); texture.Apply();
                string directory = "tasks/lobby-decoration/reports/implementation/runtime-final/";
                Directory.CreateDirectory(directory); string path = directory + label + ".png";
                File.WriteAllBytes(path,texture.EncodeToPNG()); return path;
            }
            finally
            {
                camera.targetTexture = previousTarget; camera.clearFlags = clear;
                camera.fieldOfView = fov; camera.aspect = aspect; camera.nearClipPlane = near; camera.farClipPlane = far;
                camera.transform.SetPositionAndRotation(position,rotation);
                RenderTexture.active = previousActive; RenderTexture.ReleaseTemporary(renderTarget);
                if (texture) UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
