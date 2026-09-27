using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Правило пополнения кармана магазинов (<see cref="MagazineRefillPlanner"/>) и то,
    /// что его зовут нужные политики: раунд — с префаба режима, лобби — со сцены.
    ///
    /// Оружие и магазины здесь — строки: магазин «rifle» встаёт в оружие «rifle».
    /// Сетевая сторона выдачи (<see cref="PlayerLoadoutManager"/>) проверяется в Play mode.
    /// </summary>
    public class MagazineRefillPlannerTests
    {
        private static bool Fits(string magazine, string weapon) => magazine == weapon;

        private static MagazineRefillPlanner.Plan Plan(string[] weapons, string[] stored, int perWeapon, int capacity)
        {
            return MagazineRefillPlanner.Compute(weapons, stored, Fits, _ => perWeapon, capacity);
        }

        [Test]
        public void Пустой_карман_получает_магазин_к_каждому_оружию()
        {
            var plan = Plan(new[] { "rifle", "pistol" }, new string[0], 1, 4);

            CollectionAssert.AreEquivalent(new[] { 0, 1 }, plan.SpawnFor);
            Assert.IsEmpty(plan.Discard);
        }

        [Test]
        public void Подходящий_магазин_в_кармане_выдачу_гасит()
        {
            var plan = Plan(new[] { "rifle" }, new[] { "rifle" }, 1, 4);

            Assert.IsEmpty(plan.SpawnFor,
                "Магазин к винтовке уже лежит — выдавать второй значит раздувать карман каждые полсекунды.");
        }

        [Test]
        public void Чужой_магазин_не_засчитывается()
        {
            var plan = Plan(new[] { "rifle" }, new[] { "pistol" }, 1, 4);

            CollectionAssert.AreEqual(new[] { 0 }, plan.SpawnFor,
                "Магазин от пистолета в винтовку не встаёт — к винтовке нужен свой.");
        }

        [Test]
        public void Один_магазин_засчитывается_одному_оружию()
        {
            // Два ствола под один магазин: одного магазина на двоих не хватает.
            var plan = MagazineRefillPlanner.Compute(
                new[] { "m16-a", "m16-b" }, new[] { "m16" },
                (m, w) => w.StartsWith(m), _ => 1, 4);

            CollectionAssert.AreEqual(new[] { 1 }, plan.SpawnFor);
        }

        [Test]
        public void Раундовая_норма_выдаётся_целиком()
        {
            var plan = Plan(new[] { "rifle" }, new string[0], 3, 4);

            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, plan.SpawnFor);
        }

        [Test]
        public void Полный_карман_освобождается_от_ненужных_старых_магазинов()
        {
            // Игрок сменил оружие: карман забит магазинами к прежнему.
            var plan = Plan(new[] { "rifle" }, new[] { "pistol", "shotgun", "pistol", "shotgun" }, 1, 4);

            CollectionAssert.AreEqual(new[] { 0 }, plan.SpawnFor,
                "Бесконечный карман не должен застревать на магазинах от брошенного оружия.");
            CollectionAssert.AreEqual(new[] { 0 }, plan.Discard, "Уходит самый старый ненужный магазин.");
        }

        [Test]
        public void Нужные_магазины_не_выкидываются_ради_места()
        {
            var plan = Plan(new[] { "rifle" }, new[] { "rifle", "rifle" }, 3, 2);

            Assert.IsEmpty(plan.Discard);
            Assert.IsEmpty(plan.SpawnFor, "Места нет, а выкидывать нечего — выдача урезается.");
        }

        [Test]
        public void Без_оружия_ничего_не_происходит()
        {
            var plan = Plan(new string[0], new[] { "rifle" }, 1, 4);

            Assert.IsEmpty(plan.SpawnFor);
            Assert.IsEmpty(plan.Discard, "Игрок положил оружие — это не повод отбирать его магазины.");
        }

        // ── Проводка политик ────────────────────────────────────────────────

        [Test]
        public void Раундовая_политика_лежит_на_префабе_режима()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameModes/EliminationMode.prefab");
            Assert.IsNotNull(prefab, "Контроль: префаб режима найден.");

            Assert.IsNotNull(prefab.GetComponent<RoundMagazineRefill>(),
                "На EliminationMode.prefab нет RoundMagazineRefill — к раунду карман магазинов останется пустым.");
        }

        [Test]
        public void Менеджер_магазинов_не_знает_о_режимах()
        {
            string source = File.ReadAllText("Assets/Scripts/Player/PlayerLoadoutManager.cs");

            foreach (string mode in new[] { "EliminationMode", "RoundState", "LobbyFreePlay" })
            {
                Assert.IsFalse(source.Contains(mode),
                    $"PlayerLoadoutManager упоминает {mode}. Когда и сколько выдавать, решают политики " +
                    "режима и сцены; менеджер только исполняет.");
            }
        }
    }
}
