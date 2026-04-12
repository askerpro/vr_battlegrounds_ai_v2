using UnityEditor;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    /// Custom inspector for <see cref="ShelfItemSlotController"/>.
    /// Uses all base logic from <see cref="ArsenalSlotEditorBase"/> — no extras needed.
    /// </summary>
    [InitializeOnLoad]
    [CustomEditor(typeof(ShelfItemSlotController))]
    public class ShelfItemSlotControllerEditor : ArsenalSlotEditorBase
    {
        // Domain reload cleanup for base preview name
        static ShelfItemSlotControllerEditor()
        {
            EditorApplication.delayCall += () => CleanupByName(ItemPreviewName);
        }

        // All logic inherited from ArsenalSlotEditorBase.
        // No extra fields, no extra previews needed for shelf items.
    }
}
