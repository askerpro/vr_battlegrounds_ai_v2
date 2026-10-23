using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Weapons.Sights
{
    /// <summary>Владеет только параметрами сетки своего renderer. Глаз выбирается shader, выстрел не меняется.</summary>
    public sealed class WeaponOpticView : MonoBehaviour
    {
        [SerializeField] private Renderer _lensRenderer;
        [SerializeField] private int _lensMaterialIndex = 1;
        [SerializeField] private UxrProjectileSource _source;
        [SerializeField] private int _shotTypeIndex;
        [SerializeField] private WeaponInfo _weapon;
        [SerializeField] private string _sightId;
        [SerializeField] private WeaponSightCalibrationSettings _settings;
        [SerializeField] private WeaponSightCalibrationProfile _profile;
        [Tooltip("Дистанция сохранённого результата. Пока результат не применён, сетка выключена; изменение запроса требует нового принятия.")]
        [SerializeField] private float _calibratedZeroDistance;
        [Tooltip("Камера кратности (увеличивающий прицел). Нет — прицел без увеличения (коллиматор).")]
        [SerializeField] private WeaponScopeCamera _scope;

        public const string ShaderName = "VR Battlegrounds/Weapons/Finite Focus Reticle";

        private MaterialPropertyBlock _properties;
        // Проверка шейдера кэшируется по материалу: sharedMaterials и shader.name выделяли память на каждой камере каждого кадра.
        private static readonly System.Collections.Generic.List<Material> SharedMaterials = new System.Collections.Generic.List<Material>();
        private Material _checkedMaterial;
        private bool _checkedWritable;
        private static readonly int ValidId = Shader.PropertyToID("_OpticValid");
        private static readonly int ZeroId = Shader.PropertyToID("_OpticZeroWS");
        private static readonly int ForwardId = Shader.PropertyToID("_OpticForwardWS");
        private static readonly int RightId = Shader.PropertyToID("_OpticRightWS");
        private static readonly int UpId = Shader.PropertyToID("_OpticUpWS");
        private static readonly int ScopeActiveId = Shader.PropertyToID("_ScopeActive");
        private static readonly int ScopeTexId = Shader.PropertyToID("_ScopeTex");
        private static readonly int ScopeFieldId = Shader.PropertyToID("_ScopeApparentHalfField");

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            WriteInvalid();
        }

        // Камера кратности линзу не видит (она за ближней плоскостью) — для неё запись не нужна.
        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection
                || WeaponScopeCamera.IsScopeCamera(camera)) return;
            RefreshView();
        }

        /// <summary>Читает конечную позу после SDK/отдачи перед rendering, не записывает Transform.</summary>
        public bool RefreshView()
        {
            if (!CanWrite()) return false;
            bool valid = TryGetZero(out Vector3 point, out Transform muzzle, out float distance);
            _properties = _properties ?? new MaterialPropertyBlock();
            _lensRenderer.GetPropertyBlock(_properties, _lensMaterialIndex);
            _properties.SetFloat(ValidId, valid ? 1f : 0f);
            if (valid)
            {
                _properties.SetVector(ZeroId, new Vector4(point.x, point.y, point.z, distance));
                _properties.SetVector(ForwardId, muzzle.forward);
                _properties.SetVector(RightId, muzzle.right);
                _properties.SetVector(UpId, muzzle.up);
            }
            bool scope = valid && _scope != null && _scope.IsViewing;
            _properties.SetFloat(ScopeActiveId, scope ? 1f : 0f);
            if (scope)
            {
                _properties.SetTexture(ScopeTexId, _scope.Texture);
                _properties.SetFloat(ScopeFieldId, _scope.ApparentHalfField);
            }
            _lensRenderer.SetPropertyBlock(_properties, _lensMaterialIndex);
            return valid;
        }

        /// <summary>
        /// Точка сведения в мире: ShotSource + forward · принятая дистанция. false — прицел не сведён или нет ShotSource
        /// (сетка выключена). Единственное место расчёта: им же пользуется <see cref="WeaponScopeCamera"/>.
        /// </summary>
        public bool TryGetZero(out Vector3 point, out Transform muzzle, out float distance)
        {
            point = default;
            muzzle = null;
            bool valid = _settings != null && WeaponSightCalibrationSettings.IsValidDistance(_settings.DefaultZeroDistance);
            distance = valid ? _settings.DefaultZeroDistance : 0f;
            if (_profile != null)
                valid = valid && _profile.Weapon == _weapon && _profile.SightId == _sightId
                    && _profile.ShotTypeIndex == _shotTypeIndex && _profile.TryGetZeroDistance(_settings, out distance);
            valid = valid && WeaponSightCalibrationSettings.IsValidDistance(_calibratedZeroDistance)
                && distance == _calibratedZeroDistance;
            valid = valid && _source != null && _source.ShotTypes != null && _shotTypeIndex >= 0
                && _shotTypeIndex < _source.ShotTypes.Count && _source.ShotTypes[_shotTypeIndex] != null
                && _source.ShotTypes[_shotTypeIndex].ShotSource != null;
            if (!valid) return false;
            muzzle = _source.ShotTypes[_shotTypeIndex].ShotSource;
            point = muzzle.position + muzzle.forward * distance;
            return true;
        }

        /// <summary>Мировые bounds renderer линзы (корпус прицела): по ним камера кратности находит центр и окуляр.</summary>
        public bool TryGetLensBounds(out Bounds bounds)
        {
            bounds = default;
            if (!CanWrite()) return false;
            bounds = _lensRenderer.bounds;
            return true;
        }

        private bool CanWrite()
        {
            if (_lensRenderer == null || _lensMaterialIndex < 0) return false;
            _lensRenderer.GetSharedMaterials(SharedMaterials);
            Material material = _lensMaterialIndex < SharedMaterials.Count ? SharedMaterials[_lensMaterialIndex] : null;
            SharedMaterials.Clear();
            if (material != _checkedMaterial || material == null)
            {
                _checkedMaterial = material;
                _checkedWritable = material != null && material.shader != null && material.shader.name == ShaderName;
            }
            return _checkedWritable;
        }

        private void WriteInvalid()
        {
            if (!CanWrite()) return;
            _properties = _properties ?? new MaterialPropertyBlock();
            _lensRenderer.GetPropertyBlock(_properties, _lensMaterialIndex);
            _properties.SetFloat(ValidId, 0f);
            _properties.SetFloat(ScopeActiveId, 0f);
            _lensRenderer.SetPropertyBlock(_properties, _lensMaterialIndex);
        }
    }
}
