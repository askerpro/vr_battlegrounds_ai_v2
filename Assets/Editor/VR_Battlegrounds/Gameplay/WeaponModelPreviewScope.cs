using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Владеет временной моделью источника в отдельной preview scene.
    /// Родитель с единичным TRS сохраняет прежнюю мировую систему координат модели.
    /// </summary>
    internal sealed class WeaponModelPreviewScope : IDisposable
    {
        private Scene _scene;
        private GameObject _root;
        private bool _disposed;

        public GameObject Instance { get; private set; }

        public WeaponModelPreviewScope(GameObject prefab)
        {
            CheckEditor();
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            try
            {
                _scene = EditorSceneManager.NewPreviewScene();
                _root = EditorUtility.CreateGameObjectWithHideFlags("Weapon Model Preview Scope", HideFlags.HideAndDontSave);
                SceneManager.MoveGameObjectToScene(_root, _scene);
                _root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _root.transform.localScale = Vector3.one;
                // Clone сразу рождается у собственного родителя, а не в активной пользовательской сцене.
                Instance = UnityEngine.Object.Instantiate(prefab, _root.transform, false);
                Instance.hideFlags = HideFlags.HideAndDontSave;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static void CheckEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Сэмплирование исходной модели оружия допускается только в Edit Mode.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            }
            finally
            {
                Instance = null;
                _root = null;
                if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
                _scene = default;
            }
        }
    }
}
