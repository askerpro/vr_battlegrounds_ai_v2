using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Три высотные карточки одного экземпляра без новых вариантов палитры.</summary>
    public static class BlockoutSectionPropertiesPanel
    {
        private static readonly bool[] expanded = { true, true, true };
        public static void Draw(BlockoutBlockDefinition definition, float height, BlockoutSectionSettings[] sections)
        {
            EditorGUILayout.LabelField("Вертикальные секции", EditorStyles.boldLabel);
            for (int i = 0; i < 3; i++)
            {
                var section = sections[i]; float bottom = BlockoutSectionSettings.Bottom(i), top = Mathf.Min(height, BlockoutSectionSettings.Top(i));
                bool present = top > bottom + .000001f;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string range = present ? $"{bottom:0.##}–{top:0.##} м" : $"{bottom:0.##}–{BlockoutSectionSettings.Top(i):0.##} м · скрыта";
                    expanded[i] = EditorGUILayout.Foldout(expanded[i], $"{BlockoutSectionSettings.Label(i)} · {range} · {section.material}", true);
                    if (!expanded[i]) continue;
                    if (!present) EditorGUILayout.LabelField("При текущей высоте геометрии нет; настройки сохраняются.", EditorStyles.wordWrappedMiniLabel);
                    int material = System.Array.IndexOf(definition.allowedMaterials, section.material);
                    material = EditorGUILayout.Popup("Материал секции", Mathf.Max(0, material), definition.allowedMaterials.Select(m => m.ToString()).ToArray());
                    section.material = definition.allowedMaterials[material];
                    using (new EditorGUI.DisabledScope(!definition.supportsOpenings))
                        section.openings.enabled = EditorGUILayout.Toggle("Настоящие щели", section.openings.enabled);
                    if (!definition.supportsOpenings) EditorGUILayout.LabelField(definition.openingDisabledReason, EditorStyles.wordWrappedMiniLabel);
                    else if (section.openings.enabled)
                    {
                        section.openings.spacing = EditorGUILayout.FloatField("Шаг щелей, м", section.openings.spacing);
                        section.openings.width = EditorGUILayout.FloatField("Ширина щели, м", section.openings.width);
                        section.openings.sillHeight = EditorGUILayout.FloatField("Основание от низа секции, м", section.openings.sillHeight);
                        section.openings.lintelHeight = EditorGUILayout.FloatField("Перемычка от верха секции, м", section.openings.lintelHeight);
                        EditorGUILayout.LabelField("Ноль у внутренней границы позволяет продолжить совпадающую щель соседней секции.", EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }
    }
}
