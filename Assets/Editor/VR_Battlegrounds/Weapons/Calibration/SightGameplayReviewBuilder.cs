using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using TMPro;
using UltimateXR.Core.Components;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Временные игровые A/B-копии прицелов и малые мишени обычного Lobby.</summary>
    public static class SightGameplayReviewBuilder
    {
        public const string Folder = "Assets/Data/Weapons/SightCalibration/Review";
        public const string Prefabs = "Assets/Prefabs/Weapons/SightReview";
        public const string ReportFolder = "tmp/weapon-sight-calibration/gameplay-review";
        private static readonly string[] Ids = { "TR15", "MKR9", "SniperRifle", "Viper", "SRM12" };

        public static string PrepareWeapons()
        {
            CheckEdit(); EnsureFolder(Folder); EnsureFolder(Prefabs); Directory.CreateDirectory(ReportFolder);
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(WeaponSightAudit.RegistryPath);
            var originals = Ids.Select(id => registry.GetById(id)).ToArray();
            if (originals.Any(w=>w==null)) throw new InvalidOperationException("Нет исходного оружия.");
            var variants = new List<WeaponInfo>();
            var before = originals.Select(w=>AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(w.WeaponPrefab)).ToString()).ToArray();
            var preview = EditorSceneManager.OpenPreviewScene(SightCalibrationBenchBuilder.ScenePath);
            try
            {
                var bench = preview.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<VrBattlegrounds.DebugTools.SightCalibrationBench>(true)).Single();
                foreach (WeaponInfo original in originals)
                {
                    string id = original.WeaponId + "_SightReview", infoPath = Folder + "/" + id + ".asset", prefabPath = Prefabs + "/" + id + ".prefab";
                    var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(infoPath);
                    if (info == null)
                    {
                        info = UnityEngine.Object.Instantiate(original); info.name = id;
                        var settings = new SerializedObject(info);
                        settings.FindProperty("_weaponId").stringValue = id;
                        bool calculationOnly = original.WeaponId == "Viper" || original.WeaponId == "SRM12";
                        settings.FindProperty("_displayName").stringValue = original.DisplayName + (calculationOnly ? " · расчёт, без поправки" : " · Sight Review");
                        settings.FindProperty("_description").stringValue = calculationOnly
                            ? "Проверочная копия исходной геометрии. Поправка мушки только рассчитана и не применена: " + (original.WeaponId == "Viper" ? "+3.04 мм." : "−5.32 мм.")
                            : "Новый прицел на проверке. Сравнить с исходным оружием; калибровка ещё не принята.";
                        settings.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.CreateAsset(info, infoPath);
                    }
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                    {
                        GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(original.WeaponPrefab));
                        try
                        {
                            var draft = bench.Entries.Single(e=>e.Id==original.WeaponId);
                            if (draft.Donor != null)
                            {
                                var donor = UnityEngine.Object.Instantiate(draft.Donor, root.transform);
                                donor.name = "SightReview_" + original.WeaponId; donor.transform.localPosition = Vector3.zero;
                                donor.transform.localRotation = Quaternion.identity; donor.transform.localScale = Vector3.one; donor.SetActive(true);
                            }
                            if (original.WeaponId == "TR15")
                            {
                                var lens = root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="SM_Attach_AR15_XPS2");
                                var materials = lens.sharedMaterials; materials[1] = draft.PrototypeLens; lens.sharedMaterials = materials;
                                var optic = lens.gameObject.AddComponent<WeaponOpticView>();
                                var settings = new SerializedObject(optic);
                                settings.FindProperty("_lensRenderer").objectReferenceValue = lens;
                                settings.FindProperty("_lensMaterialIndex").intValue = 1;
                                settings.FindProperty("_source").objectReferenceValue = root.GetComponent<UxrProjectileSource>();
                                settings.FindProperty("_weapon").objectReferenceValue = info;
                                settings.FindProperty("_sightId").stringValue = "XPS2";
                                settings.FindProperty("_settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(WeaponSightAudit.SettingsPath);
                                settings.FindProperty("_calibratedZeroDistance").floatValue = 15;
                                settings.ApplyModifiedPropertiesWithoutUndo();
                            }
                            // Ссылки на профиль баланса относятся к собственному WeaponInfo копии.
                            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true).Where(c=>c!=null))
                            {
                                var settings = new SerializedObject(component); var property = settings.GetIterator(); bool changed = false;
                                while(property.Next(true)) if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue==original)
                                { property.objectReferenceValue=info; changed=true; }
                                if(changed) settings.ApplyModifiedPropertiesWithoutUndo();
                            }
                            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        }
                        finally { PrefabUtility.UnloadPrefabContents(root); }
                        // A/B-копии одновременно существуют в игре: их SDK provenance различается.
                        root = PrefabUtility.LoadPrefabContents(prefabPath);
                        try
                        {
                            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
                            foreach(var component in root.GetComponentsInChildren<UxrComponent>(true))
                                component.SetEditorUniqueId(Guid.NewGuid(),true,guid);
                            PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
                        }
                        finally { PrefabUtility.UnloadPrefabContents(root); }
                    }
                    var data = new SerializedObject(info); data.FindProperty("_weaponPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    data.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(info); variants.Add(info);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            var catalog = new SerializedObject(registry); var weapons=catalog.FindProperty("_weapons");
            foreach(var info in variants) if(!registry.Weapons.Contains(info)) { int n=weapons.arraySize; weapons.arraySize=n+1; weapons.GetArrayElementAtIndex(n).objectReferenceValue=info; }
            catalog.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(registry);
            var preset=AssetDatabase.LoadAssetAtPath<ArsenalPreset>(VrBattlegrounds.EditorTools.ArsenalPresetAssetBuilder.FullPath);
            var entries=preset.Entries.Where(e=>!e.Weapon.WeaponId.EndsWith("_SightReview",StringComparison.Ordinal)).ToArray();
            var pairs=new List<ArsenalPreset.Entry>();
            foreach(var entry in entries)
            {
                pairs.Add(entry); var variant=variants.FirstOrDefault(w=>w.WeaponId==entry.Weapon.WeaponId+"_SightReview");
                if(variant!=null) pairs.Add(new ArsenalPreset.Entry {Weapon=variant,Row=entry.Row});
            }
            var assortment=new SerializedObject(preset); var array=assortment.FindProperty("_entries"); array.arraySize=pairs.Count;
            for(int i=0;i<pairs.Count;i++) {array.GetArrayElementAtIndex(i).FindPropertyRelative("Weapon").objectReferenceValue=pairs[i].Weapon;array.GetArrayElementAtIndex(i).FindPropertyRelative("Row").stringValue=pairs[i].Row;}
            assortment.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(preset);
            const string managers="Assets/Prefabs/Managers/--- MANAGERS ---.prefab";
            var managerRoot=PrefabUtility.LoadPrefabContents(managers);
            try
            {
                var network=managerRoot.GetComponentInChildren<VrBattlegrounds.Network.GameNetworkManager>(true);
                foreach(var info in variants) if(!network.spawnPrefabs.Contains(info.WeaponPrefab)) network.spawnPrefabs.Add(info.WeaponPrefab);
                PrefabUtility.SaveAsPrefabAsset(managerRoot,managers);
            }
            finally{PrefabUtility.UnloadPrefabContents(managerRoot);}
            bool unchanged=originals.Select(w=>AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(w.WeaponPrefab)).ToString()).SequenceEqual(before);
            if(!unchanged) throw new InvalidOperationException("Исходное оружие изменилось.");
            return "Weapons PASS: "+variants.Count+" playable review copies, registry="+registry.Count+", preset="+pairs.Count+", originals unchanged.";
        }

        /// <summary>Станцию лобби собирает генератор арсенала при запуске карты: достаточно сохранённого пресета.</summary>
        public static string PrepareArsenal()
        {
            CheckEdit();
            var preset=AssetDatabase.LoadAssetAtPath<ArsenalPreset>(VrBattlegrounds.EditorTools.ArsenalPresetAssetBuilder.FullPath);
            AssetDatabase.SaveAssetIfDirty(preset);
            return "Пресет "+preset.name+" сохранён; станцию лобби соберёт генератор при запуске карты.";
        }

        public static string PrepareTargets()
        {
            CheckEdit(); string path="Assets/Scenes/Lobby.unity";
            Scene scene=SceneManager.GetSceneByPath(path); bool loaded=scene.IsValid()&&scene.isLoaded;
            if(loaded&&scene.isDirty) throw new InvalidOperationException("Lobby содержит несохранённые изменения.");
            if(!loaded) scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            Scene previous=SceneManager.GetActiveScene();
            try
            {
                SceneManager.SetActiveScene(scene);
                var gameplay=scene.GetRootGameObjects().Single(g=>g.name=="Gameplay").transform;
                if(gameplay.Find("WeaponSightReview")!=null) return "Targets already exist; no overwrite.";
                EnsureFolder("Assets/Art/Weapons/Sights/Review");
                var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Weapons/Sights/Review/SmallTarget.mat");
                if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor",new Color(1,.25f,.07f)); AssetDatabase.CreateAsset(material,"Assets/Art/Weapons/Sights/Review/SmallTarget.mat"); }
                Vector3 origin=new Vector3(6.5f,1.5f,8);
                var stand=new GameObject("WeaponSightReview"); stand.transform.SetParent(gameplay,false);
                Label(stand.transform,"Точка стрелка · прицелы A/B",new Vector3(origin.x,.03f,origin.z),Quaternion.Euler(90,0,0),.09f);
                var notes=new List<object>();float[] distances={1,3,5,10,15,20};
                for(int i=0;i<distances.Length;i++)
                {
                    float x=(i-2.5f)*.16f;float z=Mathf.Sqrt(distances[i]*distances[i]-x*x);
                    Vector3 position=origin+new Vector3(x,0,z);
                    var root=new GameObject("SightTarget_"+distances[i]+"m_50mm");root.transform.SetParent(stand.transform,false);root.transform.position=position;
                    var pivot=new GameObject("Pivot").transform;pivot.SetParent(root.transform,false);
                    var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name="Target_50mm";sphere.transform.SetParent(pivot,false);sphere.transform.localScale=Vector3.one*.05f;sphere.GetComponent<Renderer>().sharedMaterial=material;
                    var target=root.AddComponent<ShootingTarget>();target.SetPivot(pivot);
                    var settings=new SerializedObject(target);settings.FindProperty("_fallAngle").floatValue=0;settings.FindProperty("_downTime").floatValue=.35f;settings.ApplyModifiedPropertiesWithoutUndo();
                    Label(root.transform,distances[i]+" м · Ø5 см",position+Vector3.up*.25f,Quaternion.Euler(0,180,0),.09f);
                    notes.Add(new {distance=distances[i],diameterMetres=.05f,position=new[]{position.x,position.y,position.z}});
                }
                EditorSceneManager.MarkSceneDirty(scene); if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Lobby save failed.");
                Directory.CreateDirectory(ReportFolder);File.WriteAllText(ReportFolder+"/targets.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{origin=new[]{origin.x,origin.y,origin.z},targets=notes},Newtonsoft.Json.Formatting.Indented));
                return "Targets PASS: 6 spheres diameter 50 mm, reference line (6.5,0,8); original lobby targets retained.";
            }
            finally{SceneManager.SetActiveScene(previous);if(!loaded)EditorSceneManager.CloseScene(scene,true);}
        }

        public static string ReviseTargetsZigzag()
        {
            CheckEdit();
            Scene scene=SceneManager.GetSceneByPath("Assets/Scenes/Lobby.unity");
            if(!scene.IsValid()||!scene.isLoaded||scene.isDirty) throw new InvalidOperationException("Нужен открытый сохранённый Lobby.");
            var gameplay=scene.GetRootGameObjects().Single(g=>g.name=="Gameplay").transform;
            var targets=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ShootingTarget>(true))
                .Where(t=>t.name.StartsWith("ShootingTarget_",StringComparison.Ordinal)).OrderBy(t=>t.name,StringComparer.Ordinal).ToArray();
            if(targets.Length!=14) throw new InvalidOperationException("Ожидались 14 исходных мишеней на стойках.");
            var review=gameplay.Find("WeaponSightReview");
            if(review!=null) foreach(var target in review.GetComponentsInChildren<ShootingTarget>(true)) UnityEngine.Object.DestroyImmediate(target.gameObject);
            Vector3 origin=new Vector3(6.5f,1.5f,8);
            float[] distances={1,3,3,5,5,7.5f,10,10,12.5f,15,15,17.5f,20,20};
            var rows=new List<object>();
            for(int i=0;i<targets.Length;i++)
            {
                var target=targets[i]; float angle=(i%2==0?-1:1)*(5+(i/2)*2)*Mathf.Deg2Rad;
                Vector3 direction=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                Vector3 centre=origin+direction*distances[i];
                target.transform.localScale=Vector3.one; target.transform.position=new Vector3(centre.x,0,centre.z);
                target.transform.rotation=Quaternion.LookRotation(-direction,Vector3.up);
                var pivot=target.Pivot; pivot.localPosition=new Vector3(0,1.464f,0); pivot.localScale=Vector3.one*.1f;
                pivot.localRotation=Quaternion.identity;
                var plate=pivot.Find("Plate"); var oldCollider=plate.GetComponent<Collider>();
                if(oldCollider!=null) UnityEngine.Object.DestroyImmediate(oldCollider);
                var ring=pivot.Find("RingOuter"); var collider=ring.GetComponent<MeshCollider>();
                if(collider==null) collider=ring.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh=ring.GetComponent<MeshFilter>().sharedMesh;collider.convex=false;
                var post=target.transform.Find("Post");post.localPosition=new Vector3(0,.732f,-.006f);post.localScale=new Vector3(.02f,1.464f,.02f);
                var foot=target.transform.Find("Foot");foot.localPosition=new Vector3(0,.02f,0);foot.localScale=new Vector3(.25f,.04f,.18f);
                Label(target.transform,distances[i]+" м · Ø5 см",centre+Vector3.up*.18f,target.transform.rotation,.09f);
                foreach(var node in target.GetComponentsInChildren<Transform>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(node);
                rows.Add(new {name=target.name,distance=distances[i],diameterMetres=.05f,position=new[]{centre.x,centre.y,centre.z}});
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Lobby save failed.");
            File.WriteAllText(ReportFolder+"/targets.json",Newtonsoft.Json.JsonConvert.SerializeObject(new {origin=new[]{origin.x,origin.y,origin.z},targets=rows,layout="ZigzagExistingStands",activeHitZone="RingOuter only"},Newtonsoft.Json.Formatting.Indented));
            return "Targets PASS: 14 retained stands in zigzag, active ring diameter 50 mm; added spheres removed.";
        }

        private static void Label(Transform parent,string value,Vector3 position,Quaternion rotation,float height)
        {
            var label=new GameObject(value).AddComponent<TextMeshPro>();label.transform.SetParent(parent,false);label.transform.SetPositionAndRotation(position,rotation);
            label.text=value;label.font=TMP_Settings.defaultFontAsset;label.fontSize=12;label.alignment=TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta=new Vector2(100,10);label.color=Color.white;label.ForceMeshUpdate();
            float measured=label.textBounds.size.y;
            if(measured>1e-6f) label.transform.localScale=Vector3.one*(height/measured);
        }
        private static void EnsureFolder(string path)
        {
            string parent="Assets";
            foreach(string part in path.Split('/').Skip(1)){string child=parent+"/"+part;if(!AssetDatabase.IsValidFolder(child))AssetDatabase.CreateFolder(parent,part);parent=child;}
        }
        private static void CheckEdit(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode required.");}
    }
}
