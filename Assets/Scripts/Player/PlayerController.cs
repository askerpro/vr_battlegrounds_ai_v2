using Mirror;
using System;
using VrBattlegrounds.Managers;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Контроллер игрока. Управляет командой и состоянием (жив/мёртв).
    /// Живёт на том же GameObject, что и UxrMirrorAvatar и UxrActor.
    /// Использует физическое перемещение по арене (телепортация не используется).
    ///
    /// Команда хранится как <see cref="TeamData.teamIndex"/> (int) — синхронизируется через SyncVar.
    /// Объект <see cref="TeamData"/> получается из <see cref="TeamRegistry"/> по индексу.
    /// </summary>
    [RequireComponent(typeof(UxrActor))]
    public class PlayerController : NetworkBehaviour
    {
        /// <summary>Событие смерти игрока.</summary>
        public event Action<PlayerController> PlayerDied;

        // ── Сетевые данные ────────────────────────────────────────────────────



        /// <summary>
        /// Сетевой ID сессии, к которой привязан этот аватар
        /// </summary>
        [SyncVar(hook = nameof(OnSessionNetIdChanged))]
        public uint SessionNetId;

        /// <summary>Разрешённая сессия. Кэш, источник правды — <see cref="SessionNetId"/>.</summary>
        private PlayerSession _session;

        /// <summary>
        /// Имя игрока, полученное из сессии для синхронизации имени объекта
        /// </summary>
        [SyncVar(hook = nameof(OnAvatarPlayerNameChanged))]
        public string AvatarPlayerName;

        private void OnAvatarPlayerNameChanged(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(newName)) return;
            string postfix = netIdentity.isOwned ? " (Local)" : " (Remote)";
            string debugName = $"{newName}{postfix}";

            gameObject.name = debugName;

            var uxrAvatar = GetComponent<UltimateXR.Networking.Integrations.Net.Mirror.UxrMirrorAvatar>();
            if (uxrAvatar != null && uxrAvatar.AvatarName != debugName)
            {
                uxrAvatar.AvatarName = debugName;
            }
        }

        /// <summary>
        /// Сессия, к которой привязан аватар. Ссылка кэшируется в <c>OnStartServer</c> /
        /// <c>OnStartClient</c>: через это свойство идут <see cref="Team"/> и
        /// <see cref="TeamIndex"/>, которые вызываются в циклах по всем игрокам, а поиск
        /// в словаре spawned на каждое обращение обходился недёшево.
        ///
        /// Если кэш пуст (сессия ещё не заспавнилась к моменту старта аватара —
        /// порядок доставки спавнов не гарантирован), разрешение повторяется лениво.
        /// </summary>
        public PlayerSession Session
        {
            get
            {
                if (_session == null) CacheSession();
                return _session;
            }
        }

        /// <summary>
        /// Разрешает <see cref="SessionNetId"/> в ссылку и закрывает связь с обратной стороны:
        /// сессия могла получить наш netId раньше, чем мы заспавнились, и не суметь его разрешить.
        /// </summary>
        private void CacheSession()
        {
            _session = null;
            if (SessionNetId == 0) return;

            NetworkIdentity identity = Mirror.Utils.GetSpawnedInServerOrClient(SessionNetId);
            if (identity == null) return;

            _session = identity.GetComponent<PlayerSession>();
            if (_session != null) _session.NotifyAvatarSpawned(this);
        }

        /// <summary>
        /// Проставляет сессию извне — зовётся самой сессией, когда та разрешила
        /// свой <c>ActiveAvatarNetId</c>. Избавляет аватар от повторного поиска в словаре.
        /// </summary>
        internal void LinkSession(PlayerSession session)
        {
            _session = session;
        }

        /// <summary>Сессия сменилась — кэш протух.</summary>
        private void OnSessionNetIdChanged(uint oldNetId, uint newNetId)
        {
            _session = null;
        }

        // ── Публичный API ─────────────────────────────────────────────────────

        /// <summary>Данные команды игрока. Null если команда не назначена или сессия отсутствует.</summary>
        public TeamData Team => Session != null ? Session.Team : null;

        public float Health => _actor != null ? _actor.Life : 0f;

        /// <summary>Числовой индекс команды.</summary>
        public int TeamIndex => Session != null ? Session.TeamIndex : 0;

        public bool IsAlive => !_actor.IsDead;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        public UxrActor _actor;

        /// <summary>Кто ранил с последнего возрождения — для зачёта убийства (сервер).</summary>
        private readonly DamageLedger _damageLedger = new DamageLedger();

        private void Awake()
        {
            _actor = GetComponent<UxrActor>();
            _actor.DamageReceiving += OnDamageReceiving;
            _actor.DamageReceived += OnDamageReceived;
            _actor.Died += OnActorDied;
        }

        private void Start()
        {
            // Убеждаемся что синхронизированное имя применяется после всех OnStartClient/OnStartServer и UXR инициализации
            if (!string.IsNullOrEmpty(AvatarPlayerName))
            {
                OnAvatarPlayerNameChanged("", AvatarPlayerName);
            }
        }

        private void OnDestroy()
        {
            if (_actor != null)
            {
                _actor.DamageReceiving -= OnDamageReceiving;
                _actor.DamageReceived -= OnDamageReceived;
                _actor.Died -= OnActorDied;
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            CacheSession();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            CacheSession();
        }

        /// <summary>
        /// <see cref="UxrActor" /> сообщил о смерти. Метод исполняется на <b>каждой</b>
        /// машине: смертельный урон уводит актора в приватный <c>DieInternal</c>, а его
        /// канал состояния UltimateXR переигрывает у клиентов — там и поднимается
        /// <c>UxrActor.Died</c>.
        ///
        /// <para>
        /// Поэтому локальных подписчиков (<see cref="Maps.TeamSpawnZone" />,
        /// <see cref="PlayerGrabManager" />) оповещают именно здесь, а не в
        /// <see cref="Die" />. <c>Die</c> помечен <c>[Server]</c>, и на выделенном сервере
        /// у клиента это заглушка: событие <see cref="PlayerDied" /> не доходило до клиента
        /// вовсе, и мёртвый игрок видел свою зону спавна не в момент гибели, а на ближайшей
        /// смене фазы раунда (остаток находки NET-04). На хосте не воспроизводилось —
        /// там <c>Die</c> исполняется по-настоящему.
        /// </para>
        ///
        /// <para>
        /// Почему сигнал берётся из <c>Died</c>, а не из отдельного <c>SyncVar</c> с хуком.
        /// Источник правды о жизни один — <c>UxrActor.Life</c>, и едет он каналом состояния.
        /// Второй, мирроровский, канал того же факта разъезжался бы с первым по времени,
        /// и подписчик, читающий <see cref="IsAlive" /> прямо в обработчике (а зона спавна
        /// делает именно так), мог бы увидеть ещё живого игрока. Здесь же <c>Life</c>
        /// обнуляется в первой строке <c>DieInternal</c>, то есть до самого события.
        /// </para>
        /// </summary>
        private void OnActorDied(UxrActor actor)
        {
            GameLog.Player.Info($"[PlayerController] {name}: UxrActor сообщил о смерти.", this);

            PlayerDied?.Invoke(this);

            // NetworkServer.active, а не isServer: это ровно та проверка, которую
            // подставляет weaver в [Server]-метод, и она не разыменовывает netIdentity —
            // значит ведёт себя одинаково и у аватара, ещё не попавшего в сеть.
            if (NetworkServer.active)
                Die();
        }

        // ── SyncVar hooks (вызываются на всех клиентах при изменении) ─────────



        private void OnIsAliveChanged(bool oldValue, bool newValue)
        {
            GameLog.Player.Verbose($"[PlayerController] {name}: сетевое состояние жизни изменено {oldValue} -> {newValue}", this);
        }


        // ── Игровая логика ────────────────────────────────────────────────────

        /// <summary>
        /// Урон ещё не применён — его можно отменить. Решает активный режим
        /// (<see cref="GameMode.PlayersTakeDamage"/>): в разминке смерти нет, урон по игроку
        /// отменяется на каждой машине, включая локальные эффекты у клиента. Игрок о режиме
        /// ничего не знает — только спрашивает правило.
        /// </summary>
        private void OnDamageReceiving(object sender, UxrDamageEventArgs e)
        {
            GameMode mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode : null;
            if (mode == null || mode.PlayersTakeDamage)
            {
                // Урон проходит — запоминаем источник. Смертельный урон приходит сюда же
                // и последним, поэтому к Die источник смертельного попадания уже записан.
                if (NetworkServer.active) _damageLedger.Record(e.ActorSource);
                return;
            }

            e.Cancel();
            GameLog.Player.Verbose($"[PlayerController] {name}: урон {e.Damage:F1} отменён — режим {mode.GetType().Name} урона по игрокам не допускает.", this);
        }

        private void OnDamageReceived(object sender, UxrDamageEventArgs e)
        {
            GameLog.Player.Verbose($"[PlayerController] {name}: получен урон {e.Damage:F1} (тип: {e.DamageType}). Текущее здоровье: {_actor.Life:F1}", this);

            if (!isServer) return;
        }

        /// <summary>
        /// Серверная половина смерти: режим наблюдателя, сброс предметов из рук
        /// на клиентах и оповещение игрового режима.
        ///
        /// <para>
        /// Локальных подписчиков отсюда больше не оповещают — <see cref="PlayerDied" />
        /// поднимает <see cref="OnActorDied" /> на каждой машине. Единственный штатный
        /// вызывающий этого метода — он же. Убить игрока из игрового кода следует
        /// уроном (<c>UxrActor.ReceiveDamage</c>), а не прямым вызовом: сам по себе
        /// <c>Die</c> здоровье не трогает, и без урона игрок остался бы «мёртвым, но живым».
        /// </para>
        /// </summary>
        [Server]
        public void Die()
        {
            GameLog.Player.Info($"[PlayerController] {name}: смерть подтверждена на сервере. Переход в режим наблюдателя.", this);

            // Trigger spectator mode on server for synchronization
            var spectator = GetComponent<SpectatorController>();
            if (spectator != null)
            {
                spectator.StartSpectating();
            }

            RpcOnDied();

            // Кто убил — по урону (DamageLedger): убийца, ассисты. Режиму и статистике серии.
            _damageLedger.Resolve(Session, out PlayerSession killer, out System.Collections.Generic.List<PlayerSession> assists);
            _damageLedger.Clear();

            if (GameplayManager.Instance != null)
                GameplayManager.Instance.OnPlayerDied(this, killer, assists);
        }

        /// <summary>
        /// Возрождает игрока: восстанавливает здоровье и выводит из режима наблюдателя.
        ///
        /// <para>
        /// <b>Никого не двигает.</b> Игрок физически стоит в зале, его место в арене задано
        /// калибровкой; перенос аватара расклеил бы картинку с телом. Раньше метод принимал
        /// точку спавна и рассылал перемещение — теперь точки нет вовсе. Где возрождаться,
        /// решает режим условием (Elimination — игрок сам пришёл в зону своей команды),
        /// а бывает ли респавн вообще — тоже режим.
        /// </para>
        /// </summary>
        [Server]
        public void Respawn()
        {
            GameLog.Player.Info($"[PlayerController] {name}: респаун на месте ({transform.position}).", this);
            _actor.Life = 100f;
            _damageLedger.Clear();

            var spectator = GetComponent<SpectatorController>();
            if (spectator != null)
            {
                spectator.EndSpectating();
            }
        }

        /// <summary>
        /// <b>Только для разработки</b>: переносит аватар в точку. Игровая логика на него
        /// не опирается (ни спавн, ни респавн, ни выбор команды): в арене игрок ходит сам.
        /// Нужен сценариям яруса C, которые разыгрывают «игрок дошёл до места» вне арены.
        /// Переезд делает владелец — <c>NetworkTransform</c> аватара едет от клиента.
        /// </summary>
        [Server]
        public void ServerDevTeleport(Vector3 position, Quaternion rotation)
        {
            GameLog.Debug.Info($"[PlayerController] {name}: отладочный перенос в {position}.", this);
            RpcDevTeleport(position, rotation);
        }

        /// <summary>
        /// Страховка на случай, если канал состояния UltimateXR молчит: предметы
        /// из рук обязаны выпасть у всех.
        ///
        /// С исправлением остатка NET-04 это дублирование: <see cref="PlayerGrabManager" />
        /// подписан на <see cref="PlayerDied" />, а тот теперь поднимается на каждой машине
        /// из <see cref="OnActorDied" />. Оставлено намеренно — каналы независимы,
        /// и повторный сброс предметов безвреден, тогда как их пропажа из-за молчащего
        /// канала состояния видна игроку сразу.
        /// </summary>
        [ClientRpc]
        private void RpcOnDied()
        {
            // Отпускание синхронизируемое, а RPC исполняется на каждом клиенте вне повтора события:
            // каждый рассылал своё отпускание своей скоростью броска. Страхует только владелец —
            // один раз; остальным отпускание придёт его событием (known-issues, Issue 23).
            if (!isOwned) return;

            var grabManager = GetComponent<PlayerGrabManager>();
            if (grabManager != null)
            {
                grabManager.ReleaseAllGrabbedObjects();
            }
        }

        [Server]
        public void RestoreHealth(float health)
        {
            if (_actor != null)
            {
                _actor.Life = health;
            }
        }

        [ClientRpc]
        private void RpcDevTeleport(Vector3 position, Quaternion rotation)
        {
            UltimateXR.Avatar.UxrAvatar avatar = GetComponent<UltimateXR.Avatar.UxrAvatar>();
            if (avatar != null && UltimateXR.Core.UxrManager.Instance != null)
            {
                UltimateXR.Core.UxrManager.Instance.MoveAvatarTo(avatar, position, rotation * Vector3.forward);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }
        }
    }
}

