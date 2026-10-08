using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Переходное имя хоста оружия (этап D WeaponSystem). Компонент <c>WeaponReadinessController</c> стал
    /// <see cref="WeaponSystem"/> (тот же скрипт, тот же GUID). Это абстрактное имя оставлено только для кода вне области
    /// этапа, который ещё обращается к хосту по старому типу: <c>BotGunner</c> (задача bots-fix), <c>PumpGrabFollow</c>,
    /// <c>WeaponLedgerIntegrity</c>, <c>ManualLoadingPrefabTests</c>. Новых обращений не добавлять; удалить, когда эти
    /// файлы перейдут на <see cref="WeaponSystem"/> (этап cleanup-h или правка владельца).
    /// </summary>
    public abstract class WeaponReadinessController : MonoBehaviour
    {
        public abstract bool IsConfigured { get; }
        public abstract WeaponReadinessProfile Profile { get; }
        public abstract int TriggerIndex { get; }

        /// <summary>Подготовка оружия бота (контракт <c>BotGunner</c>): true — можно стрелять.</summary>
        public abstract bool RequestAutomationPreparation();
    }
}
