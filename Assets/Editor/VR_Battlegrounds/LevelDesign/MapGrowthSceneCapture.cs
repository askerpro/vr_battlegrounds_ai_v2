using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Владелец замороженной native-геометрии. Workers получают только Snapshot, после завершения Capture.</summary>
    [InitializeOnLoad]
    public sealed class MapGrowthSceneCapture : IDisposable
    {
        private static readonly HashSet<MapGrowthSceneCapture> Live = new HashSet<MapGrowthSceneCapture>();
        private readonly HashSet<MapGrowthPreview> previews = new HashSet<MapGrowthPreview>();
        private readonly MapGrowthFrozenGeometry geometry;
        private MapGrowthEvaluationInput input;
        private readonly MapGrowthSearchParameters search;
        private readonly MapEvaluationProfile profile;
        private readonly bool[] forbidden, reserved;
        private readonly MapGrowthBlockRecipe[] fixedRecipes, variants;
        private readonly MapGrowthConstraintVolume[] constraints;
        private readonly MapGrowthPositionAnchor[] anchors;
        private readonly MapGrowthShapeCapabilities[] capabilities;
        private readonly List<MapGrowthFootprintTemplate> templates = new List<MapGrowthFootprintTemplate>();
        private readonly Scene source;
        private readonly BlockoutMarkup markup;
        private readonly MapGrowthSettings settings;
        private readonly GameObject[] replacedRoots;
        public System.Collections.ObjectModel.ReadOnlyCollection<GameObject> ReplacedRoots => Array.AsReadOnly(replacedRoots);
        private bool disposed, released;
        public string InputVersion { get; }
        public Scene SourceScene => source;
        public bool UsesInput(Scene scene, BlockoutMarkup intent, MapGrowthSettings options)
            => source == scene && markup == intent && settings == options;
        public float FloorY { get; }
        public float AuthorCell { get; }
        public MapGrowthSnapshot Snapshot { get; private set; }
        public int CapturedTemplates => templates.Count;
        public int TemplateCount => variants.Length;
        public bool CaptureComplete => Snapshot != null;
        static MapGrowthSceneCapture() => AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
        internal MapGrowthSceneCapture(Scene source, BlockoutMarkup markup, MapGrowthSettings settings,
            string version, float floorY, MapGrowthFrozenGeometry geometry, MapGrowthEvaluationInput input,
            bool[] forbidden, bool[] reserved, MapGrowthBlockRecipe[] fixedRecipes, MapGrowthBlockRecipe[] variants, MapGrowthConstraintVolume[] constraints, GameObject[] replacedRoots = null)
        {
            this.source = source; this.markup = markup; this.settings = settings; InputVersion = version;
            this.replacedRoots = replacedRoots?.ToArray() ?? Array.Empty<GameObject>();
            FloorY = floorY; AuthorCell = markup.step; this.geometry = geometry; this.input = input;
            // Параметры копируются до первой порции, а не читаются из изменяемого ассета при завершении.
            this.search = JsonUtility.FromJson<MapGrowthSearchParameters>(JsonUtility.ToJson(settings.search)); profile = settings.profile;
            this.forbidden = forbidden; this.reserved = reserved; this.fixedRecipes = fixedRecipes; this.variants = variants;
            this.constraints = constraints;
            anchors = markup.positions.Select(p => new MapGrowthPositionAnchor(p.id, p.mainThreatYaw)).ToArray();
            capabilities = BlockoutRegistryFactory.Current.Definitions.Where(d => d != null && d.gameplayGeometry)
                .OrderBy(d => d.shapeId, StringComparer.Ordinal).Select(d => new MapGrowthShapeCapabilities(d.shapeId,
                    d.supportsOpenings && d.supportsCellWall, d.supportsCellWall && BlockoutRegistryFactory.Validate(d,
                        new Vector3(AuthorCell, BlockoutRegistryFactory.DefaultDimensions(d).y, AuthorCell), d.defaultMaterial, default, out _), d.allowedMaterials)).ToArray();
            Live.Add(this);
        }
        public bool StepCapture(int maximumTemplates = 1)
        {
            MapGrowthSnapshotBuilder.RequireMainThread(); ThrowDisposed();
            if (maximumTemplates < 1) throw new ArgumentOutOfRangeException(nameof(maximumTemplates));
            if (Snapshot != null) return true;
            try
            {
                int remaining = maximumTemplates;
                while (templates.Count < variants.Length && remaining-- > 0)
                {
                    var recipe = variants[templates.Count];
                    var definition = BlockoutRegistryFactory.Current.Definitions.Single(d => d != null && d.shapeId == recipe.ShapeId);
                    templates.Add(MapGrowthSnapshotBuilder.CaptureFootprint(InputVersion, definition, recipe,
                        input.grid.Origin, input.grid.Cell, FloorY, AuthorCell));
                }
                if (templates.Count != variants.Length) return false;
                if (!IsCurrent()) throw new InvalidOperationException("Исходная карта или настройки изменились во время захвата; повторите захват.");
                Snapshot = new MapGrowthSnapshot(InputVersion, input, search, profile, forbidden, reserved,
                    fixedRecipes, variants, templates.ToArray(), constraints, AuthorCell, anchors, capabilities);
                input = null; return true;
            }
            catch { Dispose(); throw; }
        }
        public bool IsCurrent()
        {
            MapGrowthSnapshotBuilder.RequireMainThread();
            if (disposed || !source.IsValid() || !source.isLoaded || markup == null || settings == null) return false;
            try { return MapGrowthSourceVersion.Compute(source, markup, settings, replacedRoots) == InputVersion; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        public MapGrowthPreview CreateFixedPreview()
        {
            MapGrowthSnapshotBuilder.RequireMainThread(); ThrowDisposed();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var ownership = geometry.Instantiate(scene);
                var preview = new MapGrowthPreview(this, scene, ownership); previews.Add(preview); return preview;
            }
            catch { EditorSceneManager.ClosePreviewScene(scene); throw; }
        }
        internal void Release(MapGrowthPreview preview)
        { previews.Remove(preview); if (disposed && previews.Count == 0) ReleaseGeometry(); }
        public void Dispose()
        {
            MapGrowthSnapshotBuilder.RequireMainThread();
            if (disposed) return; disposed = true; input = null;
            // Живой PhysicsScene может ещё читать mesh: его lease должен завершиться первым.
            if (previews.Count == 0) ReleaseGeometry();
        }
        private void ReleaseGeometry()
        { if (released) return; released = true; geometry.Dispose(); Live.Remove(this); }
        private void ThrowDisposed() { if (disposed) throw new ObjectDisposedException(nameof(MapGrowthSceneCapture)); }
        private static void ReleaseAll()
        {
            foreach (var capture in Live.ToArray())
            { foreach (var preview in capture.previews.ToArray()) preview.Dispose(); capture.Dispose(); }
        }
    }
    public sealed class MapGrowthPreview : IDisposable
    {
        private MapGrowthSceneCapture owner;
        public Scene Scene { get; }
        public IReadOnlyDictionary<Collider, string> FixedColliderIds { get; }
        internal MapGrowthPreview(MapGrowthSceneCapture owner, Scene scene, Dictionary<Collider, string> ids)
        { this.owner = owner; Scene = scene; FixedColliderIds = new System.Collections.ObjectModel.ReadOnlyDictionary<Collider, string>(ids); }
        public void Dispose()
        {
            MapGrowthSnapshotBuilder.RequireMainThread(); if (owner == null) return;
            var capture = owner; owner = null;
            try
            {
                if (Scene.IsValid() && Scene.isLoaded)
                { MapGrowthRayBatch.CompleteSceneJobs(Scene.GetPhysicsScene()); EditorSceneManager.ClosePreviewScene(Scene); }
            }
            finally { capture.Release(this); }
        }
    }
}
