using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>
    /// Сетка плиток с фиксированным числом колонок: ширина ячейки считается от ширины сетки,
    /// высота — по пропорции. Сетка растёт только вниз (в скролл), вбок не вылезает никогда.
    /// </summary>
    public class KitGrid : GridLayoutGroup
    {
        [SerializeField] private int _columns = 3;
        [Tooltip("Высота ячейки / ширина.")]
        [SerializeField] private float _aspect = 0.6f;

        public void Setup(int columns, float aspect, float gap)
        {
            _columns = Mathf.Max(1, columns);
            _aspect = aspect;
            spacing = new Vector2(gap, gap);
            constraint = Constraint.FixedColumnCount;
            constraintCount = _columns;
            startAxis = Axis.Horizontal;
            childAlignment = TextAnchor.UpperLeft;
        }

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            // Ширину диктует родитель: сетка ничего не требует по горизонтали.
            SetLayoutInputForAxis(padding.horizontal, padding.horizontal, 1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            FitCells();
            base.CalculateLayoutInputVertical();
        }

        public override void SetLayoutHorizontal()
        {
            FitCells();
            base.SetLayoutHorizontal();
        }

        private void FitCells()
        {
            float width = rectTransform.rect.width - padding.horizontal - spacing.x * (_columns - 1);
            float cell = Mathf.Max(1f, width / _columns);
            cellSize = new Vector2(cell, cell * _aspect);
            constraintCount = _columns;
        }
    }
}
