using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Декоративная граница базы, организованные зоны и реальные скальные массивы за игровой ареной.</summary>
    public static class LobbyBaseBackdropBuilder
    {
        private const string Report = "tasks/lobby-decoration/reports/implementation/exterior-backdrop/";
        private static readonly Dictionary<string, GameObject> Sources = new Dictionary<string, GameObject>();
        private static readonly List<string> Missing = new List<string>();
        private static readonly List<string> Notes = new List<string>();
        [Serializable] private sealed class BuildReport { public string[] missing; public string[] notes; public int fenceSections; }

        public static void Build(Transform parent, Bounds arena, Bounds tent)
        {
            if (!parent) throw new ArgumentNullException(nameof(parent));
            if (Mathf.Abs(tent.size.x - arena.size.x) > .05f || Mathf.Abs(tent.size.z - (arena.size.z - 4)) > .05f)
                throw new ArgumentException("Backdrop ожидает актуальный шатёр по ширине арены и с двумя полосами по 2 м.");
            if (parent.Find("BaseBackdrop")) throw new InvalidOperationException("BaseBackdrop уже существует: подготовьте новый общий draft.");
            Sources.Clear(); Missing.Clear(); Notes.Clear();
            foreach (string stem in new[] { "SM_Fence_long_4m", "SM_Container_a", "SM_Container_b", "SM_Cliff_01", "SM_Cliff_02", "SM_Cliff_03", "SM_Cliff_04", "SM_WatchTower_01", "SM_Table", "SM_Chair" })
                Source(stem, true);
            var root = Group("BaseBackdrop", parent);
            float ground = arena.max.y;
            Vector3 origin = new Vector3(arena.center.x, ground, arena.center.z);
            var fence = Group("DecorativeBaseChainMesh", root);
            int sections = FenceRun(fence, origin + new Vector3(-36,0,45), origin + new Vector3(36,0,45));
            sections += FenceRun(fence, origin + new Vector3(-36,0,-38), origin + new Vector3(24,0,-38));
            sections += FenceRun(fence, origin + new Vector3(32,0,-38), origin + new Vector3(36,0,-38));
            sections += FenceRun(fence, origin + new Vector3(-36,0,-38), origin + new Vector3(-36,0,45));
            sections += FenceRun(fence, origin + new Vector3(36,0,-38), origin + new Vector3(36,0,45));

            var storage = Group("OrganizedContainerStores", root);
            Shipping(storage, "NorthWestWarehouseA", origin + new Vector3(-26,0,34), 0, false);
            Shipping(storage, "NorthWestWarehouseB", origin + new Vector3(-26,0,38), 0, false);
            Shipping(storage, "NorthWestShortStore", origin + new Vector3(-20,0,37), 0, true);
            Backstop(root, origin, tent.max.z + 20.52f);

            var banks = Group("LayeredPaidRockMasses", root);
            var northBanks = Group("NorthRangeCliffLayers", banks);
            Bank(northBanks, origin + new Vector3(-28,0,58), 7f, -18);
            Bank(northBanks, origin + new Vector3(-16,0,60), 6.5f, 17);
            Bank(northBanks, origin + new Vector3(-7,0,62), 4f, -8);
            Bank(northBanks, origin + new Vector3(0,0,63), 4f, 12);
            Bank(northBanks, origin + new Vector3(7,0,62), 4f, 25);
            Bank(northBanks, origin + new Vector3(17,0,60), 6f, -12);
            Bank(northBanks, origin + new Vector3(29,0,58), 7f, 37);
            // Связные боковые склоны за сеткой: шаг 13 м, небольшая нерегулярность высоты и поворота.
            for (int i = 0; i < 8; i++)
            {
                float z = 43 - i * 13;
                Bank(banks, origin + new Vector3(-49 + Mathf.Sin(i * 1.7f), 0, z), 5.5f + .5f * Mathf.Sin(i * 2.1f), 80 + 12 * Mathf.Sin(i));
                Bank(banks, origin + new Vector3(49 + Mathf.Sin(i * 1.3f), 0, z - 1), 5.7f + .5f * Mathf.Cos(i * 1.9f), -85 + 11 * Mathf.Sin(i));
            }
            Bank(banks, origin + new Vector3(-27,0,-55), 5.8f, 160);
            Bank(banks, origin + new Vector3(-13,0,-58), 4.8f, 191);
            Bank(banks, origin + new Vector3(20,0,-57), 5.2f, 178);
            Notes.Add("Buildings reserved: west X[-31,-16] Z[-24,-8]; east X[18,30] Z[-29,-13]. Separate owner, not built here.");
            Notes.Add("Fence is decorative base boundary, not physical arena/navigation. Existing 104 trees and distant low belt are untouched.");
            SaveReport(sections);
        }

        private static GameObject Source(string stem, bool required)
        {
            if (Sources.TryGetValue(stem, out var cached)) return cached;
            GameObject source = null;
            try { source = LobbyMilitaryCampSources.Prefab(stem); }
            catch (InvalidOperationException) { }
            if (!source)
            {
                if (!Missing.Contains(stem)) Missing.Add(stem);
                SaveReport(0);
                if (required) throw new InvalidOperationException("Не готов обязательный paid source: " + stem);
                return null;
            }
            Sources.Add(stem, source);
            return source;
        }

        private static int FenceRun(Transform parent, Vector3 start, Vector3 end)
        {
            Vector3 direction = (end-start).normalized;
            float yaw = Mathf.Atan2(-direction.z, direction.x) * Mathf.Rad2Deg;
            var probe = Place("SM_Fence_long_4m", "FenceSection", parent, start, Vector3.zero, 1);
            float nativeWidth = BoundsIn(probe, parent).size.x;
            int count = Mathf.CeilToInt(Vector3.Distance(start,end) / nativeWidth);
            float step = Vector3.Distance(start,end) / count;
            UnityEngine.Object.DestroyImmediate(probe);
            for (int i=0;i<count;i++)
            {
                var section=Place("SM_Fence_long_4m", "FenceSection", parent, start+direction*(step*(i+.5f)), new Vector3(0,yaw,0), (step+.025f)/nativeWidth);
                foreach(var renderer in section.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>source && source.name.IndexOf("Wire",StringComparison.OrdinalIgnoreCase)>=0 ? WireVariant(source) : source).ToArray();
            }
            return count;
        }

        private static void Shipping(Transform parent, string name, Vector3 foot, float yaw, bool small)
        {
            Place(small ? "SM_Container_b" : "SM_Container_a", name, parent, foot, new Vector3(0,yaw,0), 1);
            // Корпус shipping-scale настоящий; отсутствие отдельных створок заявляется явно, не маскируется плоскостью.
            Notes.Add(name + ": container body; door closure belongs to integration/source owner.");
        }

        private static void Checkpoint(Transform parent, Vector3 origin)
        {
            var site = Group("SouthEastCheckpointGateAndTower", parent);
            var tower = Place("SM_WatchTower_01", "CheckpointWatchtower", site, origin+new Vector3(47,0,-50), new Vector3(0,22,0), 1);
            float scale = 8f/BoundsIn(tower,site).size.y;
            tower.transform.localScale *= scale;
            Ground(tower, site, origin+new Vector3(47,0,-50));
            Shipping(site, "GuardPostContainer", origin+new Vector3(48,0,-54), -90, true);
            var groundMat = OwnMaterial("ApproachSoil", new Color(.35f,.32f,.25f));
            Primitive(site, "VisualGateApproach", origin+new Vector3(47,.008f,-50.5f), new Vector3(7.8f,.01f,17), groundMat);
            Notes.Add("Gate opening X[43,51], Z=-60; checkpoint reserved X[43,55] Z[-60,-48]. Old exterior tower must be removed by its owner.");
        }

        private static void Canopy(Transform parent, Vector3 foot)
        {
            var site = Group("SmallRestCanopy", parent);
            var steel = OwnMaterial("CanopySteel", new Color(.22f,.26f,.24f));
            var canvas = OwnMaterial("CanopyCanvas", new Color(.31f,.33f,.23f));
            foreach (float x in new[] {-2.4f,2.4f}) foreach (float z in new[] {-2.2f,2.2f})
                Primitive(site,"CanopyPost",foot+new Vector3(x,1.35f,z),new Vector3(.09f,2.7f,.09f),steel);
            Primitive(site,"StaticCanvasCanopy",foot+new Vector3(0,2.75f,0),new Vector3(5.2f,.10f,4.8f),canvas);
            Place("SM_Table","RestTable",site,foot,new Vector3(0,8,0),1);
            Place("SM_Chair","RestChairA",site,foot+new Vector3(-1.4f,0,.7f),new Vector3(0,105,0),1);
            Place("SM_Chair","RestChairB",site,foot+new Vector3(1.4f,0,-.6f),new Vector3(0,-72,0),1);
            Notes.Add("Canopy roof/posts are static prototype geometry; table/chairs paid sources. X[-29,-20] Z[-52,-43].");
        }

        private static void Construction(Transform parent, Vector3 foot)
        {
            var site = Group("OrganizedConstructionMaterials",parent);
            Shipping(site,"SiteStorageContainer",foot+new Vector3(3,0,0),90,true);
            var material=OwnMaterial("ConstructionConcrete",new Color(.42f,.41f,.37f));
            for(int row=0;row<2;row++) for(int i=0;i<4;i++)
                Primitive(site,"StackedConcreteSection",foot+new Vector3(-3+i*.8f,.15f+row*.3f,-1.2f),new Vector3(.65f,.28f,1.6f),material);
            Notes.Add("Construction zone X[20,35] Z[-46,-38]: organized concrete prototype sections and real storage container, not scattered weapon cases.");
        }

        private static void Backstop(Transform parent, Vector3 origin, float front)
        {
            var bank = Group("SharedFiveLaneRockBackstop",parent);
            for(int i=0;i<2;i++)
            {
                var block=Place("SM_Cliff_03","ContinuousBackstopMass",bank,new Vector3(origin.x+(i==0?-4.5f:4.5f),origin.y-.12f,0),new Vector3(0,i==0?85:98,0),3.65f);
                block.transform.localPosition+=Vector3.forward*(front-BoundsIn(block,bank).min.z);
                var toe=Place("SM_Cliff_01","BackstopToeTerrace",bank,new Vector3(origin.x+(i==0?-4:4),origin.y-.30f,0),new Vector3(0,90,0),3);
                toe.transform.localPosition+=Vector3.forward*(front-BoundsIn(toe,bank).min.z);
            }
            Notes.Add("Shared backstop actual AABB front="+front.ToString("F3")+"; target visible row is tent.max.z+15; not one wall per target.");
        }

        private static void Bank(Transform parent, Vector3 foot, float scale, float yaw)
        {
            Place("SM_Cliff_04","LayeredMainMass",parent,foot-Vector3.up*1.4f,new Vector3(0,yaw,2.2f*Mathf.Sin(yaw)),scale);
            Place("SM_Cliff_03","OverlappingFootSlope",parent,foot+new Vector3(-1,-.7f,-5),new Vector3(0,yaw+23,0),scale*.72f);
            Place("SM_Cliff_01","EmbeddedHorizontalTerrace",parent,foot+new Vector3(2,4,-1),new Vector3(0,yaw-15,0),scale*.66f);
        }

        private static GameObject Place(string stem,string name,Transform parent,Vector3 foot,Vector3 angles,float scale)
        {
            var go=UnityEngine.Object.Instantiate(Source(stem,true),parent);
            go.name=name;go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.Euler(angles);go.transform.localScale=Vector3.one*scale;
            Sanitize(go);Ground(go,parent,foot);return go;
        }

        private static void Ground(GameObject go,Transform parent,Vector3 foot)
        {
            var bounds=BoundsIn(go,parent);
            go.transform.localPosition+=foot-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        }

        private static Bounds BoundsIn(GameObject go,Transform parent)
        {
            var filters=go.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).ToArray();
            if(filters.Length==0)throw new InvalidOperationException("Нет source Mesh: "+go.name);
            var points=new List<Vector3>();
            foreach(var filter in filters)foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
                points.Add(parent.InverseTransformPoint(filter.transform.TransformPoint(filter.sharedMesh.bounds.center+Vector3.Scale(filter.sharedMesh.bounds.extents,new Vector3(x,y,z)))));
            var bounds=new Bounds(points[0],Vector3.zero);foreach(var point in points.Skip(1))bounds.Encapsulate(point);return bounds;
        }

        private static Transform Group(string name,Transform parent)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;
        }

        private static void Sanitize(GameObject go)
        {
            foreach(var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(behaviour);
            foreach(var component in go.GetComponentsInChildren<Component>(true))
                if(component && !(component is Transform) && !(component is Renderer) && !(component is MeshFilter) && !(component is LODGroup))UnityEngine.Object.DestroyImmediate(component);
            foreach(var t in go.GetComponentsInChildren<Transform>(true))GameObjectUtility.SetStaticEditorFlags(t.gameObject,StaticEditorFlags.OccludeeStatic);
            foreach(var r in go.GetComponentsInChildren<Renderer>(true)){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;r.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;}
        }

        private static void Primitive(Transform parent,string name,Vector3 center,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=center;go.transform.localScale=size;
            Sanitize(go);go.GetComponent<Renderer>().sharedMaterial=material;
        }

        private static Material OwnMaterial(string name,Color color)
        {
            const string folder="Assets/Materials/Lobby/Exterior/BaseBackdrop";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Materials/Lobby/Exterior","BaseBackdrop");
            var material=AssetDatabase.LoadAssetAtPath<Material>(folder+"/"+name+".mat");if(material)return material;
            material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,enableInstancing=true};material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.08f);
            AssetDatabase.CreateAsset(material,folder+"/"+name+".mat");return material;
        }

        private static Material WireVariant(Material source)
        {
            const string folder="Assets/Materials/Lobby/Exterior/BaseBackdrop";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Materials/Lobby/Exterior","BaseBackdrop");
            string path=folder+"/"+source.name+"_BackdropCutout.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material)return material;
            // Сохраняются реальные graph parameters/atlas; собственная очередь задаёт see-through для штатного bake classifier.
            material=new Material(source){name=source.name+"_BackdropCutout",renderQueue=2450,enableInstancing=true};
            AssetDatabase.CreateAsset(material,path);return material;
        }

        private static void SaveReport(int count)
        {
            Directory.CreateDirectory(Report);File.WriteAllText(Report+"build-source-status.json",JsonUtility.ToJson(new BuildReport{missing=Missing.ToArray(),notes=Notes.ToArray(),fenceSections=count},true));
        }
    }
}
