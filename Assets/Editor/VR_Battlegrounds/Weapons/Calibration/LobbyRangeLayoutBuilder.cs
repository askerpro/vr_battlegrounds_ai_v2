using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Две противоположные боковые станции и крупные щиты на свободных сторонах Lobby.</summary>
    public static class LobbyRangeLayoutBuilder
    {
        public const string ReportFolder="tmp/weapon-sight-calibration/lobby-range";
        public static string Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode required.");
            Scene scene=SceneManager.GetSceneByPath("Assets/Scenes/Lobby.unity");
            if(!scene.IsValid()||!scene.isLoaded)throw new InvalidOperationException("Откройте Lobby.");
            Directory.CreateDirectory(ReportFolder);
            if(!EditorSceneManager.SaveScene(scene,Path.GetFullPath(ReportFolder+"/Lobby-before-layout.unity"),true))throw new InvalidOperationException("Backup failed.");
            var gameplay=scene.GetRootGameObjects().Single(g=>g.name=="Gameplay").transform;
            var walls=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ArsenalWallController>(true)).ToArray();
            var keep=walls.Where(w=>Mathf.Abs(w.transform.position.x)>Mathf.Abs(w.transform.position.z)).ToArray();
            if(keep.Length!=2||keep[0].transform.position.x*keep[1].transform.position.x>=0)throw new InvalidOperationException("Не найдена пара боковых станций.");
            var remove=walls.Except(keep).Select(w=>PrefabUtility.GetOutermostPrefabInstanceRoot(w.gameObject)??w.gameObject).Distinct().ToArray();
            if(remove.Any(root=>keep.Any(w=>w.gameObject==root||w.transform.IsChildOf(root.transform))))throw new InvalidOperationException("Удаляемый prefab-root содержит сохраняемую станцию.");
            var targets=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ShootingTarget>(true))
                .Where(t=>t.name.StartsWith("ShootingTarget_",StringComparison.Ordinal)).OrderBy(t=>t.name,StringComparer.Ordinal).ToArray();
            if(targets.Length!=14&&targets.Length!=10)throw new InvalidOperationException("Ожидались 14 исходных или 10 размещённых щитов.");
            var definition=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PhysicalArenaDefinition>(true)).Single();
            if(!definition.Valid(out var arenaReason)||!definition.TryFloorBounds(out var arenaBounds))throw new InvalidOperationException("Нет границ физической арены: "+arenaReason);
            foreach(var target in targets)
                if(target.Pivot==null||target.Pivot.Find("Plate")==null||target.Pivot.Find("RingOuter")==null||target.transform.Find("Post")==null||target.transform.Find("Foot")==null)
                    throw new InvalidOperationException("Неполная геометрия щита: "+target.name);
            if(!AssetDatabase.IsValidFolder("Assets/Art/Weapons/Sights/Review")||Shader.Find("Universal Render Pipeline/Lit")==null||Shader.Find("Universal Render Pipeline/Unlit")==null)
                throw new InvalidOperationException("Нет папки/шейдеров монитора.");
            remove=remove.Concat(targets.Skip(10).Select(t=>t.gameObject)).ToArray();
            targets=targets.Take(10).ToArray();
            var removedObjects=new HashSet<UnityEngine.Object>();
            foreach(var root in remove)foreach(var node in root.GetComponentsInChildren<Transform>(true))
            {removedObjects.Add(node.gameObject);foreach(var component in node.GetComponents<Component>())if(component!=null)removedObjects.Add(component);}
            foreach(var component in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c!=null&&!removedObjects.Contains(c)))
            {
                var serialized=new SerializedObject(component);var p=serialized.GetIterator();bool changed=false;
                while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue!=null&&removedObjects.Contains(p.objectReferenceValue))
                {p.objectReferenceValue=null;changed=true;}
                if(changed)serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach(var root in remove)UnityEngine.Object.DestroyImmediate(root);
            var range=gameplay.Find("ShootingRange");if(range==null){range=new GameObject("ShootingRange").transform;range.SetParent(gameplay,false);}
            var oldReview=gameplay.Find("WeaponSightReview");
            if(oldReview!=null)UnityEngine.Object.DestroyImmediate(oldReview.gameObject);
            if(range.GetComponent<ShootingRangePhotoCapture>()==null)range.gameObject.AddComponent<ShootingRangePhotoCapture>();
            float[] distances={10,15,20,25,30};
            float[] offsets={-1.1f,1.1f,-4f,4.5f,-8f};
            var rows=new List<object>();
            for(int i=0;i<targets.Length;i++)
            {
                int station=i%5;int side=i<5?1:-1;
                Vector3 origin=new Vector3(arenaBounds.center.x,1.5f,side>0?arenaBounds.max.z:arenaBounds.min.z);
                Vector3 direction=new Vector3(offsets[station],0,side*Mathf.Sqrt(distances[station]*distances[station]-offsets[station]*offsets[station])).normalized;
                Vector3 centre=origin+direction*distances[station];
                var target=targets[i];target.transform.SetParent(range,true);target.transform.localScale=Vector3.one;
                var settings=new SerializedObject(target);settings.FindProperty("_fallOnHit").boolValue=false;settings.ApplyModifiedPropertiesWithoutUndo();
                target.transform.SetPositionAndRotation(new Vector3(centre.x,0,centre.z),Quaternion.LookRotation(-direction,Vector3.up));
                var pivot=target.Pivot;pivot.localPosition=new Vector3(0,1.14f,0);pivot.localRotation=Quaternion.identity;pivot.localScale=Vector3.one;
                var plate=pivot.Find("Plate");plate.localPosition=new Vector3(0,.36f,0);plate.localScale=new Vector3(.6f,.7f,.03f);
                if(plate.GetComponent<BoxCollider>()==null)plate.gameObject.AddComponent<BoxCollider>();
                var ring=pivot.Find("RingOuter");var extra=ring.GetComponent<MeshCollider>();if(extra!=null)UnityEngine.Object.DestroyImmediate(extra);
                var post=target.transform.Find("Post");post.localPosition=new Vector3(0,.57f,-.05f);post.localScale=new Vector3(.07f,1.14f,.07f);
                var foot=target.transform.Find("Foot");foot.localPosition=new Vector3(0,.03f,0);foot.localScale=new Vector3(.5f,.06f,.35f);
                var labelRoot=target.transform.Find("RangeDistanceLabel");
                if(labelRoot==null)labelRoot=new GameObject("RangeDistanceLabel").transform;
                labelRoot.SetParent(target.transform,false);labelRoot.SetPositionAndRotation(centre+Vector3.up*.53f,target.transform.rotation);labelRoot.localScale=Vector3.one;
                var label=labelRoot.GetComponent<TextMeshPro>();if(label==null)label=labelRoot.gameObject.AddComponent<TextMeshPro>();
                label.text=distances[station]+" м";label.font=TMP_Settings.defaultFontAsset;label.fontSize=12;label.alignment=TextAlignmentOptions.Center;
                label.rectTransform.sizeDelta=new Vector2(100,10);label.ForceMeshUpdate();if(label.textBounds.size.y>0)label.transform.localScale=Vector3.one*(.12f/label.textBounds.size.y);
                foreach(var node in target.GetComponentsInChildren<Transform>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(node);
                rows.Add(new {name=target.name,side=side>0?"North":"South",distance=distances[station],position=new[]{centre.x,centre.y,centre.z}});
            }
            foreach(string oldName in new[]{"ShootingPosition","RangeMonitor"})
            {var old=range.Find(oldName);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);}
            foreach(int side in new[]{1,-1})
            {
                string sideName=side>0?"North":"South";
                float edge=side>0?arenaBounds.max.z:arenaBounds.min.z;
                var originLabel=range.Find("ShootingPosition"+sideName);if(originLabel==null)originLabel=new GameObject("ShootingPosition"+sideName).transform;
                originLabel.SetParent(range,false);originLabel.position=new Vector3(arenaBounds.center.x,.03f,edge-side*.5f);originLabel.rotation=Quaternion.Euler(90,side>0?0:180,0);
                var text=originLabel.GetComponent<TextMeshPro>();if(text==null)text=originLabel.gameObject.AddComponent<TextMeshPro>();
                text.text="Стрельбище · дистанции от края";text.font=TMP_Settings.defaultFontAsset;text.fontSize=12;text.alignment=TextAlignmentOptions.Center;text.rectTransform.sizeDelta=new Vector2(100,10);
                text.ForceMeshUpdate();if(text.textBounds.size.y>0)text.transform.localScale=Vector3.one*(.12f/text.textBounds.size.y);
                BuildMonitor(range,"RangeMonitor"+sideName,new Vector3(arenaBounds.center.x+2,0,edge+side*5),side>0?Quaternion.identity:Quaternion.Euler(0,180,0));
            }
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Scene save failed.");
            File.WriteAllText(ReportFolder+"/layout.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{stations=2,targets=rows,boardMetres=new[]{.6,.7}},Newtonsoft.Json.Formatting.Indented));
            return "Layout saved: 2 side stations, 10 stationary targets outside arena at 10/15/20/25/30 m, 2 forward monitors.";
        }

        private static void BuildMonitor(Transform range,string name,Vector3 position,Quaternion rotation)
        {
            var monitor=range.Find(name);
            if(monitor==null){monitor=new GameObject(name).transform;monitor.SetParent(range,false);}
            monitor.SetPositionAndRotation(position,rotation);
            string folder="Assets/Art/Weapons/Sights/Review";
            Material shell=AssetDatabase.LoadAssetAtPath<Material>(folder+"/MonitorShell.mat");
            if(shell==null){shell=new Material(Shader.Find("Universal Render Pipeline/Lit"));shell.SetColor("_BaseColor",new Color(.06f,.07f,.08f));AssetDatabase.CreateAsset(shell,folder+"/MonitorShell.mat");}
            Material picture=AssetDatabase.LoadAssetAtPath<Material>(folder+"/MonitorPicture.mat");
            if(picture==null){picture=new Material(Shader.Find("Universal Render Pipeline/Unlit"));picture.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(picture,folder+"/MonitorPicture.mat");}
            var frame=Part("Frame",PrimitiveType.Cube,new Vector3(0,1.6f,0),new Vector3(1.65f,1.65f,.08f),shell,true);
            Part("Post",PrimitiveType.Cube,new Vector3(0,.4f,.04f),new Vector3(.08f,.8f,.08f),shell,true);
            Part("Foot",PrimitiveType.Cube,new Vector3(0,.03f,0),new Vector3(.65f,.06f,.5f),shell,true);
            var screen=Part("Screen",PrimitiveType.Quad,new Vector3(0,1.6f,-.045f),new Vector3(1.5f,1.5f,1),picture,false).GetComponent<Renderer>();
            var caption=monitor.Find("Caption");if(caption==null){caption=new GameObject("Caption").transform;caption.SetParent(monitor,false);}
            caption.localPosition=new Vector3(0,2.66f,-.05f);caption.localRotation=Quaternion.identity;caption.localScale=Vector3.one;
            var label=caption.GetComponent<TextMeshPro>();if(label==null)label=caption.gameObject.AddComponent<TextMeshPro>();
            label.text="Стрельбище\nПопадите в щит";label.font=TMP_Settings.defaultFontAsset;label.fontSize=2;label.alignment=TextAlignmentOptions.Center;label.rectTransform.sizeDelta=new Vector2(2.4f,.45f);
            var display=monitor.GetComponent<ShootingRangeDisplay>();if(display==null)display=monitor.gameObject.AddComponent<ShootingRangeDisplay>();
            var serialized=new SerializedObject(display);serialized.FindProperty("_capture").objectReferenceValue=range.GetComponent<ShootingRangePhotoCapture>();
            serialized.FindProperty("_screen").objectReferenceValue=screen;serialized.FindProperty("_caption").objectReferenceValue=label;serialized.ApplyModifiedPropertiesWithoutUndo();
            Transform Part(string name,PrimitiveType type,Vector3 position,Vector3 size,Material material,bool collider)
            {
                var found=monitor.Find(name);GameObject go=found!=null?found.gameObject:GameObject.CreatePrimitive(type);
                go.name=name;go.transform.SetParent(monitor,false);go.transform.localPosition=position;go.transform.localRotation=Quaternion.identity;go.transform.localScale=size;
                go.GetComponent<Renderer>().sharedMaterial=material;
                if(!collider&&go.GetComponent<Collider>()!=null)UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                return go.transform;
            }
        }
    }
}
