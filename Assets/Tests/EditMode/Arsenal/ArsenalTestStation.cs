using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Собранная станция для тестов поведения стены и слота.
    ///
    /// <para>
    /// В игре слот живёт только в станции, которую собрал <see cref="ArsenalStationComposer" />: представление слота
    /// готово (<see cref="ArsenalStationPresetBinding.Prepare" />), массив слотов установлен в стену
    /// (<see cref="ArsenalWallController.InstallSlots" />). Здесь — те же входы без корпуса и рядов: слоты создаёт тест,
    /// описание — резолвер по пресету в памяти, раскладка — умолчание перфопанели, стиль — игровой.
    /// </para>
    /// </summary>
    public static class ArsenalTestStation
    {
        private const string CatalogPath = "Assets/Data/Arsenal/ArsenalCompositionCatalog.asset";
        private const string StylePath = "Assets/Data/Weapons/IndustrialPegboardPresentation.asset";
        private const string LayoutPath = "Assets/Data/Arsenal/SlotLayouts/Default_Pegboard.asset";
        private const string Row = "pegboard";
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Оружейный слот под станцией: якоря оружия и магазина.</summary>
        public static FirearmSlotController AddSlot(GameObject station, string name)
        {
            var slotObject = new GameObject(name);
            slotObject.transform.SetParent(station.transform, false);
            var item = new GameObject("ItemAnchor");
            item.transform.SetParent(slotObject.transform, false);
            var magazine = new GameObject("MagAnchor");
            magazine.transform.SetParent(slotObject.transform, false);

            var slot = slotObject.AddComponent<FirearmSlotController>();
            typeof(ArsenalSlotController).GetField("_itemAnchor", Private).SetValue(slot, item.AddComponent<UxrGrabbableObjectAnchor>());
            typeof(FirearmSlotController).GetField("_magAnchor", Private).SetValue(slot, magazine.AddComponent<UxrGrabbableObjectAnchor>());
            return slot;
        }

        /// <summary>
        /// Готовит станцию как сборщик: i-й слот получает i-е оружие. Оружию нужны WeaponId и префабы оружия и магазина
        /// с <see cref="UxrGrabbableObject" />. Привязку пресета добавляет сам (вместе с ней — контроллер стены).
        /// </summary>
        public static ArsenalWallController Prepare(GameObject station, IReadOnlyList<FirearmSlotController> slots, IReadOnlyList<WeaponInfo> weapons)
        {
            var style = Load<ArsenalPresentationStyle>(StylePath);
            var layout = Load<ArsenalSlotLayout>(LayoutPath);
            var catalog = Load<ArsenalCompositionCatalog>(CatalogPath);

            var preset = ScriptableObject.CreateInstance<ArsenalPreset>();
            try
            {
                typeof(ArsenalPreset).GetField("_presetId", Private).SetValue(preset, "test-station");
                typeof(ArsenalPreset).GetField("_presentationStyle", Private).SetValue(preset, style);
                typeof(ArsenalPreset).GetField("_entries", Private).SetValue(preset,
                    weapons.Select(w => new ArsenalPreset.Entry { Weapon = w, Row = Row }).ToList());

                var description = ArsenalStationResolver.ResolveDescription(ArsenalStationBuildInput.Capture(station.name, preset, catalog,
                    new ArsenalVisualRequest(""), new ArsenalPlacementInput(default, Vector3.one, new Bounds())));
                if (!description.Success)
                    throw new InvalidOperationException("Тестовая станция не собирается: " +
                                                        string.Join("; ", description.Failures.Select(f => f.Kind + ":" + f.Detail)));

                var presentations = weapons.Select(w => ArsenalPresentationResolver.Resolve(w, ArsenalPresentationZone.Pegboard, style, layout)).ToList();
                var binding = station.GetComponent<ArsenalStationPresetBinding>();
                if (binding == null) binding = station.AddComponent<ArsenalStationPresetBinding>();
                binding.Prepare(description, slots, presentations);

                var wall = station.GetComponent<ArsenalWallController>();
                wall.InstallSlots(slots);
                return wall;
            }
            finally { Object.DestroyImmediate(preset); }
        }

        /// <summary>Слот, собранный по записи пресета, и ряд корпуса, который его повесил.</summary>
        public sealed class BuiltSlot
        {
            public ArsenalSlotManifest Manifest;
            public ArsenalSlotRow Row;
            public ArsenalPresentationSnapshot Presentation;
            public FirearmSlotController Slot;
        }

        /// <summary>
        /// Корпус станции со слотами пресета в EditMode: раскладка по оружию или умолчанию ряда, карточка по раскладке
        /// (<see cref="ArsenalPriceTag.Create(ArsenalSlotController, ArsenalPresentationSnapshot)" />), ряд вешает слоты
        /// (<see cref="ArsenalSlotRow.Arrange" />). Сборщик слота назначает ID ролей только непроснувшимся объектам Play,
        /// поэтому слот здесь — экземпляр префаба ряда; сам сборщик проверяется в Play. Корпус уничтожает тест.
        /// </summary>
        public static List<BuiltSlot> BuildSlots(GameObject shell, string presetPath)
        {
            var preset = Load<ArsenalPreset>(presetPath);
            var description = ArsenalStationResolver.ResolveDescription(ArsenalStationBuildInput.Capture(shell.name, preset, Load<ArsenalCompositionCatalog>(CatalogPath),
                new ArsenalVisualRequest(""), new ArsenalPlacementInput(default, Vector3.one, new Bounds())));
            if (!description.Success)
                throw new InvalidOperationException("Пресет не собирается: " + string.Join("; ", description.Failures.Select(f => f.Kind + ":" + f.Detail)));

            var rows = shell.GetComponentsInChildren<ArsenalSlotRow>(true);
            var staging = new GameObject("TestSlotStaging");
            staging.SetActive(false);
            try
            {
                var built = new List<BuiltSlot>();
                foreach (var manifest in description.Slots)
                {
                    var row = rows.Single(r => r.RowKey == manifest.Entry.RowKey);
                    var presentation = ArsenalPresentationResolver.Resolve(manifest.Entry.WeaponResource, row.Zone, description.Style, row.SlotPrefab.DefaultLayout);
                    var slot = (FirearmSlotController)Object.Instantiate(row.SlotPrefab, staging.transform, false);
                    slot.name = "Slot_" + manifest.Entry.NetworkIndex + "_" + manifest.Entry.LogicalSlotKey;
                    ArsenalPriceTag.Create(slot, presentation);
                    built.Add(new BuiltSlot { Manifest = manifest, Row = row, Presentation = presentation, Slot = slot });
                }
                foreach (var row in rows)
                    row.Arrange(built.Where(b => b.Row == row).Select(b => (ArsenalSlotController)b.Slot).ToList());
                return built;
            }
            finally { Object.DestroyImmediate(staging); }
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Нет ассета " + path);
            return asset;
        }
    }
}
