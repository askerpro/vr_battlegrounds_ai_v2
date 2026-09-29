using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Выбор префаба аватара при пересоздании (<see cref="AvatarManager.ChangeAvatar"/>: смена карты,
    /// скина, команды) — то же правило, что при первичном спавне (<see cref="TeamAvatarStrategy"/>).
    /// Раньше пересоздание молча выходило для игрока без команды (команда 0): после смены карты
    /// у него не появлялся аватар, хотя при подключении он получал запасной.
    /// </summary>
    public class AvatarPrefabChoiceTests : MirrorTestHarness
    {
        private const string StrategyPath = "Assets/Data/Player/Avatars/TeamAvatarStrategy.asset";

        private AvatarSpawnStrategy _strategy;
        private PlayerSession _session;

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();
            _strategy = AssetDatabase.LoadAssetAtPath<AvatarSpawnStrategy>(StrategyPath);
            Assert.IsNotNull(_strategy, $"Нет {StrategyPath}.");

            _session = CreateNetworkComponent<PlayerSession>("Session");
            SpawnOnServer(_session);
        }

        [Test]
        public void Без_команды_после_смены_карты_тот_же_аватар_что_при_подключении()
        {
            _session.TeamIndex = 0;
            _session.AvatarIndex = 0;
            GameObject onConnect = _strategy.GetPrefab(_session, null);
            Assert.IsNotNull(onConnect, "Контроль: при подключении игрок без команды получает запасной аватар.");

            GameObject onRespawn = AvatarManager.PrefabFor(0, 0, _session, _strategy, null);
            Assert.AreSame(onConnect, onRespawn, "После смены карты игрок без команды остался без аватара.");
        }

        [Test]
        public void Команда_и_скин_дают_скин_команды()
        {
            TeamData team = TeamRegistry.Instance.GetByIndex(1);
            Assert.IsNotNull(team);
            Assert.AreSame(team.GetAvatarPrefab(0), AvatarManager.PrefabFor(1, 0, _session, _strategy, null));
        }

        /// <summary>
        /// Тело выбывшего — призрак (T-35): то же правило стратегии, что выбирает скин. Команда
        /// и скин при этом не теряются — возрождённый получит свой скин обратно.
        /// </summary>
        [Test]
        public void Выбывшему_тело_призрака_возрождённому_свой_скин()
        {
            var strategy = (TeamAvatarStrategy)_strategy;
            Assert.IsNotNull(strategy.GhostPrefab, "В стратегии не задан призрак.");

            TeamData team = TeamRegistry.Instance.GetByIndex(1);
            SetPrivateField(_session, "_isEliminated", true);
            Assert.AreSame(strategy.GhostPrefab, AvatarManager.PrefabFor(1, 0, _session, _strategy, null),
                "Выбывший получил тело живого.");

            SetPrivateField(_session, "_isEliminated", false);
            Assert.AreSame(team.GetAvatarPrefab(0), AvatarManager.PrefabFor(1, 0, _session, _strategy, null),
                "Возрождённый не получил свой скин обратно.");
        }

        [Test]
        public void Неизвестная_команда_отказ_и_сессия_не_меняется()
        {
            _session.TeamIndex = 0;
            // Отказ не молчаливый: раньше пересоздание выходило без единой строки в логе.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("команды 99 нет в TeamRegistry"));
            Assert.IsNull(AvatarManager.PrefabFor(99, 0, _session, _strategy, null),
                "Запрос несуществующей команды не должен подменяться запасным аватаром.");
            Assert.AreEqual(0, _session.TeamIndex, "Выбор префаба не меняет сессию.");
        }
    }
}
