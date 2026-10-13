using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Только стенд (<see cref="BotStandRunOptions.NoRecoil"/>): запрет корневой отдачи SDK на экземпляре оружия бота через
    /// штатную точку расширения <see cref="IUxrFirearmRootRecoilPolicy"/> (SDK patch 70: любой запрет на корневом grabbable
    /// исключает смещение и поворот корня). Префаб и поля триггера не меняются. Вешает и снимает
    /// <see cref="BotStandShotConditions"/> (сервер); с <see cref="BotStandRunOptions.ClientNoRecoil"/> — клиентский
    /// <see cref="BotStandNetObserver"/> на локальную копию оружия в руке бота (до конца Play, не синхронизируется).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotStandNoRecoilPolicy : MonoBehaviour, IUxrFirearmRootRecoilPolicy
    {
        public bool AllowsRootRecoil => false;
    }
}
