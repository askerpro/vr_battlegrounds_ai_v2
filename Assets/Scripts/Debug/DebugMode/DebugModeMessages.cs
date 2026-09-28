using Mirror;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Клиент → сервер: режим отладки на устройстве включён (<c>enable</c>) или выключен —
    /// выдать или снять права админа этой сессии. Решает сервер (<see cref="DebugAdminPolicy"/>).
    /// </summary>
    public struct DebugAdminRequestMessage : NetworkMessage
    {
        public bool enable;
    }

    /// <summary>
    /// Клиент → сервер: перенести аватар отправителя к точке <c>targetId</c> из
    /// <see cref="DebugTeleportTargets"/>. Координат клиент не шлёт: точку сервер находит
    /// в своей сцене сам, по тому же правилу.
    /// </summary>
    public struct DebugTeleportRequestMessage : NetworkMessage
    {
        public string targetId;
    }

    /// <summary>Сервер → клиент: итог запроса режима отладки, текст — для таблички и планшета.</summary>
    public struct DebugReplyMessage : NetworkMessage
    {
        public bool ok;
        public string text;
    }
}
