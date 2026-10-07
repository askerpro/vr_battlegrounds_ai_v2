using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Допуск gameplay запуска карты (<see cref="MapRunAdmission"/>) и Closing.
    ///
    /// <para>
    /// Что доказывает. Пока запуск карты не открыл server Ready, предметы карты не выдаются и аватары ждут в очереди
    /// (по одному запросу на сессию); Closing (принята загрузка следующей карты) закрывает допуск, отменяет scope и не
    /// даёт закоммитить режим. Класс «выдача предмета мимо допуска» закрыт структурно: в игровом коде экземпляр
    /// сетевого предмета создаёт только <see cref="MapRunAdmission.CreateMapItem"/> — проверка и создание одна операция.
    /// </para>
    /// </summary>
    public class MapRunAdmissionTests : MirrorTestHarness
    {
        private GameObject _mapRoot;
        private MapBootstrap _bootstrap;

        [TearDown]
        public void DropBootstrap()
        {
            if (_bootstrap != null) InvokeLifecycleMethod(_bootstrap, "OnDestroy");
            if (_mapRoot != null) UnityEngine.Object.DestroyImmediate(_mapRoot);
            MapRunAdmission.DiscardAvatars("тест");
        }

        /// <summary>Сцена с запуском карты, который ещё не готов (MapBootstrap ждёт предусловий).</summary>
        private void LoadUnreadyMap()
        {
            _mapRoot = new GameObject("MapRoot");
            _mapRoot.AddComponent<MapRoot>();
            _bootstrap = _mapRoot.AddComponent<MapBootstrap>();
            InvokeLifecycleMethod(_bootstrap, "Awake");
            Assert.AreSame(_bootstrap, MapBootstrap.ForScene(_mapRoot.scene), "Контроль: сцена под запуском карты.");
        }

        [Test]
        public void До_server_Ready_предметы_карты_не_создаются()
        {
            SilenceMirrorNoise();
            LoadUnreadyMap();
            var prefab = new GameObject("ItemPrefab");
            try
            {
                Assert.IsFalse(MapRunAdmission.CanActivateMapGameplay(_mapRoot.scene));
                Assert.IsNull(MapRunAdmission.CreateMapItem(_mapRoot.scene, prefab),
                    "Предмет карты создан до server Ready.");
                Assert.IsNull(MapRunAdmission.CreateActiveMapItem(prefab), "Карман/бот выдали предмет до server Ready.");
            }
            finally { UnityEngine.Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void До_server_Ready_аватар_ждёт_допуска_одним_запросом_на_сессию()
        {
            SilenceMirrorNoise();
            LoadUnreadyMap();
            PlayerSession session = CreateNetworkComponent<PlayerSession>("Session");
            int created = 0;

            Assert.IsFalse(MapRunAdmission.TryAdmitAvatar(session, () => created++));
            Assert.IsFalse(MapRunAdmission.TryAdmitAvatar(session, () => created++));
            Assert.AreEqual(1, MapRunAdmission.PendingAvatarCount, "Сессия поставила в очередь два аватара.");
            Assert.AreEqual(0, created, "Аватар создан до server Ready.");
        }

        [Test]
        public void Принятая_загрузка_снимает_отложенные_аватары_старой_карты()
        {
            SilenceMirrorNoise();
            LoadUnreadyMap();
            PlayerSession session = CreateNetworkComponent<PlayerSession>("Session");
            MapRunAdmission.TryAdmitAvatar(session, () => { });

            InvokePrivateMethod(_bootstrap, "HandleMapLoadStarted", "NextMap");

            Assert.AreEqual(0, MapRunAdmission.PendingAvatarCount, "Аватар старой карты создался бы после выгрузки.");
            InvokePrivateMethod(_bootstrap, "Update");
            Assert.AreEqual(MapBootstrapStage.WaitingPrerequisites, _bootstrap.Stage,
                "Закрытая загрузкой карта всё же начала запуск.");
        }

        [Test]
        public void Сцена_без_запуска_карты_открыта()
        {
            SilenceMirrorNoise();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Assert.IsNull(MapBootstrap.ForScene(scene), "Контроль: в открытой сцене нет MapBootstrap.");
            Assert.IsTrue(MapRunAdmission.CanActivateMapGameplay(scene), "Стенд без MapRoot закрыт допуском карты.");
        }

        [Test]
        public void Closing_закрывает_допуск_и_коммиты_режима()
        {
            SilenceMirrorNoise();
            MapReferee referee = CreateNetworkComponent<MapReferee>("MapReferee");
            InvokeLifecycleMethod(referee, "Awake");
            TestMapRun run = PublishMapState(referee, MapState.Warmup, "elimination");
            Assert.IsTrue(run.IsServerReady, "Контроль: запуск открыл server Ready.");

            Assert.IsTrue(run.Authority.Close(run.Scope, run.Authority.Current.Revision));

            Assert.AreEqual(MapBootstrapStatus.Closing, run.Authority.Current.Status);
            Assert.IsTrue(run.Scope.Cancellation.IsCancellationRequested, "Closing не отменил scope — писатели продолжат.");
            Assert.IsFalse(run.Scope.IsDisposed, "Closing — не teardown: службы живут до выгрузки сцены.");
            Assert.IsFalse(run.IsServerReady, "После Closing допуск открыт.");
            Assert.IsFalse(run.Authority.CommitMode(run.Scope, run.Authority.Current.Revision, MapState.Live, "elimination", referee.netId),
                "После Closing закоммичен новый режим.");
        }

        /// <summary>
        /// Класс ошибки «выдача предмета мимо допуска»: каждый <c>NetworkUxrIdentity.CreateInstance</c> в игровом коде,
        /// кроме сетевого слоя, самого допуска и Debug-стендов, создал бы предмет и после Closing, и до server Ready.
        /// Единственная точка — <see cref="MapRunAdmission.CreateMapItem"/>.
        /// </summary>
        [Test]
        public void Сетевые_предметы_игры_создаются_только_через_допуск_карты()
        {
            string scripts = Path.Combine(Application.dataPath, "Scripts");
            var allowed = new[]
            {
                Path.Combine("Network", "NetworkUxrIdentity.cs"),
                Path.Combine("Maps", "Runtime", "MapRunAdmission.cs"),
            };
            var call = new Regex(@"NetworkUxrIdentity\s*\.\s*CreateInstance\s*\(");

            string[] offenders = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
                .Select(path => path.Substring(scripts.Length + 1))
                .Where(relative => !relative.StartsWith("Debug" + Path.DirectorySeparatorChar))
                .Where(relative => !allowed.Contains(relative))
                .Where(relative => call.IsMatch(File.ReadAllText(Path.Combine(scripts, relative))))
                .ToArray();

            CollectionAssert.IsEmpty(offenders,
                "Предмет создаётся мимо MapRunAdmission.CreateMapItem/CreateActiveMapItem: " + string.Join(", ", offenders));
        }
    }
}
