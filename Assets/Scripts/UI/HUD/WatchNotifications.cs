using System;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Единая точка входа нотификаций наручных часов (T-46). Любой менеджер или виджет этой машины
    /// говорит игроку через <see cref="Post(WatchNotification)"/> — часы прервут статус, покажут текст,
    /// сыграют звук и дадут вибрацию контроллеру руки с часами.
    ///
    /// <para>
    /// <b>Только локально.</b> Сети здесь нет: нотификация — реакция этой машины на событие, которое
    /// до неё уже доехало (SyncVar-хук, ClientRpc, локальное событие). Серверу, которому надо сказать
    /// что-то одному игроку, — существующий канал события этого игрока, а на клиенте — <see cref="Post(WatchNotification)"/>.
    /// На выделенном сервере часов нет, и нотификации просто истекают в очереди.
    /// </para>
    ///
    /// <para>
    /// Очередь живёт здесь, а не на часах: часы — часть аватара и пропадают при его смене (смерть → призрак,
    /// смена скина). Нотификация, поднятая без часов, дождётся их не дольше
    /// <see cref="WatchNotificationQueue.MaxWaitSeconds"/>. Очередь продвигает <see cref="WatchNotificationRunner"/>.
    /// </para>
    /// </summary>
    public static class WatchNotifications
    {
        private static readonly WatchNotificationQueue Queue = new WatchNotificationQueue();

        /// <summary>Нотификация началась (часы: звук и вибрация). Поднимается из <see cref="Tick"/>.</summary>
        public static event Action<WatchNotification> Started;

        /// <summary>Часы показывают нотификацию (иначе — статус).</summary>
        public static bool IsShowing => Queue.IsShowing;

        /// <summary>Показанная нотификация; осмысленна при <see cref="IsShowing"/>.</summary>
        public static WatchNotification Current => Queue.Current;

        /// <summary>Поднять нотификацию на часах игрока этой машины.</summary>
        public static void Post(WatchNotification notification)
        {
            if (notification.IsEmpty) return;
            GameLog.UI.Verbose($"[{nameof(WatchNotifications)}] {notification}");
            Queue.Post(notification, Time.unscaledTime);
        }

        /// <summary>Короткая форма: текст, важность, звук.</summary>
        public static void Post(string text, WatchPriority priority = WatchPriority.Normal, WatchSound sound = WatchSound.Beep,
                                float duration = WatchNotification.DefaultDuration, string key = null) =>
            Post(new WatchNotification(text, priority, sound, duration, key));

        /// <summary>Продвинуть очередь. Зовёт <see cref="WatchNotificationRunner"/> раз в кадр.</summary>
        public static void Tick(float now)
        {
            if (Queue.Tick(now, out WatchNotification started))
                Started?.Invoke(started);
        }

        /// <summary>Сбросить очередь — часы сразу к статусу (смена карты).</summary>
        public static void Clear() => Queue.Clear();

        /// <summary>
        /// Без перезагрузки домена (Enter Play Mode Options) статика переживает Play Mode — чистим на старте.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Queue.Clear();
            Started = null;
        }
    }
}
