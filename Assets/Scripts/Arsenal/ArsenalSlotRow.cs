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
            var widths = new float[slots.Count];
            for (int i = 0; i < slots.Count; i++) widths[i] = slots[i].SlotWidth;
            Pose[] poses = SlotLocalPoses(widths);
            for (int i = 0; i < slots.Count; i++) Mount(slots[i].transform, poses[i]);
        }

        /// <summary>
        ///     Позы слотов с данными ширинами в этом порядке, в локальных координатах ряда: вдоль оси X с зазором
        ///     <see cref="Gap" /> и выравниванием <see cref="Alignment" />. Чистый расчёт без объектов — единая геометрия
        ///     ряда для сборщика станции и редакторского превью.
        /// </summary>
        public Pose[] SlotLocalPoses(IReadOnlyList<float> widths)
        {
            if (widths == null || widths.Count == 0) return Array.Empty<Pose>();
            float total = _gap * (widths.Count - 1);
            foreach (float width in widths) total += width;
            float cursor = _alignment == ArsenalRowAlignment.Center ? -total * .5f
                : _alignment == ArsenalRowAlignment.End ? -total : 0f;
            Quaternion rotation = SlotRotation;
            var poses = new Pose[widths.Count];
            for (int i = 0; i < widths.Count; i++)
            {
                float center = cursor + widths[i] * .5f;
                poses[i] = new Pose(_slotOrigin + new Vector3(center, 0f, 0f), rotation);
                cursor += widths[i] + _gap;
            }
            return poses;
        }

        /// <summary>Вешает слот в ряд: ребёнок ряда в рассчитанной позе (<see cref="SlotLocalPoses" />).</summary>
        public void Mount(Transform slot, Pose localPose)
        {
            slot.SetParent(transform, false);
            slot.SetLocalPositionAndRotation(localPose.position, localPose.rotation);
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
