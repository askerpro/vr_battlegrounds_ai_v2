using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Переключение конструкции и позы единого стенда без запуска сетевой игры.</summary>
    [InitializeOnLoad]
    public static class ArsenalBoundaryReviewPreview
    {
        private static readonly Dictionary<int,float> Progress = new Dictionary<int,float>();
        private static readonly Dictionary<int,Matrix4x4> MarkerMatrices = new Dictionary<int,Matrix4x4>();
        private static int _markerScene;
        private static Scene _animatedScene;
        private static double _animationStarted;
        private static bool _appearing;
        private static bool _animating;

        static ArsenalBoundaryReviewPreview()
        {
            SceneView.duringSceneGui += Draw;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if(state==PlayModeStateChange.ExitingEditMode)
                {
                    _animating=false;
                    var scene=SceneManager.GetActiveScene();
                    if(scene.path=="Assets/Scenes/Tools/CommonArsenalReview.unity") Apply(scene,1f,false);
                }
            };
        }

        private static void Draw(SceneView view)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/Tools/CommonArsenalReview.unity") return;
            var wall = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalBoundaryWall>(true)).FirstOrDefault();
            if (wall == null) return;
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12, 12, 390, 210), GUI.skin.box);
            GUILayout.Label("Арсенал в нише задней стены");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Открыто")) Apply(scene, 1f);
            if (GUILayout.Button("Закрыто")) Apply(scene, 0f);
            GUILayout.EndHorizontal();
            if(GUILayout.Button("Настройка закрытой позы")){_animating=false;Apply(scene,0f);}
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("▶ Выдвинуть")) Play(scene,true);
            if(GUILayout.Button("▶ Убрать")) Play(scene,false);
            if(GUILayout.Button("Стоп")) _animating=false;
            GUILayout.EndHorizontal();
            float pose=GetPose(scene);
            float slider=GUILayout.HorizontalSlider(pose,0f,1f);
            if(Mathf.Abs(slider-pose)>.0001f){_animating=false;Apply(scene,slider);}
            GUILayout.Label(GetPose(scene) < .01f ? "Содержимое убрано" : "Содержимое предоставлено");
            GUILayout.Label("Точки: PresentationRoot / DeploymentPoses");
            if(GUILayout.Button("Пересобрать ниши стены"))
            {
                ArsenalWallOpenings.Apply(scene);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        /// <summary>Редакторская поза стенда; не вызывает серверные команды и правила фаз.</summary>
        public static void Apply(Scene scene, float raised, bool recordUndo = true)
        {
            if (EditorApplication.isPlaying || AnimationMode.InAnimationMode()) return;
            Progress[scene.handle]=raised;
            var roots = scene.GetRootGameObjects();
            var wall = roots.SelectMany(r => r.GetComponentsInChildren<ArsenalBoundaryWall>(true)).First();
            if(recordUndo)Undo.RegisterFullObjectHierarchyUndo(wall.gameObject, "Поза ограды");
            foreach (var station in roots.SelectMany(r => r.GetComponentsInChildren<ArsenalDeploymentAnimator>(true)))
            {
                if(recordUndo)Undo.RegisterFullObjectHierarchyUndo(station.gameObject, "Поза арсенала");
                var equipment=station.GetComponent<ArsenalEquipmentPoses>();
                if(equipment!=null)equipment.Apply(raised);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
        }

        private static float GetPose(Scene scene)
        {
            float progress;if(Progress.TryGetValue(scene.handle,out progress))return progress;
            var station = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalDeploymentAnimator>(true)).FirstOrDefault();
            if (station == null) return 1f;
                var equipment=station.GetComponent<ArsenalEquipmentPoses>();
                if(equipment!=null)
                    foreach(var pose in equipment.Targets)
                    {
                        if(pose.Target==null||pose.ClosedPose==null||pose.OpenPose==null)continue;
                        var delta=pose.OpenPose.position-pose.ClosedPose.position;
                        if(delta.sqrMagnitude<.000001f)continue;
                        float t=Mathf.Clamp01(Vector3.Dot(pose.Target.position-pose.ClosedPose.position,delta)/delta.sqrMagnitude);
                        // Обратная функция SmoothStep сохраняет положение ползунка после reload.
                        return .5f-Mathf.Sin(Mathf.Asin(1f-2f*t)/3f);
                    }
                return 1f;
        }

        public static void Play(Scene scene,bool appearing)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||AnimationMode.InAnimationMode())return;
            _animatedScene=scene;_appearing=appearing;_animationStarted=EditorApplication.timeSinceStartup;_animating=true;
            Apply(scene,appearing?0f:1f);
        }

        private static void Tick()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||AnimationMode.InAnimationMode())return;
            if(!_animating)
            {
                RefreshMarkerPreview(SceneManager.GetActiveScene());
                return;
            }
            if(!_animatedScene.IsValid()||!_animatedScene.isLoaded||EditorApplication.isPlayingOrWillChangePlaymode){_animating=false;return;}
            var motion=new ArsenalDeploymentAnimator.DeploymentMotion{From=_appearing?0f:1f,To=_appearing?1f:0f,
                StartedAt=_animationStarted,Duration=1.35f};
            Apply(_animatedScene,ArsenalDeploymentAnimator.Evaluate(motion,EditorApplication.timeSinceStartup),false);
            if(EditorApplication.timeSinceStartup-_animationStarted>=1.35)_animating=false;
        }

        // Следим за маркерами, а не за hasChanged: не сбрасываем флаги, используемые другими редакторами.
        private static void RefreshMarkerPreview(Scene scene)
        {
            if(!scene.IsValid()||!scene.isLoaded||scene.path!="Assets/Scenes/Tools/CommonArsenalReview.unity")return;
            if(_markerScene!=scene.handle){MarkerMatrices.Clear();_markerScene=scene.handle;}
            float progress=GetPose(scene);
            Progress[scene.handle]=progress;
            bool changed=false;
            foreach(var equipment in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ArsenalEquipmentPoses>(true)))
                foreach(var pose in equipment.Targets)
                    foreach(var marker in new[]{pose.ClosedPose,pose.OpenPose})
                    {
                        if(marker==null)continue;
                        int id=marker.GetInstanceID();
                        Matrix4x4 previous;
                        var current=marker.localToWorldMatrix;
                        if(MarkerMatrices.TryGetValue(id,out previous)&&previous!=current)changed=true;
                        MarkerMatrices[id]=current;
                    }
            if(changed)Apply(scene,progress,false);
        }
    }
}
