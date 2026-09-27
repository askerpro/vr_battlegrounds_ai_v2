using System.Collections.Generic;
using Mirror;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Interaction
{
    /// <summary>Какой это мусор.</summary>
    public enum LooseItemKind
    {
        Magazine,
        Weapon
    }

    /// <summary>
    /// Ничьи предметы — оружие и магазины, которые лежат в мире сами по себе: их никто
    /// не держит, они не стоят ни в каком якоре (кобура, слот стены, гнездо магазина
    /// в оружии) и не спрятаны в карман. Механизм без правил: что с ними делать и когда,
    /// решают <see cref="LooseItemSweeper"/> и правила режима.
    ///
    /// <para>
    /// <b>Кто что видит.</b> Сетевые предметы (со своим <c>netId</c>) перебирает и удаляет
    /// только сервер — <c>NetworkServer.Destroy</c> расходится на клиенты. Магазин без
    /// своего <c>netId</c> (встроенный в префаб оружия и вынутый из него) по сети удалить
    /// нельзя: его каждая машина находит и удаляет у себя сама. Тот же разбор, что
    /// у <see cref="OutOfWorldGuard"/>, и то же решение — <see cref="OutOfWorldGuard.Decide"/>.
    /// </para>
    /// </summary>
    public static class LooseItems
    {
        /// <summary>
        /// Собирает кандидатов, которых эта машина вправе убрать: на сервере — заспавненные
        /// оружие и магазины, на любой машине — магазины без своего <c>netId</c>.
        /// Ничьи ли они, проверяет <see cref="IsLoose"/>.
        /// </summary>
        public static void CollectCandidates(List<UxrGrabbableObject> result)
        {
            result.Clear();

            if (NetworkServer.active)
            {
                foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
                {
                    if (identity == null) continue;

                    UxrGrabbableObject grabbable = identity.GetComponent<UxrGrabbableObject>();
                    if (grabbable != null && TryGetKind(grabbable, out _))
                        result.Add(grabbable);
                }
            }

            foreach (UxrFirearmMag magazine in Object.FindObjectsByType<UxrFirearmMag>(FindObjectsSortMode.None))
            {
                NetworkIdentity identity = magazine.GetComponent<NetworkIdentity>();
                if (identity != null && identity.netId != 0) continue;

                UxrGrabbableObject grabbable = magazine.GetComponent<UxrGrabbableObject>();
                if (grabbable != null) result.Add(grabbable);
            }
        }

        public static bool TryGetKind(UxrGrabbableObject item, out LooseItemKind kind)
        {
            kind = LooseItemKind.Magazine;
            if (item == null) return false;

            if (item.GetComponent<UxrFirearmMag>() != null)
            {
                kind = LooseItemKind.Magazine;
                return true;
            }

            if (item.GetComponent<WeaponComponent>() != null || item.GetComponent<UxrFirearmWeapon>() != null)
            {
                kind = LooseItemKind.Weapon;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Лежит ли предмет в мире сам по себе.
        ///
        /// <para>
        /// Выключенный объект — магазин в кармане (<see cref="UxrMagazinePocket"/> прячет их
        /// выключенными). Выключенный захват или кинематическое тело — декоративный магазин
        /// у слота стены и прочие предметы-витрины: их не бросали, они так задуманы.
        /// </para>
        /// </summary>
        public static bool IsLoose(UxrGrabbableObject item)
        {
            if (item == null || !item.enabled || !item.gameObject.activeInHierarchy) return false;
            if (item.CurrentAnchor != null) return false;
            if (UxrGrabManager.HasInstance && item.IsBeingGrabbed) return false;

            Rigidbody body = item.RigidBodySource;
            return body == null || !body.isKinematic;
        }

        /// <summary>
        /// Удаляет предмет так, как это вправе сделать эта машина.
        /// </summary>
        /// <returns>true — удалён здесь; false — удалит сервер или предмет в руке.</returns>
        public static bool Remove(UxrGrabbableObject item)
        {
            if (item == null) return false;

            NetworkIdentity identity = item.GetComponent<NetworkIdentity>();
            bool grabbed = UxrGrabManager.HasInstance && item.IsBeingGrabbed;
            bool hasOwnNetId = identity != null && identity.netId != 0;

            OutOfWorldGuard.Removal removal =
                OutOfWorldGuard.Decide(grabbed, NetworkServer.active, NetworkClient.active, hasOwnNetId);

            switch (removal)
            {
                case OutOfWorldGuard.Removal.ServerDestroy:
                    NetworkServer.Destroy(item.gameObject);
                    return true;

                case OutOfWorldGuard.Removal.LocalDestroy:
                    Object.Destroy(item.gameObject);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Убирает все ничьи предметы, которые эта машина вправе убрать. Зовётся на каждой
        /// машине: сетевые уберёт сервер, магазины без <c>netId</c> — каждая у себя.
        /// </summary>
        /// <returns>Сколько предметов убрано здесь.</returns>
        public static int RemoveAll()
        {
            var candidates = new List<UxrGrabbableObject>();
            CollectCandidates(candidates);

            int removed = 0;
            foreach (UxrGrabbableObject item in candidates)
            {
                if (IsLoose(item) && Remove(item)) removed++;
            }

            if (removed > 0)
                GameLog.WeaponSystem.Info($"[LooseItems] Убрано ничьих предметов: {removed}.");

            return removed;
        }
    }
}
