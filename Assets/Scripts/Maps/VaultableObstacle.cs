using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Метка перешагиваемой преграды: заборчик, барьер, бревно — то, что взрослый в бою
    /// перешагивает или перепрыгивает. Через неё ходить законно.
    ///
    /// <para>
    /// Все остальные препятствия карты непроходимы, и проход сквозь них наказывается (T-40). Эта метка —
    /// единственное исключение и единственный источник правды: анализ карты считает помеченное
    /// проходимым, наказание его не видит. Ставится на корень блока (префаб <c>LD_Fence_Vault</c>), не
    /// руками на произвольный объект: размеры помеченного ограничены правилом LD-48 — не выше
    /// <see cref="MaxHeight"/> и не толще <see cref="MaxThickness"/>, проверка — <c>MapPrinciplesTests</c>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VaultableObstacle : MonoBehaviour
    {
        /// <summary>LD-48: выше — уже не перешагнуть, м.</summary>
        public const float MaxHeight = 1.0f;

        /// <summary>LD-48: толще — не перешагнуть одним шагом, м.</summary>
        public const float MaxThickness = 0.3f;
    }
}
