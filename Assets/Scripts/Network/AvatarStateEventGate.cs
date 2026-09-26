using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core.StateSync;
using UltimateXR.Core.Unique;
using UltimateXR.Networking.Integrations.Net.Mirror;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Network
{
    /// <summary>
    ///     Придерживает исходящие события канала состояния UltimateXR, пока сетевой
    ///     аватар, к которому они относятся, не выровнял свои <c>UniqueId</c>, и отдаёт
    ///     их по сигналу <see cref="UxrMirrorAvatar.AvatarSpawned" /> (NET-26).
    ///
    ///     <para>
    ///     Mirror создаёт аватар в порядке <c>Awake</c>/<c>OnEnable</c> → <c>OnStartClient</c>,
    ///     и только в <c>OnStartClient</c> <see cref="UxrMirrorAvatar" /> зовёт
    ///     <c>CombineUniqueId(netId)</c>. Если за это окно SDK успевает породить событие —
    ///     например, <c>UxrAvatar.OnControllerInputChanged</c> при уже подключённом
    ///     контроллере, — оно сериализуется с исходными id из префаба, и другая сторона
    ///     его отвергает (<c>UxrComponentNotFoundException</c>).
    ///     </para>
    ///
    ///     <para>
    ///     Почему события откладываются, а не отбрасываются: часть из них SDK не повторяет
    ///     (<c>OnControllerInputChanged</c> — только при смене контроллера), и потеря
    ///     оставила бы другую сторону без этого состояния навсегда. Событие хранится
    ///     несериализованным: параметры держат ссылки на компоненты, и сериализация после
    ///     выравнивания запишет уже общие id.
    ///     </para>
    ///
    ///     <para>
    ///     «Уже выровнен ли аватар» определяется по <see cref="IUxrUniqueId.CombineIdSource" />:
    ///     он пуст до <c>CombineUniqueId</c>. Сигнал <c>AvatarSpawned</c> на этот вопрос не
    ///     отвечает — он говорит, когда выравнивание произошло, но не произошло ли оно раньше.
    ///     Внутри <c>InitializeNetworkAvatar</c> сеттер <c>AvatarMode</c> срабатывает до
    ///     <c>CombineUniqueId</c> — такие события признак тоже ловит.
    ///     </para>
    ///
    ///     <para>
    ///     Известное ограничение: проверяется аватар, которому принадлежит компонент-цель
    ///     события. Событие чужого объекта, ссылающееся параметром на невыровненный аватар,
    ///     не задерживается. В окне между <c>Awake</c> и <c>OnStartClient</c> таких не бывает.
    ///     </para>
    /// </summary>
    public class AvatarStateEventGate
    {
        /// <summary>
        ///     Сколько ждать выравнивания. Аватар выравнивается в том же кадре, что и спавн,
        ///     поэтому истечение означает, что инициализация сломалась, а не затянулась.
        ///     Проверяется при поступлении новых событий.
        /// </summary>
        public const float MaxWaitSeconds = 10f;

        private readonly struct Pending
        {
            public readonly IUxrStateSync Component;
            public readonly UxrSyncEventArgs EventArgs;

            public Pending(IUxrStateSync component, UxrSyncEventArgs eventArgs)
            {
                Component = component;
                EventArgs = eventArgs;
            }
        }

        /// <summary>Очередь одного невыровненного аватара и подписки на его сигналы.</summary>
        private sealed class AvatarQueue
        {
            public readonly UxrAvatar Avatar;
            public readonly UxrMirrorAvatar Mirror;
            public readonly List<Pending> Events = new List<Pending>();
            public readonly float CreatedAt;
            public Action OnSpawned;
            public Action OnDespawned;

            public AvatarQueue(UxrAvatar avatar, UxrMirrorAvatar mirror, float createdAt)
            {
                Avatar = avatar;
                Mirror = mirror;
                CreatedAt = createdAt;
            }
        }

        private readonly Action<IUxrStateSync, UxrSyncEventArgs> _send;
        private readonly List<AvatarQueue> _queues = new List<AvatarQueue>();

        /// <param name="send">Отправка события в сеть — сериализует в момент вызова.</param>
        public AvatarStateEventGate(Action<IUxrStateSync, UxrSyncEventArgs> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        /// <summary>Сколько событий сейчас придержано.</summary>
        public int PendingCount
        {
            get
            {
                int count = 0;
                foreach (AvatarQueue queue in _queues) count += queue.Events.Count;
                return count;
            }
        }

        /// <summary>
        ///     Сетевой аватар, которому принадлежит компонент и который ещё не выровнял id,
        ///     или <c>null</c>, если событие можно сериализовать сразу. Аватар без
        ///     <see cref="UxrMirrorAvatar" /> — локальный, не сетевой: его id никто не выравнивает.
        /// </summary>
        public static UxrAvatar FindUnalignedNetworkAvatar(IUxrStateSync component)
        {
            Component target = component?.Component;
            if (target == null) return null;

            UxrAvatar avatar = target.GetComponentInParent<UxrAvatar>(true);
            if (avatar == null || avatar.GetComponent<UxrMirrorAvatar>() == null) return null;

            return ((IUxrUniqueId)avatar).CombineIdSource == Guid.Empty ? avatar : null;
        }

        /// <summary>
        ///     Придерживает событие, если его аватар ещё не выровнен. Отправит его
        ///     переданная в конструктор функция — по сигналу <c>AvatarSpawned</c>.
        /// </summary>
        /// <returns><c>true</c>, если событие придержано и отправлять его сейчас нельзя.</returns>
        public bool TryDefer(IUxrStateSync component, UxrSyncEventArgs eventArgs, float now)
        {
            DropStale(now);

            UxrAvatar avatar = FindUnalignedNetworkAvatar(component);
            if (avatar == null) return false;

            AvatarQueue queue = FindQueue(avatar) ?? Watch(avatar, now);
            queue.Events.Add(new Pending(component, eventArgs));

            GameLog.Network.Verbose(
                $"[AvatarStateEventGate] Событие {eventArgs?.GetType().Name} придержано: аватар " +
                $"'{avatar.name}' ещё не выровнял UniqueId.");
            return true;
        }

        /// <summary>Забыть всё придержанное и отписаться — при остановке канала.</summary>
        public void Clear()
        {
            foreach (AvatarQueue queue in _queues) Unwatch(queue);
            _queues.Clear();
        }

        // ── Сигналы аватара ────────────────────────────────────────────────

        private AvatarQueue Watch(UxrAvatar avatar, float now)
        {
            var queue = new AvatarQueue(avatar, avatar.GetComponent<UxrMirrorAvatar>(), now);
            queue.OnSpawned = () => ReleaseQueue(queue);
            queue.OnDespawned = () => DropQueue(queue, "аватар снят со сцены до выравнивания UniqueId");

            queue.Mirror.AvatarSpawned += queue.OnSpawned;
            queue.Mirror.AvatarDespawned += queue.OnDespawned;
            _queues.Add(queue);
            return queue;
        }

        private static void Unwatch(AvatarQueue queue)
        {
            if (queue.Mirror == null) return;

            queue.Mirror.AvatarSpawned -= queue.OnSpawned;
            queue.Mirror.AvatarDespawned -= queue.OnDespawned;
        }

        /// <summary>
        ///     <c>AvatarSpawned</c> поднимается сразу после <c>CombineUniqueId</c> — id уже общие.
        ///     Очередь снимается до отправки: <c>_send</c> может породить новые события.
        /// </summary>
        private void ReleaseQueue(AvatarQueue queue)
        {
            Unwatch(queue);
            _queues.Remove(queue);

            foreach (Pending entry in queue.Events)
            {
                if (entry.Component?.Component != null)
                    _send(entry.Component, entry.EventArgs);
            }
        }

        private void DropQueue(AvatarQueue queue, string reason)
        {
            Unwatch(queue);
            _queues.Remove(queue);

            if (queue.Events.Count > 0)
            {
                GameLog.Network.Warning(
                    $"[AvatarStateEventGate] Выброшено событий: {queue.Events.Count} — {reason} " +
                    $"('{(queue.Avatar != null ? queue.Avatar.name : "уничтожен")}').");
            }
        }

        private void DropStale(float now)
        {
            for (int i = _queues.Count - 1; i >= 0; i--)
            {
                AvatarQueue queue = _queues[i];

                if (queue.Avatar == null || queue.Mirror == null)
                    DropQueue(queue, "аватар уничтожен до выравнивания UniqueId");
                else if (now - queue.CreatedAt > MaxWaitSeconds)
                    DropQueue(queue, $"аватар за {MaxWaitSeconds} с так и не выровнял UniqueId");
            }
        }

        private AvatarQueue FindQueue(UxrAvatar avatar)
        {
            foreach (AvatarQueue queue in _queues)
            {
                if (queue.Avatar == avatar) return queue;
            }

            return null;
        }
    }
}
