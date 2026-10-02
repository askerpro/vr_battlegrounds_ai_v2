using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>Исходный прямоугольный блок ограды; редактор раскладывает его вокруг объёмов ниш.</summary>
    public sealed class SpawnZoneBoundaryBlock : MonoBehaviour
    {
        [SerializeField] private bool _sourceCaptured;
        [SerializeField] private bool _rendererEnabled;
        [SerializeField] private Collider[] _sourceColliders;
        [SerializeField] private bool[] _colliderEnabled;
        public bool SourceCaptured => _sourceCaptured;

        public void CaptureSourceState()
        {
            var colliders = GetComponents<Collider>();
            var enabled = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++) enabled[i] = colliders[i].enabled;
            CaptureSourceState(GetComponent<Renderer>().enabled, enabled);
        }

        /// <summary>Состояние исходного блока; сохраняется до первой разметки, чтобы удаление ниши восстановило ограду.</summary>
        public void CaptureSourceState(bool rendererEnabled, bool[] colliderEnabled)
        {
            _sourceColliders = GetComponents<Collider>();
            if (colliderEnabled.Length != _sourceColliders.Length) throw new System.ArgumentException("Число исходных коллайдеров не совпадает.");
            _rendererEnabled = rendererEnabled;
            _colliderEnabled = (bool[])colliderEnabled.Clone();
            _sourceCaptured = true;
        }

        public void RestoreSourceState()
        {
            if (!_sourceCaptured) return;
            GetComponent<Renderer>().enabled = _rendererEnabled;
            for (int i = 0; i < _sourceColliders.Length; i++)
                if (_sourceColliders[i] != null) _sourceColliders[i].enabled = _colliderEnabled[i];
        }
    }
}
