using Mirror;
using UnityEditor;
using VrBattlegrounds.Core;
using VrBattlegrounds.Bots;

namespace VrBattlegrounds.Editor.DevTools
{
    /// <summary>
    ///     Боты-противники из редактора (Play Mode). Редактор-сервер или хост добавляет бота сам,
    ///     редактор-клиент просит сервер тем же запросом, что кнопка планшета (<see cref="BotNetwork" />):
    ///     нужны права админа и сервер, разрешающий отладку. Подробности — <see cref="BotDirector" />.
    /// </summary>
    internal static class BotMenu
    {
        private const string Root = "Tools/VR Battlegrounds/Debug/Bots/";

        [MenuItem(Root + "Add Bot")]
        private static void Add() => Send(add: true);

        [MenuItem(Root + "Remove All Bots")]
        private static void RemoveAll() => Send(add: false);

        [MenuItem(Root + "Add Bot", true)]
        [MenuItem(Root + "Remove All Bots", true)]
        private static bool CanSend() => EditorApplication.isPlaying && (NetworkServer.active || NetworkClient.isConnected);

        private static void Send(bool add)
        {
            if (NetworkServer.active)
            {
                if (add) BotDirector.EnsureInstance()?.AddBot();
                else BotDirector.Instance?.RemoveAll();
                return;
            }

            if (!BotNetwork.Request(add, out string reason))
            {
                EditorUtility.DisplayDialog("Bots", "Запрос не отправлен: " + reason, "OK");
            }
            else
            {
                GameLog.Debug.Info("[Bots] Запрос отправлен серверу — ответ придёт строкой статуса «Отладки» и табличкой в шлеме.");
            }
        }
    }
}
