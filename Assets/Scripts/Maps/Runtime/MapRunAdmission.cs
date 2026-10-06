using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>
    /// Производный допуск gameplay текущего запуска карты. Собственного состояния готовности нет:
    /// ответ вычисляется из <see cref="MapBootstrap.IsServerReady"/>, а тот — из descriptor
    /// <see cref="MapRunAuthority"/>. Сцена без MapRoot (стенд, Offline) не управляется bootstrap
    /// и открыта, как раньше.
    ///
    /// <para>
    /// Единственное, что хранится, — отложенные запросы создания аватара: по одному на сессию,
    /// до server Ready. Отказ или снятие запуска их отбрасывает; после смены карты аватар снова
    /// запрашивает <c>GameNetworkManager.OnServerReady</c>.
    /// </para>
    /// </summary>
    public static class MapRunAdmission
    {
        private static readonly Dictionary<PlayerSession, Action> PendingAvatars = new Dictionary<PlayerSession, Action>();

        /// <summary>Сервер: можно ли выдавать оружие и запускать механики карты этой сцены.</summary>
        public static bool CanActivateMapGameplay(Scene scene)
        {
            MapBootstrap bootstrap = MapBootstrap.ForScene(scene);
            return bootstrap == null || bootstrap.IsServerReady;
        }

        /// <summary>
        /// Сервер: открыт ли gameplay активной карты. Для писателей вне сцены карты (аватары, карманы
        /// игроков живут в DontDestroyOnLoad): после Closing они не выдают предметов, которые
        /// серия уже изъяла перед загрузкой.
        /// </summary>
        public static bool CanActivateActiveMap => CanActivateMapGameplay(SceneManager.GetActiveScene());

        /// <summary>Сервер: можно ли сейчас создать аватар на активной карте.</summary>
        public static bool CanCreateAvatar => CanActivateActiveMap;

        /// <summary>
        /// Допуск создания аватара сессии. true — создавать сейчас. false — запрос отложен до server Ready;
        /// у сессии хранится только первый отложенный запрос, его делегат читает состояние сессии при выполнении.
        /// </summary>
        public static bool TryAdmitAvatar(PlayerSession session, Action create)
        {
            if (CanCreateAvatar) return true;
            if (session == null || create == null) return false;
            if (!PendingAvatars.ContainsKey(session))
            {
                PendingAvatars.Add(session, create);
                GameLog.Player.Info($"[MapRunAdmission] Аватар {session.PlayerName} ждёт готовности карты.");
            }
            return false;
        }

        /// <summary>Сервер Ready: создать отложенные аватары.</summary>
        internal static void DrainAvatars()
        {
            if (PendingAvatars.Count == 0) return;
            var pending = new List<KeyValuePair<PlayerSession, Action>>(PendingAvatars);
            PendingAvatars.Clear();
            foreach (KeyValuePair<PlayerSession, Action> request in pending)
            {
                // Сессия ушла (отключение) — Unity-null.
                if (request.Key == null) continue;
                try { request.Value(); }
                catch (Exception error) { GameLog.Error("[MapRunAdmission] Отложенный аватар не создан: " + error); }
            }
        }

        /// <summary>Запуск снят или отказал: отложенные запросы старой карты не выполняются.</summary>
        internal static void DiscardAvatars(string reason)
        {
            if (PendingAvatars.Count == 0) return;
            GameLog.Player.Verbose($"[MapRunAdmission] Отложенные аватары ({PendingAvatars.Count}) сняты: {reason}.");
            PendingAvatars.Clear();
        }
    }
}
