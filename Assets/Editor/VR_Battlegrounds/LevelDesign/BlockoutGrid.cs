using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Привязка к явному паспорту арены; имена объектов не являются источником origin или ролей.</summary>
    [InitializeOnLoad]
    public static class BlockoutGrid
    {
        private static Scene cellScene;
        private static VrBattlegrounds.LevelDesign.PhysicalArenaDefinition cellArena;
        private static bool sourceDirty=true, snapPending;
        public static float Cell
        {
            get
            {
                var scene=SceneManager.GetActiveScene();
                if(sourceDirty||cellScene!=scene) {cellScene=scene;cellArena=PhysicalArenaPanel.Find(scene);sourceDirty=false;}
                return cellArena!=null ? cellArena.gridStep : BlockoutGridSettings.Current.step;
            }
        }
        public static float Module => BlockoutGridSettings.Current.module;
        public const float PositionSize = 1.5f;
        private const string Prefix = "VrBattlegrounds.BlockoutGrid.";
        private const string Menu = "Tools/VR Battlegrounds/Level Design/Blocking/Build/Grid/";
        private const string Alphabet = "Assets/Prefabs/LevelDesign/LD_Alphabet/";
        private static double nextUpdate;
        private static bool applying;

        static BlockoutGrid()
        {
            SceneView.duringSceneGui += Draw;
            EditorApplication.update += Update;
            EditorApplication.hierarchyChanged += InvalidateSource;
            ObjectChangeEvents.changesPublished += ObjectsChanged;
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawDimensions;
        }
        private static void InvalidateSource() => sourceDirty=true;
        private static void ObjectsChanged(ref ObjectChangeEventStream stream)
        {
            sourceDirty=true;
            for(int i=0;i<stream.length;i++)
                if(stream.GetEventType(i)==ObjectChangeKind.ChangeGameObjectOrComponentProperties)
                {
                    stream.GetChangeGameObjectOrComponentPropertiesEvent(i,out var change);
                    if(change.scene!=SceneManager.GetActiveScene()) continue;
                    var component=EditorUtility.InstanceIDToObject(change.instanceId) as Component;
                    if(component!=null && component.gameObject.name.StartsWith("LD_",StringComparison.Ordinal)
                        && (component.gameObject.hideFlags&HideFlags.DontSave)==0) snapPending=true;
                }
        }

        private static string Key(Scene scene) => Prefix + AssetDatabase.AssetPathToGUID(scene.path);
        public static bool Enabled(Scene scene) => scene.IsValid() && !string.IsNullOrEmpty(scene.path)
            && EditorPrefs.GetBool(Key(scene), false);

        public static void EnableActive()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!TryOrigin(scene, out _, out _))
                throw new InvalidOperationException("Настройте PhysicalArenaDefinition, пол и origin во вкладке «Арена».");
            EditorPrefs.SetBool(Key(scene), true);
            EditorSnapSettings.move = Vector3.one * Cell;
            EditorSnapSettings.rotate = 15;
            SceneView.RepaintAll();
        }

        public static void DisableActive()
        {
            EditorPrefs.SetBool(Key(SceneManager.GetActiveScene()), false);
            SceneView.RepaintAll();
        }

        private static void SnapActive() => Snap(SceneManager.GetActiveScene(), true);

        // Настройки личного редактора; сцены других агентов не включаются автоматически.
        private static void Update()
        {
            if (!snapPending || applying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextUpdate) return;
            nextUpdate = EditorApplication.timeSinceStartup + .15;
            snapPending=false;
            // Нормализация геометрии разрешена только явной операцией обслуживания.
        }

        public static bool TryOrigin(Scene scene, out Vector2 origin, out Collider[] pillars)
        {
            origin = default;
            pillars = Array.Empty<Collider>();
            var arena = PhysicalArenaPanel.Find(scene);
            if (arena != null)
            {
                if (!arena.Valid(out _)) return false;
                origin = arena.WorldOrigin;
                pillars = arena.GetComponentsInChildren<VrBattlegrounds.LevelDesign.PhysicalObstacleMarker>(true)
                    .Where(m => m.useColliders).SelectMany(m => m.sourceColliders ?? Array.Empty<Collider>()).Where(c => c != null).Distinct().ToArray();
                return true;
            }
            // Неподготовленная арена требует явной настройки; имена объектов не задают origin.
            return false;
        }

        public static bool TryFloor(Scene scene, out Bounds floor)
        {
            floor=default;
            var arena=PhysicalArenaPanel.Find(scene);
            if(arena!=null)
            {
                if(!arena.Valid(out _))return false;
                return BlockoutSupportSurfaces.TryMapFloor(scene, out floor);
            }
            return false;
        }

        public static GameObject[] Blocks(Scene scene)
        {
            var found = new HashSet<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.activeInHierarchy) continue;
                foreach (var t in root.GetComponentsInChildren<Transform>(false))
                {
                    if(t.GetComponentInParent<VrBattlegrounds.LevelDesign.PhysicalObstacleMarker>()!=null||BlockoutBlockRoles.IsProtection(t.gameObject))continue;
                    if (t.TryGetComponent<VrBattlegrounds.LevelDesign.BlockoutCellWall>(out _)||t.TryGetComponent<VrBattlegrounds.LevelDesign.BlockoutBlockInstance>(out _)) found.Add(t.gameObject);
                    var go = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                    if (go != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go).StartsWith(Alphabet))
                        found.Add(go);
                }
            }
            return found.ToArray();
        }

        public static float SnapEdge(float value, float origin) => origin + Mathf.Round((value - origin) / Cell) * Cell;

        public static int Snap(Scene scene, bool recordUndo)
        {
            if (applying || !TryOrigin(scene, out Vector2 origin, out _)) return 0;
            applying = true;
            int changed = 0;
            try
            {
                if(!TryFloor(scene,out Bounds floor))return 0;
                foreach (var go in Blocks(scene))
                {
                    if(BlockoutRegistryFactory.TryDefinition(go,out _))
                    {
                        go.GetComponent<VrBattlegrounds.LevelDesign.BlockoutSectionGeometry>()?.Rebuild();
                        var registeredPosition=go.transform.position;
                        var snapped=new Vector3(SnapEdge(registeredPosition.x,origin.x),registeredPosition.y,SnapEdge(registeredPosition.z,origin.y));
                        var registeredWall=go.GetComponent<VrBattlegrounds.LevelDesign.BlockoutCellWall>();
                        float registeredYaw=registeredWall!=null?registeredWall.RotationYaw:go.transform.eulerAngles.y;
                        if(registeredPosition!=snapped&&BlockoutRegistryFactory.ApplyTransform(go,snapped,registeredYaw,out _)
                            &&go.transform.position!=registeredPosition)changed++;
                        continue;
                    }
                    if (go.TryGetComponent<VrBattlegrounds.LevelDesign.BlockoutCellWall>(out var wall))
                    {
                        var position = go.transform.position;
                        var snapped = new Vector3(SnapEdge(position.x,origin.x),position.y,SnapEdge(position.z,origin.y));
                        if (position != snapped)
                        {
                            if(BlockoutWallEditing.Conflict(scene,floor,wall.PartsAt(snapped,go.transform.rotation),go)) continue;
                            Undo.RecordObject(go.transform,"Привязать клеточную стену");
                            go.transform.position=snapped; changed++;
                        }
                        continue;
                    }
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go));
                    if (source == null) continue;
                    Transform t = go.transform;
                    var sourceFilter=source.GetComponent<MeshFilter>();
                    if(sourceFilter!=null && sourceFilter.sharedMesh!=null)
                    {
                        int oldSuffix=go.name.IndexOf(" [",StringComparison.Ordinal);
                        string prefix=oldSuffix<0 ? go.name : go.name.Substring(0,oldSuffix);
                        string displayName=prefix+" ["+FormatSize(sourceFilter.sharedMesh.bounds.size)+" m]";
                        if(go.name!=displayName)
                        {
                            Undo.RecordObject(go,"Update block dimensions in name");
                            go.name=displayName;
                            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
                            changed++;
                        }
                    }
                    // Родительские организационные группы не должны растягивать геометрию.
                    for (var parent = t.parent; parent != null; parent = parent.parent)
                        if ((parent.localScale - Vector3.one).sqrMagnitude > .000001f)
                        {
                            Undo.RecordObject(parent, "Restore blockout group scale");
                            parent.localScale = Vector3.one;
                            changed++;
                        }
                    Vector3 before = t.position;
                    Quaternion beforeRotation = t.rotation;
                    Vector3 beforeScale = t.localScale;
                    float yaw = (t.rotation * Quaternion.Inverse(source.transform.localRotation)).eulerAngles.y;
                    Quaternion rotation = Quaternion.Euler(0, Mathf.Round(yaw / 90) * 90, 0);
                    // Beam имеет штатный наклон внутри корня; сохраняем его относительно сетки.
                    rotation *= source.transform.localRotation;
                    Undo.RecordObject(t, "Snap LD block to grid");
                    t.rotation = rotation;
                    t.localScale = source.transform.localScale;
                    Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
                    if (renderers.Length == 0) continue;
                    Bounds bounds = renderers[0].bounds;
                    foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    t.position += new Vector3(SnapEdge(bounds.min.x, origin.x) - bounds.min.x, 0,
                        SnapEdge(bounds.min.z, origin.y) - bounds.min.z);
                    if ((t.position-before).sqrMagnitude > .00000001f || Quaternion.Angle(t.rotation, beforeRotation) > .001f
                        || (t.localScale-beforeScale).sqrMagnitude > .00000001f)
                    {
                        var afterBounds=BlockoutPainter.WorldBounds(go);
                        if(BlockoutWallEditing.PrefabConflict(source,scene,floor,afterBounds,Mathf.RoundToInt(yaw/90),go))
                        { t.SetPositionAndRotation(before,beforeRotation); t.localScale=beforeScale; continue; }
                        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                        changed++;
                    }
                }
                if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
                return changed;
            }
            finally { applying = false; }
        }

        private static void Draw(SceneView view)
        {
            if(Event.current.type!=EventType.Repaint) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!Enabled(scene) || !TryOrigin(scene, out Vector2 origin, out Collider[] pillars)) return;
            if(!TryFloor(scene,out Bounds arena))return;
            float y = arena.max.y + .015f;
            float cell=Cell;
            var oldTest = Handles.zTest;
            var oldColor = Handles.color;
            Handles.zTest = CompareFunction.Always;
            try
            {
                int x0 = Mathf.CeilToInt((arena.min.x-origin.x)/cell), x1 = Mathf.FloorToInt((arena.max.x-origin.x)/cell);
                int z0 = Mathf.CeilToInt((arena.min.z-origin.y)/cell), z1 = Mathf.FloorToInt((arena.max.z-origin.y)/cell);
                for (int i=x0; i<=x1; i++)
                {
                    Handles.color = LineColor(i,cell);
                    float x=origin.x+i*cell;
                    Handles.DrawLine(new Vector3(x,y,arena.min.z), new Vector3(x,y,arena.max.z));
                }
                for (int i=z0; i<=z1; i++)
                {
                    Handles.color = LineColor(i,cell);
                    float z=origin.y+i*cell;
                    Handles.DrawLine(new Vector3(arena.min.x,y,z), new Vector3(arena.max.x,y,z));
                }
                var definition = PhysicalArenaPanel.Find(scene);
                foreach (var marker in definition.GetComponentsInChildren<VrBattlegrounds.LevelDesign.PhysicalObstacleMarker>(true))
                {
                    if (!marker.TryBounds(definition, out Bounds b, out _)) continue;
                    int a=Mathf.FloorToInt((b.min.x-origin.x+.0001f)/cell), c=Mathf.CeilToInt((b.max.x-origin.x-.0001f)/cell);
                    int d=Mathf.FloorToInt((b.min.z-origin.y+.0001f)/cell), e=Mathf.CeilToInt((b.max.z-origin.y-.0001f)/cell);
                    for (int ix=a;ix<c;ix++) for (int iz=d;iz<e;iz++)
                    {
                        float x=origin.x+ix*cell,z=origin.y+iz*cell;
                        Handles.DrawSolidRectangleWithOutline(new[]{new Vector3(x,y,z),new Vector3(x+cell,y,z),
                            new Vector3(x+cell,y,z+cell),new Vector3(x,y,z+cell)},new Color(1,.35f,.1f,.2f),Color.yellow);
                    }
                }
                Handles.color=Color.yellow;
                Handles.Label(new Vector3(origin.x,y,origin.y), $"0 · арена / шаг {Cell:0.##} м; модуль {Module:0.##} м; позиция 1.5×1.5 м");
            }
            finally { Handles.zTest=oldTest; Handles.color=oldColor; }
        }

        private static Color LineColor(int index,float cell)
        {
            float distance = index * cell;
            bool position = Mathf.Abs(distance/PositionSize-Mathf.Round(distance/PositionSize))<.001f;
            bool module = Mathf.Abs(distance/Module-Mathf.Round(distance/Module))<.001f;
            return new Color(.2f,.9f,1,position ? .65f : module ? .35f : .15f);
        }

        private static string FormatSize(Vector3 size) => size.x.ToString("0.##",CultureInfo.InvariantCulture)+"x"
            +size.y.ToString("0.##",CultureInfo.InvariantCulture)+"x"+size.z.ToString("0.##",CultureInfo.InvariantCulture);

        private static void DrawDimensions(UnityEditor.Editor editor)
        {
            var go=editor.target as GameObject;
            if(go==null) return;
            string path=AssetDatabase.GetAssetPath(go);
            if(string.IsNullOrEmpty(path)) path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if(string.IsNullOrEmpty(path) || !path.StartsWith(Alphabet,StringComparison.Ordinal)) return;
            var filter=go.GetComponent<MeshFilter>();
            if(filter==null || filter.sharedMesh==null) return;
            Vector3 size=Vector3.Scale(filter.sharedMesh.bounds.size,go.transform.lossyScale);
            EditorGUILayout.LabelField("Габариты, м",FormatSize(size));
            EditorGUILayout.LabelField("Штатный Scale", "1 x 1 x 1; размер задаётся мешем");
        }
    }
}
