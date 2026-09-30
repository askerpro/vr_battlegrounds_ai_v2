using System.Reflection;
using NUnit.Framework;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Пауза UltimateXR, пока редактор не в фокусе (патч 33): выключает IK и стадии обновления — тесты и стенды в
    /// фоне видят аватар в T-позе. Личный флаг <see cref="UxrManager.EditorFocusPauseEnabled"/> (EditorPrefs, меню
    /// <c>Tools/VR Battlegrounds/Debug/Pause XR When Editor Unfocused</c>), снятый во время паузы, обязан её снять.
    /// </summary>
    public class EditorFocusPauseTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _go;
        private bool _savedPause;

        [SetUp]
        public void SetUp()
        {
            _savedPause = UxrManager.EditorFocusPauseEnabled;
            _go = new GameObject("UxrManager_FocusTest");
        }

        [TearDown]
        public void TearDown()
        {
            // Личная настройка разработчика — вернуть как было.
            UxrManager.EditorFocusPauseEnabled = _savedPause;
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void Снятый_во_время_паузы_флаг_возвращает_обновление_и_IK()
        {
            UxrManager manager = PausedManager();

            UxrManager.EditorFocusPauseEnabled = false;
            Invoke(manager, "HandleEditorFocusChange");

            Assert.IsFalse(manager.IsPausedByEditorFocus, "Update снова работает: стадии и события обновления идут.");
            Assert.AreEqual(UxrPostUpdateMode.LateUpdate, manager.PostUpdateMode, "IK снова считается.");
        }

        [Test]
        public void Флаг_хранится_в_EditorPrefs_а_не_в_ассете()
        {
            UxrManager.EditorFocusPauseEnabled = false;
            Assert.IsFalse(UnityEditor.EditorPrefs.GetBool("VrBattlegrounds.PauseXrWhenEditorUnfocused", true));

            UxrManager.EditorFocusPauseEnabled = true;
            Assert.IsTrue(UnityEditor.EditorPrefs.GetBool("VrBattlegrounds.PauseXrWhenEditorUnfocused", false));
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
