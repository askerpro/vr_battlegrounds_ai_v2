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
using VrBattlegrounds.Editor.Avatars;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.GripAuthoring.ContactPoints
{
    /// <summary>Одна кисть и фиксированные опорные точки. Предмет нужен только для проверки касания.</summary>
    public sealed class HandContactProbeWindow : EditorWindow
    {
        const string Reports = "tasks/hand-rig-quality/reports/phalanx-probes-ui";
        [SerializeField] UxrAvatar _avatar;
        [SerializeField] UxrHandPoseAsset _referencePose;
        [SerializeField] UxrHandSide _side = UxrHandSide.Right;
        [SerializeField] GameObject _weapon;
        [SerializeField] UxrHandPoseAsset _measurementPose;
        [SerializeField] int _grabPoint;
        [SerializeField] string _grabbableIndexPath = "";
        [SerializeField] bool _measureOpen, _allowDefaultGrip, _showMirror;
        [SerializeField] Vector2 _orbit = Vector2.zero;
        [SerializeField] float _zoom = 1;
        HandContactProbeCapture _primaryCapture, _mirrorCapture;
        HandPhalanxProbeSet _primarySet, _mirrorSet;
        HandPhalanxProbeReport _measurement;
        PreviewRenderUtility _preview;
        Material _material;
        Mesh _mesh;
        bool _meshDirty = true, _stale;
        string _captureSignature, _measurementSignature, _message;
        Vector2 _scroll;

        HandContactProbeCapture CurrentCapture => _showMirror ? _mirrorCapture : _primaryCapture;
        HandPhalanxProbeSet CurrentSet => _showMirror ? _mirrorSet : _primarySet;

        [MenuItem("VR Battlegrounds/Avatars/Черновики/Опорные точки контакта", false, 0)]
        public static void Open() => GetWindow<HandContactProbeWindow>("Опорные точки кисти");

        /// <summary>Изолированное подготовленное окно для проверки: не заменяет состояние уже открытых окон.</summary>
        public static HandContactProbeWindow OpenPrepared(UxrAvatar avatar, UxrHandSide side,
            UxrHandPoseAsset referencePose = null)
        {
            if (!avatar) throw new ArgumentNullException(nameof(avatar));
            var window = CreateInstance<HandContactProbeWindow>();
            try
            {
                window.titleContent = new GUIContent("Опорные точки кисти");
                window._avatar = avatar; window._side = side; window._referencePose = referencePose;
                window.Capture();
                window.Show(); window.Repaint();
                return window;
            }
            catch
            {
                DestroyImmediate(window);
                throw;
            }
        }

        void OnEnable()
        {
            minSize = new Vector2(570, 650);
            if (!_avatar)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GripTargetProfile.MefCyborgHands().RigPrefabPath);
                if (prefab) _avatar = prefab.GetComponent<UxrAvatar>();
            }
        }

        void OnDisable()
        {
            _preview?.Cleanup(); _preview = null;
            if (_mesh) DestroyImmediate(_mesh);
            if (_material) DestroyImmediate(_material);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Опорные точки на ладонной коже фаланг", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Без предмета: выбор и просмотр точек одной кисти. С предметом: касание только этих точек в пределах ±2 мм.", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            _avatar = (UxrAvatar)EditorGUILayout.ObjectField("Prefab аватара", _avatar, typeof(UxrAvatar), false);
            _side = (UxrHandSide)EditorGUILayout.EnumPopup("Исходная кисть", _side);
            _referencePose = (UxrHandPoseAsset)EditorGUILayout.ObjectField("Эталонная поза (необязательно)", _referencePose, typeof(UxrHandPoseAsset), false);
            if (EditorGUI.EndChangeCheck()) InputsChanged();
            if (_primaryCapture != null && !_stale && CaptureSignature() != _captureSignature) InputsChanged();
            if (!_referencePose) EditorGUILayout.LabelField("Поза: SDK DefaultHandPose prefab (Fixed/None).", EditorStyles.miniLabel);
            if (_stale) EditorGUILayout.HelpBox("Входы изменились. Прежние точки сохранены для просмотра; перезахват явно заменит их. Проверка и экспорт приостановлены.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(!_avatar || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating))
                if (GUILayout.Button(_primarySet == null ? "Захватить кисть и автоматически выбрать опорные точки" : "Перезахватить: заменить исходные точки и сбросить зеркало", GUILayout.Height(30))) Run(Capture);
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
            if (_primarySet == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_stale))
                    if (GUILayout.Button((_mirrorSet == null ? "Отзеркалить в " : "Пересоздать зеркало в ") + OtherSide(_primaryCapture.Side))) Run(Mirror);
                using (new EditorGUI.DisabledScope(_mirrorSet == null))
                {
                    EditorGUI.BeginChangeCheck();
                    bool show = GUILayout.Toggle(_showMirror, "Показать зеркало", "Button");
                    if (EditorGUI.EndChangeCheck()) { _showMirror = show; _meshDirty = true; ClearMeasurement(); }
                }
            }
            var capture = CurrentCapture;
            var set = CurrentSet;
            if (capture == null || set == null) return;
            int selected = ValidProbes(capture, set).Count();
            EditorGUILayout.LabelField("Кисть " + capture.Side + ": " + selected + "/" + set.ExpectedCount + " опорных точек; " +
                (set.Valid ? "набор валиден" : "есть пропуски / ошибки") + "; Inferred=" + set.Inferred, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Метод: " + set.Method + "; binding: " + Short(capture.BindingFingerprint), EditorStyles.wordWrappedMiniLabel);
            if (set.Inferred) EditorGUILayout.HelpBox("Это выведенное зеркало (Inferred), требующее визуальной проверки. Неоднозначные соответствия остаются ошибками.", MessageType.Warning);
            var rect = GUILayoutUtility.GetRect(150, Mathf.Clamp(position.height * .40f, 240, 560), GUILayout.ExpandWidth(true));
            Run(() => DrawPreview(rect, capture, set));
            EditorGUILayout.LabelField("ПКМ — вращение; колесо — масштаб. Яркие грани содержат опорные точки; номера соответствуют списку. Метки поверх изображения — проекция, не проверка видимости.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(_stale))
            {
                if (GUILayout.Button(!_showMirror && _mirrorSet != null ? "Загрузить исходный JSON (сбросит зеркало)" : "Загрузить JSON для показанной кисти")) Run(Load);
                if (GUILayout.Button("Экспорт JSON + PNG + отчёты обеих кистей")) Run(Export);
            }
            DrawMeasurement();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawErrorsAndPoints(capture, set);
            EditorGUILayout.EndScrollView();
        }

        void Run(Action action)
        {
            try { action(); }
            catch (ExitGUIException) { throw; }
            catch (Exception error) { _message = error.Message; }
        }

        void InputsChanged()
        {
            _stale = _primarySet != null;
            ClearMeasurement();
        }
        void ClearMeasurement() { _measurement = null; _measurementSignature = null; }

        static string AssetSignature(UnityEngine.Object asset)
        {
            if (!asset) return "none";
            string path = AssetDatabase.GetAssetPath(asset);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id);
            return guid + ":" + id + ":" + AssetDatabase.GetAssetDependencyHash(path);
        }
        string CaptureSignature() => AssetSignature(_avatar) + "|" + AssetSignature(_referencePose) + "|" + _side;
        string MeasurementSignature() => CaptureSignature() + "|" + AssetSignature(_weapon) + "|" +
            AssetSignature(_measurementPose) + "|" + _grabPoint + "|" + _grabbableIndexPath + "|" + _allowDefaultGrip + "|" + _showMirror;
        static UxrHandSide OtherSide(UxrHandSide side) => side == UxrHandSide.Right ? UxrHandSide.Left : UxrHandSide.Right;

        void Capture()
        {
            // До успешного захвата и автовыбора прежнее рабочее состояние не меняется.
            var captured = HandContactProbeCapture.Capture(_avatar, _side, _referencePose);
            var probes = captured.CreateProbes();
            _primaryCapture = captured; _primarySet = probes;
            _mirrorCapture = null; _mirrorSet = null; _showMirror = false;
            _captureSignature = CaptureSignature(); _stale = false; _meshDirty = true;
            ClearMeasurement();
            _message = "Кисть захвачена; опорные точки выбраны автоматически. Проверьте каждую метку и явные пропуски.";
        }

        void Mirror()
        {
            var target = HandContactProbeCapture.Capture(_avatar, OtherSide(_primaryCapture.Side), _referencePose);
            var probes = HandContactProbeCapture.Mirror(_primaryCapture, target, _primarySet);
            _mirrorCapture = target; _mirrorSet = probes; _showMirror = true; _meshDirty = true;
            ClearMeasurement();
            _message = "Зеркало подготовлено отдельно. Inferred и ошибки соответствия показаны рядом с результатом.";
        }

        void Load()
        {
            string initialDirectory = Path.GetFullPath(Directory.Exists(Reports) ? Reports : "tasks/hand-rig-quality/reports");
            string path = EditorUtility.OpenFilePanel("Опорные точки " + CurrentCapture.Side, initialDirectory, "json");
            if (string.IsNullOrEmpty(path)) return;
            var loaded = CurrentCapture.LoadProbes(path);
            if (_showMirror) _mirrorSet = loaded;
            else { _primarySet = loaded; _mirrorSet = null; _mirrorCapture = null; }
            _meshDirty = true; ClearMeasurement();
            _message = "JSON загружен для показанной кисти; mesh binding проверен. Inferred=" + loaded.Inferred;
        }

        void Export()
        {
            Directory.CreateDirectory(Reports);
            string initialDirectory = Path.GetFullPath(Reports);
            string selected = EditorUtility.OpenFolderPanel("Каталог отчётов внутри tasks/hand-rig-quality/reports", initialDirectory, "");
            if (string.IsNullOrEmpty(selected)) return;
            string root = Path.GetFullPath("tasks/hand-rig-quality/reports").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(selected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Выберите каталог внутри tasks/hand-rig-quality/reports/.");
            string directory = Path.Combine(full, "phalanx-probes-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            var reports = new List<string>();
            reports.Add(_primaryCapture.Export(Path.Combine(directory, _primaryCapture.Side.ToString()), _primarySet));
            if (_mirrorCapture != null && _mirrorSet != null)
                reports.Add(_mirrorCapture.Export(Path.Combine(directory, _mirrorCapture.Side.ToString()), _mirrorSet));
            _message = "Экспортированы фактические наборы и preview-отчёты: " + string.Join("; ", reports);
        }

        void DrawMeasurement()
        {
            _measureOpen = EditorGUILayout.Foldout(_measureOpen, "Дополнительно: проверить касание предмета (±2 мм)", true);
            if (!_measureOpen) return;
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            _weapon = (GameObject)EditorGUILayout.ObjectField("Prefab предмета", _weapon, typeof(GameObject), false);
            _measurementPose = (UxrHandPoseAsset)EditorGUILayout.ObjectField("Поза хвата (override)", _measurementPose, typeof(UxrHandPoseAsset), false);
            _grabPoint = EditorGUILayout.IntField("GrabPoint", _grabPoint);
            _grabbableIndexPath = EditorGUILayout.TextField("Путь Grabbable (индексы)", _grabbableIndexPath);
            _allowDefaultGrip = EditorGUILayout.Toggle("Разрешить default grip", _allowDefaultGrip);
            if (EditorGUI.EndChangeCheck()) ClearMeasurement();
            if (_measurement != null && MeasurementSignature() != _measurementSignature) ClearMeasurement();
            using (new EditorGUI.DisabledScope(_stale || !_weapon || CurrentSet == null || !CurrentSet.Valid || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Измерить расстояние только опорных точек")) Run(Measure);
            if (_measurement != null)
            {
                EditorGUILayout.LabelField("Измерено " + _measurement.MeasuredCount + "/" + _measurement.ExpectedCount +
                    "; пропущено " + _measurement.MissingCount + "; валидно: " + _measurement.Valid +
                    "; все в пределах ±2 мм: " + (_measurement.Valid ? _measurement.AllWithinBand.ToString() : "unknown"), EditorStyles.wordWrappedLabel);
                if (_measurement.Errors != null)
                    foreach (string error in _measurement.Errors) EditorGUILayout.HelpBox(error, MessageType.Warning);
                if (_measurement.Points != null)
                    foreach (var point in _measurement.Points)
                        EditorGUILayout.LabelField(point.SegmentId + ": " + Number(point.DistanceMm) + " мм; signed " + Number(point.SignedDistanceMm) +
                            "; band " + (point.WithinBand.HasValue ? point.WithinBand.Value.ToString() : "unknown") +
                            (string.IsNullOrEmpty(point.Error) ? "" : "; " + point.Error), EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.LabelField("Без предмета основной режим работает полностью. Это статическое измерение; знак и пропуски не подменяются нулями.", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
        }

        void Measure()
        {
            var report = HandContactProbeCapture.Measure(new HandPoseFitRequest
            {
                AvatarPrefab = _avatar, ObjectPrefab = _weapon, PoseOverride = _measurementPose,
                Side = CurrentCapture.Side, GrabPoint = _grabPoint, GrabbableIndexPath = _grabbableIndexPath,
                AllowDefaultGrip = _allowDefaultGrip,
                OutputDirectory = Path.Combine(Reports, "measure-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"))
            }, CurrentSet);
            _measurement = report; _measurementSignature = MeasurementSignature();
        }

        void EnsurePreview(HandContactProbeCapture capture, HandPhalanxProbeSet set)
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
            var selected = new HashSet<(string, int, int)>(ValidProbes(capture, set).Select(Key));
            var vertices = new List<Vector3>(); var colors = new List<Color>();
            foreach (var triangle in capture.Hand)
            {
                Color color = selected.Contains(Key(triangle)) ? new Color(1f, .85f, .12f) : new Color(.43f, .48f, .55f);
                vertices.Add(triangle.A); vertices.Add(triangle.B); vertices.Add(triangle.C);
                colors.Add(color); colors.Add(color); colors.Add(color);
            }
            _mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            _mesh.SetVertices(vertices); _mesh.SetColors(colors);
            _mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0);
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds(); _meshDirty = false;
        }

        void DrawPreview(Rect rect, HandContactProbeCapture capture, HandPhalanxProbeSet set)
        {
            EnsurePreview(capture, set);
            var camera = _preview.camera;
            Quaternion rotation = PalmarView(capture) * Quaternion.Euler(_orbit.y, _orbit.x, 0);
            camera.transform.SetPositionAndRotation(capture.HandBounds.center - rotation * Vector3.forward * 2, rotation);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(.005f, capture.HandBounds.extents.magnitude * 1.15f * _zoom);
            camera.aspect = rect.width / rect.height; camera.nearClipPlane = .001f; camera.farClipPlane = 10;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .075f, .095f);
            camera.cullingMask = -1;
            Event e = Event.current;
            if (e.type == EventType.Repaint)
            {
                _preview.BeginPreview(rect, GUIStyle.none);
                _preview.DrawMesh(_mesh, Matrix4x4.identity, _material, 0); _preview.Render(true);
                GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false);
                int number = 0;
                foreach (var probe in set.Probes ?? new List<HandPhalanxProbe>())
                {
                    number++;
                    if (!TryPosition(capture, probe, out Vector3 materialPosition)) continue;
                    Vector3 viewport = camera.WorldToViewportPoint(materialPosition);
                    if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) continue;
                    Vector2 position = new Vector2(rect.x + viewport.x * rect.width, rect.y + (1 - viewport.y) * rect.height);
                    EditorGUI.DrawRect(new Rect(position.x - 5, position.y - 5, 10, 10), new Color(1, .2f, .12f));
                    var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } };
                    GUI.Label(new Rect(position.x + 7, position.y - 10, 38, 23), number.ToString(), style);
                }
            }
            if (!rect.Contains(e.mousePosition)) return;
            if (e.type == EventType.MouseDrag && e.button == 1)
            { _orbit += e.delta * .6f; e.Use(); Repaint(); }
            else if (e.type == EventType.ScrollWheel)
            { _zoom = Mathf.Clamp(_zoom * Mathf.Exp(e.delta.y * .06f), .15f, 5); e.Use(); Repaint(); }
        }

        static void DrawErrorsAndPoints(HandContactProbeCapture capture, HandPhalanxProbeSet set)
        {
            if (set.Errors != null)
                foreach (string error in set.Errors) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (set.Probes == null) return;
            for (int i = 0; i < set.Probes.Count; i++)
            {
                var probe = set.Probes[i];
                if (probe == null) { EditorGUILayout.HelpBox((i + 1) + ": отсутствует запись точки", MessageType.Warning); continue; }
                bool usable = Usable(capture, probe);
                EditorGUILayout.LabelField((i + 1) + ". " + probe.SegmentId + " — " + (usable ? "выбрана" : "MISSING / ошибка") +
                    "; " + probe.SelectionMode + "; смещение выбора " + Number(probe.SelectionDistanceMm) + " мм" +
                    (string.IsNullOrEmpty(probe.Error) ? "" : "; " + probe.Error), EditorStyles.wordWrappedLabel);
            }
        }

        static IEnumerable<HandPhalanxProbe> ValidProbes(HandContactProbeCapture capture, HandPhalanxProbeSet set) =>
            set.Probes == null ? Enumerable.Empty<HandPhalanxProbe>() : set.Probes.Where(probe => Usable(capture, probe));
        static bool Usable(HandContactProbeCapture capture, HandPhalanxProbe probe) => TryPosition(capture, probe, out _);
        static bool TryPosition(HandContactProbeCapture capture, HandPhalanxProbe probe, out Vector3 position)
        {
            position = Vector3.zero;
            if (probe == null || !string.IsNullOrEmpty(probe.Error)) return false;
            var bary = probe.Barycentric;
            if (float.IsNaN(bary.sqrMagnitude) || float.IsInfinity(bary.sqrMagnitude) || bary.x < -1e-5f || bary.y < -1e-5f || bary.z < -1e-5f ||
                Mathf.Abs(bary.x + bary.y + bary.z - 1) > 1e-4f) return false;
            var matches = capture.Hand.Where(t => Key(t).Equals(Key(probe))).Take(2).ToArray();
            if (matches.Length != 1) return false;
            var triangle = matches[0];
            position = triangle.A * bary.x + triangle.B * bary.y + triangle.C * bary.z;
            return true;
        }
        static Quaternion PalmarView(HandContactProbeCapture capture)
        {
            var middle = capture.Segments?.FirstOrDefault(segment => segment.Id == "middle/proximal");
            if (middle == null) return Quaternion.identity;
            Vector3 palmar = middle.PalmarDirection.normalized;
            Vector3 up = Vector3.ProjectOnPlane(middle.End - middle.Start, palmar).normalized;
            return palmar.sqrMagnitude > .5f && up.sqrMagnitude > .5f ? Quaternion.LookRotation(-palmar, up) : Quaternion.identity;
        }
        static string Number(double? value) => value.HasValue ? value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "unknown";
        static string Short(string text) => string.IsNullOrEmpty(text) ? "unknown" : text.Substring(0, Math.Min(18, text.Length)) + "…";
        static (string, int, int) Key(FitTriangle triangle) => (triangle.Source, triangle.SourceSubMesh, triangle.SourceTriangle);
        static (string, int, int) Key(HandPhalanxProbe probe) => (probe.Source, probe.SourceSubMesh, probe.SourceTriangle);
    }
}
