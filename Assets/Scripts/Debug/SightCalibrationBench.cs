using System;
using Mirror;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.DebugTools
{
    /// <summary>Изолированный экранный стенд: геометрия, finite-focus и одиночный SDK projectile.</summary>
    public sealed class SightCalibrationBench : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public string Id, Label, Status;
            public GameObject Root, Donor;
            public UxrProjectileSource Source;
            public Renderer Lens;
            public Material OriginalLens, PrototypeLens;
            public WeaponOpticView Optic;
            public bool HasReferences;
            public Vector3 Rear, Front, Centre;
            public float Size;
        }

        [Serializable]
        public sealed class Observation
        {
            public string weaponId;
            public float distance;
            public Vector3 sourcePosition, sourceForward, impactPoint;
            public Vector2 impactOffsetMillimetres;
            public bool sdkImpact;
        }

        [SerializeField] private Entry[] _entries;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _target, _lineMark, _impactMark;
        [SerializeField] private UxrWeaponManager _manager;
        [SerializeField] private WeaponSightCalibrationSettings _settings;
        public Entry[] Entries => _entries;
        public Observation LastImpact { get; private set; }
        public int ImpactCount { get; private set; }
        public int FailureCount { get; private set; }
        public string SelectedId => Current.Id;
        public bool AwaitingImpact => _pending;
        public bool PrototypeValid => Current.Optic != null && Current.Optic.RefreshView();
        public Camera ViewCamera => _camera;
        public float TargetDistance => _distance;

        private int _selected, _view;
        private bool _candidate = true, _pending;
        private float _distance = 15, _eyeX, _eyeY, _eyeBack = .14f, _zoom = 1, _deadline;
        private string _message = "Одиночный SDK projectile; хват, магазин, отдача и сеть здесь не проверяются.";
        private Vector2 _scroll;
        private Vector3 _shotPosition, _shotForward;
        private WeaponSightCalibrationSettings _runtimeSettings;
        private Entry Current => _entries[_selected];

        private void Awake()
        {
            // Стенд не подключается к сетевой игровой сцене и не меняет её singleton.
            if (NetworkClient.active || NetworkServer.active || _manager == null || _entries == null || _entries.Length == 0)
            { enabled = false; GameLog.WeaponSystem.Error("[SightBench] Требуется отдельная offline-сцена.", this); return; }
#if UNITY_EDITOR
            _runtimeSettings = Instantiate(_settings);
            foreach (var entry in _entries)
                if (entry.Optic != null)
                {
                    var serialized = new UnityEditor.SerializedObject(entry.Optic);
                    serialized.FindProperty("_settings").objectReferenceValue = _runtimeSettings;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
#endif
            _manager.NonActorImpacted += HandleImpact;
            Select(0);
        }

        private void OnDestroy()
        {
            if (_manager != null) _manager.NonActorImpacted -= HandleImpact;
            if (_runtimeSettings != null) Destroy(_runtimeSettings);
        }

        private void Update()
        {
            // UxrManager обновляет SDK projectiles; второй ручной tick здесь запрещён.
            if (_pending && Time.unscaledTime > _deadline)
            {
                _pending = false; FailureCount++;
                _message = "SDK impact не получен за 3 секунды; результат не принят.";
                GameLog.WeaponSystem.Error("[SightBench] " + _message, this);
            }
        }

        public bool Select(int index)
        {
            if (_pending || index < 0 || index >= _entries.Length) return false;
            foreach (var entry in _entries) entry.Root.SetActive(false);
            _selected = index; Current.Root.SetActive(true);
            _eyeX = _eyeY = 0; _zoom = 1; LastImpact = null;
            _impactMark.gameObject.SetActive(false);
            SetCandidate(_candidate); SetDistance(_distance); SetView(0);
            return true;
        }

        public void SetCandidate(bool candidate)
        {
            _candidate = candidate;
            if (Current.Donor != null) Current.Donor.SetActive(candidate);
            if (Current.Optic != null)
            {
                Current.Optic.enabled = false;
                var materials = Current.Lens.sharedMaterials;
                materials[1] = candidate ? Current.PrototypeLens : Current.OriginalLens;
                Current.Lens.sharedMaterials = materials;
                Current.Lens.SetPropertyBlock(null, 1);
                Current.Optic.enabled = candidate;
                if (candidate) Current.Optic.RefreshView();
            }
            UpdateTarget(); UpdateCamera();
        }

        public bool SetDistance(float distance)
        {
            if (_pending || !WeaponSightCalibrationSettings.IsValidDistance(distance)) return false;
            _distance = distance; LastImpact = null; _impactMark.gameObject.SetActive(false);
            UpdateTarget(); UpdateCamera(); return true;
        }

        public bool SetRequestedZero(float distance)
        {
            if (_runtimeSettings == null || !WeaponSightCalibrationSettings.IsValidDistance(distance)) return false;
            JsonUtility.FromJsonOverwrite("{\"_defaultZeroDistance\":" + distance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}", _runtimeSettings);
            if (Current.Optic != null) Current.Optic.RefreshView();
            return true;
        }

        public void SetEye(float x, float y)
        { _eyeX = Mathf.Clamp(x, -.015f, .015f); _eyeY = Mathf.Clamp(y, -.012f, .012f); UpdateCamera(); }
        public void SetView(int view) { _view = Mathf.Clamp(view, 0, 2); UpdateCamera(); }

        private void UpdateCamera()
        {
            Entry entry = Current;
#if UNITY_EDITOR
            _camera.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(gameObject.scene);
#endif
            _camera.rect = new Rect(.30f, 0, .70f, 1);
            _camera.orthographic = _view != 2;
            _camera.nearClipPlane = .001f; _camera.farClipPlane = 50;
            if (_view == 2)
            {
                Vector3 rear = entry.Root.transform.TransformPoint(entry.Rear);
                Vector3 front = entry.Root.transform.TransformPoint(entry.Front);
                // Направление фиксируется по R/F. Смещение глаза не перенаводит оружие или камеру на мишень.
                Vector3 direction = entry.HasReferences ? (front - rear).normalized : entry.Source.ShotTypes[0].ShotSource.forward;
                Vector3 eye = rear - direction * _eyeBack + Vector3.right * _eyeX + Vector3.up * _eyeY;
                _camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(direction, Vector3.up));
                _camera.fieldOfView = 35 / _zoom;
            }
            else
            {
                Vector3 centre = entry.Root.transform.TransformPoint(entry.Centre);
                Vector3 direction = _view == 0 ? Vector3.right : Vector3.up;
                Vector3 up = _view == 0 ? Vector3.up : Vector3.forward;
                _camera.transform.SetPositionAndRotation(centre + direction * 2, Quaternion.LookRotation(-direction, up));
                _camera.orthographicSize = Mathf.Max(.04f, entry.Size * .65f / _zoom);
            }
        }

        private void UpdateTarget()
        {
            Transform muzzle = Current.Source.ShotTypes[0].ShotSource;
            Vector3 targetPlane = muzzle.position + muzzle.forward * _distance;
            // Ближняя грань коллайдера толщиной 1 см находится точно на выбранной дистанции.
            _target.SetPositionAndRotation(targetPlane + muzzle.forward * .005f, muzzle.rotation);
            bool hasLine = Current.HasReferences && (Current.Donor == null || _candidate);
            _lineMark.gameObject.SetActive(hasLine);
            if (!hasLine) return;
            Vector3 rear = Current.Root.transform.TransformPoint(Current.Rear);
            Vector3 front = Current.Root.transform.TransformPoint(Current.Front);
            Vector3 line = (front - rear).normalized;
            float denominator = Vector3.Dot(line, muzzle.forward);
            if (denominator <= 1e-6f) { _lineMark.gameObject.SetActive(false); return; }
            float travel = Vector3.Dot(targetPlane - rear, muzzle.forward) / denominator;
            _lineMark.position = rear + line * travel - muzzle.forward * .012f;
        }

        public bool FireSdkShot()
        {
            if (_pending || NetworkClient.active || NetworkServer.active || !StateEventAuthority.IsAuthorOfItem(Current.Source)) return false;
            Transform source = Current.Source.ShotTypes[0].ShotSource;
            _shotPosition = source.position; _shotForward = source.forward;
            _pending = true; _deadline = Time.unscaledTime + 3;
            _message = "Ожидание события SDK impact…";
            try { Current.Source.Shoot(0); return true; }
            catch (Exception exception)
            {
                _pending = false; FailureCount++;
                _message = exception.Message; GameLog.WeaponSystem.Error("[SightBench] " + exception, this); return false;
            }
        }

        private void HandleImpact(object sender, UxrNonDamagingImpactEventArgs args)
        {
            if (!_pending || args.ProjectileSource != Current.Source) return;
            _pending = false;
            bool hitTarget = args.RaycastHit.collider.transform == _target;
            Vector3 delta = args.RaycastHit.point - _target.position;
            LastImpact = new Observation { weaponId = Current.Id, distance = _distance, sourcePosition = _shotPosition,
                sourceForward = _shotForward, impactPoint = args.RaycastHit.point, sdkImpact = hitTarget,
                impactOffsetMillimetres = new Vector2(Vector3.Dot(delta, _target.right), Vector3.Dot(delta, _target.up)) * 1000 };
            if (!hitTarget) { FailureCount++; _message = "SDK impact пришёл вне мишени."; return; }
            ImpactCount++; _impactMark.position = args.RaycastHit.point - _target.forward * .016f;
            _impactMark.gameObject.SetActive(true);
            _message = "SDK impact: " + LastImpact.impactOffsetMillimetres.ToString("F2") + " мм от оси ствола.";
        }

        private void OnGUI()
        {
            if (_entries == null || _entries.Length == 0) return;
            GUILayout.BeginArea(new Rect(8, 8, Mathf.Max(220, Screen.width * .30f - 16), Screen.height - 16), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("ПРОВЕРКА ПРИЦЕЛОВ · default 15 м");
            GUILayout.Label(Current.Label + "\nID: " + Current.Id);
            GUILayout.Label(Current.Status);
            GUI.enabled = !_pending;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("←")) Select((_selected + _entries.Length - 1) % _entries.Length);
            if (GUILayout.Button("→")) Select((_selected + 1) % _entries.Length);
            GUILayout.EndHorizontal();
            bool candidate = GUILayout.Toggle(_candidate, "Подготовленный вариант (снять: исходник)");
            if (candidate != _candidate) SetCandidate(candidate);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Сбоку")) SetView(0);
            if (GUILayout.Button("Сверху")) SetView(1);
            if (GUILayout.Button("Прицел")) SetView(2);
            GUILayout.EndHorizontal();
            GUILayout.Label("Увеличение камеры осмотра (не зум прицела)");
            float zoom = GUILayout.HorizontalSlider(_zoom, .5f, 4);
            if (zoom != _zoom) { _zoom = zoom; UpdateCamera(); }
            GUILayout.Label("Дистанция мишени от ShotSource: " + _distance + " м");
            GUILayout.BeginHorizontal();
            foreach (float distance in new[] { 1f, 3f, 5f, 10f, 15f, 20f })
                if (GUILayout.Button(distance.ToString())) SetDistance(distance);
            GUILayout.EndHorizontal();
            GUILayout.Label("Смещение глаза X/Y: " + (_eyeX * 1000).ToString("F1") + "/" + (_eyeY * 1000).ToString("F1") + " мм");
            float x = GUILayout.HorizontalSlider(_eyeX, -.015f, .015f), y = GUILayout.HorizontalSlider(_eyeY, -.012f, .012f);
            if (x != _eyeX || y != _eyeY) SetEye(x, y);
            if (GUILayout.Button("Глаз по центру")) SetEye(0, 0);
            if (Current.Optic != null)
            {
                GUILayout.Label("Прототип сохранён на 15 м; запрос Z:");
                GUILayout.BeginHorizontal();
                foreach (float distance in new[] { 10f, 15f, 20f })
                    if (GUILayout.Button(distance.ToString())) SetRequestedZero(distance);
                GUILayout.EndHorizontal();
                GUILayout.Label("Сетка кандидата: " + (PrototypeValid ? "действительна" : "выключена / устарела"));
            }
            if (GUILayout.Button("Одиночный SDK projectile")) FireSdkShot();
            GUI.enabled = true;
            GUILayout.Label(_message);
            GUILayout.Label("Мишень: центр — ось ствола; сетка 5 см, центральные деления 1 см.\nЖёлтый крест — расчёт R/F. Красная отметка — фактический SDK impact.");
            GUILayout.Label("Ни один кандидат пока не принят как калибровка. Взгляд монокамерой не проверяет Quest stereo.");
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}
