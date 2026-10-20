using System.Collections.Generic;
using System.Linq;
using Mirror;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Bots
{
    /// <summary>Пассивный снимок решения директора. MindIdentity — непрозрачная ссылка, не право менять память бота.</summary>
    public readonly struct BotDirectorEquipmentObservation
    {
        public readonly string Kind;
        public readonly PlayerSession Session;
        public readonly PlayerController Body;
        public readonly object MindIdentity;
        public readonly MapRunSnapshot Snapshot;
        public readonly int ShopToken;
        public readonly VrBattlegrounds.Arsenal.WeaponInfo Weapon;
        public readonly bool? Accepted;
        public readonly bool ContextReset, ArmBefore, ArmAfter;

        public BotDirectorEquipmentObservation(string kind, PlayerSession session, PlayerController body, object mindIdentity,
            MapRunSnapshot snapshot, int shopToken, VrBattlegrounds.Arsenal.WeaponInfo weapon, bool? accepted,
            bool contextReset, bool armBefore, bool armAfter)
        {
            Kind = kind; Session = session; Body = body; MindIdentity = mindIdentity; Snapshot = snapshot;
            ShopToken = shopToken; Weapon = weapon; Accepted = accepted; ContextReset = contextReset;
            ArmBefore = armBefore; ArmAfter = armAfter;
        }
    }

    /// <summary>
    /// Боты — игроки без шлема (T-48: игровая фича, раньше — отладка). Только сервер. Создаётся
    /// по запросу: «Матч с ботами» на «Обзоре» планшета (<see cref="BotMatchStarter"/>), кнопки
    /// «Добавить бота» экрана «Отладка» (<see cref="BotNetwork"/>), меню редактора, оркестратор отладки.
    ///
    /// <para>
    /// <b>Бот для игры — обычный игрок.</b> У него настоящая <see cref="PlayerSession"/>
    /// в <see cref="PlayersManager"/> (без соединения) и настоящий аватар без владельца
    /// (<c>AvatarManager.SpawnAvatar(null, …)</c>). Поэтому попадания, урон, смерть,
    /// конец раунда, счёт, деньги и возрождение идут тем же кодом, что у людей, — ни одной ветки
    /// «если бот» в игровой логике нет.
    /// </para>
    ///
    /// <para>
    /// <b>Директор думает, тело исполняет.</b> Каждый тик директор собирает ситуацию бота
    /// (<see cref="BotSenses"/>), спрашивает приказ у чистой <see cref="BotOrders"/> и раздаёт его:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Ноги</b> — <see cref="BotNavigator"/>: домой (своё место в зоне спавна), к врагу, стоять.</item>
    /// <item><b>Закупка</b> — <see cref="BotShopper"/>, раз за раунд, на базе: эко / пистолетный / форс / полная.</item>
    /// <item><b>Оружие</b> — <see cref="BotGunner"/>: купленное или стартовый пистолет; стреляет в бою сам.</item>
    /// <item><b>Готовность</b> — <c>ServerSetReady</c>, когда стоит на базе в закупке и покупка решена.</item>
    /// <item><b>Команда</b> — <see cref="BotTeamChoice"/> (выравнивает составы), скин — <see cref="BotSkin"/>.</item>
    /// <item><b>Тело</b> после смены карты возвращает директор (у бота нет соединения); новое тело
    /// встаёт на ноги прежнего (<see cref="BotMind"/>).</item>
    /// <item><b>Комплект</b> бот-компонентов — один раз на тело по <c>AvatarSpawned</c> (<see cref="BotKit"/>): живое —
    /// полный, призрак — пассивный; ссылки — в <see cref="BotMind.Kit"/>, тик не ищет компоненты.</item>
    /// </list>
    /// </summary>
    public sealed class BotDirector : MonoBehaviour
    {
        public static BotDirector Instance { get; private set; }

        /// <summary>Только наблюдение: без подписчиков снимки и списки вызовов не создаются.</summary>
        public static event System.Action<BotDirectorEquipmentObservation> EquipmentObserved;

        private static void ObserveEquipment(string kind, PlayerSession bot, PlayerController avatar, BotMind mind,
            MapRunSnapshot snapshot, int token, bool contextReset, bool? accepted, bool armBefore, bool armAfter)
        {
            var handlers = EquipmentObserved;
            if (handlers == null) return;
            var observation = new BotDirectorEquipmentObservation(kind, bot, avatar, mind, snapshot, token,
                mind.Purchase, accepted, contextReset, armBefore, armAfter);
            foreach (System.Action<BotDirectorEquipmentObservation> handler in handlers.GetInvocationList())
            {
                // Диагностика не меняет исход Buy/OfferPurchase и не срывает Think чужим исключением.
                try { handler(observation); }
                catch (System.Exception) { /* Ошибки собственного журнала считает наблюдатель. */ }
            }
        }

        private static readonly ProfilerMarker ThinkMarker = new ProfilerMarker("BotDirector.Think");

        private readonly List<PlayerSession> _bots = new List<PlayerSession>();
        private readonly Dictionary<PlayerSession, BotMind> _minds = new Dictionary<PlayerSession, BotMind>();
        private readonly Dictionary<PlayerController, ShotWindow> _shotWindows = new Dictionary<PlayerController, ShotWindow>();
        private int _nextNumber = 1;

        /// <summary>Боты на сервере.</summary>
        public IReadOnlyList<PlayerSession> Bots => _bots;

        /// <summary>
        /// Ограниченное окно новых выстрелов собственного тела. Стенд сохраняет оружие и цель после deadline,
        /// чтобы наблюдать хвост; директор остаётся единственным владельцем разрешения огня.
        /// </summary>
        public System.IDisposable OpenShotWindow(PlayerSession subject, float deadline)
        {
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority || subject == null ||
                !_minds.ContainsKey(subject) || subject.ActiveAvatar == null ||
                float.IsNaN(deadline) || float.IsInfinity(deadline))
                throw new System.InvalidOperationException("Окно огня требует собственного серверного тела и конечного deadline.");
            PlayerController player = subject.ActiveAvatar;
            if (_shotWindows.ContainsKey(player))
                throw new System.InvalidOperationException("У тела уже есть окно огня; сначала освободите своё окно.");
            var window = new ShotWindow(this, subject, player, deadline);
            _shotWindows.Add(player, window);
            return window;
        }

        /// <summary>Стрелок только читает намерение директора, в том числе непосредственно перед SDK attempt.</summary>
        public bool IsShotAllowed(PlayerController player)
        {
            if (player == null) return false;
            if (!_shotWindows.TryGetValue(player, out ShotWindow window)) return true;
            return window.Subject != null && ReferenceEquals(window.Subject.ActiveAvatar, player) &&
                player.gameObject.scene.handle == window.Scene && Time.time < window.Deadline;
        }

        private sealed class ShotWindow : System.IDisposable
        {
            private BotDirector _owner;
            public readonly PlayerSession Subject;
            public readonly PlayerController Player;
            public readonly float Deadline;
            public readonly int Scene;

            public ShotWindow(BotDirector owner, PlayerSession subject, PlayerController player, float deadline)
            {
                _owner = owner; Subject = subject; Player = player; Deadline = deadline;
                Scene = player.gameObject.scene.handle;
            }

            public void Dispose()
            {
                BotDirector owner = _owner; _owner = null;
                if (owner != null && owner._shotWindows.TryGetValue(Player, out ShotWindow current) &&
                    ReferenceEquals(current, this)) owner._shotWindows.Remove(Player);
            }
        }

        /// <summary>Префикс <c>DeviceToken</c> сессии бота. Токен реплицируется — по нему бота узнаёт и клиент.</summary>
        public const string TokenPrefix = "bot_";

        public static bool IsBotToken(string deviceToken) =>
            !string.IsNullOrEmpty(deviceToken) && deviceToken.StartsWith(TokenPrefix, System.StringComparison.Ordinal);

        /// <summary>Является ли сессия ботом (сервер).</summary>
        public static bool IsBot(PlayerSession session)
        {
            return Instance != null && session != null && Instance._bots.Contains(session);
        }

        /// <summary>Директор на сервере; создаётся при первом обращении. Без сервера — null.</summary>
        public static BotDirector EnsureInstance()
        {
            if (!NetworkServer.active) return null;
            if (Instance != null) return Instance;

            var go = new GameObject("BotDirector");
            DontDestroyOnLoad(go);
            return go.AddComponent<BotDirector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            _shotWindows.Clear();
            if (Instance == this) Instance = null;
        }

        private void OnEnable() => AvatarManager.AvatarSpawned += OnAvatarSpawned;

        private void OnDisable() => AvatarManager.AvatarSpawned -= OnAvatarSpawned;

        // ── Создание и удаление ─────────────────────────────────────────────

        /// <summary>Доводит число ботов до <paramref name="count"/> (только добавляет).</summary>
        public void EnsureCount(int count)
        {
            while (_bots.Count < count)
            {
                if (AddBot() == null) return;
            }
        }

        /// <summary>
        /// Добавляет бота — обычного игрока без шлема (player-flow): сессия получает калибровку роста
        /// (<see cref="BotPlayerFlowSettings.Calibration"/>) до спавна, как человек при подключении; комплект
        /// бот-компонентов тело получает по <c>AvatarSpawned</c> (<see cref="BotKit"/>). null — сервер не готов
        /// (нет менеджеров).
        /// </summary>
        public PlayerSession AddBot() => AddBot(null);

        /// <summary>Добавляет бота с настройками автоматического снаряжения; null — штатные настройки матча.</summary>
        public PlayerSession AddBot(BotAddOptions options)
        {
            PlayersManager players = PlayersManager.Instance;
            if (!NetworkServer.active || players == null || AvatarManager.Instance == null)
            {
                GameLog.Player.Warning("[BotDirector] Бот не создан: сервер не поднят или нет PlayersManager/AvatarManager.");
                return null;
            }

            int number = _nextNumber++;
            PlayerSession session = players.CreateBotSession($"Бот {number}", TokenPrefix + number, BotPlayerFlowSettings.Calibration);
            if (session == null) return null;

            _bots.Add(session);
            // Источник ввода — до SpawnBody: AvatarSpawned (сборка комплекта) приходит внутри спавна.
            _minds[session] = new BotMind { HomeSlot = _bots.Count - 1, AutoEquip = options?.AutoEquip ?? true,
                                            InputSource = options?.InputSource };

            // Команда — до тела: спавн берёт зону команды. Сами режимы команду не выдают
            // (у разминки команд нет, матч ждёт выбора) — выбираем.
            ChooseTeam(session, force: true);
            SpawnBody(session);

            GameLog.Player.Info($"[BotDirector] Добавлен {session.PlayerName}: команда {TeamName(session)}, скин {session.AvatarIndex}.");
            return session;
        }

        /// <summary>Убирает всех ботов.</summary>
        public void RemoveAll()
        {
            for (int i = _bots.Count - 1; i >= 0; i--)
            {
                Remove(_bots[i]);
            }
        }

        /// <summary>Снять конкретного серверного бота, сохраняя остальных акторов.</summary>
        public bool RemoveBot(PlayerSession session)
        {
            if (!NetworkServer.active || !VrBattlegrounds.Network.StateEventAuthority.IsWorldAuthority || session == null || !_bots.Contains(session)) return false;
            Remove(session);
            return true;
        }

        private void Remove(PlayerSession session)
        {
            _bots.Remove(session);
            if (session == null) return;
            _minds.Remove(session);

            PlayerController avatar = session.ActiveAvatar;
            if (avatar != null) _shotWindows.Remove(avatar);
            PlayersManager.Instance?.UnregisterBot(session);

            if (!NetworkServer.active) return;

            if (avatar != null)
            {
                AvatarTeardown.ReleaseBeforeDestroy(avatar, "бот убран");
                NetworkServer.Destroy(avatar.gameObject);
            }
            NetworkServer.Destroy(session.gameObject);

            GameLog.Player.Info($"[BotDirector] Убран {session.PlayerName}.");
        }

        // ── Тик ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!NetworkServer.active)
            {
                // Сервер остановлен: объекты уже убрал Mirror, из реестра — убираем сами.
                foreach (PlayerSession bot in _bots) PlayersManager.Instance?.UnregisterBot(bot);
                _bots.Clear();
                _minds.Clear();
                BotNavMesh.Clear();
                Destroy(gameObject);
                return;
            }

            if (NetworkServer.isLoadingScene) return;

            BotStage stage = BotSenses.Stage();

            for (int i = _bots.Count - 1; i >= 0; i--)
            {
                PlayerSession bot = _bots[i];
                if (bot == null)
                {
                    _bots.RemoveAt(i);
                    continue;
                }

                ChooseTeam(bot, force: false);

                if (bot.ActiveAvatar == null)
                {
                    // Смена карты уничтожила тело вместе со сценой.
                    SpawnBody(bot);
                    continue;
                }

                using (ThinkMarker.Auto())
                {
                    Think(bot, MindOf(bot), stage);
                }
            }
        }

        /// <summary>Один тик решений бота: приказ → ноги, закупка, оружие, готовность.</summary>
        private void Think(PlayerSession bot, BotMind mind, BotStage stage)
        {
            PlayerController avatar = bot.ActiveAvatar;
            // Бот на вводе устройства: решений директора нет — всё делает источник ввода (bot-input).
            if (mind.InputSource != null)
            {
                if (!avatar.TryGetComponent(out BotInputKit _)) AttachKit(mind, avatar);
                return;
            }

            BotKitRefs kit = mind.Kit;
            if (kit == null || !ReferenceEquals(kit.Avatar, avatar))
            {
                // Страховка: комплект собирается по AvatarSpawned; сюда тело приходит без него только при ошибке потока.
                if (!mind.KitFallbackWarned)
                {
                    mind.KitFallbackWarned = true;
                    GameLog.Player.Warning($"[BotDirector] {bot.PlayerName}: тело без комплекта бота (AvatarSpawned не дошёл) — комплект из тика.", avatar);
                }
                AttachKit(mind, avatar);
                kit = mind.Kit;
                if (kit == null) return;
            }
            else if (kit.Passive && avatar.IsAlive)
            {
                // Призрак ожил на месте (стратегия без призрака) — полный комплект на то же тело.
                kit = mind.Kit = BotKit.Upgrade(kit);
            }

            BotBody body = kit.Body;
            BotNavigator legs = kit.Legs;
            BotGunner gunner = kit.Gunner;
            BotCombatDriver combat = kit.Combat;
            if (body == null || legs == null || gunner == null) return;

            int scene = SceneManager.GetActiveScene().handle;
            mind.Remember(body, scene);

            bool alive = avatar.IsAlive;
            int token = BotSenses.ShopToken(avatar);
            MapRunSnapshot purchaseSnapshot = default;
            bool contextReset = false;
            bool purchaseContextReady = mind.AutoEquip && TrySyncPurchaseContext(mind, avatar, out purchaseSnapshot, out contextReset);
            if (contextReset)
                ObserveEquipment("contextReset", bot, avatar, mind, purchaseSnapshot, token, true, null, gunner.ArmRequested, gunner.ArmRequested);
            bool shopped = mind.ShoppedFor(token);
            // В бою восприятие и маршруты принадлежат Blaze, а не всеведущему NearestEnemy. Без оружия (Unarmed)
            // Blaze не создаётся — не атакует и не просит выстрел; ноги — по приказу (домой, к закупке). Пассивный
            // комплект (призрак) Blaze не имеет.
            bool fighting = stage == BotStage.Combat && alive && gunner.HasWeapon;
            if (combat != null) combat.SetCombatEnabled(fighting);

            BotOrder order = BotOrders.Decide(new BotSituation
            {
                Stage = stage,
                Alive = alive,
                InOwnZone = bot.IsInSpawnZone,
                EnemyKnown = combat != null && combat.Target != null,
                EnemyVisible = gunner.SeesTarget
            });

            if (fighting) legs.Stop();
            else switch (order)
            {
                case BotOrder.GoHome:
                    if (TryHomePoint(bot, mind, scene, out Vector3 home)) legs.GoTo(home);
                    else legs.Stop();
                    break;
                default:
                    legs.Stop();
                    break;
            }

            if (purchaseContextReady && BotOrders.ShouldShop(order, alive, shopped, free: Economy.MatchEconomy.Current == null))
            {
                mind.Purchase = BotShopper.Buy(bot);
                mind.ShopToken = token;
                shopped = true;
                ObserveEquipment("buy", bot, avatar, mind, purchaseSnapshot, token, contextReset, null, gunner.ArmRequested, gunner.ArmRequested);
            }

            if (mind.AutoEquip)
            {
                if (BotOrders.ShouldArm(stage, alive, shopped))
                {
                    // OfferPurchase тоже может вызвать Arm: исходное значение снимаем до предложения.
                    bool armBefore = gunner.ArmRequested;
                    if (purchaseContextReady && shopped && mind.Purchase != null &&
                        (!ReferenceEquals(mind.PurchaseOfferedBody, avatar) || mind.PurchaseOfferedToken != token))
                    {
                        // Даже отказ стрелка завершает предложение: не повторяем его из каждого тика.
                        mind.PurchaseOfferedBody = avatar;
                        mind.PurchaseOfferedToken = token;
                        bool accepted = gunner.OfferPurchase(mind.Purchase);
                        ObserveEquipment("offer", bot, avatar, mind, purchaseSnapshot, token, contextReset, accepted, armBefore, gunner.ArmRequested);
                    }

                    // Покупку принимает только OfferPurchase; без неё стрелок берёт штатный пистолет из кобуры.
                    gunner.Arm(null);
                    if (!armBefore && gunner.ArmRequested)
                        ObserveEquipment("armRequested", bot, avatar, mind, purchaseSnapshot, token, contextReset, null, false, true);
                }
                else if (stage == BotStage.NoMatch || stage == BotStage.BetweenRounds)
                    gunner.StandDown();
            }

            if (purchaseContextReady && BotOrders.ShouldDeclareReady(order, alive, bot.IsInSpawnZone, shopped, bot.ReadyState))
                bot.ServerSetReady(true, "бот закупился и стоит на базе");
        }

        /// <summary>Контекст закупки берётся только из опубликованного запуска и его текущего режима.</summary>
        private static bool TrySyncPurchaseContext(BotMind mind, PlayerController avatar, out MapRunSnapshot snapshot, out bool changed)
        {
            snapshot = default;
            changed = false;
            MapReferee referee = MapReferee.Instance;
            GameMode mode = referee != null ? referee.ActiveGameMode : null;
            MapRunAuthority authority = MapRunAuthority.Instance;
            if (referee == null || mode == null || authority == null) return false;

            snapshot = authority.Current;
            if (!snapshot.IsReady || !snapshot.Key.IsValid || snapshot.ModeEpoch == 0 ||
                snapshot.RefereeNetId != referee.netId || snapshot.ActiveModeNetId != mode.netId) return false;

            changed = mind.SyncPurchaseContext(snapshot.Key, snapshot.ModeEpoch, mode is EliminationMode ? null : avatar);
            return true;
        }

        // ── База ────────────────────────────────────────────────────────────

        /// <summary>
        /// Место бота на базе: точка вокруг центра зоны спавна его команды (своё у каждого бота, чтобы не стояли
        /// друг в друге). Не влезает в план зоны — центр зоны. Зона кешируется в <see cref="BotMind"/>: поиск по сцене —
        /// только при смене сцены или команды, когда кешированная зона перестала быть зоной команды (смена сторон) или
        /// исчезла; без зоны — не чаще <see cref="HomeRetrySeconds"/>.
        /// </summary>
        private static bool TryHomePoint(PlayerSession bot, BotMind mind, int scene, out Vector3 point)
        {
            point = default;
            TeamData team = bot.Team;
            if (team == null) return false;

            TeamSpawnZone zone = mind.HomeZone;
            bool keyChanged = mind.HomeScene != scene || mind.HomeTeam != team;
            if (zone == null || zone.Team != team)
            {
                if (keyChanged || Time.time >= mind.HomeRetryAt) zone = FindHomeZone(team);
                else zone = null;
                mind.HomeZone = zone;
                mind.HomeScene = scene;
                mind.HomeTeam = team;
                if (zone == null) mind.HomeRetryAt = Time.time + HomeRetrySeconds;
            }
            if (zone == null) return false;

            Vector3 center = zone.SpawnPoint.position;
            Vector3 slot = center + zone.SpawnPoint.rotation * BotHomeSlot.Offset(mind.HomeSlot);
            point = zone.ContainsInPlan(slot) ? slot : center;
            return true;
        }

        /// <summary>Повтор поиска зоны спавна, когда её не нашлось, секунды.</summary>
        private const float HomeRetrySeconds = 2f;

        private static TeamSpawnZone FindHomeZone(TeamData team)
        {
            foreach (TeamSpawnZone zone in FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.None))
            {
                if (zone != null && zone.Team == team) return zone;
            }
            return null;
        }

        // ── Команда ─────────────────────────────────────────────────────────

        /// <summary>
        /// Ставит бота в команду режима по <see cref="BotTeamChoice"/>. В разминке не
        /// вмешивается — команду даёт режим. Пока выбор команды открыт, пересчитывает
        /// каждый тик: человек пришёл в команду бота — бот уходит. После закрытия выбора
        /// трогает только бота без команды режима (добавлен посреди матча).
        /// Смена команды тело не двигает: в свою зону бот идёт сам (приказ <see cref="BotOrder.GoHome"/>).
        /// </summary>
        private void ChooseTeam(PlayerSession bot, bool force)
        {
            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            if (mode == null || mode.IsWarmup || mode.Teams == null || mode.Teams.Length == 0) return;

            // Цикл, а не Any с лямбдой: тик каждого бота в бою без аллокаций.
            bool inModeTeam = false;
            foreach (TeamData team in mode.Teams)
            {
                if (team != null && team.teamIndex == bot.TeamIndex) { inModeTeam = true; break; }
            }
            if (inModeTeam && mode.TeamChoiceLocked && !force) return;

            var humans = new List<int>();
            var otherBots = new List<int>();
            if (PlayersManager.Instance != null)
            {
                foreach (PlayerSession session in PlayersManager.Instance.Sessions)
                {
                    if (session == null || session == bot || session.Role != GameRole.Player) continue;
                    if (_bots.Contains(session)) otherBots.Add(session.TeamIndex);
                    else humans.Add(session.TeamIndex);
                }
            }

            TeamData desired = BotTeamChoice.Choose(mode.Teams, bot.TeamIndex, humans, otherBots);
            if (desired == null || desired.teamIndex == bot.TeamIndex) return;

            SessionTeamAssigner.Apply(bot, desired, BotSkin.PickHittable(desired), "BotDirector");
        }

        // ── Тело ────────────────────────────────────────────────────────────

        private BotMind MindOf(PlayerSession bot)
        {
            if (!_minds.TryGetValue(bot, out BotMind mind))
            {
                mind = new BotMind { HomeSlot = _bots.IndexOf(bot) };
                _minds[bot] = mind;
            }
            return mind;
        }

        private static void SpawnBody(PlayerSession bot)
        {
            if (AvatarManager.Instance == null) return;

            // Новое тело на новой карте — место не переносится (сцена другая, см. BotMind.SceneHandle).
            AvatarManager.Instance.SpawnAvatar(null, null, bot);
        }

        /// <summary>
        /// Тело сменилось (смерть → призрак, возрождение, скин) — в том же кадре: комплект бота и ноги прежнего
        /// тела. Иначе новое тело кадр стояло бы головой на полу у корня (на базе) и зона спавна приняла бы призрака.
        /// </summary>
        private void OnAvatarSpawned(PlayerController avatar)
        {
            if (avatar == null || !NetworkServer.active) return;

            PlayerSession bot = null;
            for (int i = 0; i < _bots.Count; i++)
            {
                if (_bots[i] != null && _bots[i].ActiveAvatar == avatar) { bot = _bots[i]; break; }
            }
            if (bot == null) return;

            AttachKit(MindOf(bot), avatar);
        }

        /// <summary>
        /// Комплект бот-компонентов — один раз на тело (<see cref="BotKit"/>). Повторный <c>AvatarSpawned</c> для того же
        /// тела (вложенная смена тела в <c>AvatarManager.ReplaceBody</c>) ничего не добавляет; пассивное тело, ожившее на
        /// месте, повышается до живого. Живое тело — полный комплект; пришедшее мёртвым (призрак выбывшего) — пассивный:
        /// драйвер ведёт его без оружия (кисти в покое), BotBody — источник ног/курса/взгляда; призрак ходит, куда велит
        /// директор, но не хватает и не стреляет. Погибшее живое тело комплект не меняет — оно не ведётся до смены на
        /// призрака. Новое тело на той же карте встаёт на ноги прежнего.
        /// </summary>
        private static void AttachKit(BotMind mind, PlayerController avatar)
        {
            if (avatar == null) return;

            if (mind.InputSource != null)
            {
                // Новое тело на той же карте продолжает с прежней позы головы (физическая голова не телепортируется).
                bool sameScene = mind.SceneHandle == SceneManager.GetActiveScene().handle;
                BotInputKit.Attach(avatar, mind.InputSource, keepHead: sameScene);
                mind.SceneHandle = SceneManager.GetActiveScene().handle;
                return;
            }

            if (mind.Kit != null && ReferenceEquals(mind.Kit.Avatar, avatar))
            {
                if (mind.Kit.Passive && avatar.IsAlive) mind.Kit = BotKit.Upgrade(mind.Kit);
                return;
            }

            mind.Kit = BotKit.Attach(avatar, avatar.IsAlive ? BotKitKind.Live : BotKitKind.Passive);

            // Тело сменилось на этой же карте — бот продолжает с того места, куда дошёл.
            if (mind.Kit != null && mind.HasPlace && mind.SceneHandle == SceneManager.GetActiveScene().handle)
                mind.Kit.Body.Place(mind.Feet, mind.Yaw);
        }

        private static string TeamName(PlayerSession session)
        {
            TeamData team = session.Team;
            return team != null ? team.Name : "нет";
        }
    }
}
