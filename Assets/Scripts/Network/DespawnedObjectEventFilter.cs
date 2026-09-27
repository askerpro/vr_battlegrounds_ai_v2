using Mirror;
using UltimateXR.Core.StateSync;
using UnityEngine;

namespace VrBattlegrounds.Network
{
    /// <summary>
    ///     Отсекает исходящие события канала состояния от сетевого объекта, который Mirror уже
    ///     снял со спавна, но Unity ещё не уничтожила.
    ///
    ///     <para>
    ///     Часть компонентов UltimateXR синхронизирует себя из <c>OnDisable</c> — например,
    ///     <c>UxrTeleportLocomotion</c> зовёт <c>UpdateTeleportState(false, false, false, identity)</c>.
    ///     Mirror снимает объект сразу (убирает из <c>spawned</c>, зовёт <c>OnStopClient</c>), а
    ///     <c>GameObject.Destroy</c> отложен до конца кадра, и <c>OnDisable</c> срабатывает уже после.
    ///     Событие уходит другой стороне, где объекта больше нет:
    ///     <c>UxrComponentNotFoundException … Sender: Player_… /…/TeleportLeft</c>. Так бывает при
    ///     каждой смене скина — и у своего аватара, и у копии чужого.
    ///     </para>
    ///
    ///     <para>
    ///     Признак «снят»: у <see cref="NetworkIdentity" /> уже есть <c>netId</c>, но его нет в
    ///     <c>spawned</c> этой стороны (сервера, если он поднят, иначе клиента). До спавна
    ///     <c>netId</c> равен 0 — такие события не трогаем, их придерживает
    ///     <see cref="AvatarStateEventGate" /> (NET-26). На клиенте объект попадает в
    ///     <c>spawned</c> раньше, чем получает <c>netId</c>, так что окна между ними нет.
    ///     </para>
    /// </summary>
    public static class DespawnedObjectEventFilter
    {
        /// <returns><c>true</c>, если объект события уже снят сетью и отправлять нечего.</returns>
        public static bool ShouldDrop(IUxrStateSync component)
        {
            Component target = component?.Component;
            if (target == null) return false;

            NetworkIdentity identity = target.GetComponentInParent<NetworkIdentity>(true);
            if (identity == null || identity.netId == 0) return false;

            return NetworkServer.active
                ? !NetworkServer.spawned.ContainsKey(identity.netId)
                : !NetworkClient.spawned.ContainsKey(identity.netId);
        }
    }
}
