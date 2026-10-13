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
    ///     <c>OnDisable</c> срабатывает синхронно внутри <c>GameObject.Destroy</c> (и
    ///     <c>SetActive(false)</c>), <c>OnDestroy</c> — в конце кадра. Событие уходит другой
    ///     стороне, где объекта больше нет:
    ///     <c>UxrComponentNotFoundException … Sender: Player_… /…/TeleportLeft</c>. Так бывает при
    ///     каждой смене скина — и у своего аватара, и у копии чужого.
    ///     </para>
    ///
    ///     <para>
    ///     Порядок снятия, на который опирается фильтр. Сервер (и хост): <c>NetworkServer.Destroy</c>
    ///     убирает объект из <c>NetworkServer.spawned</c> до <c>Destroy</c>. Клиент:
    ///     <c>NetworkClient.DestroyObject</c> зовёт <c>OnStopLocalPlayer</c>/<c>OnStopClient</c>
    ///     (объект ещё в <c>spawned</c>), затем убирает его из <c>spawned</c> и <c>owned</c> и только
    ///     потом зовёт unspawn-обработчик, <c>Destroy</c> или <c>SetActive(false)</c> — это
    ///     VR Battlegrounds patch 4 Mirror (tasks/network-sync-hardening, дефект 6048). До патча клиент
    ///     убирал объект после <c>Destroy</c>, и событие своего аватара из <c>OnDisable</c> проходило.
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
