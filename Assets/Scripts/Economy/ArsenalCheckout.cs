using System.Collections.Generic;
using Mirror;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Economy
{
    /// <summary>
    /// Касса стены арсенала (T-45): покупка — это взять ствол со своей стены. Сервер ловит факт
    /// захвата (<see cref="ArsenalWallController.ItemTakenServer"/>) и решает по своему состоянию
    /// (<see cref="ArsenalPurchaseRules.Checkout"/>): списать цену с владельца стены или отменить —
    /// рука отпускает, ствол возвращается в слот. Клиенту не доверяем: правило хвата на его машине
    /// (<c>ArsenalGrabRule</c>) — удобство, а не защита.
    ///
    /// <para>
    /// Ствол, купленный в эту закупку и повешенный обратно на стену, возвращает деньги
    /// (<see cref="ArsenalWallController.ItemReturnedServer"/>) — иначе «взял, передумал, повесил»
    /// стоило бы цену ствола. Чеки живут один раунд.
    /// </para>
    ///
    /// <para>
    /// Лежит на префабе режима рядом с <see cref="MatchEconomy"/>: нет экономики — нет кассы.
    /// Отмена откладывается на следующий кадр: отпускать предмет прямо в обработчике события
    /// захвата значит менять состояние менеджера захвата посреди его же рассылки.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MatchEconomy))]
    public sealed class ArsenalCheckout : MonoBehaviour
    {
        private MatchEconomy _economy;

        private readonly List<(ArsenalWallController wall, UxrGrabbableObject item)> _rejected =
            new List<(ArsenalWallController, UxrGrabbableObject)>();

        private void Awake()
        {
            _economy = GetComponent<MatchEconomy>();
        }

        private void OnEnable()
        {
            ArsenalWallController.ItemTakenServer += HandleItemTaken;
            ArsenalWallController.ItemReturnedServer += HandleItemReturned;
        }

        private void OnDisable()
        {
            ArsenalWallController.ItemTakenServer -= HandleItemTaken;
            ArsenalWallController.ItemReturnedServer -= HandleItemReturned;
            _rejected.Clear();
        }

        private void Update()
        {
            if (_rejected.Count > 0) ServerFlushRejected();
        }

        /// <summary>Выполняет отложенные отмены. Для тестов — напрямую.</summary>
        public void ServerFlushRejected()
        {
            foreach ((ArsenalWallController wall, UxrGrabbableObject item) in _rejected)
            {
                if (wall != null && item != null) wall.ServerRejectTake(item);
            }

            _rejected.Clear();
        }

        /// <summary>Сколько отмен ждёт следующего кадра. Для тестов.</summary>
        public int PendingRejections => _rejected.Count;

        /// <summary>Со стены унесли предмет (сервер).</summary>
        public void HandleItemTaken(ArsenalWallController wall, ArsenalSlotController slot,
                                    UxrGrabbableObject item, UxrGrabber grabber)
        {
            if (_economy == null) _economy = GetComponent<MatchEconomy>();
            if (!NetworkServer.active || _economy == null || wall == null || slot == null || slot.WeaponData == null) return;

            PlayerSession owner = wall.OwnerSession;
            PlayerSession taker = SessionOf(grabber);
            int ownerMoney = owner != null ? _economy.GetMoney(owner) : 0;

            CheckoutVerdict verdict = ArsenalPurchaseRules.Checkout(
                true, wall.OwnerSessionNetId, taker != null ? taker.netId : 0u, ownerMoney, slot.WeaponData.Price);

            if (verdict == CheckoutVerdict.Charge &&
                _economy.ServerTryPurchase(owner, slot.WeaponData, NetIdOf(item)))
            {
                return;
            }

            if (verdict == CheckoutVerdict.Free) return;

            GameLog.Arsenal.Warning(
                $"[Economy] Покупка '{slot.WeaponData.DisplayName}' со стены '{wall.name}' отменена: " +
                $"владелец {(owner != null ? owner.PlayerName : "—")}, взял {(taker != null ? taker.PlayerName : "?")}, " +
                $"деньги {ownerMoney}, цена {slot.WeaponData.Price}.", wall);

            UxrGrabbableObject root = RootOf(item);
            if (root != null) _rejected.Add((wall, root));
        }

        /// <summary>Предмет повесили на стену руками (сервер): купленный в эту закупку — деньги назад.</summary>
        public void HandleItemReturned(ArsenalWallController wall, ArsenalSlotController slot, UxrGrabbableObject item)
        {
            if (_economy == null) _economy = GetComponent<MatchEconomy>();
            if (!NetworkServer.active || _economy == null) return;

            uint netId = NetIdOf(item);
            if (netId != 0) _economy.ServerTryRefund(netId, slot != null ? slot.DisplayName : item != null ? item.name : "");
        }

        private static PlayerSession SessionOf(UxrGrabber grabber)
        {
            if (grabber == null || grabber.Avatar == null) return null;
            PlayerController player = grabber.Avatar.GetComponentInParent<PlayerController>();
            return player != null ? player.Session : null;
        }

        /// <summary>Корень предмета — оружие целиком, даже если рука взяла деталь.</summary>
        private static UxrGrabbableObject RootOf(UxrGrabbableObject item)
        {
            if (item == null) return null;
            WeaponComponent weapon = item.GetComponentInParent<WeaponComponent>();
            UxrGrabbableObject root = weapon != null ? weapon.GetComponent<UxrGrabbableObject>() : null;
            return root != null ? root : item;
        }

        private static uint NetIdOf(UxrGrabbableObject item)
        {
            NetworkIdentity identity = item != null ? item.GetComponentInParent<NetworkIdentity>() : null;
            return identity != null ? identity.netId : 0u;
        }
    }
}
