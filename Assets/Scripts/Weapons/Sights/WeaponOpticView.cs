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

        public const string ShaderName = "VR Battlegrounds/Weapons/Finite Focus Reticle";

        private MaterialPropertyBlock _properties;
        private static readonly int ValidId = Shader.PropertyToID("_OpticValid");
        private static readonly int ZeroId = Shader.PropertyToID("_OpticZeroWS");
        private static readonly int ForwardId = Shader.PropertyToID("_OpticForwardWS");
        private static readonly int RightId = Shader.PropertyToID("_OpticRightWS");
        private static readonly int UpId = Shader.PropertyToID("_OpticUpWS");

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            WriteInvalid();
        }

        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera) => RefreshView();

        /// <summary>Читает конечную позу после SDK/отдачи перед rendering, не записывает Transform.</summary>
        public bool RefreshView()
        {
            if (!CanWrite()) return false;
            float distance;
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
            _properties = _properties ?? new MaterialPropertyBlock();
            _lensRenderer.GetPropertyBlock(_properties, _lensMaterialIndex);
            _properties.SetFloat(ValidId, valid ? 1f : 0f);
            if (valid)
            {
                Transform muzzle = _source.ShotTypes[_shotTypeIndex].ShotSource;
                Vector3 point = muzzle.position + muzzle.forward * distance;
                _properties.SetVector(ZeroId, new Vector4(point.x, point.y, point.z, distance));
                _properties.SetVector(ForwardId, muzzle.forward);
                _properties.SetVector(RightId, muzzle.right);
                _properties.SetVector(UpId, muzzle.up);
            }
            _lensRenderer.SetPropertyBlock(_properties, _lensMaterialIndex);
            return valid;
        }

        private bool CanWrite() => _lensRenderer != null && _lensMaterialIndex >= 0
            && _lensMaterialIndex < _lensRenderer.sharedMaterials.Length
            && _lensRenderer.sharedMaterials[_lensMaterialIndex] != null
            && _lensRenderer.sharedMaterials[_lensMaterialIndex].shader != null
            && _lensRenderer.sharedMaterials[_lensMaterialIndex].shader.name == ShaderName;

        private void WriteInvalid()
        {
            if (!CanWrite()) return;
            _properties = _properties ?? new MaterialPropertyBlock();
            _lensRenderer.GetPropertyBlock(_properties, _lensMaterialIndex);
            _properties.SetFloat(ValidId, 0f);
            _lensRenderer.SetPropertyBlock(_properties, _lensMaterialIndex);
        }
    }
}
