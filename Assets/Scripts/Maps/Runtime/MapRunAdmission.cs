using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>
    /// Производный допуск gameplay текущего запуска карты. Собственного состояния готовности нет:
    /// серверный ответ вычисляется из <see cref="MapBootstrap.IsServerReady"/>, а тот — из descriptor
    /// <see cref="MapRunAuthority"/>; клиентский <see cref="IsLocalPlayable"/> — из принятого клиентом run и
    /// открытого канала начального снимка <see cref="NetworkStateRelay"/>. Сцена без MapRoot (стенд, Offline)
    /// не управляется bootstrap и открыта.
    ///
    /// <para>
    /// Единственное, что хранится, — отложенные запросы создания аватара: по одному на сессию,
    /// до server Ready. Отказ или снятие запуска их отбрасывает; после смены карты аватар снова
    /// запрашивает <c>GameNetworkManager.OnServerReady</c>.
    /// </para>
    ///
    /// <para>
    /// Выдача предметов карты (оружие стены, магазины склада и кармана, покупка бота) идёт только через
    /// <see cref="CreateMapItem"/>/<see cref="CreateActiveMapItem"/>: создание экземпляра и проверка допуска —
    /// одна операция, обойти проверку новым вызывающим нельзя. Проверка — <c>MapRunAdmissionTests</c>.
    /// </para>
    /// </summary>
    public static class MapRunAdmission
    {
        private static readonly Dictionary<PlayerSession, Action> PendingAvatars = new Dictionary<PlayerSession, Action>();

        private static int _playableFrame = -1;
        private static bool _playable;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetProcessState()
        {
            PendingAvatars.Clear();
            _playableFrame = -1;
        }

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
        /// Сервер: выдать предмет карты сцены <paramref name="scene"/>. Допуск закрыт — null, экземпляр не создаётся.
        /// Иначе — выключенный экземпляр <see cref="NetworkUxrIdentity.CreateInstance"/>, дальше как обычно:
        /// донастроить, включить, <see cref="NetworkUxrIdentity.SpawnServerObject"/>.
        /// </summary>
        public static GameObject CreateMapItem(Scene scene, GameObject prefab)
        {
            if (!CanActivateMapGameplay(scene)) return null;
            return NetworkUxrIdentity.CreateInstance(prefab);
        }

        /// <summary>Сервер: выдать предмет активной карты (писатели вне сцены карты: карман, бот).</summary>
        public static GameObject CreateActiveMapItem(GameObject prefab) => CreateMapItem(SceneManager.GetActiveScene(), prefab);

        /// <summary>
        /// Открыт ли gameplay активной карты на этой машине для локального взаимодействия (захват предметов).
        /// Сервер и host — server Ready. Удалённый клиент — принятый run локально готов и свежий начальный
        /// снимок SDK этого run применён: до снимка предметы карты описывают не то состояние, что у сервера.
        /// Без сети (стенды) — открыт. Значение кэшируется на кадр: его спрашивает правило захвата каждой точки.
        /// </summary>
        public static bool IsLocalPlayable
        {
            get
            {
                if (_playableFrame == Time.frameCount) return _playable;
                _playableFrame = Time.frameCount;
                _playable = ComputeLocalPlayable(SceneManager.GetActiveScene());
                return _playable;
            }
        }

        /// <summary>Без кэша: то же правило для произвольной сцены.</summary>
        public static bool ComputeLocalPlayable(Scene scene)
        {
            if (NetworkServer.active) return CanActivateMapGameplay(scene);
            if (!NetworkClient.active) return true;

            NetworkStateRelay relay = NetworkStateRelay.Instance;
            MapBootstrap bootstrap = MapBootstrap.ForScene(scene);
            if (bootstrap == null) return relay != null && relay.HasInitialState(default);

            MapRunKey key = bootstrap.LocalRunKey;
            return bootstrap.IsLocallyReady(key) && relay != null && relay.HasInitialState(key);
        }

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

        /// <summary>Сколько аватаров ждёт допуска (диагностика и тесты).</summary>
        public static int PendingAvatarCount => PendingAvatars.Count;

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
