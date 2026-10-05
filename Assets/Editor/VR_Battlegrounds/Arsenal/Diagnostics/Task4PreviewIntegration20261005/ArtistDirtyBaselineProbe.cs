using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using TMPro;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>PREPARED finite native stages. Не исполнялся; addressed asset/Unity GO нужен отдельно.</summary>
    public static class ArtistDirtyBaselineProbe
    {
        private sealed class Context
        {
            public string Folder, Path, Before, Authored;
            public string PreviousPreset, PreviousCatalog;
            public int SdkBefore, StageRoot, UndoGroup;
            public Object[] Selection, ArtistSelection;
            public PrefabStage Stage;
            public Scene Receiver, OriginalActive;
            public GameObject Allocation;
            public Transform AllocationTransform;
            public MeshFilter AllocationFilter;
            public MeshRenderer AllocationRenderer;
            public Mesh Mesh;
            public Material Material;
            public ArsenalPreset Preset;
            public ArsenalCompositionCatalog Catalog;
            public Dictionary<string,string> Guids = new Dictionary<string,string>();
        }
        private static Context own;
        private static readonly string[] FileNames = { "Triangle.asset", "Material.mat", "Artwork.prefab", "Catalog.asset", "Preset.asset" };
        public static object Prepare(string presetPath, string catalogPath, string nonce, string stylePath = null)
        {
            if (own != null || string.IsNullOrWhiteSpace(nonce) || nonce.Any(c => !char.IsLetterOrDigit(c))) throw new ArgumentException("Existing fixture/nonce");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Active human stage/Play/import/compile; refuse");
            string parent = "Assets/Editor/VR_Battlegrounds/Arsenal";
            string folder = parent + "/Task4Fixture_" + nonce;
            if (!AssetDatabase.IsValidFolder(parent) || Directory.Exists(folder) || File.Exists(folder + ".meta")) throw new InvalidOperationException("Invalid/existing output");
            var preset = AssetDatabase.LoadAssetAtPath<ArsenalPreset>(presetPath);
            var catalog = AssetDatabase.LoadAssetAtPath<ArsenalCompositionCatalog>(catalogPath);
            if (preset == null || catalog == null) throw new InvalidOperationException("Need actual preset/catalog");
            var style = preset.PresentationStyle != null ? preset.PresentationStyle : AssetDatabase.LoadAssetAtPath<ArsenalPresentationStyle>(stylePath);
            if (style == null) throw new InvalidOperationException("Explicit style prerequisite missing; production preset remains untouched");
            var c = new Context { Folder = folder, Path = folder + "/Artwork.prefab", Before = SceneFingerprint(), SdkBefore = SdkComponents(),
                Selection = Selection.objects, OriginalActive = SceneManager.GetActiveScene(),
                PreviousPreset = SessionState.GetString("VrBattlegrounds.GeneratorPreview.Preset", ""),
                PreviousCatalog = SessionState.GetString("VrBattlegrounds.GeneratorPreview.Catalog", "") };
            own = c;
            try
            {
                c.Preset = Object.Instantiate(preset); // Own Scriptable data only; never gameplay/SDK prefab.
                if (c.Preset.PresentationStyle == null)
                { var data = new SerializedObject(c.Preset); data.FindProperty("_presentationStyle").objectReferenceValue = style; data.ApplyModifiedPropertiesWithoutUndo(); }
                var initial = ArsenalStationResolver.ResolveDescription(ArsenalStationBuildInput.Capture("artist-baseline", c.Preset, catalog,
                    new ArsenalVisualRequest("fixture"), new ArsenalPlacementInput(default, Vector3.one, new Bounds(Vector3.zero, Vector3.one * 10000))));
                if (!initial.Success || initial.Slots.Count != preset.Entries.Count || initial.Slots.Count == 0) throw new InvalidOperationException("Resolver prerequisite failed");
                FontPrerequisite(initial); // Refusal before any asset writes.
                c.Receiver = EditorSceneManager.NewPreviewScene();
                // Fail BEFORE GameObject allocation if isolated receiver cannot become target.
                if (!SceneManager.SetActiveScene(c.Receiver)) throw new InvalidOperationException("NO_GO isolated active allocation receiver");
                c.Guids[folder] = AssetDatabase.CreateFolder(parent, "Task4Fixture_" + nonce);
                c.Allocation = EditorUtility.CreateGameObjectWithHideFlags("OwnedArtistArtwork", HideFlags.DontSave);
                c.AllocationTransform = c.Allocation.transform;
                if (c.Allocation.scene != c.Receiver) throw new InvalidOperationException("Wrong allocation scene; not a product RED");
                c.Allocation.hideFlags = HideFlags.None; // Only own receiver, never ordinary foreign scene.
                c.Mesh = new Mesh(); c.Mesh.name = "OwnedTriangle";
                c.Mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; c.Mesh.triangles = new[] { 0, 1, 2 }; c.Mesh.RecalculateBounds();
                c.Material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? throw new InvalidOperationException("Missing shader"));
                c.AllocationFilter = c.Allocation.AddComponent<MeshFilter>(); c.AllocationFilter.sharedMesh = c.Mesh;
                c.AllocationRenderer = c.Allocation.AddComponent<MeshRenderer>(); c.AllocationRenderer.sharedMaterial = c.Material;
                SaveOwn(c, c.Mesh, "Triangle.asset"); SaveOwn(c, c.Material, "Material.mat");
                RequireExactReceiver(c);
                PrefabUtility.SaveAsPrefabAsset(c.Allocation, c.Path); c.Guids[c.Path] = AssetDatabase.AssetPathToGUID(c.Path);
                RequireExactReceiver(c);
                Object.DestroyImmediate(c.Allocation); c.Allocation = null;
                SceneManager.SetActiveScene(c.OriginalActive); CloseReceiver(c);
                var artworkAsset = AssetDatabase.LoadAssetAtPath<GameObject>(c.Path);
                var descriptor = new ArsenalDecorationDescriptor("fixture/" + nonce, "fixture", artworkAsset,
                    AssetDatabase.GetAssetDependencyHash(c.Path).ToString(), initial.Slots.Count, initial.Slots.Count, true,
                    default, Vector3.one, new Bounds(Vector3.zero, Vector3.one * 100), c.Mesh.bounds);
                c.Catalog = ArsenalCompositionCatalog.CreateCompiled("fixture/" + nonce, catalog.Templates,
                    new[] { descriptor }, catalog.Weapons, catalog.Supports, catalog.Materials);
                SaveOwn(c, c.Catalog, "Catalog.asset");
                SaveOwn(c, c.Preset, "Preset.asset");
                var expected = ArsenalStationResolver.ResolveDescription(ArsenalStationBuildInput.Capture("artist-baseline", c.Preset, c.Catalog,
                    new ArsenalVisualRequest("fixture"), new ArsenalPlacementInput(default, Vector3.one, new Bounds(Vector3.zero, Vector3.one * 10000))));
                if (!expected.Success || expected.Selection.Decoration.Prefab != artworkAsset) throw new InvalidOperationException("Fixture catalog/descriptor prerequisite invalid");
                c.Stage = PrefabStageUtility.OpenPrefab(c.Path);
                if (c.Stage == null || c.Stage.assetPath != c.Path) throw new InvalidOperationException("Own stage failed");
                c.StageRoot = c.Stage.prefabContentsRoot.GetInstanceID();
                c.Stage.prefabContentsRoot.transform.localPosition = new Vector3(.07f, .02f, -.03f);
                EditorUtility.SetDirty(c.Stage.prefabContentsRoot.transform); EditorSceneManager.MarkSceneDirty(c.Stage.scene);
                c.Material.color = new Color(.2f,.5f,.7f); EditorUtility.SetDirty(c.Material); // Unsaved own material asset, no transient dangling ref.
                c.Authored = Authored(c); c.UndoGroup = Undo.GetCurrentGroup(); c.ArtistSelection = Selection.objects;
                return new { passed = true, executionCompleted = true, phase = "Prepared; no refresh dispatched", fixture = c.Path,
                    expectedSlots = expected.Slots.Count, expectedFingerprint = expected.LayoutFingerprint, sourceStyle = style.name };
            }
            catch { Cleanup(); throw; }
            finally
            {
                if (c.Receiver.IsValid()) { if (c.OriginalActive.IsValid()) SceneManager.SetActiveScene(c.OriginalActive); CloseReceiver(c); }
            }
        }
        /// <summary>Session only. После completed preparation release UI owner сам acquires canonical lease.</summary>
        public static object SelectSession()
        {
            var c = RequireOwnStage();
            var source = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp-Editor")?
                .GetType("VrBattlegrounds.Editor.Arsenal.ArsenalPrefabStagePreview", false);
            if (source != null) source.GetMethod("Select", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { c.Preset, c.Catalog });
            else
            {
                SessionState.SetString("VrBattlegrounds.GeneratorPreview.Preset", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Preset)));
                SessionState.SetString("VrBattlegrounds.GeneratorPreview.Catalog", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.Catalog)));
            }
            return new { passed = true, executionCompleted = true, phase = "Session selected only; observe after UI ownlease release", nativePreviewApi = source != null };
        }
        public static object Observe(bool expectedGreen)
        {
            var c = RequireOwnStage(); var artwork = c.Stage.prefabContentsRoot;
            var overlays = c.Stage.scene.GetRootGameObjects().Where(r => r != artwork && r.name.StartsWith("__ArsenalGeneratedPreview_", StringComparison.Ordinal)).ToArray();
            // Names are observation ONLY; never ownership/deletion.
            int count = overlays.Length; int items = overlays.Sum(r => r.GetComponentsInChildren<Transform>(true).Count(t => t.name == "ItemVisual"));
            bool sourcePreserved = c.Authored == Authored(c);
            bool undo = c.UndoGroup == Undo.GetCurrentGroup() && c.ArtistSelection.SequenceEqual(Selection.objects);
            var report = new { passed = count == 1 && items == c.Preset.Entries.Count && sourcePreserved && undo,
                executionCompleted = true, expectedGreen, overlayCount = count, itemCount = items, expectedItems = c.Preset.Entries.Count,
                sourcePreserved, undoSelectionPreserved = undo, sdkPreserved = c.SdkBefore == SdkComponents(),
                actualDescriptor = c.Catalog.Decorations.Single().DecorationId, actualStyle = c.Preset.PresentationStyle.name,
                boundary = "Baseline meaningful RED: same valid descriptor/preset expects visual output; type absence alone not oracle" };
            File.WriteAllText("tmp/arsenal-generator-proofs/stage4-preview-20261005/native-observe-" + (expectedGreen ? "green" : "red") + ".json",
                Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            return report;
        }
        public static object Cleanup()
        {
            var c = own; if (c == null) return new { passed = true, executionCompleted = true, cleanupCompleted = true };
            bool stageSafe = c.Stage == null || PrefabStageUtility.GetCurrentPrefabStage() == c.Stage && c.Stage.assetPath == c.Path &&
                c.Stage.prefabContentsRoot.GetInstanceID() == c.StageRoot;
            if (!stageSafe) return new { passed = false, executionCompleted = true, cleanupCompleted = false, reason = "Foreign/replaced stage preserved; retained fixture" };
            try
            {
                if (c.Stage != null) { c.Stage.ClearDirtiness(); StageUtility.GoToMainStage(); c.Stage = null; } // Own disposable fixture only.
                RequireExactReceiver(c);
                if (c.Allocation != null) Object.DestroyImmediate(c.Allocation);
                if (c.Receiver.IsValid()) { if (c.OriginalActive.IsValid()) SceneManager.SetActiveScene(c.OriginalActive); CloseReceiver(c); }
                foreach (var obj in new Object[] { c.Preset, c.Catalog, c.Material, c.Mesh }) if (obj != null && !EditorUtility.IsPersistent(obj)) Object.DestroyImmediate(obj);
                var files = Directory.Exists(c.Folder) ? Directory.GetFileSystemEntries(c.Folder,"*",SearchOption.AllDirectories).Select(p => p.Replace('\\','/')).ToArray() : Array.Empty<string>();
                var allowedFiles = new HashSet<string>(c.Guids.Keys.Where(p => p != c.Folder).SelectMany(p => new[]{p,p+".meta"}));
                bool exactFiles = files.All(p => allowedFiles.Contains(p)) && c.Guids.All(p => AssetDatabase.AssetPathToGUID(p.Key) == p.Value);
                if (!exactFiles) return new { passed = false, executionCompleted = true, cleanupCompleted = false, reason = "Unknown/changed own-folder asset; retained" };
                if (AssetDatabase.IsValidFolder(c.Folder)) AssetDatabase.DeleteAsset(c.Folder);
                SessionState.SetString("VrBattlegrounds.GeneratorPreview.Preset", c.PreviousPreset); SessionState.SetString("VrBattlegrounds.GeneratorPreview.Catalog", c.PreviousCatalog);
                Selection.objects = c.Selection.Where(o => o != null).ToArray();
                bool scene = c.Before == SceneFingerprint(); bool sdk = c.SdkBefore == SdkComponents();
                own = null;
                return new { passed = scene && sdk && !Directory.Exists(c.Folder), executionCompleted = true, cleanupCompleted = true,
                    foreignScenePreserved = scene, sdkPreserved = sdk, boundary = "UI detached resource recovery observed separately after external lease release" };
            }
            catch (Exception exception) { return new { passed = false, executionCompleted = true, cleanupCompleted = false, reason = exception.ToString() }; }
        }
        private static Context RequireOwnStage()
        {
            var c = own;
            if (c == null || c.Stage == null || PrefabStageUtility.GetCurrentPrefabStage() != c.Stage || c.Stage.assetPath != c.Path ||
                c.Stage.prefabContentsRoot.GetInstanceID() != c.StageRoot) throw new InvalidOperationException("Stale/foreign fixture context");
            return c;
        }
        private static void RequireExactReceiver(Context c)
        {
            if (!c.Receiver.IsValid()) return;
            if (c.Receiver.GetRootGameObjects().Any(r => r != c.Allocation)) throw new InvalidOperationException("Foreign receiver root: retained, no close/destruction");
            if (c.Allocation == null) return;
            var ids = new Object[] { c.AllocationTransform, c.AllocationFilter, c.AllocationRenderer }.Where(o => o != null).Select(o => o.GetInstanceID()).ToArray();
            if (c.Allocation.transform.childCount != 0 || c.Allocation.GetComponents<Component>().Any(o => o == null || !ids.Contains(o.GetInstanceID())))
                throw new InvalidOperationException("Foreign allocation subtree/components: retained");
        }
        private static void CloseReceiver(Context c)
        { RequireExactReceiver(c); EditorSceneManager.ClosePreviewScene(c.Receiver); c.Receiver = default; }
        private static void SaveOwn(Context c, Object obj, string name)
        { string path = c.Folder + "/" + name; if (File.Exists(path) || File.Exists(path + ".meta")) throw new InvalidOperationException("Existing output"); AssetDatabase.CreateAsset(obj,path); c.Guids[path] = AssetDatabase.AssetPathToGUID(path); }
        private static string Authored(Context c) => EditorJsonUtility.ToJson(c.Stage.prefabContentsRoot.transform) + EditorJsonUtility.ToJson(c.Material);
        private static void FontPrerequisite(ArsenalStationDescription description)
        {
            var font = TMP_Settings.defaultFontAsset;
            if (font == null || font.material == null || font.atlasPopulationMode != AtlasPopulationMode.Static || font.characterTable == null ||
                font.fontWeightTable != null && font.fontWeightTable.Any(w => w.regularTypeface != null || w.italicTypeface != null) || font.characterTable.Any(c => c.glyph == null || c.glyph.atlasIndex != 0))
                throw new InvalidOperationException("NO_GO font prerequisite before fixture/assets: need populated static single-atlas font");
            var glyphs = new HashSet<uint>(font.characterTable.Select(c => c.unicode));
            foreach (var slot in description.Slots)
                foreach (char value in GlyphOracle(slot.Entry.Card))
                    if (!char.IsWhiteSpace(value) && !glyphs.Contains(value)) throw new InvalidOperationException("NO_GO font glyph prerequisite U+" + ((int)value).ToString("X4"));
        }
        private static string GlyphOracle(ArsenalCardContent card)
        {
            // Temporary expected glyph input, не renderer/второй product formatter.
            // BeforeSource shared API ещё отсутствует; afterSource используем именно его.
            var formatter = typeof(ArsenalPriceTag).GetMethod("FormatCard", BindingFlags.Public | BindingFlags.Static);
            if (formatter != null) return System.Text.RegularExpressions.Regex.Replace((string)formatter.Invoke(null, new object[]{card,false}), "<[^>]*>", "");
            string text = (card.DisplayName ?? "").Replace("<","‹").Replace(">","›") + "$" + card.Price;
            if (card.HasBalance) return text + $"Урон {card.Pellets} × {card.Damage:0.#} Магазин {card.MagazineSize} До {card.Rpm}/мин " + (card.FullAuto?"АВТО":"ПОЛУАВТО");
            switch(card.Category)
            { case WeaponCategory.Rifle: return text+"ОГНЕСТРЕЛЬНОЕ ОРУЖИЕ";
              case WeaponCategory.Pistol: return text+"КОРОТКОСТВОЛЬНОЕ ОРУЖИЕ";
              case WeaponCategory.Melee: return text+"ХОЛОДНОЕ ОРУЖИЕ";
              default: return text+"СНАРЯЖЕНИЕ"; }
        }
        private static int SdkComponents() => Resources.FindObjectsOfTypeAll<Component>().Count(c => c != null && !EditorUtility.IsPersistent(c) && c.GetType().FullName.StartsWith("UltimateXR.", StringComparison.Ordinal));
        private static string SceneFingerprint()
        {
            var rows = new List<string>();
            for (int i=0;i<SceneManager.sceneCount;i++) { var scene=SceneManager.GetSceneAt(i); if(EditorSceneManager.IsPreviewScene(scene))continue;
                rows.Add(scene.handle.GetRawData()+"|"+scene.path+"|"+scene.isDirty+"|loaded="+scene.isLoaded);
                if(!scene.isLoaded)continue;
                foreach(var root in scene.GetRootGameObjects())foreach(var c in root.GetComponentsInChildren<Component>(true))if(c!=null)rows.Add(c.GetInstanceID()+"|"+EditorJsonUtility.ToJson(c)); }
            return string.Join("\n",rows.OrderBy(s=>s,StringComparer.Ordinal));
        }
    }
}
