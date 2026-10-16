using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Editor.Avatars.ContactIk;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.GripAuthoring.ContactPoints
{
    /// <summary>Неизменяемая кожная опора. UI не переназначает её при перемещении пальца.</summary>
    public sealed class HandContactIkPreviewAnchor
    {
        readonly ContactIkSurfaceAnchor _skin;
        public string Id { get; }
        public ContactIkTargetRole Role { get; }
        public string FingerId { get; }
        public string Provenance { get; }
        public ContactIkSurfaceAnchor SkinAnchor => _skin.Copy();
        public HandContactIkPreviewAnchor(string id, ContactIkTargetRole role, string fingerId,
            ContactIkSurfaceAnchor skinAnchor, string provenance = "")
        { Id = id; Role = role; FingerId = fingerId; _skin = skinAnchor?.Copy() ?? throw new ArgumentNullException(nameof(skinAnchor)); Provenance = provenance ?? ""; }
        public HandContactIkPreviewAnchor Copy() => new HandContactIkPreviewAnchor(Id, Role, FingerId, _skin, Provenance);
    }

    /// <summary>Явная цель на материальной поверхности; ни pick, ни inference не подтверждают качество хвата.</summary>
    public sealed class HandContactIkPreviewTarget
    {
        readonly ContactIkSurfaceAnchor _object;
        public string Id { get; }
        public string Provenance { get; }
        public bool Reviewed => false;
        public ContactIkSurfaceAnchor ObjectAnchor => _object.Copy();
        public HandContactIkPreviewTarget(string id, ContactIkSurfaceAnchor objectAnchor, string provenance = "")
        { Id = id; _object = objectAnchor?.Copy() ?? throw new ArgumentNullException(nameof(objectAnchor)); Provenance = provenance ?? ""; }
        public HandContactIkPreviewTarget Copy() => new HandContactIkPreviewTarget(Id, _object, Provenance);
    }

    /// <summary>Замороженный вход root bridge: все позиции в одной системе выбранного предмета.</summary>
    public sealed class HandContactIkPreviewView
    {
        readonly FitTriangle[] _before, _object;
        readonly HandContactIkPreviewAnchor[] _skin;
        readonly HandContactIkPreviewTarget[] _initial;
        public string CaseId { get; }
        public string Provenance { get; }
        public string BindingFingerprint { get; }
        public string ObjectFingerprint { get; }
        public Quaternion CameraRotation { get; }
        public FitTriangle[] BeforeHand => (FitTriangle[])_before.Clone();
        public FitTriangle[] ObjectSurface => (FitTriangle[])_object.Clone();
        public HandContactIkPreviewAnchor[] SkinAnchors => _skin.Select(x => x?.Copy()).ToArray();
        public HandContactIkPreviewTarget[] InitialTargets => _initial.Select(x => x?.Copy()).ToArray();
        public HandContactIkPreviewView(string caseId, string provenance, string bindingFingerprint, string objectFingerprint,
            FitTriangle[] beforeHand, FitTriangle[] objectSurface, HandContactIkPreviewAnchor[] skinAnchors,
            HandContactIkPreviewTarget[] initialTargets = null, Quaternion? cameraRotation = null)
        {
            CaseId = caseId; Provenance = provenance ?? ""; BindingFingerprint = bindingFingerprint; ObjectFingerprint = objectFingerprint;
            _before = (FitTriangle[])(beforeHand ?? throw new ArgumentNullException(nameof(beforeHand))).Clone();
            _object = (FitTriangle[])(objectSurface ?? throw new ArgumentNullException(nameof(objectSurface))).Clone();
            _skin = (skinAnchors ?? throw new ArgumentNullException(nameof(skinAnchors))).Select(x => x?.Copy()).ToArray();
            _initial = (initialTargets ?? Array.Empty<HandContactIkPreviewTarget>()).Select(x => x?.Copy()).ToArray();
            CameraRotation = cameraRotation ?? Quaternion.identity;
        }
    }

    public sealed class HandContactIkPreviewDiagnostic
    {
        public string Id { get; }
        public ContactIkTargetRole Role { get; }
        public double? ResidualMm { get; }
        public double Weight { get; }
        public bool Ignored { get; }
        public string Detail { get; }
        public HandContactIkPreviewDiagnostic(string id, ContactIkTargetRole role, double? residualMm,
            double weight, bool ignored, string detail = "")
        { Id = id; Role = role; ResidualMm = residualMm; Weight = weight; Ignored = ignored; Detail = detail ?? ""; }
    }

    public sealed class HandContactIkPreviewResult
    {
        readonly FitTriangle[] _after;
        readonly HandContactIkPreviewDiagnostic[] _targets;
        readonly string[] _errors;
        public FitTriangle[] AfterHand => (FitTriangle[])_after.Clone();
        public HandContactIkPreviewDiagnostic[] Targets => (HandContactIkPreviewDiagnostic[])_targets.Clone();
        public Vector3 PalmOffsetMeters { get; }
        public string Diagnostics { get; }
        public bool NumericalReached { get; }
        public bool Reviewed => false;
        public string[] Errors => (string[])_errors.Clone();
        public HandContactIkPreviewResult(FitTriangle[] afterHand, HandContactIkPreviewDiagnostic[] targets,
            Vector3 palmOffsetMeters, string diagnostics, bool numericalReached = false, string[] errors = null)
        {
            _after = (FitTriangle[])(afterHand ?? throw new ArgumentNullException(nameof(afterHand))).Clone();
            _targets = (HandContactIkPreviewDiagnostic[])(targets ?? Array.Empty<HandContactIkPreviewDiagnostic>()).Clone();
            PalmOffsetMeters = palmOffsetMeters; Diagnostics = diagnostics ?? ""; NumericalReached = numericalReached;
            _errors = (string[])(errors ?? Array.Empty<string>()).Clone();
        }
    }

    /// <summary>Только frozen preview и явные object anchors. Native rig и solver принадлежат callback root.</summary>
    public sealed class HandContactIkPreviewWindow : EditorWindow
    {
        const string Reports = "tasks/hand-rig-quality/reports/phalanx-probes-ui";
        static readonly Color SkinColor = new Color(.1f, 1f, .85f), TargetColor = new Color(1f, .3f, .15f);
        HandContactIkPreviewView _view;
        Func<float, HandContactIkPreviewTarget[], HandContactIkPreviewResult> _run;
        FitTriangle[] _before, _surface, _after;
        HandContactIkPreviewAnchor[] _anchors;
        readonly Dictionary<string, HandContactIkPreviewTarget> _targets = new Dictionary<string, HandContactIkPreviewTarget>(StringComparer.Ordinal);
        HandContactIkPreviewResult _result;
        int _active;
        float _priority, _zoom = 1;
        bool _showAfter, _meshDirty = true;
        string _message;
        Vector2 _orbit, _scroll;
        Vector3 _frameCenter;
        float _frameSize;
        PreviewRenderUtility _preview;
        Material _material, _markerMaterial;
        Mesh _handMesh, _objectMesh;

        [MenuItem("VR Battlegrounds/Avatars/Черновики/Контактный IK — preview v2", false, 1)]
        public static void Open() => GetWindow<HandContactIkPreviewWindow>("Контактный IK v2");

        public static HandContactIkPreviewWindow OpenPrepared(HandContactIkPreviewView view,
            Func<float, HandContactIkPreviewTarget[], HandContactIkPreviewResult> run)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (run == null) throw new ArgumentNullException(nameof(run));
            var window = CreateInstance<HandContactIkPreviewWindow>();
            try
            {
                window.Prepare(view, run);
                window.titleContent = new GUIContent("Контактный IK v2"); window.Show(); window.Repaint();
                return window;
            }
            catch { DestroyImmediate(window); throw; }
        }

        void OnEnable() => minSize = new Vector2(640, 720);
        void OnDisable()
        {
            _preview?.Cleanup(); _preview = null;
            ReleasePreviewObject(_handMesh); ReleasePreviewObject(_objectMesh); ReleasePreviewObject(_material); ReleasePreviewObject(_markerMaterial);
        }

        void Prepare(HandContactIkPreviewView view, Func<float, HandContactIkPreviewTarget[], HandContactIkPreviewResult> run)
        {
            var before = view.BeforeHand; var surface = view.ObjectSurface; var anchors = view.SkinAnchors;
            ValidateGeometry(before, "before hand"); ValidateGeometry(surface, "object surface");
            if (string.IsNullOrWhiteSpace(view.BindingFingerprint) || string.IsNullOrWhiteSpace(view.ObjectFingerprint))
                throw new ArgumentException("Нужны явные skin и object fingerprints.");
            var fingers = new HashSet<string>(new[] { "thumb", "index", "middle", "ring", "little" }, StringComparer.OrdinalIgnoreCase);
            if (anchors.Length != 6 || anchors.Any(a => a == null || string.IsNullOrWhiteSpace(a.Id)) ||
                anchors.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != anchors.Length ||
                anchors.Count(a => a.Role == ContactIkTargetRole.Palm) != 1 || anchors.Count(a => a.Role == ContactIkTargetRole.Distal) != 5 ||
                !fingers.SetEquals(anchors.Where(a => a.Role == ContactIkTargetRole.Distal).Select(a => a.FingerId)))
                throw new ArgumentException("Нужны уникальные slots: ладонь и distal пяти разных пальцев.");
            foreach (var anchor in anchors) anchor.SkinAnchor.Evaluate(before);
            if (!Finite(view.CameraRotation.x) || !Finite(view.CameraRotation.y) || !Finite(view.CameraRotation.z) || !Finite(view.CameraRotation.w) ||
                Mathf.Abs(view.CameraRotation.x * view.CameraRotation.x + view.CameraRotation.y * view.CameraRotation.y + view.CameraRotation.z * view.CameraRotation.z + view.CameraRotation.w * view.CameraRotation.w - 1) > .01f)
                throw new ArgumentException("Нужен нормированный поворот камеры.");
            foreach (var target in view.InitialTargets)
            {
                if (target == null || !anchors.Any(a => a.Id == target.Id) || _targets.ContainsKey(target.Id))
                    throw new ArgumentException("Неверная или повторная начальная цель.");
                target.ObjectAnchor.Evaluate(surface); _targets.Add(target.Id, target.Copy());
            }
            _view = view; _run = run; _before = before; _surface = surface; _anchors = anchors;
            var bounds = before[0].Bounds; foreach (var t in before) bounds.Encapsulate(t.Bounds);
            _frameCenter = bounds.center; _frameSize = Mathf.Max(.01f, bounds.extents.magnitude * 1.6f);
        }

        void OnGUI()
        {
            if (_view == null)
            {
                EditorGUILayout.HelpBox("Окно открывается подготовленным захватом через OpenPrepared(view, callback). Root bridge задаёт основной хват, fixed skin anchors, object surface и solver callback. Случайное оружие не выбирается.", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("Контактный IK v2 · " + _view.CaseId, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_view.Provenance, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Review=false · численное достижение и качество хвата различаются. ЛКМ по предмету назначает цель активному slot.", EditorStyles.wordWrappedMiniLabel);
            string[] labels = _anchors.Select((a, i) => (i + 1) + ". " + a.Id + (_targets.ContainsKey(a.Id) ? " ✓" : " — цель не задана")).ToArray();
            _active = GUILayout.SelectionGrid(_active, labels, 3);
            var active = _anchors[_active];
            EditorGUILayout.LabelField("Кожа: " + active.Role + "/" + active.FingerId + "; " + active.Provenance, EditorStyles.wordWrappedMiniLabel);
            if (_targets.TryGetValue(active.Id, out var selected))
            {
                EditorGUILayout.LabelField("Цель: " + selected.Provenance + "; Reviewed=" + selected.Reviewed, EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Удалить цель «" + active.Id + "»")) { _targets.Remove(active.Id); Changed(); }
            }
            EditorGUI.BeginChangeCheck();
            _priority = EditorGUILayout.Slider("DistalPriority", _priority, 0, 1);
            if (EditorGUI.EndChangeCheck()) Changed();
            EditorGUILayout.LabelField("Вес промежуточных = " + (1 - _priority).ToString("0.###", CultureInfo.InvariantCulture) +
                "; distal и palm не отключаются. Fullskin — отдельная диагностика.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_targets.Count != _anchors.Length || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating))
                    if (GUILayout.Button("Рассчитать preview")) Safe(RunPreview);
                using (new EditorGUI.DisabledScope(_result == null))
                {
                    EditorGUI.BeginChangeCheck(); bool show = GUILayout.Toggle(_showAfter, "Показать after", "Button");
                    if (EditorGUI.EndChangeCheck()) { _showAfter = show; _meshDirty = true; }
                    if (GUILayout.Button("Экспорт before / after")) Safe(ExportDialog);
                }
            }
            var rect = GUILayoutUtility.GetRect(150, Mathf.Clamp(position.height * .42f, 250, 550), GUILayout.ExpandWidth(true));
            Safe(() => DrawPreview(rect));
            EditorGUILayout.LabelField((_showAfter ? "AFTER" : "BEFORE") + " · бирюзовый: skin; красный: object target; номера совпадают со slots. " +
                "ПКМ — вращать, колесо — масштаб; projected метки видны сквозь геометрию.", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_result != null)
            {
                EditorGUILayout.LabelField("NumericalReached=" + _result.NumericalReached + "; Reviewed=false; смещение ладони " +
                    (_result.PalmOffsetMeters * 1000).ToString("F3") + " мм", EditorStyles.wordWrappedLabel);
                foreach (var error in _result.Errors) EditorGUILayout.HelpBox(error, MessageType.Warning);
                foreach (var diagnostic in _result.Targets)
                    EditorGUILayout.LabelField(diagnostic.Id + " / " + diagnostic.Role + ": " + Number(diagnostic.ResidualMm) +
                        " мм; weight=" + Number(diagnostic.Weight) + "; ignored=" + diagnostic.Ignored + "; " + diagnostic.Detail, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(_result.Diagnostics, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        void Changed()
        {
            _result = null; _after = null; _showAfter = false; _meshDirty = true;
            _message = "Цели или приоритет изменились: прежний after сброшен; нужен новый расчёт из того же before.";
        }
        void Safe(Action action)
        {
            try { action(); }
            catch (ExitGUIException) { throw; }
            catch (Exception error) { _message = error.Message; }
        }

        void RunPreview()
        {
            _result = null; _after = null; _showAfter = false; _meshDirty = true;
            var targets = _anchors.Select(a => _targets[a.Id].Copy()).ToArray();
            foreach (var target in targets) target.ObjectAnchor.Evaluate(_surface);
            var result = _run(_priority, targets) ?? throw new InvalidOperationException("Callback не вернул результат.");
            var after = result.AfterHand; ValidateGeometry(after, "after hand");
            var beforeKeys = new HashSet<(string, int, int)>(_before.Select(Key));
            if (!beforeKeys.SetEquals(after.Select(Key))) throw new ArgumentException("Материальная топология after не совпадает с before.");
            foreach (var anchor in _anchors) anchor.SkinAnchor.Evaluate(after);
            if (!Finite(result.PalmOffsetMeters)) throw new ArgumentException("Нечисловое смещение ладони.");
            foreach (var d in result.Targets)
                if (d == null || !Finite(d.Weight) || d.Weight < 0 || (d.ResidualMm.HasValue && !Finite(d.ResidualMm.Value)))
                    throw new ArgumentException("Невалидная численная диагностика callback.");
            _after = after; _result = result; _showAfter = true;
            _message = "Получен frozen after. NumericalReached=" + result.NumericalReached + "; review/acceptance остаются false.";
            Repaint();
        }

        void EnsureRenderer()
        {
            if (_preview == null) _preview = new PreviewRenderUtility();
            if (!_material)
            {
                var shader = Shader.Find("Hidden/VRBattlegrounds/HandPoseFit");
                if (!shader) throw new InvalidOperationException("Не найден HandPoseFit shader.");
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _markerMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _markerMaterial.SetFloat("_Shade", 0); _markerMaterial.SetFloat("_ZWrite", 0);
                _markerMaterial.SetFloat("_ZTest", (float)CompareFunction.Always); _markerMaterial.renderQueue = 3001;
            }
            if (!_objectMesh) _objectMesh = MakeMesh(_surface, new Color(.4f, .45f, .5f));
            if (!_handMesh || _meshDirty)
            { ReleasePreviewObject(_handMesh); _handMesh = MakeMesh(_showAfter && _after != null ? _after : _before, new Color(.82f, .65f, .46f)); _meshDirty = false; }
        }

        void ConfigureCamera(Rect rect)
        {
            var camera = _preview.camera;
            Quaternion rotation = _view.CameraRotation * Quaternion.Euler(_orbit.y, _orbit.x, 0);
            camera.transform.SetPositionAndRotation(_frameCenter - rotation * Vector3.forward * 2, rotation);
            camera.orthographic = true; camera.orthographicSize = _frameSize * _zoom; camera.aspect = rect.width / rect.height;
            camera.nearClipPlane = .001f; camera.farClipPlane = 100; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .075f, .095f); camera.cullingMask = -1;
        }

        void DrawPreview(Rect rect)
        {
            EnsureRenderer(); ConfigureCamera(rect);
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                _preview.BeginPreview(rect, GUIStyle.none);
                _preview.DrawMesh(_objectMesh, Matrix4x4.identity, _material, 0); _preview.DrawMesh(_handMesh, Matrix4x4.identity, _material, 0);
                Mesh marks = MarkerMesh(_showAfter && _after != null ? _after : _before, _frameSize * _zoom * .012f);
                try { _preview.DrawMesh(marks, Matrix4x4.identity, _markerMaterial, 0); _preview.DrawMesh(marks, Matrix4x4.identity, _markerMaterial, 1); _preview.Render(true); GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false); }
                finally { ReleasePreviewObject(marks); }
                DrawLabels(rect, _showAfter && _after != null ? _after : _before);
            }
            if (!rect.Contains(e.mousePosition)) return;
            if (e.type == EventType.MouseDrag && e.button == 1) { _orbit += e.delta * .6f; e.Use(); Repaint(); }
            else if (e.type == EventType.ScrollWheel) { _zoom = Mathf.Clamp(_zoom * Mathf.Exp(e.delta.y * .06f), .15f, 12); e.Use(); Repaint(); }
            else if (e.type == EventType.MouseDown && e.button == 0) { Pick(rect, e.mousePosition); e.Use(); }
        }

        void Pick(Rect rect, Vector2 mouse)
        {
            var ray = _preview.camera.ViewportPointToRay(new Vector3((mouse.x - rect.x) / rect.width, 1 - (mouse.y - rect.y) / rect.height, 0));
            float nearest = float.PositiveInfinity; FitTriangle? best = null; Vector3 bary = Vector3.zero;
            foreach (var triangle in _surface)
                if (Intersect(ray, triangle, out float distance, out Vector3 point) && distance < nearest)
                { nearest = distance; best = triangle; bary = point; }
            if (!best.HasValue) { _message = "Луч не попал в разрешённую поверхность предмета. Цель сохранена."; return; }
            var t = best.Value;
            _targets[_anchors[_active].Id] = new HandContactIkPreviewTarget(_anchors[_active].Id, new ContactIkSurfaceAnchor
            { Source = t.Source, SourceSubMesh = t.SourceSubMesh, SourceTriangle = t.SourceTriangle, Barycentric = bary }, "manual_pick; review=false");
            Changed(); _message = "Явная object material target назначена «" + _anchors[_active].Id + "». Reviewed=false."; Repaint();
        }

        static bool Intersect(Ray ray, FitTriangle t, out float distance, out Vector3 bary)
        {
            distance = 0; bary = Vector3.zero;
            Vector3 ab = t.B - t.A, ac = t.C - t.A, p = Vector3.Cross(ray.direction, ac); float det = Vector3.Dot(ab, p);
            if (Mathf.Abs(det) < 1e-14f) return false;
            Vector3 delta = ray.origin - t.A; float u = Vector3.Dot(delta, p) / det;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(delta, ab); float v = Vector3.Dot(ray.direction, q) / det;
            if (v < 0 || u + v > 1) return false;
            distance = Vector3.Dot(ac, q) / det;
            bary = new Vector3(Mathf.Max(0, 1 - u - v), u, v); bary /= bary.x + bary.y + bary.z;
            return distance > 0;
        }

        void DrawLabels(Rect rect, FitTriangle[] hand)
        {
            var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } };
            for (int i = 0; i < _anchors.Length; i++)
            {
                Vector3 p = _preview.camera.WorldToViewportPoint(_anchors[i].SkinAnchor.Evaluate(hand).PositionMeters);
                if (InView(p)) GUI.Label(new Rect(rect.x + p.x * rect.width + 7, rect.y + (1 - p.y) * rect.height - 10, 35, 25), (i + 1).ToString(), style);
                if (_targets.TryGetValue(_anchors[i].Id, out var target))
                {
                    p = _preview.camera.WorldToViewportPoint(target.ObjectAnchor.Evaluate(_surface).PositionMeters);
                    if (InView(p)) GUI.Label(new Rect(rect.x + p.x * rect.width + 7, rect.y + (1 - p.y) * rect.height - 10, 35, 25), (i + 1).ToString(), style);
                }
            }
        }

        Mesh MarkerMesh(FitTriangle[] hand, float radius)
        {
            var vertices = new List<Vector3>(); var colors = new List<Color>();
            var lines = new List<Vector3>();
            void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            { vertices.Add(a); vertices.Add(b); vertices.Add(c); colors.Add(color); colors.Add(color); colors.Add(color); }
            void Dot(Vector3 center, Color color)
            {
                var ring = new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back };
                for (int i = 0; i < 4; i++)
                {
                    Triangle(center + Vector3.up * radius, center + ring[i] * radius, center + ring[(i + 1) % 4] * radius, color);
                    Triangle(center - Vector3.up * radius, center + ring[(i + 1) % 4] * radius, center + ring[i] * radius, color);
                }
            }
            foreach (var slot in _anchors)
            {
                Vector3 skin = slot.SkinAnchor.Evaluate(hand).PositionMeters; Dot(skin, SkinColor);
                if (_targets.TryGetValue(slot.Id, out var target))
                {
                    Vector3 destination = target.ObjectAnchor.Evaluate(_surface).PositionMeters; Dot(destination, TargetColor);
                    lines.Add(skin); lines.Add(destination);
                }
            }
            int triangleCount = vertices.Count; vertices.AddRange(lines); colors.AddRange(lines.Select(_ => new Color(1, 1, .25f)));
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32, subMeshCount = 2 };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(Enumerable.Range(0, triangleCount).ToArray(), 0);
            mesh.SetIndices(Enumerable.Range(triangleCount, lines.Count).ToArray(), MeshTopology.Lines, 1); mesh.RecalculateBounds();
            return mesh;
        }

        public string ExportReview(string directory)
        {
            if (_result == null || _after == null) throw new InvalidOperationException("Нет актуального after; сравнение не экспортируется.");
            string full = Path.GetFullPath(directory), root = Path.GetFullPath("tasks/hand-rig-quality/reports").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Экспорт только внутри task reports.");
            if (Directory.Exists(full) || File.Exists(full)) throw new IOException("Каталог экспорта уже существует; история не перезаписывается.");
            // Оба изображения используют одну камеру, центр и масштаб; bounds after не меняют кадр.
            byte[] before = RenderPng(_before), after = RenderPng(_after);
            Directory.CreateDirectory(full);
            string beforePath = Path.Combine(full, "before.png"), afterPath = Path.Combine(full, "after.png");
            File.WriteAllBytes(beforePath, before); File.WriteAllBytes(afterPath, after);
            var camera = _preview.camera;
            var report = new
            {
                frameMode = "editor_frozen_preview", _view.CaseId, _view.Provenance, _view.BindingFingerprint, _view.ObjectFingerprint,
                DistalPriority = _priority, IntermediateWeightMultiplier = 1 - _priority, Reviewed = false, Accepted = false,
                SkinAnchors = _anchors, ObjectTargets = _anchors.Select(a => _targets[a.Id]).ToArray(),
                _result.NumericalReached, _result.PalmOffsetMeters, TargetDiagnostics = _result.Targets,
                FullSkinDiagnosticOnly = true, Diagnostics = _result.Diagnostics, Errors = _result.Errors,
                Camera = new { camera.transform.position, camera.transform.rotation, camera.orthographicSize },
                before = beforePath, after = afterPath
            };
            Write(Path.Combine(full, "preview.json"), JsonConvert.SerializeObject(report, FitUnityJsonConverter.Settings));
            var text = new StringBuilder("# Контактный IK v2: frozen preview\n\n").Append(_view.CaseId).Append("\n\n")
                .Append(_view.Provenance).Append("\n\nDistalPriority=").Append(Number(_priority)).Append("; intermediate multiplier=")
                .Append(Number(1 - _priority)).Append(". NumericalReached=").Append(_result.NumericalReached)
                .Append("; Reviewed=false; Accepted=false. Наблюдение в редакторе, не runtime/Quest acceptance.\n\n")
                .Append("Before и after сняты одной камерой. Бирюзовые markers — кожа, красные — заданные object targets; номера соответствуют slots. Метки и линии проецируются сквозь геометрию.\n\n")
                .Append("![Before](").Append(beforePath.Replace('\\', '/')).Append(")\n\n![After](").Append(afterPath.Replace('\\', '/')).Append(")\n\n")
                .Append("| № | Skin slot | Role | Target origin | Reviewed |\n|---:|---|---|---|---|\n");
            for (int i = 0; i < _anchors.Length; i++) text.Append('|').Append(i + 1).Append('|').Append(_anchors[i].Id).Append('|').Append(_anchors[i].Role)
                .Append('|').Append(_targets[_anchors[i].Id].Provenance.Replace("|", "/")).Append("|false|\n");
            text.Append("\n| Цель | Role | Ошибка, мм | Вес | Ignored | Диагностика |\n|---|---|---:|---:|---|---|\n");
            foreach (var d in _result.Targets) text.Append('|').Append(d.Id).Append('|').Append(d.Role).Append('|').Append(Number(d.ResidualMm)).Append('|')
                .Append(Number(d.Weight)).Append('|').Append(d.Ignored).Append('|').Append(d.Detail.Replace("|", "/")).Append("|\n");
            text.Append("\nСмещение ладони, м: ").Append(_result.PalmOffsetMeters.ToString("F6")).Append(".\n\nПолная кожа — только диагностика:\n\n")
                .Append(_result.Diagnostics).Append("\n\nОшибки: ").Append(string.Join("; ", _result.Errors)).Append('\n');
            string path = Path.Combine(full, "report.md"); Write(path, text.ToString()); return path;
        }

        void ExportDialog()
        {
            string initial = Path.GetFullPath(Directory.Exists(Reports) ? Reports : "tasks/hand-rig-quality/reports");
            string selected = EditorUtility.OpenFolderPanel("Экспорт preview внутри task reports", initial, "");
            if (string.IsNullOrEmpty(selected)) return;
            _message = "Экспортирован реальный before/after: " + ExportReview(Path.Combine(selected, "contact-ik-v2-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)));
        }

        byte[] RenderPng(FitTriangle[] hand)
        {
            EnsureRenderer(); var rect = new Rect(0, 0, 1024, 1024); ConfigureCamera(rect);
            Mesh mesh = MakeMesh(hand, new Color(.82f, .65f, .46f)), marks = MarkerMesh(hand, _frameSize * _zoom * .012f); Texture2D image = null;
            try
            {
                _preview.BeginStaticPreview(rect); _preview.DrawMesh(_objectMesh, Matrix4x4.identity, _material, 0);
                _preview.DrawMesh(mesh, Matrix4x4.identity, _material, 0);
                _preview.DrawMesh(marks, Matrix4x4.identity, _markerMaterial, 0); _preview.DrawMesh(marks, Matrix4x4.identity, _markerMaterial, 1);
                _preview.Render(true); image = _preview.EndStaticPreview();
                for (int i = 0; i < _anchors.Length; i++)
                {
                    LabelPng(image, _anchors[i].SkinAnchor.Evaluate(hand).PositionMeters, i + 1);
                    LabelPng(image, _targets[_anchors[i].Id].ObjectAnchor.Evaluate(_surface).PositionMeters, i + 1);
                }
                image.Apply(); return image.EncodeToPNG();
            }
            finally { ReleasePreviewObject(image); ReleasePreviewObject(mesh); ReleasePreviewObject(marks); }
        }

        void LabelPng(Texture2D image, Vector3 position, int number)
        {
            Vector3 p = _preview.camera.WorldToViewportPoint(position);
            if (p.z <= 0 || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) return;
            int x = Mathf.RoundToInt(p.x * image.width) + 8, y = Mathf.RoundToInt(p.y * image.height) + 3;
            int[] digits = { 63, 6, 91, 79, 102, 109, 125, 7, 127, 111 };
            void Pixel(int px, int py, Color color) { if (px >= 0 && py >= 0 && px < image.width && py < image.height) image.SetPixel(px, py, color); }
            for (int dx = -2; dx < 14; dx++) for (int dy = -2; dy < 21; dy++) Pixel(x + dx, y + dy, Color.black);
            void Bar(int sx, int sy, int width, int height)
            { for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++) Pixel(x + sx + dx, y + sy + dy, Color.white); }
            int bits = digits[number % 10];
            if ((bits & 1) != 0) Bar(2, 16, 8, 2); if ((bits & 2) != 0) Bar(10, 9, 2, 8); if ((bits & 4) != 0) Bar(10, 1, 2, 8);
            if ((bits & 8) != 0) Bar(2, 0, 8, 2); if ((bits & 16) != 0) Bar(0, 1, 2, 8); if ((bits & 32) != 0) Bar(0, 9, 2, 8); if ((bits & 64) != 0) Bar(2, 8, 8, 2);
        }

        static Mesh MakeMesh(FitTriangle[] triangles, Color color)
        {
            var vertices = new List<Vector3>(); var colors = new List<Color>();
            foreach (var t in triangles) { vertices.Add(t.A); vertices.Add(t.B); vertices.Add(t.C); colors.Add(color); colors.Add(color); colors.Add(color); }
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static void ValidateGeometry(FitTriangle[] triangles, string name)
        {
            if (triangles == null || triangles.Length == 0) throw new ArgumentException("Нет геометрии: " + name);
            var keys = new HashSet<(string, int, int)>();
            foreach (var t in triangles)
                if (!Finite(t.A) || !Finite(t.B) || !Finite(t.C) || !Finite(t.Area) || t.Area <= 1e-12f ||
                    string.IsNullOrWhiteSpace(t.Source) || t.SourceSubMesh < 0 || t.SourceTriangle < 0 || !keys.Add(Key(t)))
                    throw new ArgumentException("Неверная/повторная материальная грань или nonfinite geometry: " + name);
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static (string, int, int) Key(FitTriangle t) => (t.Source, t.SourceSubMesh, t.SourceTriangle);
        static string Number(double? value) => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "unknown";
        static void Write(string path, string value) => File.WriteAllText(path, value.Replace("\r\n", "\n"), new UTF8Encoding(false));
        static bool InView(Vector3 value) => value.z > 0 && value.x >= 0 && value.x <= 1 && value.y >= 0 && value.y <= 1;
        static void ReleasePreviewObject(UnityEngine.Object value) { if (value) DestroyImmediate(value); }
    }
}
