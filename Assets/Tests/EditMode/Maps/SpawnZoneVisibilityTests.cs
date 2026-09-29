using NUnit.Framework;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Границы рисует только режим, которому они нужны (матч; разминка — свободная арена).
    /// Игрок видит только свою зону спавна. Выбывший — ярко и сквозь стены (по цвету пола находит
    /// базу), живой — до боя (где стоять на закупке и отсчёте), в бою граница пропадает.
    /// </summary>
    public class SpawnZoneVisibilityTests
    {
        [TestCase(true, true, RoundPhase.Equipment, true, false, TestName = "Живой_на_закупке_видит_свою")]
        [TestCase(true, true, RoundPhase.Countdown, true, false, TestName = "Живой_на_отсчёте_видит_свою")]
        [TestCase(true, true, RoundPhase.Combat, false, false, TestName = "Живой_в_бою_не_видит_свою")]
        [TestCase(true, false, RoundPhase.Equipment, false, false, TestName = "Живой_не_видит_чужую")]
        [TestCase(false, true, RoundPhase.Combat, true, true, TestName = "Выбывший_в_бою_видит_свою_сквозь_стены")]
        [TestCase(false, true, RoundPhase.Setup, true, true, TestName = "Выбывший_в_подготовке_видит_свою_сквозь_стены")]
        [TestCase(false, false, RoundPhase.Combat, false, false, TestName = "Выбывший_не_видит_чужую")]
        [TestCase(false, false, RoundPhase.Setup, false, false, TestName = "Выбывший_в_подготовке_не_видит_чужую")]
        public void Игрок_видит_только_свою_зону(bool alive, bool ownTeam, RoundPhase state, bool visible, bool xray)
        {
            SpawnZoneVisibility.Decide(true, true, alive, ownTeam, state, out bool isVisible, out bool isXray);

            Assert.AreEqual(visible, isVisible, "Видимость зоны.");
            Assert.AreEqual(xray, isXray, "Сквозь стены.");
        }

        [Test]
        public void Без_своего_аватара_зон_не_видно()
        {
            SpawnZoneVisibility.Decide(true, false, false, false, RoundPhase.Equipment, out bool visible, out _);
            Assert.IsFalse(visible, "Своей зоны нет — видна чужая.");
        }

        [TestCase(true, TestName = "Разминка_живому_границ_не_рисует")]
        [TestCase(false, TestName = "Разминка_выбывшему_границ_не_рисует")]
        public void Режим_без_границ_не_рисует_даже_свою_зону(bool alive)
        {
            SpawnZoneVisibility.Decide(false, true, alive, true, RoundPhase.Equipment, out bool visible, out _);
            Assert.IsFalse(visible, "Режим без границ (разминка, лобби), а зона нарисована.");
        }

        [Test]
        public void Границы_рисует_матч_а_не_разминка()
        {
            var go = new UnityEngine.GameObject("Modes");
            go.AddComponent<Mirror.NetworkIdentity>(); // режим — NetworkBehaviour
            try
            {
                Assert.IsTrue(go.AddComponent<EliminationMode>().ShowsSpawnZones, "Матч не рисует зоны — игрок не видит, где стоять.");
                Assert.IsFalse(go.AddComponent<WarmupMode>().ShowsSpawnZones, "Разминка рисует зоны — лобби перестаёт быть свободной ареной.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
