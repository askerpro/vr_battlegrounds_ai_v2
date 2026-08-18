// Ярус C (два процесса) — контракт сценария.
#if !VRBG_NO_E2E
using System.Collections;

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>
    /// Сценарий e2e-прогона. Один экземпляр живёт в одном процессе и знает,
    /// что именно проверять в своей роли (сервер / клиент).
    ///
    /// Правила реализации:
    /// <list type="bullet">
    /// <item>объявить полный список проверок через <see cref="E2EResult.Declare"/>
    ///       в самом начале — иначе при обрыве непонятно, до чего сценарий не дошёл;</item>
    /// <item>не звать вложенные корутины через <c>yield return StartCoroutine(...)</c>:
    ///       <see cref="E2ERunner"/> прокручивает сценарий вручную, чтобы ловить
    ///       исключения, и внутрь вложенной корутины он не заглядывает;</item>
    /// <item>ничего не чинить в игровом коде — сценарий только наблюдает.</item>
    /// </list>
    /// </summary>
    public interface IE2EScenario
    {
        /// <summary>Имя для аргумента <c>-e2eScenario</c>.</summary>
        string Name { get; }

        /// <summary>Проигрывает сценарий и заполняет <paramref name="result"/>.</summary>
        IEnumerator Run(E2EContext context, E2EResult result);
    }
}
#endif
