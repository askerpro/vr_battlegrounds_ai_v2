using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Изолированный render-only прототип оптики. Не является Apply или XR-приёмкой.</summary>
    public static class SightPrototypeReview
    {
        public const string Folder = "tmp/weapon-sight-calibration/prototype";

        [Serializable] public sealed class Result
        {
            public bool passed;
            public bool xrAccepted;
            public bool actualSdkShotVerified;
            public List<string> failures = new List<string>();
            public List<string> images = new List<string>();
            public int visiblePupilSamples;
            public int finiteCentreMatches;
            public int baselineCentreMatches;
            public bool staleDistanceRejected;
            public bool unappliedDistanceRejected;
            public bool unrelatedPropertyPreserved;
            public bool sourceUnchanged;
        }

        public static Result CaptureTR15()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required");
            Directory.CreateDirectory(Folder);
            var result = new Result();
            var info = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(WeaponSightAudit.RegistryPath).GetById("TR15");
            var prefab = info.WeaponPrefab;
            string path = AssetDatabase.GetAssetPath(prefab), before = AssetDatabase.GetAssetDependencyHash(path).ToString();
            var source = prefab.GetComponent<UltimateXR.Mechanics.Weapons.UxrProjectileSource>();
            Transform muzzle = source.ShotTypes[0].ShotSource;
            var preview = new PreviewRenderUtility();
            var root = new GameObject("TR15RenderOnlyPrototype") { hideFlags = HideFlags.HideAndDontSave };
            Material material = null;
            try
            {
                preview.AddSingleGO(root);
                MeshRenderer lens = null;
                foreach (var original in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!Included(original.transform, prefab.transform) || !original.enabled) continue;
                    var filter = original.GetComponent<MeshFilter>(); if (filter == null || filter.sharedMesh == null) continue;
                    var go = new GameObject(original.name); go.transform.SetParent(root.transform, false);
                    go.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                    go.transform.localScale = original.transform.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = original.sharedMaterials;
                    if (original.name == "SM_Attach_AR15_XPS2") lens = renderer;
                }
                if (lens == null || lens.sharedMaterials.Length != 2) throw new InvalidOperationException("Unknown XPS2 lens binding");
                Shader shader = Shader.Find(WeaponOpticView.ShaderName);
                if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Missing or invalid finite-focus shader");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                Material[] originals = lens.sharedMaterials;
                var view = lens.gameObject.AddComponent<WeaponOpticView>();
                var config = new SerializedObject(view);
                config.FindProperty("_lensRenderer").objectReferenceValue = lens;
                config.FindProperty("_lensMaterialIndex").intValue = 1;
                config.FindProperty("_source").objectReferenceValue = source;
                config.FindProperty("_weapon").objectReferenceValue = info;
                config.FindProperty("_sightId").stringValue = "XPS2";
                config.FindProperty("_settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(WeaponSightAudit.SettingsPath);
                config.FindProperty("_calibratedZeroDistance").floatValue = 15f;
                config.ApplyModifiedPropertiesWithoutUndo();
                preview.lights[0].intensity = 1.5f; preview.lights[0].transform.rotation = Quaternion.Euler(35,35,0);
                preview.lights[1].intensity = 1f; preview.lights[1].transform.rotation = Quaternion.Euler(340,215,0);
                preview.ambientColor = new Color(.35f,.35f,.35f);
                Camera camera = preview.camera;
                camera.orthographic = false; camera.fieldOfView = 12f; camera.nearClipPlane = .005f; camera.farClipPlane = 50;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f,.18f,.21f);
                Vector3 centre = new Vector3(.000286f,.107165f,.052617f);
                Vector3 target = muzzle.position + muzzle.forward * 15f;
                Vector3[] offsets = { Vector3.zero, Vector3.left*.008f, Vector3.right*.008f, Vector3.up*.006f, Vector3.down*.006f };
                string[] names = { "centre", "pupil-left", "pupil-right", "pupil-up", "pupil-down" };
                for (int i = 0; i < offsets.Length; i++)
                {
                    camera.transform.SetPositionAndRotation(centre + offsets[i] - Vector3.forward*.12f,
                        Quaternion.LookRotation(target - (centre + offsets[i] - Vector3.forward*.12f), Vector3.up));
                    lens.sharedMaterials = originals;
                    lens.SetPropertyBlock(null, 1);
                    if (Render("TR15-surface-"+names[i])) result.baselineCentreMatches++;
                    lens.sharedMaterials = new [] { originals[0], material };
                    if (!view.RefreshView()) throw new InvalidOperationException("Prototype parameter binding rejected");
                    if (Render("TR15-finite15-"+names[i])) result.finiteCentreMatches++;
                    result.visiblePupilSamples++;
                }
                var properties = new MaterialPropertyBlock();
                lens.GetPropertyBlock(properties, 1); properties.SetFloat("_OtherOwnerMarker", 42); lens.SetPropertyBlock(properties, 1);
                view.RefreshView(); lens.GetPropertyBlock(properties, 1);
                result.unrelatedPropertyPreserved = properties.GetFloat("_OtherOwnerMarker") == 42;
                config.Update(); config.FindProperty("_calibratedZeroDistance").floatValue = 10; config.ApplyModifiedPropertiesWithoutUndo();
                result.staleDistanceRejected = !view.RefreshView();
                config.Update(); config.FindProperty("_calibratedZeroDistance").floatValue = 0; config.ApplyModifiedPropertiesWithoutUndo();
                result.unappliedDistanceRejected = !view.RefreshView();
                result.sourceUnchanged = before == AssetDatabase.GetAssetDependencyHash(path).ToString();
                if (result.finiteCentreMatches != offsets.Length) result.failures.Add("Finite centre mismatch");
                if (result.baselineCentreMatches == offsets.Length) result.failures.Add("Surface baseline did not expose parallax");
                if (!result.staleDistanceRejected || !result.unappliedDistanceRejected || !result.unrelatedPropertyPreserved || !result.sourceUnchanged)
                    result.failures.Add("Parameter/lifecycle/source invariant failed");
                result.passed = result.failures.Count == 0;

                bool Render(string name)
                {
                    preview.BeginStaticPreview(new Rect(0,0,1024,768)); preview.Render(true);
                    Texture2D texture = preview.EndStaticPreview();
                    try
                    {
                        string image = Folder + "/" + name + ".png"; File.WriteAllBytes(image, texture.EncodeToPNG()); result.images.Add(image);
                        Vector3 pixel = camera.WorldToViewportPoint(target);
                        int x = Mathf.RoundToInt(pixel.x*(texture.width-1)), y = Mathf.RoundToInt(pixel.y*(texture.height-1));
                        Color32[] colors = texture.GetPixels32();
                        for (int dy=-2;dy<=2;dy++) for (int dx=-2;dx<=2;dx++)
                        {
                            int px=x+dx,py=y+dy; if(px<0||px>=texture.width||py<0||py>=texture.height)continue;
                            Color32 c=colors[py*texture.width+px]; if(c.r>120 && c.g<90 && c.b<90)return true;
                        }
                        return false;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                }
            }
            catch (Exception e) { result.failures.Add(e.GetType().Name+": "+e.Message); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); preview.Cleanup();
                if(material!=null)UnityEngine.Object.DestroyImmediate(material);
            }
            File.WriteAllText(Folder + "/TR15-prototype.json", JsonUtility.ToJson(result,true));
            return result;
        }

        private static bool Included(Transform node, Transform root)
        {
            for (var current=node;current!=null;current=current.parent)
            {
                if(!current.gameObject.activeSelf || current.name.IndexOf("Highlight",StringComparison.OrdinalIgnoreCase)>=0
                    || current.name.IndexOf("Decal",StringComparison.OrdinalIgnoreCase)>=0)return false;
                if(current==root)return true;
            }
            return false;
        }
    }
}
