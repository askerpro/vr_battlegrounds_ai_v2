using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Общая игровая станция соответствует исходному игровому пресету. WeaponRegistry — каталог,
    /// ассортимент карты выбирает MapData.arsenalPreset; Demo/Lobby используют отдельную широкую станцию.
    ///
    /// <para>
    /// Класс ошибки: набор стены правился по месту — слот базовой стены, переопределение в префабе карты, — и
    /// разные стены показывали разное. Авторский набор берётся из пресета; ручные переопределения
    /// слотов вне заготовок арсенала запрещены. Требование равенства всему каталогу отменено с пресетами.
    /// </para>
    /// </summary>
    public class ArsenalWallCoversRegistryTests
    {
        private const string Wall = "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab";
        private const string GameplayPreset = "Assets/Data/Weapons/CurrentGameplayArsenal.asset";
        private static readonly string[] MapRoots = { "Assets/Prefabs", "Assets/Scenes" };

        [Test]
        public void Общая_станция_принимает_весь_игровой_пресет()
        {
            WeaponRegistry registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/Resources/WeaponRegistry.asset");
            Assert.IsNotNull(registry, "Нет WeaponRegistry.");
            var preset = AssetDatabase.LoadAssetAtPath<ArsenalPreset>(GameplayPreset);
            Assert.IsNotNull(preset, "Нет исходного игрового пресета.");
            Assert.IsNotEmpty(preset.Entries, "Пустой пресет не является успешной проверкой станции.");
            Assert.That(preset.Entries.All(e => e.Weapon != null && registry.Weapons.Contains(e.Weapon)), Is.True,
                "Игровой пресет содержит пустое оружие или запись вне каталога.");
            Assert.That(preset.Entries.Select(e => e.Weapon).Distinct().Count(), Is.EqualTo(preset.Entries.Count), "В игровом пресете повторяется оружие.");

            // Ассортимент стены — пресет: каждую запись вешает ряд корпуса, раскладка — своя у оружия или умолчание ряда.
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(Wall);
            Assert.IsNotNull(wall, "Нет общей игровой станции.");
            var rows = wall.GetComponentsInChildren<ArsenalSlotRow>(true).ToDictionary(r => r.RowKey);
            var problems = new List<string>();
            foreach (var entry in preset.Entries)
            {
                if (!rows.TryGetValue(entry.Row, out ArsenalSlotRow row)) { problems.Add(entry.Weapon.name + ": нет ряда " + entry.Row); continue; }
                if (!entry.Weapon.TryGetSlotLayout(row.Zone, out _) && (row.SlotPrefab == null || row.SlotPrefab.DefaultLayout == null))
                    problems.Add(entry.Weapon.name + ": нет раскладки для ряда " + entry.Row);
            }
            Assert.IsEmpty(problems, "Общая станция не принимает записи игрового пресета:\n" + string.Join("\n", problems));
        }

        [Test]
        public void Слоты_стены_не_переопределяются_в_картах()
        {
            var offenders = new List<string>();
            foreach (string root in MapRoots)
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Заготовки самого арсенала (стена, варианты слотов) — источник набора, не карта.
                if (path.StartsWith("Assets/Prefabs/Arsenal/")) continue;
                if (!path.EndsWith(".prefab") && !path.EndsWith(".unity")) continue;

                // Переопределение слота в экземпляре — строка propertyPath: _weaponInfo в PrefabInstance.
                string text = System.IO.File.ReadAllText(path);
                if (text.Contains("propertyPath: _weaponInfo")) offenders.Add(path);
            }

            Assert.IsEmpty(offenders, "Слоты стены переопределены вне CommonOpenArsenalStation — стены покажут разное:\n" + string.Join("\n", offenders));
        }
    }
}
