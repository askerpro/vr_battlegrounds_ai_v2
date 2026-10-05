using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Editor.Manipulation.HandPoses;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Один владелец SDK-панели, SceneView overlay и временных ресурсов. Только Editor.</summary>
    [InitializeOnLoad]
    public static class HandPoseSdkDiagnostics
    {
        static UxrHandPoseEditorWindow _window;
        static UxrAvatar _avatar;
        static UxrHandPoseAsset _pose;
        static GameObject _root;
        static UxrGrabbableObject _grabbable;
        static int _grabPoint;
        static readonly HandPoseSdkPreviewSession Session = new HandPoseSdkPreviewSession();
        static bool _forceCapture, _reportCurrent;
        static int _queryGeneration;
        static string _reportInputKey;
        static float _blend, _voxelMm = 1f;
        static Vector3 _fieldSize = new Vector3(.1f,.08f,.12f), _fieldCenter;
        static bool _regionInitialized, _fieldUnavailable;
        static UxrHandSide _side = UxrHandSide.Right;
        static bool _xray, _analyze, _settings;
        static double _nextCapture, _nextQueryRepaint;
        static string _error = "", _hash, _objectHash, _reportHash;
        static string _queryHash;
        static HandPoseGpuField _queryField;
        static Mesh _hand, _weapon, _other;
        static Material _handMaterial, _objectMaterial;
        static HandPoseGpuField _field;
        static HandPoseGpuField.Contact[] _contacts = Array.Empty<HandPoseGpuField.Contact>();
        static Vector3[] _samplePoints;
        static readonly HashSet<Renderer> Originals = new HashSet<Renderer>();
        static readonly HandPoseCameraScope CameraScope = new HandPoseCameraScope();
        static HandPoseFitReport _report;
        public static bool Enabled => _xray;
        public static int OwnedHiddenCount => CameraScope.OwnedCount;
        public static string CurrentFingerprint => _hash;

        static HandPoseSdkDiagnostics()
        {
            UxrHandPoseEditorWindow.DiagnosticsGUI += OnGUI;
            UxrHandPoseEditorWindow.DiagnosticsContextUpdated += OnContext;
            UxrHandPoseEditorWindow.DiagnosticsClosed += OnClosed;
            EditorApplication.update += Tick;
            SceneView.duringSceneGui += Draw;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.playModeStateChanged += state => { if (state != PlayModeStateChange.EnteredEditMode) Cleanup(); };
            EditorApplication.quitting += Cleanup;
        }

        static void OnContext(UxrHandPoseEditorWindow window, UxrAvatar avatar, UxrHandPoseAsset pose, float blend)
        {
            if (_window != window || _avatar != avatar) { Cleanup(); _hash = null; }
            if (_pose != pose) _hash = null;
            _window = window; _avatar = avatar; _pose = pose; _blend = blend;
        }
        static void OnClosed(UxrHandPoseEditorWindow window) { if (_window == window) { Cleanup(); _window = null; _avatar = null; _pose = null; } }

        static void OnGUI(UxrHandPoseEditorWindow window)
        {
            if (_window && _window != window) return;
            _window = window;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                EditorGUILayout.LabelField("Диагностика текущего хвата", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var root=(GameObject)EditorGUILayout.ObjectField("Предмет в сцене",_root,typeof(GameObject),true);
                var choices=root?root.GetComponentsInChildren<UxrGrabbableObject>(true):Array.Empty<UxrGrabbableObject>();
                var grabbable=choices.Contains(_grabbable)?_grabbable:choices.FirstOrDefault();
                if(choices.Length>0) {
                    int index=Array.IndexOf(choices,grabbable);
                    index=EditorGUILayout.Popup("Grabbable",index,choices.Select(g=>string.IsNullOrEmpty(HandPoseFitSnapshot.HumanPath(g.transform,root.transform))?root.name:HandPoseFitSnapshot.HumanPath(g.transform,root.transform)).ToArray());
                    grabbable=choices[index];
                }
                int point=grabbable==_grabbable?_grabPoint:0;
                if(grabbable && grabbable.GrabPointCount>0) {
                    point=Mathf.Clamp(point,0,grabbable.GrabPointCount-1);
                    point=EditorGUILayout.Popup("Точка хвата",point,Enumerable.Range(0,grabbable.GrabPointCount).Select(i=>UxrGrabPointIndex.GetIndexDisplayName(grabbable,i)).ToArray());
                }
                var side=(UxrHandSide)EditorGUILayout.EnumPopup("Кисть",_side);
                if(EditorGUI.EndChangeCheck() || root!=_root || grabbable!=_grabbable || point!=_grabPoint) {
                    bool wasXray=_xray;Cleanup();_root=root;_grabbable=grabbable;_grabPoint=point;_side=side;_xray=wasXray;_nextCapture=0;_forceCapture=true;
                }
                bool valid=_avatar && _pose && _root && _grabbable && !EditorUtility.IsPersistent(_root) && !EditorApplication.isPlayingOrWillChangePlaymode;
                if(_pose)EditorGUILayout.LabelField("Поза окна: "+_pose.name);
                if(valid) {
                    var assigned=_grabbable.GetGrabPoint(_grabPoint).GetGripPoseInfo(_avatar)?.HandPose;
                    if(assigned && assigned.name!=_pose.name)EditorGUILayout.HelpBox("Для этой точки назначена "+assigned.name+". Анализируется текущая поза окна как кандидат.",MessageType.Info);
                }
                using (new EditorGUI.DisabledScope(!valid)) {
                    bool xray = GUILayout.Toggle(_xray, "Рентген · контакт 0–2 мм", "Button");
                    if (xray != _xray) { if (!xray) Cleanup(); else { _xray = true; _nextCapture = 0; _error = ""; } }
                    if (GUILayout.Button("Анализировать и записать отчёт")) { _analyze = true; _nextCapture=0; _error = ""; }
                    if(GUILayout.Button("Обновить геометрию")){_forceCapture=true;_nextCapture=0;}
                }
                _settings = EditorGUILayout.Foldout(_settings, "Параметры и легенда");
                if (_settings) {
                    EditorGUI.BeginChangeCheck();
                    _voxelMm = EditorGUILayout.Slider("Шаг GPU-поля, мм", _voxelMm, .25f, 5);
                    _fieldSize = EditorGUILayout.Vector3Field("Размер области, м", _fieldSize);
                    _fieldCenter = EditorGUILayout.Vector3Field("Центр в осях предмета, м", _fieldCenter);
                    if (EditorGUI.EndChangeCheck()) { ClearField(); _objectHash = null; _fieldUnavailable = false; _regionInitialized = true; }
                    if (GUILayout.Button("Центрировать поле на кисти")) { _regionInitialized = false; ClearField(); _fieldUnavailable = false; _objectHash = null; }
                    EditorGUILayout.HelpBox("Зелёный: уверенный внешний контакт. Красный: установленное вложение. Жёлтый: пограничная дистанция/неизвестный знак. Серый: дальше контакта. Поле приближённое; это не балл качества.", MessageType.Info);
                }
                if (!valid) EditorGUILayout.HelpBox("Нужны аватар/поза SDK, сценовый Grabbable и Edit Mode.", MessageType.Info);
                if (_field != null && !_field.Ready) EditorGUILayout.LabelField("GPU-поле: " + _field.Progress.ToString("P0"));
                if (_field != null && _field.Ready) EditorGUILayout.LabelField("GPU: ±" + (_field.ErrorMetres * 1000).ToString("F2") + " мм; шаг " + (_field.VoxelMetres*1000).ToString("F2") + " мм; знак " + (_field.CanSign ? "доступен" : "unknown"));
                int outside = _contacts.Count(c => c.TargetState.w == -2);
                if (outside > 0) EditorGUILayout.LabelField("Вне GPU-области: " + outside + " точек (не оценены)");
                if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Warning);
                if (_report != null) {
                    EditorGUILayout.LabelField(_reportCurrent ? "Результат текущей геометрии" : "Результат устарел — повторите анализ", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Контактных точек: " + _report.ContactCandidateCount + "; пересечённых граней: " + _report.IntersectingHandTriangles);
                    EditorGUILayout.LabelField("Достоверность: " + _report.Status);
                    if (!string.IsNullOrEmpty(_report.Error)) EditorGUILayout.HelpBox(_report.Error, MessageType.Warning);
                    foreach (var zone in _report.Zones.Take(6)) EditorGUILayout.LabelField(zone.Zone + ": близость " + zone.NearSurfaceFraction.ToString("P1") + ", P50 " + zone.DistanceP50Mm.ToString("F1") + " мм");
                    if (!string.IsNullOrEmpty(_report.JsonPath)) {
                        if (GUILayout.Button("Открыть сохранённый отчёт")) Application.OpenURL(new Uri(Path.Combine(_report.OutputDirectory, "index.html")).AbsoluteUri);
                        EditorGUILayout.SelectableLabel(_report.JsonPath, GUILayout.Height(32));
                    }
                }
            }
        }

        static HandPoseFitRequest Settings(bool full) => new HandPoseFitRequest {
            Side = _side, GrabPoint=_grabPoint, GrabbableIndexPath=_grabbable?HandPoseFitSnapshot.IndexPath(_grabbable.transform,_root.transform):"", IncludeOtherHand = false, RenderImages = full, SampleCount = 6000,
            StateName = "editor-visible", CheckHandSelfIntersections = full,
            OutputDirectory = "UserReports/HandPoseFit", FrameSizeMeters = .35f
        };

        static void Tick()
        {
            if(!_window || !_avatar || !_pose || !_root || !_grabbable) {
                if(Session.Snapshot!=null || _xray || _analyze || _forceCapture || _field!=null || _hand || _weapon || _reportCurrent || CameraScope.OwnedCount>0)Cleanup();
                return;
            }
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating){Cleanup();return;}
            if(!_xray && !_analyze && !_forceCapture && _report==null)return;
            try {
                if(EditorApplication.timeSinceStartup>=_nextCapture) {
                    _nextCapture=EditorApplication.timeSinceStartup+.1;
                    if(!_xray && !_analyze && !_forceCapture) {
                        bool current=HandPoseSdkPreviewSession.BuildInputKey(_avatar,_pose,_blend,_root,Settings(false))==_reportInputKey;
                        if(current!=_reportCurrent){_reportCurrent=current;_window.Repaint();}
                        return;
                    }
                    bool changed=Session.Ensure(_avatar,_pose,_blend,_root,Settings(false),_forceCapture);_forceCapture=false;
                    var snapshot=Session.Snapshot;_reportCurrent=_reportInputKey==Session.InputKey;
                    if(changed || (_xray && !_hand)) {
                        _hash=Session.Metadata.EditorSnapshotHash;ResetQuery();
                        if(_xray) {
                            if(_hand)Object.DestroyImmediate(_hand);_hand=Mesh(snapshot.Hand);
                            if(!_weapon || _objectHash!=Session.ObjectHash){if(_weapon)Object.DestroyImmediate(_weapon);_weapon=Mesh(snapshot.ObjectSurface);}
                            _samplePoints=HandPoseFitGeometry.Sample(snapshot.ContactHand.ToArray(),300,42).Select(p=>p.Position).ToArray();
                            CollectOriginals();
                        }
                        _window.Repaint();if(_xray)SceneView.RepaintAll();
                    }
                    if(_xray) {
                        if(!_regionInitialized){_fieldCenter=snapshot.HandBounds.center;_fieldSize=Vector3.Max(_fieldSize,snapshot.HandBounds.size+Vector3.one*.012f);_regionInitialized=true;}
                        if(_objectHash!=Session.ObjectHash || (_field==null && !_fieldUnavailable)) {
                            ClearField();_objectHash=Session.ObjectHash;_fieldUnavailable=false;
                            try {
                                float voxel=_voxelMm*.001f;
                                while(voxel<.005f && TooLarge(_fieldSize,voxel))voxel=Mathf.Min(.005f,voxel*1.1f);
                                _field=new HandPoseGpuField(snapshot.ContactObject.ToArray(),voxel,_objectHash,new Bounds(_fieldCenter,_fieldSize));_error="";
                            } catch(Exception error){_error=error.Message+" Рентген доступен без GPU-оценки.";_fieldUnavailable=true;}
                        }
                    }
                    if(_analyze) {
                        _analyze=false;
                        try {
                            EditorUtility.DisplayProgressBar("Анализ хвата","Измерения и изображения из текущего preview",.2f);
                            var field=_field!=null && _field.Ready && _field.Fingerprint==Session.ObjectHash?_field:null;
                            AcceptReport(HandPoseEditorCapture.ExportCaptured(snapshot,Session.CopyMetadata(),_root,Settings(true),SceneView.lastActiveSceneView?.camera,field));
                        } finally {EditorUtility.ClearProgressBar();}
                        _window.Repaint();
                    }
                }
                if(_xray && _field!=null) {
                    if(!_field.Ready){_field.BuildStep();SceneView.RepaintAll();_window.Repaint();}
                    if(_field.Ready && _samplePoints!=null && (_queryHash!=_hash || _queryField!=_field)) {
                        string capturedHash=_hash;var capturedField=_field;int generation=_queryGeneration;
                        bool submitted=_field.Query(_samplePoints,contacts=>{
                            if(IsCurrentQuery(generation,capturedHash,capturedField)) {
                                _contacts=contacts;_queryHash=capturedHash;_queryField=capturedField;SceneView.RepaintAll();_window?.Repaint();
                            }
                        });
                        if(!SystemInfo.supportsAsyncGPUReadback){_error="GPU-подсветка доступна, но чтение контактных меток не поддерживается. CPU-отчёт доступен.";_queryHash=_hash;_queryField=_field;_window.Repaint();}
                        if(submitted || (_field.QueryPending && EditorApplication.timeSinceStartup>=_nextQueryRepaint)){_nextQueryRepaint=EditorApplication.timeSinceStartup+.05;SceneView.RepaintAll();}
                    }
                }
            } catch(Exception error){_error=error.Message;Cleanup();_window?.Repaint();}
        }

        static bool TooLarge(Vector3 size,float voxel)
        {
            long x=Mathf.CeilToInt(size.x/voxel)+1,y=Mathf.CeilToInt(size.y/voxel)+1,z=Mathf.CeilToInt(size.z/voxel)+1;
            return x>512 || y>512 || z>512 || x*y*z>8000000;
        }

        static bool IsCurrentQuery(int generation,string hash,HandPoseGpuField field)
        {
            return _xray && generation==_queryGeneration && hash==_hash && field==_field;
        }

        static void ResetQuery()
        {
            _queryGeneration++;_contacts=Array.Empty<HandPoseGpuField.Contact>();_queryHash=null;_queryField=null;
        }

        static void CollectOriginals()
        {
            var renderers = _root.GetComponentsInChildren<Renderer>().ToList();
            // SDK proxy расположен в корне сцены; исключаем визуальное наложение surrogate grip mesh.
            foreach(var proxy in Resources.FindObjectsOfTypeAll<UxrGrabbableObjectPreviewMeshProxy>())
                if(proxy && proxy.PreviewMeshComponent && proxy.PreviewMeshComponent.transform.IsChildOf(_root.transform)) {
                    var renderer=proxy.GetComponent<Renderer>();if(renderer)renderers.Add(renderer);
                }
            Originals.Clear();
            foreach(var renderer in renderers)if(renderer&&renderer.enabled&&renderer.gameObject.activeInHierarchy&&!(renderer is ParticleSystemRenderer))Originals.Add(renderer);
        }
        static void BeginCamera(ScriptableRenderContext context,Camera camera)
        {
            if(!_xray)return;
            CameraScope.Begin(camera,camera.cameraType==CameraType.SceneView,Originals);
        }
        static void EndCamera(ScriptableRenderContext context,Camera camera) { CameraScope.End(camera); }

        static void Draw(SceneView view)
        {
            if (!_xray || !_root || !_hand || !_weapon || Event.current.type != EventType.Repaint) return;
            if (!_handMaterial) {
                var shader = Shader.Find("Hidden/VRBattlegrounds/HandPoseLiveXray"); if (!shader) return;
                _handMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _objectMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            var matrix = Matrix4x4.TRS(_root.transform.position, _root.transform.rotation, Vector3.one);
            _objectMaterial.SetFloat("_Alpha", .15f); _objectMaterial.SetColor("_Tint", new Color(.7f,.75f,.85f));
            _objectMaterial.SetPass(0); Graphics.DrawMeshNow(_weapon, matrix);
            if (_other) { _objectMaterial.SetFloat("_Alpha", .5f); _objectMaterial.SetColor("_Tint", new Color(.65f,.3f,.9f)); _objectMaterial.SetPass(0); Graphics.DrawMeshNow(_other, matrix); }
            _handMaterial.SetFloat("_Alpha", .9f); _handMaterial.SetColor("_Tint", new Color(.85f,.86f,.9f));
            _handMaterial.SetFloat("_UseField", _field != null && _field.Ready ? 1 : 0);
            if (_field != null && _field.Ready) {
                _handMaterial.SetTexture("_DistanceField", _field.Texture);
                _handMaterial.SetVector("_FieldMinimum", _field.Minimum); _handMaterial.SetVector("_FieldSize", _field.Size);
                _handMaterial.SetVector("_FieldDimensions", _field.Dimensions);
                _handMaterial.SetFloat("_FieldVoxel", _field.VoxelMetres); _handMaterial.SetFloat("_FieldError", _field.ErrorMetres);
            }
            _handMaterial.SetPass(0); Graphics.DrawMeshNow(_hand, matrix);
            var oldColor = Handles.color; var oldDepth = Handles.zTest; var oldMatrix = Handles.matrix; Handles.zTest = CompareFunction.Always;
            try {
                Handles.matrix = matrix; Handles.color = new Color(1,.65f,.15f);
                Handles.DrawWireCube(_fieldCenter,_fieldSize); Handles.matrix = oldMatrix;
                foreach (var contact in _contacts) {
                    if (contact.TargetState.w < 0) continue;
                    Vector3 p = matrix.MultiplyPoint3x4(contact.PointDistance), target = matrix.MultiplyPoint3x4(contact.TargetState);
                    Handles.color = contact.TargetState.w > 1.5f ? Color.red : contact.TargetState.w > .5f ? Color.green : Color.yellow;
                    Handles.DrawLine(p, target); Handles.DotHandleCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * .012f, EventType.Repaint);
                    Handles.color = Color.cyan; Handles.DotHandleCap(0, target, Quaternion.identity, HandleUtility.GetHandleSize(target) * .007f, EventType.Repaint);
                }
            } finally { Handles.color = oldColor; Handles.zTest = oldDepth; Handles.matrix = oldMatrix; }
        }

        static Mesh Mesh(IEnumerable<FitTriangle> triangles)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            foreach (var t in triangles) { vertices.Add(t.A); vertices.Add(t.B); vertices.Add(t.C); normals.Add(t.Normal); normals.Add(t.Normal); normals.Add(t.Normal); }
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0); mesh.RecalculateBounds(); return mesh;
        }
        static void AcceptReport(HandPoseFitReport report)
        {
            _report=report;_reportHash=report.EditorSnapshotHash;_reportInputKey=Session.InputKey;_reportCurrent=report.EditorSnapshotHash==Session.Metadata?.EditorSnapshotHash;
        }
        static void ClearField()
        {
            _field?.Dispose(); _field = null;
            // Метки/запрос принадлежат конкретному полю, а не только позе.
            ResetQuery();
        }
        static void DestroyMeshes() { if (_hand) Object.DestroyImmediate(_hand); if (_weapon) Object.DestroyImmediate(_weapon); if (_other) Object.DestroyImmediate(_other); _hand = _weapon = _other = null; }
        public static void Cleanup()
        {
            _xray = false; _analyze = false; _contacts = Array.Empty<HandPoseGpuField.Contact>(); _samplePoints = null; _queryHash = null; _queryField = null;
            ClearField(); Session.Dispose(); _hash=null; _reportCurrent=false; _forceCapture=false; _objectHash = null; _fieldUnavailable = false; _regionInitialized = false;
            DestroyMeshes();
            if (_handMaterial) Object.DestroyImmediate(_handMaterial); if (_objectMaterial) Object.DestroyImmediate(_objectMaterial);
            _handMaterial = _objectMaterial = null;
            CameraScope.Dispose(); Originals.Clear();
            SceneView.RepaintAll();
        }
    }
}
