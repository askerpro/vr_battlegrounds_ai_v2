using System.Reflection;
using NUnit.Framework;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Пауза UltimateXR, пока редактор не в фокусе (патч 33): выключает IK и стадии обновления — тесты и стенды в
    /// фоне видят аватар в T-позе. Процессный override <see cref="UxrManager.EditorFocusPauseEnabled"/>
    /// не меняет checkout-профиль или EditorPrefs; снятый во время паузы флаг обязан её снять.
    /// </summary>
    public class EditorFocusPauseTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _go;
        private System.IDisposable _focus;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("UxrManager_FocusTest");
        }

        [TearDown]
        public void TearDown()
        {
            _focus?.Dispose(); _focus = null;
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void Снятый_во_время_паузы_флаг_возвращает_обновление_и_IK()
        {
            UxrManager manager = PausedManager();

            _focus = UxrManager.BeginEditorFocusPauseOverride(false);
            Invoke(manager, "HandleEditorFocusChange");

            Assert.IsFalse(manager.IsPausedByEditorFocus, "Update снова работает: стадии и события обновления идут.");
            Assert.AreEqual(UxrPostUpdateMode.LateUpdate, manager.PostUpdateMode, "IK снова считается.");
        }

        [Test]
        public void Процессный_scope_не_меняет_EditorPrefs_и_восстанавливает_provider()
        {
            const string key = "VrBattlegrounds.PauseXrWhenEditorUnfocused";
            bool present = UnityEditor.EditorPrefs.HasKey(key);
            bool saved = UnityEditor.EditorPrefs.GetBool(key, true);
            bool effective = UxrManager.EditorFocusPauseEnabled;
            using (UxrManager.BeginEditorFocusPauseOverride(!effective))
            {
                Assert.AreEqual(!effective, UxrManager.EditorFocusPauseEnabled);
                Assert.AreEqual(present, UnityEditor.EditorPrefs.HasKey(key));
                Assert.AreEqual(saved, UnityEditor.EditorPrefs.GetBool(key, true));
                Assert.Throws<System.InvalidOperationException>(() => UxrManager.BeginEditorFocusPauseOverride(effective));
            }
            Assert.AreEqual(effective, UxrManager.EditorFocusPauseEnabled);
        }

        /// <summary>
        /// Менеджер в состоянии «на паузе без фокуса». В EditMode Awake не вызывается — компонент не становится
        /// синглтоном и не трогает сцену.
        /// </summary>
        private UxrManager PausedManager()
        {
            var manager = _go.AddComponent<UxrManager>();
            typeof(UxrManager).GetField("_postUpdateMode", Private).SetValue(manager, UxrPostUpdateMode.None);
            typeof(UxrManager).GetField("_savedPostUpdateMode", Private).SetValue(manager, UxrPostUpdateMode.LateUpdate);
            typeof(UxrManager).GetField("_shouldUpdate", Private).SetValue(manager, false);
            return manager;
        }

        private static void Invoke(UxrManager manager, string method)
        {
            typeof(UxrManager).GetMethod(method, Private).Invoke(manager, null);
        }
    }
}
