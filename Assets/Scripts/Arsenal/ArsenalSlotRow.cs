using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    public enum ArsenalRowAlignment { Center, Start, End }

    /// <summary>
    ///     Ряд корпуса станции: место, куда вешаются слоты сгенерированной станции. Ряд знает свою геометрию —
    ///     точку и поворот слотов, зазор и выравнивание — и какой префаб слота на нём монтируется. Ширину слота
    ///     знает сам слот (<see cref="ArsenalSlotController.SlotWidth" />), содержимое слота ряд не знает.
    ///     <para>
    ///     Слоты становятся детьми ряда и двигаются вместе с ним: если ряд — цель поз станции или лежит внутри
    ///     неё (полка), открытие станции везёт слоты без отдельных целей. Ряд настраивается в Prefab Mode корпуса.
    ///     </para>
    /// </summary>
    public sealed class ArsenalSlotRow : MonoBehaviour
    {
        [Tooltip("Ключ ряда: записи пресета указывают его, чтобы попасть в этот ряд.")]
        [SerializeField] private string _rowKey;
        [Tooltip("Вид слота ряда: какую раскладку оружия (панель или полка) применять к его слотам.")]
        [SerializeField] private ArsenalPresentationZone _zone;
        [Tooltip("Префаб слота, который монтируется в этот ряд.")]
        [SerializeField] private ArsenalSlotController _slotPrefab;
        [Tooltip("Точка ряда в его локальных координатах: от неё раскладываются слоты вдоль локальной оси X.")]
        [SerializeField] private Vector3 _slotOrigin;
        [Tooltip("Поворот каждого слота в ряду.")]
        [SerializeField] private Vector3 _slotEulerAngles;
        [Tooltip("Зазор между соседними слотами, м.")]
        [SerializeField] private float _gap;
        [SerializeField] private ArsenalRowAlignment _alignment = ArsenalRowAlignment.Center;

        public string RowKey => _rowKey ?? string.Empty;
        public ArsenalPresentationZone Zone => _zone;
        public ArsenalSlotController SlotPrefab => _slotPrefab;
        public Vector3 SlotOrigin => _slotOrigin;
        public Quaternion SlotRotation => Quaternion.Euler(_slotEulerAngles);
        public float Gap => _gap;
        public ArsenalRowAlignment Alignment => _alignment;

        /// <summary>
        ///     Вешает слоты в ряд в данном порядке: каждый становится ребёнком ряда, стоит вдоль локальной оси X
        ///     с зазором <see cref="Gap" /> и выравниванием <see cref="Alignment" />. Активность слотов не меняется.
        ///     Вместимость не проверяется — настройку корпуса и ассортимента делает человек.
        /// </summary>
        public void Arrange(IReadOnlyList<ArsenalSlotController> slots)
        {
            if (slots == null || slots.Count == 0) return;
            float total = _gap * (slots.Count - 1);
            foreach (ArsenalSlotController slot in slots) total += slot.SlotWidth;
            float cursor = _alignment == ArsenalRowAlignment.Center ? -total * .5f
                : _alignment == ArsenalRowAlignment.End ? -total : 0f;
            Quaternion rotation = SlotRotation;
            foreach (ArsenalSlotController slot in slots)
            {
                float center = cursor + slot.SlotWidth * .5f;
                slot.transform.SetParent(transform, false);
                slot.transform.SetLocalPositionAndRotation(_slotOrigin + new Vector3(center, 0f, 0f), rotation);
                cursor += slot.SlotWidth + _gap;
            }
        }

        /// <summary>Настройка из редакторского инструмента разметки корпуса.</summary>
        public void Configure(string rowKey, ArsenalPresentationZone zone, ArsenalSlotController slotPrefab, Vector3 slotOrigin,
            Quaternion slotRotation, float gap, ArsenalRowAlignment alignment)
        {
            _rowKey = rowKey; _zone = zone; _slotPrefab = slotPrefab; _slotOrigin = slotOrigin;
            _slotEulerAngles = slotRotation.eulerAngles; _gap = gap; _alignment = alignment;
        }
    }
}
