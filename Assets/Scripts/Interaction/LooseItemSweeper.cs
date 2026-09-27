using System.Collections.Generic;
using Mirror;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Interaction
{
    /// <summary>Что делать с оружием, пролежавшим ничьим дольше срока.</summary>
    public enum LooseWeaponAction
    {
        /// <summary>Не трогать — пусть лежит (в раунде ствол убитого можно подобрать).</summary>
        Keep,

        /// <summary>Уничтожить.</summary>
        Destroy,

        /// <summary>
        /// Вернуть в слот стены, который его выдал (<see cref="WeaponComponent.HomeSlot"/>);
        /// если дом занят или его нет — уничтожить.
        /// </summary>
        ReturnHome
    }

    /// <summary>
    /// Уборка ничьих предметов по срокам: магазин, пролежавший на полу дольше срока,
    /// исчезает; оружие — по выбранному действию.
    ///
    /// <para>
    /// Правило задаётся тем, где лежит компонент и как он настроен, а не проверками
    /// режима внутри: в лобби (<c>Lobby.unity</c>) оружие возвращается домой, на префабе
    /// режима Elimination — лежит до конца раунда. Что считается ничьим, решает
    /// <see cref="LooseItems"/>, сколько предмет пролежал — <see cref="LooseItemClock"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class LooseItemSweeper : MonoBehaviour
    {
        [Tooltip("Через сколько секунд на полу магазин исчезает. 0 — не трогать.")]
        [Min(0f)]
        [SerializeField] private float _magazineLifetime = 30f;

        [Tooltip("Через сколько секунд на полу с оружием поступают по действию ниже. 0 — не трогать.")]
        [Min(0f)]
        [SerializeField] private float _weaponLifetime = 30f;

        [Tooltip("Что делать с оружием, пролежавшим на полу дольше срока.")]
        [SerializeField] private LooseWeaponAction _weaponAction = LooseWeaponAction.Keep;

        [Tooltip("Как часто проверять, секунды.")]
        [Min(0.1f)]
        [SerializeField] private float _checkInterval = 0.5f;

        private readonly LooseItemClock _clock = new LooseItemClock();
        private readonly List<UxrGrabbableObject> _candidates = new List<UxrGrabbableObject>();
        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < _checkInterval) return;
            _timer = 0f;

            Sweep(Time.time);
        }

        /// <summary>Один проход уборки. Отдельно от <c>Update</c> — для проверки из редактора.</summary>
        internal void Sweep(float now)
        {
            LooseItems.CollectCandidates(_candidates);
            _clock.BeginPass();

            foreach (UxrGrabbableObject item in _candidates)
            {
                if (item == null || !LooseItems.TryGetKind(item, out LooseItemKind kind)) continue;

                int id = item.GetInstanceID();
                float looseFor = _clock.Observe(id, LooseItems.IsLoose(item), now);

                float lifetime = kind == LooseItemKind.Magazine ? _magazineLifetime : _weaponLifetime;
                if (lifetime <= 0f || looseFor < lifetime) continue;

                if (Expire(item, kind))
                    _clock.Forget(id);
            }

            _clock.EndPass();
        }

        private bool Expire(UxrGrabbableObject item, LooseItemKind kind)
        {
            if (kind == LooseItemKind.Weapon)
            {
                if (_weaponAction == LooseWeaponAction.Keep) return false;

                if (_weaponAction == LooseWeaponAction.ReturnHome && TryReturnHome(item))
                    return true;
            }

            GameLog.WeaponSystem.Verbose($"[LooseItemSweeper] '{item.name}' пролежал на полу срок — убираю.", item);
            return LooseItems.Remove(item);
        }

        private static bool TryReturnHome(UxrGrabbableObject item)
        {
            if (!NetworkServer.active) return false;

            WeaponComponent weapon = item.GetComponent<WeaponComponent>();
            if (weapon == null || weapon.HomeSlot == null) return false;

            ArsenalWallController wall = weapon.HomeSlot.GetComponentInParent<ArsenalWallController>();
            return wall != null && wall.ServerReturnHome(weapon);
        }
    }
}
