namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Shelf item slot on the Arsenal Wall.
    /// Used for smaller items: pistols, grenades, medkits, etc.
    /// No additional logic beyond <see cref="ArsenalSlotController"/> — just a type marker
    /// so the Editor can provide a dedicated inspector and the prefab can be distinguished.
    /// </summary>
    public class ShelfItemSlotController : ArsenalSlotController
    {
        // All logic inherited from ArsenalSlotController.
        // This class exists as a type marker for:
        // 1. Custom Editor (ShelfItemSlotControllerEditor)
        // 2. Prefab distinction (ShelfItemSlotPrefab vs RifleSlotPrefab)
        // 3. Future shelf-specific logic (item rotation display, etc.)
    }
}
