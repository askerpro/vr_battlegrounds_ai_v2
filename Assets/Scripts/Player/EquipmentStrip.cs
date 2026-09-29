using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Снаряжение не переживает переходов: при любой смене режима на карте (разминка → матч,
    /// матч → разминка, пауза, «Продолжить») и при переходе на другую карту у каждого игрока
    /// забирается всё — оружие и предметы в руках, в кобурах, магазины в кармане — а ничьё
    /// с пола убирается.
    ///
    /// <para>
    /// <b>Уничтожается, а не роняется.</b> Смерть (<see cref="PlayerLoadoutManager.ServerDropEquipment"/>)
    /// роняет оружие на пол — его можно подобрать. Здесь так нельзя: упавшее снова стало бы
    /// ничьим и дожило бы до нового режима. Сетевые предметы уничтожаются через
    /// <c>NetworkServer.Destroy</c> — уходит всем машинам.
    /// </para>
    ///
    /// <para>
    /// <b>Порядок — как у <see cref="Avatars.AvatarTeardown"/>.</b> Сначала руки отпускают
    /// (иначе в <c>UxrGrabManager</c> остался бы захват уничтоженного предмета), потом предмет
    /// уничтожается. Уничтожается только снаряжение — оружие и магазины
    /// (<see cref="IsEquipment"/>); планшет, жетон и прочее только отпускаются.
    /// </para>
    /// </summary>
    public static class EquipmentStrip
    {
        /// <summary>
        /// Сервер: снаряжение забирается у всех (параметр — причина). Для тестов и для тех,
        /// кому нужно знать, что переход состоялся.
        /// </summary>
        public static event Action<string> ServerStripAllRequested;

        /// <summary>Снаряжение ли это: оружие (или его часть) либо магазин.</summary>
        public static bool IsEquipment(UxrGrabbableObject item)
        {
            if (item == null) return false;
            if (LooseItems.TryGetKind(item, out _)) return true;
            return item.GetComponentInParent<Arsenal.WeaponComponent>() != null;
        }

        /// <summary>
        /// Забирает снаряжение у всех игроков сервера и убирает ничьё с пола.
        /// Зовут <c>MapReferee</c> при смене режима и <c>Series</c> перед сменой карты.
        /// </summary>
        public static void ServerStripAll(string reason)
        {
            ServerStripAllRequested?.Invoke(reason);

            if (!NetworkServer.active) return;

            int total = 0;
            foreach (PlayerLoadoutManager loadout in new List<PlayerLoadoutManager>(PlayerLoadoutManager.ServerInstances))
            {
                if (loadout == null) continue;
                PlayerController player = loadout.GetComponent<PlayerController>();
                if (player != null) total += ServerStrip(player, reason);
            }

            int floor = LooseItems.RemoveAll();
            GameLog.Player.Info($"[EquipmentStrip] {reason}: у игроков забрано предметов {total}, с пола убрано {floor}.");
        }

        /// <summary>
        /// Забирает снаряжение у одного аватара: руки, кобуры, карман.
        /// </summary>
        /// <returns>Сколько предметов уничтожено (руки и кобуры; магазины кармана не считаются).</returns>
        public static int ServerStrip(PlayerController avatar, string reason)
        {
            if (avatar == null || !UxrGrabManager.HasInstance) return 0;

            var doomed = new List<GameObject>();

            // Руки: отпустить всё, уничтожить снаряжение.
            foreach (UxrGrabber grabber in avatar.GetComponentsInChildren<UxrGrabber>(true))
            {
                UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
                if (held == null) continue;

                UxrGrabManager.Instance.ReleaseObject(grabber, held, true);
                if (IsEquipment(held)) AddRoot(held, avatar, doomed);
            }

            // Кобуры и прочие якоря-карманы аватара.
            foreach (UxrGrabbableObjectAnchor anchor in avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                if (!AnchorRole.IsAvatarPocket(anchor)) continue;

                UxrGrabbableObject placed = anchor.CurrentPlacedObject;
                if (placed == null || !IsEquipment(placed)) continue;

                UxrGrabManager.Instance.RemoveObjectFromAnchor(placed, true, true);
                AddRoot(placed, avatar, doomed);
            }

            foreach (GameObject item in doomed) Destroy(item);

            // Карман магазинов — у менеджера снаряжения (SyncList netId).
            PlayerLoadoutManager loadout = avatar.GetComponent<PlayerLoadoutManager>();
            if (loadout != null && NetworkServer.active) loadout.ServerClearMagazines();

            if (doomed.Count > 0)
                GameLog.Player.Info($"[EquipmentStrip] {avatar.name}: {reason} — уничтожено предметов {doomed.Count}.", avatar);

            return doomed.Count;
        }

        /// <summary>
        /// Корень предмета — его сетевой объект; часть оружия (затвор) уничтожается вместе
        /// со всем оружием. Сам аватар корнем не бывает.
        /// </summary>
        private static void AddRoot(UxrGrabbableObject item, PlayerController avatar, List<GameObject> doomed)
        {
            Arsenal.WeaponComponent weapon = item.GetComponentInParent<Arsenal.WeaponComponent>();
            NetworkIdentity identity = item.GetComponentInParent<NetworkIdentity>();

            GameObject root = weapon != null ? weapon.gameObject
                : identity != null && identity.gameObject != avatar.gameObject ? identity.gameObject
                : item.gameObject;

            if (root == avatar.gameObject) return;

            if (!doomed.Contains(root)) doomed.Add(root);
        }

        private static void Destroy(GameObject item)
        {
            if (item == null) return;

            NetworkIdentity identity = item.GetComponent<NetworkIdentity>();
            if (identity != null && identity.netId != 0 && NetworkServer.active)
                NetworkServer.Destroy(item);
            else if (Application.isPlaying)
                UnityEngine.Object.Destroy(item);
            else
                UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
