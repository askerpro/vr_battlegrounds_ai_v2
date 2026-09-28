using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Правило авторства канала состояния UltimateXR: какое событие эта машина вправе разослать.
    ///
    /// <para>
    /// <b>Откуда проблема.</b> В исходной схеме UltimateXR (<c>UxrFishNetAvatar</c>) событие шлёт только
    /// владелец — локальный игрок, сервер не порождает ничего. <see cref="NetworkStateRelay"/> (патч 1)
    /// ретранслирует всё, что родилось на машине, — иначе нельзя: урон и смерть (<c>UxrActor</c>),
    /// снятие снаряжения, арсенал идут от сервера. Но UltimateXR ещё и пересчитывает действия чужого
    /// игрока на каждой машине (выстрел — по синхронизированному спуску, затвор — по его ходу).
    /// Пересчитанное порождает те же синхронизируемые вызовы, и копия рассылала их всем — событие
    /// размножалось: вторая пуля у стрелка, двойной урон на сервере.
    /// </para>
    ///
    /// <para>
    /// <b>Правило.</b> Событие компонента <b>на предмете в руке</b> аватара шлёт только автор этого
    /// аватара: машина, где он свой (<c>isOwned</c>), а для аватара без владельца (кукла стресс-теста) —
    /// сервер. Вне правила: <c>UxrGrabManager</c> (автор хвата — в аргументах; сервер законно
    /// отпускает и вынимает предметы из чужих рук), <c>UxrActor</c> (здоровье — серверное), предметы,
    /// которые никто не держит, в том числе лежащие в карманах. Отброшенное считается по
    /// типу и методу — по счётчику видно, кто ещё пересчитывает действия чужих игроков.
    /// </para>
    /// </summary>
    public static class StateEventAuthority
    {
        /// <summary>Сколько событий отброшено, по ключу «Тип.Метод».</summary>
        public static IReadOnlyDictionary<string, int> DroppedCounts => Dropped;

        private static readonly Dictionary<string, int> Dropped = new Dictionary<string, int>();

        /// <summary>
        /// Является ли эта машина автором действий аватара. Подменяется в тестах: вне сети
        /// <c>NetworkIdentity</c> не заспавнен и авторство по нему не определить.
        /// </summary>
        public static Func<UxrAvatar, bool> IsAuthoredHere = DefaultIsAuthoredHere;

        /// <summary>
        /// Автор мирового состояния — сервер; вне сети — сама машина. Арсенал, фазы, ничьи предметы.
        /// </summary>
        public static bool IsWorldAuthority => NetworkServer.active || !NetworkClient.active;

        /// <summary>
        /// Вправе ли эта машина совершать синхронизируемые действия над предметом (перезарядка
        /// затвором и т. п.): предмет держит аватар, чей автор — эта машина; никто не держит —
        /// автор мира (<see cref="IsWorldAuthority"/>). Остальные машины получат действие событием
        /// автора — пересчитывать его у себя значит размножить (known-issues, Issue 23).
        /// </summary>
        public static bool IsAuthorOfItem(Component item)
        {
            if (item == null) return false;
            if (!UxrGrabManager.HasInstance || !TryGetHolders(item.transform, out List<UxrAvatar> holders))
                return IsWorldAuthority;

            foreach (UxrAvatar holder in holders)
            {
                if (IsAuthoredHere(holder)) return true;
            }
            return false;
        }

        /// <summary>
        /// Чистое правило: свой аватар — автор; аватар без владельца — автор сервер.
        /// </summary>
        public static bool IsAuthor(bool ownedHere, bool hasOwnerConnection, bool isServer)
        {
            return ownedHere || (isServer && !hasOwnerConnection);
        }

        /// <summary>
        /// Слать ли событие компонента. Ложь — событие пересчитано на копии чужого действия,
        /// автор разошлёт его сам.
        /// </summary>
        public static bool ShouldSend(IUxrStateSync component, UxrSyncEventArgs eventArgs)
        {
            Component target = component?.Component;
            if (target == null) return true;

            // Автор определяется не компонентом: хват — аргументами, здоровье — сервером.
            if (target is UxrGrabManager || target is UxrActor) return true;

            if (!UxrGrabManager.HasInstance) return true;

            if (!TryGetHolders(target.transform, out List<UxrAvatar> holders)) return true;

            foreach (UxrAvatar holder in holders)
            {
                if (IsAuthoredHere(holder)) return true;
            }

            CountDropped(target, eventArgs, holders[0]);
            return false;
        }

        /// <summary>
        /// Аватары, держащие предмет, на котором лежит компонент: от компонента вверх до первого
        /// захваченного <see cref="UxrGrabbableObject"/>. Упёрлись в аватар раньше — компонент
        /// на самом аватаре или на предмете в его кармане, правило не про него.
        /// </summary>
        internal static bool TryGetHolders(Transform start, out List<UxrAvatar> holders)
        {
            holders = null;

            for (Transform node = start; node != null; node = node.parent)
            {
                if (node.TryGetComponent(out UxrGrabbableObject grabbable) && UxrGrabManager.Instance.IsBeingGrabbed(grabbable))
                {
                    foreach (UxrGrabber grabber in UxrGrabManager.Instance.GetGrabbingHands(grabbable))
                    {
                        if (grabber == null || grabber.Avatar == null) continue;
                        holders ??= new List<UxrAvatar>(2);
                        if (!holders.Contains(grabber.Avatar)) holders.Add(grabber.Avatar);
                    }

                    return holders != null;
                }

                if (node.TryGetComponent(out UxrAvatar _)) return false;
            }

            return false;
        }

        private static bool DefaultIsAuthoredHere(UxrAvatar avatar)
        {
            NetworkIdentity identity = avatar.GetComponent<NetworkIdentity>();

            // Вне сети (или аватар не заспавнен) авторство не определить — событие никуда и не уйдёт.
            if (identity == null || identity.netId == 0) return true;

            return IsAuthor(identity.isOwned, identity.connectionToClient != null, NetworkServer.active);
        }

        private static void CountDropped(Component target, UxrSyncEventArgs eventArgs, UxrAvatar holder)
        {
            string member = eventArgs is UxrMethodInvokedSyncEventArgs method ? method.MethodName
                          : eventArgs is UxrPropertyChangedSyncEventArgs property ? property.PropertyName
                          : eventArgs.GetType().Name;
            string key = target.GetType().Name + "." + member;

            Dropped.TryGetValue(key, out int count);
            Dropped[key] = count + 1;

            // Первый раз по ключу — в лог: это список компонентов, пересчитывающих чужие действия.
            if (count == 0)
            {
                GameLog.Network.Info($"[StateEventAuthority] Не рассылаю {key}: пересчитано на копии, предмет держит " +
                                     $"'{holder.name}', автор не эта машина. Дальше такие события только считаются.");
            }
        }
    }
}
