using Mirror;
using System;
using VrBattlegrounds.Managers;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player.Avatars;

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

        /// <summary>
        /// Попадание по игроку на этой машине — после решения режима, пропускать ли урон
        /// (<c>UxrDamageEventArgs.IsCanceled</c>). Пули симулирует каждая машина, поэтому событие есть
        /// и у клиента. Для отладки и эффектов, не для правил игры (урон считает сервер).
        /// </summary>
        public static event Action<PlayerController, UxrDamageEventArgs> HitReceivedLocal;

        // ── Сетевые данные ────────────────────────────────────────────────────



        /// <summary>
        /// Сетевой ID сессии, к которой привязан этот аватар
        /// </summary>
        [SyncVar(hook = nameof(OnSessionNetIdChanged))]
        public uint SessionNetId;

        /// <summary>
        /// Префаб, из которого сервер создал это тело (<c>AvatarManager</c>). Только сервер: по нему
        /// видно, соответствует ли тело состоянию игрока (<see cref="AvatarManager.ServerReconcileBody"/>).
        /// </summary>
        [System.NonSerialized] public GameObject SourcePrefab;

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

        /// <summary>
        /// Жив ли игрок этого аватара. Выбывание — на сессии (<see cref="PlayerSession.IsEliminated"/>,
        /// переживает смену аватара); тело может погибнуть раньше, чем сессия об этом узнает: у клиента
        /// <c>UxrActor.Died</c> приходит каналом состояния, а <c>SyncVar</c> сессии — позже, и
        /// подписчики <see cref="PlayerDied"/> читают жизнь прямо в обработчике. Поэтому мёртв —
        /// если так говорит любой из двух.
        /// </summary>
        public bool IsAlive => !_actor.IsDead && (Session == null || !Session.IsEliminated);

        /// <summary>
        /// Выбывание без смерти: выживший в конце боя, новый аватар посреди матча (после смены
        /// карты, первый вход), новый аватар взамен выбывшего. Живым игрок бывает только в закупке
        /// и бою, оживают все одинаково — на своей базе в подготовке (<see cref="Respawn"/>).
        ///
        /// <para>
        /// <b>Это не смерть для игры.</b> Жизнь обнуляется свойством, мимо урона: <c>UxrActor.Died</c>
        /// не поднимается, значит нет <see cref="Die"/>, записи в статистику серии, ленты убийств
        /// и «Вы погибли» на HUD. Режим наблюдателя включается здесь же. Оружие забирает режим
        /// (<c>EquipmentStrip</c>) — его раунд не переживает ни у кого.
        /// </para>
        /// </summary>
        /// <param name="reason">Для лога: «бой кончился», «новый аватар в матче».</param>
        [Server]
        public void ServerEliminateSilently(string reason)
        {
            // Тело уже мёртвое и выбывание записано — делать нечего. Новый аватар выбывшего
            // (сессия уже выбыла, тело живое) доводится до согласованного состояния.
            if (_actor.IsDead && (Session == null || Session.IsEliminated)) return;

            _actor.Life = 0f;
            if (Session != null) Session.ServerSetEliminated(true);
            Ledger.Clear();

            GameLog.Player.Info($"[PlayerController] {name}: выбыл до возвращения на базу — {reason} (без записи в статистику).", this);

            ReconcileBody(reason);
        }

        /// <summary>
        /// Тело — представление состояния игрока: выбывший становится призраком, возрождённый —
        /// аватаром своей команды (T-35). Когда менять, решает <see cref="AvatarManager"/>: с верхнего
        /// уровня — сразу (этот аватар после вызова может быть уже заменён — дальше работать со
        /// <see cref="PlayerSession.ActiveAvatar"/>), изнутри синхронизации UltimateXR (смерть) —
        /// в <c>LateUpdate</c> этого кадра. Без <c>AvatarManager</c> (тест) — ничего: тело остаётся тем же.
        /// </summary>
        private void ReconcileBody(string reason)
        {
            if (Session != null && AvatarManager.Instance != null)
                AvatarManager.Instance.ServerReconcileBody(Session, reason);
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        public UxrActor _actor;

        /// <summary>
        /// Кто ранил с последнего возрождения — для зачёта убийства (сервер). Живёт на сессии
        /// и переживает смену аватара; аватар без сессии (тест, кукла до связи) ведёт свой.
        /// </summary>
        private DamageLedger Ledger => Session != null ? Session.DamageLedger : _detachedLedger;

        private readonly DamageLedger _detachedLedger = new DamageLedger();

        /// <summary>Толчок последнего прошедшего урона (сервер) — с ним падает труп.</summary>
        private DeathImpact _lastImpact = DeathImpact.None;

        private int _lastImpactFrame = -1;

        /// <summary>
        /// Строка «[Hit]», копящаяся до конца кадра (сервер): дробины одного выстрела попадают
        /// в одном кадре от одного стрелка и пишутся одной строкой с их числом. Только для лога.
        /// </summary>
        private struct PendingHitLog
        {
            public int Frame;
            public int Count;
            public UxrActor Source;
            public string Attacker;
            public string Weapon;
            public string Parts;
            public int PartCount;
            public float Damage;
            public float HealthAfter;
            public float Distance;
            public UxrDamageType DamageType;
        }

        private PendingHitLog _pendingHit;

        /// <summary>Кто и чем ранил последним (сервер, для строки смерти). Сброс — в <see cref="Die"/> и <see cref="Respawn"/>.</summary>
        private string _lastHitAttacker;
        private string _lastHitWeapon;

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

        private void LateUpdate()
        {
            if (_pendingHit.Count > 0) FlushHitLog();
        }

        private void OnDestroy()
        {
            if (_pendingHit.Count > 0) FlushHitLog();

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
        /// <c>GhostViewEffect</c>) оповещают именно здесь, а не в
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
        ///
        /// <para>
        /// На сервере обработчик исполняется внутри <c>BeginSync</c> … <c>EndSync</c> смерти: тело здесь
        /// не снимается (иначе событие смерти не ушло бы клиентам) — смену тела <see cref="Die"/> отдаёт
        /// <see cref="AvatarManager"/>, а тот откладывает её до верхнего уровня.
        /// </para>
        /// </summary>
        private void OnActorDied(UxrActor actor)
        {
            GameLog.Player.Info($"[PlayerController] {name}: UxrActor сообщил о смерти.", this);

            // До отпускания: снаряжение ещё в руках и кобурах — ему назначается отлёт от тела (T-35).
            DeathDropEjection.ScheduleFor(this);

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
            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            if (mode == null || mode.PlayersTakeDamage)
            {
                // Урон проходит — запоминаем источник. Смертельный урон приходит сюда же
                // и последним, поэтому к Die источник смертельного попадания уже записан.
                if (NetworkServer.active)
                {
                    Ledger.Record(e.ActorSource);
                    // Последний прошедший урон перед Died — смертельный: его толчком падает труп.
                    // Попадания одного кадра (дробины одного выстрела) складываются.
                    // Центр тела — таз, не корень: при физической ходьбе корень стоит в стороне от тела.
                    DeathImpact impact = DeathImpact.From(e, PlayerBodyCenter.Of(this));
                    _lastImpact = Time.frameCount == _lastImpactFrame ? DeathImpact.Combine(_lastImpact, impact) : impact;
                    _lastImpactFrame = Time.frameCount;
                    QueueHitLog(e);
                }
                HitReceivedLocal?.Invoke(this, e);
                return;
            }

            e.Cancel();
            GameLog.Player.Verbose($"[PlayerController] {name}: урон {e.Damage:F1} отменён — режим {mode.GetType().Name} урона по игрокам не допускает.", this);
            HitReceivedLocal?.Invoke(this, e);
        }

        private void OnDamageReceived(object sender, UxrDamageEventArgs e)
        {
            GameLog.Player.Verbose($"[PlayerController] {name}: получен урон {e.Damage:F1} (тип: {e.DamageType}). Текущее здоровье: {_actor.Life:F1}", this);

            if (!isServer) return;
        }

        /// <summary>
        /// Серверная половина смерти: выбывание, оповещение игрового режима, труп и смена тела
        /// на призрака.
        ///
        /// <para>
        /// Исполняется внутри синхронизации смерти (<c>UxrActor.DieInternal</c>). Смену тела
        /// (<see cref="ReconcileBody"/>) <see cref="AvatarManager"/> откладывает до <c>LateUpdate</c>
        /// этого кадра — после <c>EndSync</c>. Тогда же, на верхнем уровне, сервер отпускает руки
        /// и роняет снаряжение (<see cref="AvatarTeardown.ReleaseBeforeDestroy"/>).
        /// </para>
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
            // Смертельное попадание этого кадра — строкой «[Hit]» раньше строк смерти.
            if (_pendingHit.Count > 0) FlushHitLog();

            GameLog.Player.Info($"[PlayerController] {name}: смерть подтверждена на сервере. Переход в режим наблюдателя.", this);

            if (Session != null) Session.ServerSetEliminated(true);

            // Кто убил — по урону (DamageLedger): убийца, ассисты. Режиму и статистике серии.
            Ledger.Resolve(Session, out PlayerSession killer, out System.Collections.Generic.List<PlayerSession> assists);
            Ledger.Clear();

            if (MapReferee.Instance != null)
                MapReferee.Instance.OnPlayerDied(this, killer, assists);

            // Тело падает трупом у каждого клиента — до смены на призрака: RPC уходит раньше
            // сообщения об уничтожении, и клиент снимает позу с живого ещё аватара.
            LogDeathPose(_lastImpact, killer, assists);
            ServerBecomeCorpse(_lastImpact);
            _lastImpact = DeathImpact.None;
            _lastHitAttacker = null;
            _lastHitWeapon = null;

            // Последним: режим и статистика смотрели на погибшее тело. Мы внутри синхронизации
            // смерти — AvatarManager не меняет тело сейчас, а ставит запрос до LateUpdate.
            ReconcileBody("смерть");
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
            if (Session != null) Session.ServerSetEliminated(false);
            Ledger.Clear();
            _lastHitAttacker = null;
            _lastHitWeapon = null;

            ReconcileBody("возрождение");
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
            if (Session != null)
                Session.GetComponent<WallPass.WallPassMonitor>()?.ResetForServerTeleport(position);
            RpcDevTeleport(position, rotation);
        }

        /// <summary>
        /// Тело погибшего падает трупом (T-35) — на каждом клиенте свой локальный рэгдолл от одного
        /// толчка. Труп — эффект: на игру не влияет, поздний клиент его не видит.
        /// <para>
        /// <b>Хост — сам и сразу.</b> Тело уничтожается в этом же кадре. Удалённому клиенту RPC и
        /// уничтожение приходят одним пакетом по порядку, а свой клиент хоста разбирает очередь
        /// только на следующем кадре — тела уже нет, RPC теряется (<c>HostDeathEffectsTests</c>).
        /// </para>
        /// </summary>
        [Server]
        private void ServerBecomeCorpse(DeathImpact impact)
        {
            if (isClient) SpawnCorpse(impact);
            RpcBecomeCorpse(impact.Point, impact.Impulse);
        }

        /// <summary>
        /// Лог сервера в момент смерти — одна строка: где корень, голова и тело (при физической ходьбе
        /// корень стоит в стороне) и с каким толчком падает труп. С ним сверяются логи трупа на клиентах.
        /// Кто убил — по <see cref="DamageLedger"/>; чем — по последнему попаданию (<see cref="QueueHitLog"/>).
        /// </summary>
        private void LogDeathPose(DeathImpact impact, PlayerSession killer,
                                  System.Collections.Generic.List<PlayerSession> assists)
        {
            if (!GameLog.Player.IsEnabled(LogLevel.Info)) return;

            // Убийцы нет — урон без источника (мир), от не-игрока или от самого себя: показываем последний источник.
            string killerText = killer != null
                ? PlayerLogName.Of(killer)
                : $"нет (последний источник: {_lastHitAttacker ?? "неизвестен"})";
            string assistText = "нет";
            if (assists != null && assists.Count > 0)
            {
                var names = new string[assists.Count];
                for (int i = 0; i < assists.Count; i++) names[i] = PlayerLogName.Of(assists[i]);
                assistText = string.Join(", ", names);
            }

            Transform hips = PlayerBodyCenter.HipsOf(this);
            string head = PlayerBodyCenter.TryGetHead(this, out Vector3 headPosition) ? headPosition.ToString("F2") : "нет";
            string hipsText = hips != null ? hips.position.ToString("F2") : $"нет (центр тела {PlayerBodyCenter.Of(this):F2})";
            string push = impact.HasImpulse
                ? $"точка удара={impact.Point:F2}, импульс={impact.Impulse:F1} ({impact.Impulse.magnitude:F1})"
                : "без толчка";
            string damage = impact.DamageType.HasValue ? impact.DamageType.Value.ToString() : "неизвестен";

            GameLog.Player.Info($"[Corpse] {name}: смерть — player={PlayerLogName.Of(this)}, killer={killerText}, " +
                                $"weapon={_lastHitWeapon ?? "неизвестно"}, assists={assistText}, " +
                                $"root={transform.position:F2}, head={head}, hips={hipsText}, " +
                                $"{push}, тип урона {damage}.", this);
        }

        // ── Лог попаданий (сервер) ───────────────────────────────────────────

        /// <summary>Сколько разных частей тела перечислять в строке дробового попадания.</summary>
        private const int MaxLoggedHitParts = 3;

        /// <summary>
        /// Копит попадание в строку «[Hit]» (сервер, урон прошёл правило режима). Попадания одного кадра
        /// от одного источника — дробины выстрела — складываются; строка уходит в <see cref="LateUpdate"/>,
        /// в <see cref="Die"/> или при уничтожении. Здоровье «после» — то, что останется после этого урона:
        /// SDK вычитает его сразу после события.
        /// </summary>
        private void QueueHitLog(UxrDamageEventArgs e)
        {
            if (!GameLog.Player.IsEnabled(LogLevel.Info)) return;

            if (_pendingHit.Count > 0 && (_pendingHit.Frame != Time.frameCount || _pendingHit.Source != e.ActorSource))
                FlushHitLog();

            string part = DescribeHitPart(e);
            if (_pendingHit.Count == 0)
            {
                string weapon = DescribeWeapon(e.ActorSource, e.DamageType, out Vector3? origin);
                Vector3 point = e.RaycastHit.collider != null ? e.RaycastHit.point
                    : e.DamageType == UxrDamageType.Explosive ? e.ExplosionPosition
                    : PlayerBodyCenter.Of(this);

                _pendingHit = new PendingHitLog
                {
                    Frame = Time.frameCount,
                    Source = e.ActorSource,
                    Attacker = PlayerLogName.OfSource(e.ActorSource),
                    Weapon = weapon,
                    Parts = part,
                    PartCount = 1,
                    Distance = origin.HasValue ? Vector3.Distance(origin.Value, point) : -1f,
                    DamageType = e.DamageType
                };
                _lastHitAttacker = _pendingHit.Attacker;
                _lastHitWeapon = weapon;
            }
            else if (_pendingHit.PartCount < MaxLoggedHitParts && !_pendingHit.Parts.Contains(part))
            {
                _pendingHit.Parts += "+" + part;
                _pendingHit.PartCount++;
            }

            _pendingHit.Count++;
            _pendingHit.Damage += e.Damage;
            _pendingHit.HealthAfter = Mathf.Max(0f, _actor.Life - e.Damage);
        }

        private void FlushHitLog()
        {
            PendingHitLog hit = _pendingHit;
            _pendingHit = default;
            if (hit.Count == 0) return;

            string distance = hit.Distance >= 0f
                ? hit.Distance.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                : "—";
            string pellets = hit.Count > 1 ? $" hits={hit.Count}" : string.Empty;
            GameLog.Player.Info(
                FormattableString.Invariant($"[Hit] victim={PlayerLogName.Of(this)} attacker={hit.Attacker} weapon={hit.Weapon} part={hit.Parts} ") +
                FormattableString.Invariant($"dmg={hit.Damage:F1}{pellets} hp={hit.HealthAfter:F1} dist={distance} type={hit.DamageType}"), this);
        }

        /// <summary>Часть тела по коллайдеру попадания: «Head(имя коллайдера)»; не хитбокс — имя коллайдера.</summary>
        private static string DescribeHitPart(UxrDamageEventArgs e)
        {
            Collider collider = e.RaycastHit.collider;
            if (collider == null) return e.DamageType == UxrDamageType.Explosive ? "взрыв" : "—";
            return Hitbox.TryGetPart(collider, out HitZone zone) ? $"{zone}({collider.name})" : collider.name;
        }

        /// <summary>
        /// Чем нанесён урон: огнестрел в руках источника (имя объекта без «(Clone)»), откуда считать дистанцию —
        /// от оружия, иначе от тела источника. Оружия в руках нет (бот, урон без источника) — тип урона.
        /// Пуля летит, поэтому оружие — то, что в руках в момент попадания: для лога этого достаточно.
        /// </summary>
        private static string DescribeWeapon(UxrActor source, UxrDamageType type, out Vector3? origin)
        {
            origin = null;
            if (source == null) return type.ToString();

            PlayerController shooter = source.GetComponent<PlayerController>();
            origin = shooter != null ? PlayerBodyCenter.Of(shooter) : source.transform.position;

            UltimateXR.Avatar.UxrAvatar avatar = source.GetComponent<UltimateXR.Avatar.UxrAvatar>();
            if (avatar == null || !UltimateXR.Manipulation.UxrGrabManager.HasInstance) return type.ToString();

            UltimateXR.Manipulation.UxrGrabManager grab = UltimateXR.Manipulation.UxrGrabManager.Instance;
            UltimateXR.Core.UxrHandSide[] sides = { UltimateXR.Core.UxrHandSide.Right, UltimateXR.Core.UxrHandSide.Left };
            foreach (UltimateXR.Core.UxrHandSide side in sides)
            {
                if (!grab.GetObjectBeingGrabbed(avatar, side, out UltimateXR.Manipulation.UxrGrabbableObject grabbable) ||
                    grabbable == null) continue;

                UxrFirearmWeapon firearm = grabbable.GetComponentInParent<UxrFirearmWeapon>();
                if (firearm == null) continue;

                origin = firearm.transform.position;
                return firearm.name.Replace("(Clone)", string.Empty).Trim();
            }

            return type.ToString();
        }

        [ClientRpc]
        private void RpcBecomeCorpse(Vector3 point, Vector3 impulse)
        {
            if (isServer) return; // хост уже уронил сам
            SpawnCorpse(new DeathImpact(point, impulse));
        }

        private void SpawnCorpse(DeathImpact impact)
        {
            var source = GetComponent<CorpseSource>();
            if (source != null) source.Spawn(impact);
        }

        /// <summary>
        /// Владелец отпускает локальные предметы — не сетевые, о которых сервер не знает (планшет,
        /// <c>LocalMenuManager</c>), — перед тем как сервер уничтожит этот аватар. Иначе рука
        /// погибла бы с захватом (Issue 17), а сервер отпустить его не может. Зовёт
        /// <see cref="AvatarTeardown"/>.
        /// <para>
        /// Хост отпускает своё сам и сразу — RPC своему клиенту пришёл бы после уничтожения
        /// аватара (см. <see cref="ServerBecomeCorpse"/>).
        /// </para>
        /// </summary>
        [Server]
        internal void ServerReleaseLocalItems()
        {
            if (isClient) ReleaseLocalItemsIfOwned();
            RpcReleaseLocalItems();
        }

        [ClientRpc]
        private void RpcReleaseLocalItems()
        {
            if (isServer) return; // хост уже отпустил сам
            ReleaseLocalItemsIfOwned();
        }

        private void ReleaseLocalItemsIfOwned()
        {
            if (!isOwned) return;

            var grabManager = GetComponent<PlayerGrabManager>();
            if (grabManager != null) grabManager.ReleaseLocalOnlyItems();
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
            // MoveAvatarTo синхронизируемый (BeginSync): переносит только автор аватара — владелец,
            // у куклы без владельца сервер. Остальные получат переезд его событием и NetworkTransform;
            // перенос на каждой машине рассылал бы копии (known-issues, Issue 23).
            if (!VrBattlegrounds.Network.StateEventAuthority.IsAuthor(isOwned, connectionToClient != null, isServer)) return;

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

