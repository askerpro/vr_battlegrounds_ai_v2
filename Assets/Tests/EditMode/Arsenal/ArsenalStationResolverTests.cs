using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Раскладка станции по пресету (генератор арсенала, решение 2026-10-07 «применять, не проверять»).
    ///
    /// Что доказывает. Генератор раскладывает слоты по порядку пресета (геометрию ряда знает ряд корпуса,
    /// не раскладка), позы берёт из стиля как есть и не требует геометрии оружия: станция собирается даже при
    /// нулевом отведённом месте.
    /// Отказ — только когда станцию нельзя собрать или сломается сеть. Отпечаток раскладки зависит от входа,
    /// а не от вычислений, и одинаков при повторе.
    /// Работает на настоящих каталоге, стиле и FullDemoArsenal; стиль назначается копии пресета в памяти.
    /// </summary>
    public class ArsenalStationResolverTests
    {
        private const string CatalogPath = "Assets/Data/Arsenal/ArsenalCompositionCatalog.asset";
        private const string StylePath = "Assets/Data/Weapons/IndustrialPegboardPresentation.asset";
        private const string PresetPath = "Assets/Data/Weapons/FullDemoArsenal.asset";
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private ArsenalCompositionCatalog _catalog;
        private ArsenalPreset _preset;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<ArsenalCompositionCatalog>(CatalogPath);
            var source = AssetDatabase.LoadAssetAtPath<ArsenalPreset>(PresetPath);
            Assert.IsNotNull(_catalog, CatalogPath);
            Assert.IsNotNull(source, PresetPath);
            _preset = Object.Instantiate(source);
            SetStyle(_preset, AssetDatabase.LoadAssetAtPath<ArsenalPresentationStyle>(StylePath));
            // Записи нового формата: оружие и ряд корпуса.
            SetEntries(_preset, source.Entries.Where(e => e.Weapon != null)
                .Select((e, i) => new ArsenalPreset.Entry { Weapon = e.Weapon, Row = i % 2 == 0 ? "pegboard" : "shelf" }).ToList());
        }

        [TearDown]
        public void TearDown()
        {
            if (_preset != null) Object.DestroyImmediate(_preset);
        }

        private static void SetStyle(ArsenalPreset preset, ArsenalPresentationStyle style) =>
            typeof(ArsenalPreset).GetField("_presentationStyle", Private).SetValue(preset, style);

        private static void SetEntries(ArsenalPreset preset, List<ArsenalPreset.Entry> entries) =>
            typeof(ArsenalPreset).GetField("_entries", Private).SetValue(preset, entries);

        private ArsenalStationDescription Resolve(ArsenalPreset preset) =>
            ArsenalStationResolver.ResolveDescription(ArsenalStationBuildInput.Capture("station-test", preset, _catalog,
                new ArsenalVisualRequest(""), new ArsenalPlacementInput(default, Vector3.one, new Bounds())));

        [Test]
        public void Станция_собирается_без_геометрии_оружия_и_без_места()
        {
            ArsenalStationDescription description = Resolve(_preset);
            Assert.IsTrue(description.Success, string.Join("; ", description.Failures.Select(f => f.Kind + ":" + f.Detail)));
            Assert.AreEqual(_preset.Entries.Count, description.Slots.Count);
            Assert.AreEqual(ArsenalDecorationFallback.Bare, description.Selection.Kind, "В каталоге нет корпусов — станция без корпуса, не отказ.");
        }

        [Test]
        public void Слоты_в_порядке_пресета()
        {
            ArsenalStationDescription description = Resolve(_preset);
            for (int i = 0; i < description.Slots.Count; i++)
            {
                Assert.AreEqual(i, description.Slots[i].Entry.NetworkIndex);
                Assert.AreEqual(_preset.Entries[i].Weapon.WeaponId, description.Slots[i].Entry.LogicalSlotKey);
                Assert.AreEqual(_preset.Entries[i].Row, description.Slots[i].Entry.RowKey);
            }
        }

        [Test]
        public void Пресет_без_стиля_отказ_UnstyledPreset()
        {
            SetStyle(_preset, null);
            ArsenalStationDescription description = Resolve(_preset);
            Assert.IsFalse(description.Success);
            Assert.IsTrue(description.Failures.All(f => f.Kind == ArsenalCompositionFailureKind.UnstyledPreset));
        }

        [Test]
        public void Повтор_оружия_отказ_DuplicateLogicalKey()
        {
            var entries = _preset.Entries.ToList();
            entries.Add(entries[0]);
            SetEntries(_preset, entries);
            ArsenalStationDescription description = Resolve(_preset);
            Assert.IsTrue(description.Failures.Any(f => f.Kind == ArsenalCompositionFailureKind.DuplicateLogicalKey));
        }

        [Test]
        public void Отпечаток_стабилен_и_зависит_от_порядка()
        {
            string first = Resolve(_preset).LayoutFingerprint;
            Assert.AreEqual(first, Resolve(_preset).LayoutFingerprint, "Тот же вход — тот же отпечаток.");
            var entries = _preset.Entries.ToList();
            entries.Reverse();
            SetEntries(_preset, entries);
            Assert.AreNotEqual(first, Resolve(_preset).LayoutFingerprint, "Другой порядок — другой отпечаток.");
        }
    }
}
