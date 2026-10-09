using System;
using NUnit.Framework;
using UnityEditor;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Tests.Managers
{
    /// <summary>
    /// Маршрут старта сервера (контракт map-startup-route rev 2): проверка цели по настоящему каталогу и жизнь запроса
    /// без сети. Захват режима серией и сам старт в цель проверяются в Play на worker (нужен живой сервер).
    /// </summary>
    public class ServerStartupRouteTests
    {
        private const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";
        private const string Lobby = "Assets/Scenes/Lobby.unity";
        private static readonly Func<string, bool> AllLoadable = _ => true;

        private MapRuntimeCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath);
            Assert.IsNotNull(_catalog, "Нет каталога запуска карт: " + CatalogPath);
            Reset();
        }

        [TearDown]
        public void TearDown() => Reset();

        private static void Reset()
        {
            // Снять незавершённый запрос теста: Cancel снимает только Requested, остальное — сбросом процесса.
            typeof(ServerStartupRoute).GetMethod("ResetProcessState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
        }

        private bool Request(string scene, string mode, string owner, out StartupRouteHandle handle, out string error) =>
            ServerStartupRoute.TryRequestCore(scene, mode, owner, _catalog, AllLoadable, false, out handle, out error);

        [Test]
        public void Карта_каталога_с_совместимым_режимом_принимается()
        {
            Assert.IsTrue(ServerStartupRoute.Validate(_catalog, AllLoadable, "TestMap1", "elimination",
                out string scene, out string error), error);
            Assert.AreEqual("TestMap1", scene);
        }

        [Test]
        public void Сцена_вне_каталога_отказ_SceneNotLoadable()
        {
            Assert.IsFalse(ServerStartupRoute.Validate(_catalog, AllLoadable, "NoSuchMap", "", out _, out string error));
            StringAssert.StartsWith(ServerStartupRoute.SceneNotLoadable, error);
        }

        [Test]
        public void Сцена_вне_списка_сборки_отказ_SceneNotLoadable()
        {
            Assert.IsFalse(ServerStartupRoute.Validate(_catalog, _ => false, "TestMap1", "", out _, out string error));
            StringAssert.StartsWith(ServerStartupRoute.SceneNotLoadable, error);
        }

        [Test]
        public void Без_каталога_отказ_SceneNotLoadable()
        {
            Assert.IsFalse(ServerStartupRoute.Validate(null, AllLoadable, "TestMap1", "", out _, out string error));
            StringAssert.StartsWith(ServerStartupRoute.SceneNotLoadable, error);
        }

        [Test]
        public void Разминка_и_режим_вне_карты_отказ_ModeIncompatible()
        {
            Assert.IsFalse(ServerStartupRoute.Validate(_catalog, AllLoadable, "TestMap1", "warmup", out _, out string warmup));
            StringAssert.StartsWith(ServerStartupRoute.ModeIncompatible, warmup);
            Assert.IsFalse(ServerStartupRoute.Validate(_catalog, AllLoadable, "Lobby", "elimination", out _, out string lobby));
            StringAssert.StartsWith(ServerStartupRoute.ModeIncompatible, lobby);
            Assert.IsFalse(ServerStartupRoute.Validate(_catalog, AllLoadable, "TestMap1", "nosuchmode", out _, out string unknown));
            StringAssert.StartsWith(ServerStartupRoute.ModeIncompatible, unknown);
        }

        [Test]
        public void Второй_живой_запрос_отказ_Busy()
        {
            Assert.IsTrue(Request("TestMap1", "", "a", out _, out string error), error);
            Assert.IsFalse(Request("TestMap2", "", "b", out StartupRouteHandle second, out string busy));
            Assert.IsNull(second);
            StringAssert.StartsWith(ServerStartupRoute.Busy, busy);
        }

        [Test]
        public void Запрос_при_запущенном_сервере_отказ_NetworkActive()
        {
            Assert.IsFalse(ServerStartupRoute.TryRequestCore("TestMap1", "", "a", _catalog, AllLoadable, true, out _, out string error));
            StringAssert.StartsWith(ServerStartupRoute.NetworkActive, error);
            Assert.AreEqual(StartupRouteState.None, ServerStartupRoute.State);
        }

        [Test]
        public void Отмена_до_старта_и_новый_запрос()
        {
            Assert.IsTrue(Request("TestMap1", "elimination", "a", out StartupRouteHandle handle, out string error), error);
            handle.Dispose();
            Assert.AreEqual(StartupRouteState.None, ServerStartupRoute.State);
            Assert.IsTrue(Request("TestMap2", "", "b", out _, out error), error);
            Assert.AreEqual("TestMap2", ServerStartupRoute.TargetScene);
        }

        [Test]
        public void Неудачный_старт_снимается_Dispose_владельца()
        {
            // Сеть не поднялась: ServerChangeScene не вызван, StopServer при неактивном сервере OnStopServer не зовёт.
            Assert.IsTrue(Request("TestMap1", "", "a", out StartupRouteHandle handle, out string error), error);
            ServerStartupRoute.OnServerStopped();
            Assert.AreEqual(StartupRouteState.Requested, ServerStartupRoute.State, "Очистка не опирается на остановку");
            handle.Dispose();
            Assert.AreEqual(StartupRouteState.None, ServerStartupRoute.State);
            Assert.IsTrue(Request("TestMap1", "", "b", out _, out error), error);
        }

        [Test]
        public void Чужой_или_старый_handle_не_снимает_новый_запрос()
        {
            Assert.IsTrue(Request("TestMap1", "", "a", out StartupRouteHandle old, out string error), error);
            old.Dispose();
            Assert.IsTrue(Request("TestMap2", "", "b", out StartupRouteHandle current, out error), error);
            old.Dispose();
            Assert.IsFalse(ServerStartupRoute.Cancel(current.RequestId, "a"), "Чужой владелец");
            Assert.AreEqual(current.RequestId, ServerStartupRoute.RequestId);
        }

        [Test]
        public void Первая_смена_сцены_сервера_грузит_цель_вместо_onlineScene()
        {
            Assert.IsTrue(Request("TestMap1", "", "a", out _, out string error), error);
            Assert.AreEqual("Assets/Scenes/Maps/TestMap2.unity",
                ServerStartupRoute.ResolveServerScene("Assets/Scenes/Maps/TestMap2.unity", Lobby), "Не onlineScene — без изменений");
            Assert.AreEqual("TestMap1", ServerStartupRoute.ResolveServerScene(Lobby, Lobby));
            Assert.AreEqual(StartupRouteState.None, ServerStartupRoute.State, "Без режима запрос исполнен сразу");
            Assert.AreEqual(Lobby, ServerStartupRoute.ResolveServerScene(Lobby, Lobby), "Следующая смена — штатная");
        }

        [Test]
        public void Хост_режим_сохраняется_до_OnStartServer_и_Dispose_его_не_снимает()
        {
            // У хоста Series ещё нет при выборе сцены: запрос ждёт захвата режима.
            Assert.IsTrue(Request("TestMap1", "elimination", "a", out StartupRouteHandle handle, out string error), error);
            Assert.AreEqual("TestMap1", ServerStartupRoute.ResolveServerScene(Lobby, Lobby));
            Assert.AreEqual(StartupRouteState.SceneConsumed, ServerStartupRoute.State);
            handle.Dispose();
            Assert.AreEqual(StartupRouteState.SceneConsumed, ServerStartupRoute.State, "После выбора сцены Dispose не снимает");
            Assert.AreEqual("elimination", ServerStartupRoute.ModeId);
            Assert.IsFalse(Request("TestMap2", "", "b", out _, out string busy));
            StringAssert.StartsWith(ServerStartupRoute.Busy, busy);
        }

        [Test]
        public void Остановка_сервера_до_захвата_режима_снимает_запрос()
        {
            Assert.IsTrue(Request("TestMap1", "elimination", "a", out _, out string error), error);
            ServerStartupRoute.ResolveServerScene(Lobby, Lobby);
            ServerStartupRoute.OnServerStopped();
            Assert.AreEqual(StartupRouteState.None, ServerStartupRoute.State);
        }
    }
}
