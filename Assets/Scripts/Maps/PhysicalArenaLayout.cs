using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Роль общего чертежа площадки. Якоря активны, Geometry — только диагностическое изображение.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(PhysicalArenaDefinition))]
    public sealed class PhysicalArenaLayout : MonoBehaviour
    {
        public GameObject diagnosticGeometry;

        private void Awake()
        {
            if (diagnosticGeometry != null) diagnosticGeometry.SetActive(false);
        }
    }
}
