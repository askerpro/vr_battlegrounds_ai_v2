using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.GripAuthoring.ContactMask
{
    /// <summary>Ручная разметка кожи. Выбор области никак не использует расстояние до предмета.</summary>
    public sealed class HandContactMaskWindow : EditorWindow
    {
        static readonly string[] Labels = { "Ладонь", "Большой", "Указательный", "Средний", "Безымянный", "Мизинец" };
        static readonly Color[] Palette = { new Color(.95f, .65f, .2f), new Color(.95f, .25f, .3f),
            new Color(.2f, .8f, .4f), new Color(.2f, .65f, 1f), new Color(.75f, .4f, 1f), new Color(1f, .4f, .8f) };
        [SerializeField] UxrAvatar _avatar;
        [SerializeField] GameObject _weapon;
        [SerializeField] UxrHandPoseAsset _pose;
        [SerializeField] UxrHandSide _side = UxrHandSide.Right;
        [SerializeField] int _grabPoint, _region;
        [SerializeField] string _grabbablePath = "", _output = HandContactMaskCapture.DefaultOutputDirectory;
        [SerializeField] bool _allowDefaultGrip;
        [SerializeField] float _brushPixels = 12;
        [SerializeField] int _subdivisions = 8;
        [SerializeField] Vector2 _orbit = new Vector2(25, -25);
        [SerializeField] float _zoom = 1;
        HandContactMaskCapture _capture;
        HandPosePotentialContactMask _mask;
        HandPosePotentialContactCoverageReport _coverage;
        PreviewRenderUtility _preview;
        Material _material;
        Mesh _mesh;
        bool _stale = true, _meshDirty;
        string _message, _captureInputSignature;
        Vector2 _scroll;

        [MenuItem("VR Battlegrounds/Avatars/Hand Contact Mask")]
        public static void Open() => GetWindow<HandContactMaskWindow>("Контактная маска кисти");

        public static Color RegionColor(string region)
        {
            int index = Array.IndexOf(HandContactMaskCapture.RegionNames, region);
            return index >= 0 ? Palette[index] : new Color(.35f, .38f, .42f);
        }

        void OnDisable()
        {
            _preview?.Cleanup(); _preview = null;
            if (_mesh) DestroyImmediate(_mesh);
            if (_material) DestroyImmediate(_material);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Потенциально контактная кожа: ладонь и 5 пальцев", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _avatar = (UxrAvatar)EditorGUILayout.ObjectField("Prefab аватара", _avatar, typeof(UxrAvatar), false);
            _weapon = (GameObject)EditorGUILayout.ObjectField("Prefab предмета", _weapon, typeof(GameObject), false);
            _pose = (UxrHandPoseAsset)EditorGUILayout.ObjectField("Override позы (необязательно)", _pose, typeof(UxrHandPoseAsset), false);
            _side = (UxrHandSide)EditorGUILayout.EnumPopup("Сторона", _side);
            _grabPoint = EditorGUILayout.IntField("GrabPoint", _grabPoint);
            _grabbablePath = EditorGUILayout.TextField("Путь Grabbable (индексы)", _grabbablePath);
            _allowDefaultGrip = EditorGUILayout.Toggle("Разрешить default grip", _allowDefaultGrip);
            if (EditorGUI.EndChangeCheck()) InvalidateCapture();
            // Внешняя правка позы/предмета также требует нового BakeMesh, но не меняет mesh binding маски.
            if (_capture != null && !_stale && InputSignature() != _captureInputSignature) InvalidateCapture();
            using (new EditorGUI.DisabledScope(!_avatar || !_weapon || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Захватить статический снимок (исходные ассеты не меняются)")) Run(Capture);
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
            if (_capture == null) return;
            if (_stale) EditorGUILayout.HelpBox("Снимок устарел. Перезахватите после смены входов; проценты скрыты.", MessageType.Warning);
            EditorGUILayout.LabelField("Binding: " + _capture.BindingFingerprint.Substring(0, 16) + "…");
            using (new EditorGUI.DisabledScope(_stale))
            {
                _region = GUILayout.SelectionGrid(_region, Labels, 3);
                _brushPixels = EditorGUILayout.Slider("Кисть, px (0 = одна грань)", _brushPixels, 0, 55);
                EditorGUILayout.LabelField("ЛКМ — целые грани; Ctrl+ЛКМ — стереть; ПКМ — вращать; колесо — масштаб.", EditorStyles.miniLabel);
                var rect = GUILayoutUtility.GetRect(150, Mathf.Clamp(position.height * .38f, 180, 460), GUILayout.ExpandWidth(true));
                Run(() => DrawPreview(rect));
                EditorGUI.BeginChangeCheck();
                var reviewed = EditorGUILayout.ToggleLeft("Reviewed: я проверил границы всех шести областей", _mask.Metadata.Reviewed);
                if (EditorGUI.EndChangeCheck()) { _mask.Metadata.Reviewed = reviewed; _coverage = null; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Загрузить JSON")) Run(Load);
                    if (GUILayout.Button("Новая пустая маска")) { _mask = _capture.NewMask(); Changed(); }
                }
                EditorGUI.BeginChangeCheck();
                _subdivisions = EditorGUILayout.IntSlider("Подразбиения для площади", _subdivisions, 2, 24);
                if (EditorGUI.EndChangeCheck()) _coverage = null;
                if (GUILayout.Button("Рассчитать площадь в пределах ±2 мм")) Run(() => _coverage = _capture.Evaluate(_mask, Settings()));
                _output = EditorGUILayout.TextField("Каталог отчёта", _output);
                if (GUILayout.Button("Сохранить JSON + отчёт + PNG")) Run(Save);
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawCountsAndCoverage();
            EditorGUILayout.EndScrollView();
        }

        void InvalidateCapture() { _stale = true; _coverage = null; }
        void Run(Action action)
        {
            try { action(); }
            catch (Exception e) { _message = e.Message; }
        }
        string InputSignature()
        {
            string Asset(UnityEngine.Object asset)
            {
                if (!asset) return "null";
                string path = AssetDatabase.GetAssetPath(asset);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId);
                return guid + ":" + localId + ":" + AssetDatabase.GetAssetDependencyHash(path);
            }
            return Asset(_avatar) + "|" + Asset(_weapon) + "|" + Asset(_pose) + "|" + _side + "|" + _grabPoint + "|" + _grabbablePath + "|" + _allowDefaultGrip;
        }
        HandPosePotentialContactCoverageSettings Settings() => new HandPosePotentialContactCoverageSettings { ContactBandMm = 2, Subdivisions = _subdivisions };

        void Capture()
        {
            var captured = HandContactMaskCapture.Capture(new HandPoseFitRequest
            {
                AvatarPrefab = _avatar, ObjectPrefab = _weapon, PoseOverride = _pose,
                Side = _side, GrabPoint = _grabPoint, GrabbableIndexPath = _grabbablePath,
                AllowDefaultGrip = _allowDefaultGrip,
                OutputDirectory = Path.Combine(HandContactMaskCapture.DefaultOutputDirectory, "captures", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"))
            });
            bool reuse = _mask?.Metadata?.BindingFingerprint == captured.BindingFingerprint;
            _capture = captured;
            if (!reuse) _mask = captured.NewMask();
            _coverage = null; _stale = false; _meshDirty = true;
            _captureInputSignature = InputSignature();
            _message = reuse ? "Снимок обновлён; mesh binding совпал, ручная маска сохранена." : "Снимок получен. Новая пустая маска; выбирайте только потенциально контактную кожу.";
            Repaint();
        }

        void Load()
        {
            string path = EditorUtility.OpenFilePanel("Маска контакта", Path.GetFullPath(HandContactMaskCapture.DefaultOutputDirectory), "json");
            if (string.IsNullOrEmpty(path)) return;
            _mask = _capture.LoadMask(path);
            _coverage = null; _meshDirty = true;
            _message = "Маска загружена; mesh binding и ключи проверены.";
        }

        void Changed()
        {
            _mask.Metadata.Reviewed = false;
            _coverage = null; _meshDirty = true;
            Repaint();
        }

        void EnsurePreview()
        {
            if (_preview == null) _preview = new PreviewRenderUtility();
            if (!_material)
            {
                var shader = Shader.Find("Hidden/VRBattlegrounds/HandPoseFit");
                if (!shader) throw new InvalidOperationException("Шейдер HandPoseFit не найден.");
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_mesh && !_meshDirty) return;
            if (_mesh) DestroyImmediate(_mesh);
            var entries = _mask.Triangles.ToDictionary(Key);
            var vertices = new List<Vector3>(_capture.Hand.Length * 3);
            var colors = new List<Color>(_capture.Hand.Length * 3);
            foreach (var triangle in _capture.Hand)
            {
                Color color = new Color(.35f, .38f, .42f);
                if (entries.TryGetValue(Key(triangle), out var entry))
                {
                    int index = Array.IndexOf(HandContactMaskCapture.RegionNames, entry.Region);
                    if (index >= 0) color = Palette[index];
                }
                vertices.Add(triangle.A); vertices.Add(triangle.B); vertices.Add(triangle.C);
                colors.Add(color); colors.Add(color); colors.Add(color);
            }
            _mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            _mesh.SetVertices(vertices); _mesh.SetColors(colors);
            _mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0);
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds(); _meshDirty = false;
        }

        void ConfigureCamera(Rect rect)
        {
            var camera = _preview.camera;
            Quaternion rotation = Quaternion.Euler(_orbit.y, _orbit.x, 0);
            camera.transform.SetPositionAndRotation(_capture.HandBounds.center - rotation * Vector3.forward * 2, rotation);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(.005f, _capture.HandBounds.extents.magnitude * 1.15f * _zoom);
            camera.aspect = rect.width / rect.height;
            camera.nearClipPlane = .001f; camera.farClipPlane = 10;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .085f, .105f);
            camera.cullingMask = -1;
        }

        void DrawPreview(Rect rect)
        {
            EnsurePreview(); ConfigureCamera(rect);
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                _preview.BeginPreview(rect, GUIStyle.none);
                _preview.DrawMesh(_mesh, Matrix4x4.identity, _material, 0);
                _preview.Render(true);
                GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false);
            }
            if (_stale || !rect.Contains(e.mousePosition)) return;
            if (e.type == EventType.MouseDrag && e.button == 1)
            { _orbit += e.delta * .6f; e.Use(); Repaint(); }
            else if (e.type == EventType.ScrollWheel)
            { _zoom = Mathf.Clamp(_zoom * Mathf.Exp(e.delta.y * .06f), .15f, 5); e.Use(); Repaint(); }
            else if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
            { Paint(rect, e.mousePosition, e.control || e.command); e.Use(); }
        }

        void Paint(Rect rect, Vector2 point, bool erase)
        {
            var camera = _preview.camera;
            Ray RayAt(Vector2 p) => camera.ViewportPointToRay(new Vector3((p.x - rect.x) / rect.width, 1 - (p.y - rect.y) / rect.height, 0));
            // Каждый луч выбирает только ближайшую видимую грань: кисть не красит обратную сторону.
            var hits = new HashSet<int>();
            void Pick(Vector2 p)
            {
                var ray = RayAt(p); float nearest = float.PositiveInfinity; int found = -1;
                for (int i = 0; i < _capture.Hand.Length; i++)
                    if (Intersect(ray, _capture.Hand[i], out float distance) && distance < nearest)
                    { nearest = distance; found = i; }
                if (found >= 0) hits.Add(found);
            }
            Pick(point);
            if (_brushPixels > 0)
                for (float y = -_brushPixels; y <= _brushPixels; y += 5)
                    for (float x = -_brushPixels; x <= _brushPixels; x += 5)
                        if (x * x + y * y <= _brushPixels * _brushPixels) Pick(point + new Vector2(x, y));
            if (_capture.SetTriangles(_mask, hits.Select(index => Key(_capture.Hand[index])), HandContactMaskCapture.RegionNames[_region], erase)) Changed();
        }

        static bool Intersect(Ray ray, FitTriangle t, out float distance)
        {
            distance = 0;
            Vector3 ab = t.B - t.A, ac = t.C - t.A, p = Vector3.Cross(ray.direction, ac);
            float determinant = Vector3.Dot(ab, p);
            if (Mathf.Abs(determinant) < 1e-12f) return false;
            float inverse = 1 / determinant;
            Vector3 delta = ray.origin - t.A;
            float u = Vector3.Dot(delta, p) * inverse;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(delta, ab);
            float v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0 || u + v > 1) return false;
            distance = Vector3.Dot(ac, q) * inverse;
            return distance > 0;
        }

        void Save()
        {
            EnsurePreview();
            var rect = new Rect(0, 0, 1024, 1024); ConfigureCamera(rect);
            _preview.BeginStaticPreview(rect);
            _preview.DrawMesh(_mesh, Matrix4x4.identity, _material, 0);
            _preview.Render(true);
            Texture2D image = _preview.EndStaticPreview();
            try
            {
                _coverage = _capture.Evaluate(_mask, Settings());
                string directory = Path.Combine(_output, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
                _message = "Сохранено: " + _capture.Export(directory, _mask, _coverage, image.EncodeToPNG());
            }
            finally { DestroyImmediate(image); }
        }

        void DrawCountsAndCoverage()
        {
            for (int i = 0; i < Labels.Length; i++)
            {
                string region = HandContactMaskCapture.RegionNames[i];
                Color previous = GUI.color; GUI.color = Palette[i];
                EditorGUILayout.LabelField(Labels[i] + ": " + _mask.Triangles.Count(t => t.Region == region) + " граней");
                GUI.color = previous;
            }
            if (_stale || _coverage == null) return;
            EditorGUILayout.LabelField("Статус: " + _coverage.Status + "; Reviewed: " + _coverage.MaskReviewed);
            foreach (var error in _coverage.Errors) EditorGUILayout.HelpBox(error, MessageType.Warning);
            foreach (var r in _coverage.Regions.Concat(_coverage.Total == null ? Enumerable.Empty<HandPosePotentialContactCoverageRegion>() : new[] { _coverage.Total }))
            {
                string N(double? value) => HandContactMaskCapture.Number(value);
                EditorGUILayout.LabelField(r.Region + ": " + N(r.CurrentCoveragePercent) + "%; potential " +
                    N(r.CurrentCoveragePercent.HasValue ? (double?)r.CurrentPotentialAreaMm2 : null) + " мм²; band " +
                    N(r.CurrentCoveragePercent.HasValue ? (double?)r.ContactBandAreaMm2 : null) + " мм²", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("reference weighted " + N(r.ReferenceWeightedContactPercent) + "%; qualified " + N(r.QualifiedCoveragePercent) +
                    "%; bounds " + N(r.ContactPercentLowerBound) + " … " + N(r.ContactPercentUpperBound) +
                    "%; cell ≤ " + N(r.CurrentCoveragePercent.HasValue ? (double?)r.MaxSampleCellDiameterMm : null) + " мм; Δfine/coarse " +
                    N(r.FineMinusCoarsePercentagePoints) + " п.п.", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.HelpBox("Статический снимок prefab. Gap/deep — unknown без сертифицированного знака. Qualified: band и противоположная нормаль. " + _coverage.PrecisionMeaning, MessageType.Info);
        }

        static HandPosePotentialContactTriangleKey Key(FitTriangle t) => new HandPosePotentialContactTriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle);
        static HandPosePotentialContactTriangleKey Key(HandPosePotentialContactMaskTriangle t) => new HandPosePotentialContactTriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle);
    }
}
