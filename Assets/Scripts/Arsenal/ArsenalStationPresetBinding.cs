using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Контекст ассортимента собранной станции: какое описание её собрало и какое представление у каждого слота.
    ///     Заполняет только <see cref="ArsenalStationComposer" /> через <see cref="Prepare" />; до этого станция ждёт молча
    ///     (склад и стена не выдают предметы, пока <see cref="IsPrepared" /> ложно).
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ArsenalWallController))]
    public sealed class ArsenalStationPresetBinding : MonoBehaviour
    {
        private ArsenalStationDescription _prepared;
        private readonly Dictionary<ArsenalSlotController, ArsenalPresentationSnapshot> _presentation =
            new Dictionary<ArsenalSlotController, ArsenalPresentationSnapshot>();

        public bool IsPrepared => _prepared != null;
        public ArsenalStationDescription Prepared => _prepared;

        public ArsenalPresentationSnapshot ResolvePresentation(ArsenalSlotController slot)
        {
            if (!IsPrepared) throw new InvalidOperationException("Станция ещё не собрана: представления нет.");
            if (_presentation.TryGetValue(slot, out ArsenalPresentationSnapshot current)) return current;
            throw new InvalidOperationException("Слот не принадлежит собранной станции: " + (slot != null ? slot.name : "null"));
        }

        /// <summary>
        ///     Слоты созданы сборщиком в порядке манифеста описания, i-й слот — i-я запись манифеста. Сначала проверяет
        ///     всё, потом публикует контекст представления и только затем настраивает оружие слотов (оно читает
        ///     представление). Повтор с тем же описанием безопасен.
        /// </summary>
        public void Prepare(ArsenalStationDescription description, IReadOnlyList<ArsenalSlotController> slots,
            IReadOnlyList<ArsenalPresentationSnapshot> presentations)
        {
            if (description == null || !description.Success) throw new InvalidOperationException("ArsenalPreset.Prepare: описание с ошибками.");
            if (_prepared != null)
            {
                if (!ReferenceEquals(_prepared, description)) throw new InvalidOperationException("Нельзя менять описание собранной станции.");
                return;
            }
            if (slots == null || presentations == null || slots.Count != description.Slots.Count || presentations.Count != slots.Count)
                throw new InvalidOperationException("ArsenalPreset.Prepare: число слотов не совпадает с манифестом.");

            var prepared = new Dictionary<ArsenalSlotController, ArsenalPresentationSnapshot>();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var entry = description.Slots[i].Entry;
                if (slot == null || prepared.ContainsKey(slot)) throw new InvalidOperationException("ArsenalPreset.Prepare: пустой или повторный слот " + i + ".");
                var weapon = entry.WeaponResource;
                if (weapon == null || weapon.WeaponPrefab == null || weapon.MagazinePrefab == null)
                    throw new InvalidOperationException("ArsenalPreset.Prepare: нет оружия или магазина у " + entry.LogicalSlotKey + ".");
                var firearm = slot as FirearmSlotController;
                if (firearm == null || slot.ItemAnchor == null || firearm.MagAnchor == null)
                    throw new InvalidOperationException("ArsenalPreset.Prepare: слот без оружейного/магазинного якоря " + i + ".");
                if (presentations[i] == null) throw new InvalidOperationException("ArsenalPreset.Prepare: нет представления слота " + i + ".");
                prepared.Add(slot, presentations[i]);
            }

            _presentation.Clear();
            foreach (var pair in prepared) _presentation.Add(pair.Key, pair.Value);
            _prepared = description;
            for (int i = 0; i < slots.Count; i++) slots[i].ConfigureWeapon(description.Slots[i].Entry.WeaponResource);
        }
    }
}
