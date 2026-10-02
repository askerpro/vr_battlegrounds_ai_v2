using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Bots
{
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
    /// </list>
    /// </summary>
    public sealed class BotDirector : MonoBehaviour
    {
        public static BotDirector Instance { get; private set; }

        private readonly List<PlayerSession> _bots = new List<PlayerSession>();
        private readonly Dictionary<PlayerSession, BotMind> _minds = new Dictionary<PlayerSession, BotMind>();
        private int _nextNumber = 1;

        /// <summary>Боты на сервере.</summary>
        public IReadOnlyList<PlayerSession> Bots => _bots;

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

        /// <summary>Добавляет бота. null — сервер не готов (нет менеджеров).</summary>
        public PlayerSession AddBot()
        {
            PlayersManager players = PlayersManager.Instance;
            if (!NetworkServer.active || players == null || AvatarManager.Instance == null)
            {
                GameLog.Player.Warning("[BotDirector] Бот не создан: сервер не поднят или нет PlayersManager/AvatarManager.");
                return null;
            }

            int number = _nextNumber++;
            PlayerSession session = players.CreateBotSession($"Бот {number}", TokenPrefix + number);
            if (session == null) return null;

            _bots.Add(session);
            _minds[session] = new BotMind { HomeSlot = _bots.Count - 1 };

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

        private void Remove(PlayerSession session)
        {
            _bots.Remove(session);
            if (session == null) return;
            _minds.Remove(session);

            PlayerController avatar = session.ActiveAvatar;
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

                Think(bot, MindOf(bot), stage);
            }
        }

        /// <summary>Один тик решений бота: приказ → ноги, закупка, оружие, готовность.</summary>
        private void Think(PlayerSession bot, BotMind mind, BotStage stage)
        {
            PlayerController avatar = bot.ActiveAvatar;
            EnsureBody(bot, avatar);

            var body = avatar.GetComponent<BotBody>();
            var legs = avatar.GetComponent<BotNavigator>();
            var gunner = avatar.GetComponent<BotGunner>();
            if (body == null || legs == null || gunner == null) return;

            mind.Remember(body, SceneManager.GetActiveScene().handle);

            bool alive = avatar.IsAlive;
            int token = BotSenses.ShopToken(avatar);
            PlayerController enemy = stage == BotStage.Combat && alive ? BotSenses.NearestEnemy(bot, body.Feet) : null;

            BotOrder order = BotOrders.Decide(new BotSituation
            {
                Stage = stage,
                Alive = alive,
                InOwnZone = bot.IsInSpawnZone,
                EnemyKnown = enemy != null,
                EnemyVisible = gunner.SeesTarget
            });

            switch (order)
            {
                case BotOrder.GoHome:
                    if (TryHomePoint(bot, mind, out Vector3 home)) legs.GoTo(home);
                    else legs.Stop();
                    break;
                case BotOrder.Hunt:
                    legs.GoTo(BotSenses.FeetOf(enemy));
                    break;
                default:
                    legs.Stop();
                    break;
            }

            if (BotOrders.ShouldShop(order, alive, mind.ShoppedFor(token), free: Economy.MatchEconomy.Current == null))
            {
                mind.Purchase = BotShopper.Buy(bot);
                mind.ShopToken = token;
            }

            if (BotOrders.ShouldArm(stage, alive, mind.ShoppedFor(token)))
                gunner.Arm(mind.ShoppedFor(token) ? mind.Purchase : null);
            else if (stage == BotStage.NoMatch || stage == BotStage.BetweenRounds)
                gunner.StandDown();

            if (BotOrders.ShouldDeclareReady(order, alive, bot.IsInSpawnZone, mind.ShoppedFor(token), bot.ReadyState))
                bot.ServerSetReady(true, "бот закупился и стоит на базе");
        }

        // ── База ────────────────────────────────────────────────────────────

        /// <summary>
        /// Место бота на базе: точка вокруг центра зоны спавна его команды (своё у каждого бота, чтобы не стояли
        /// друг в друге). Не влезает в план зоны — центр зоны.
        /// </summary>
        private static bool TryHomePoint(PlayerSession bot, BotMind mind, out Vector3 point)
        {
            point = default;
            TeamData team = bot.Team;
            if (team == null) return false;

            foreach (TeamSpawnZone zone in FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.None))
            {
                if (zone == null || zone.Team != team) continue;

                Vector3 center = zone.SpawnPoint.position;
                Vector3 slot = center + zone.SpawnPoint.rotation * BotHomeSlot.Offset(mind.HomeSlot);
                point = zone.ContainsInPlan(slot) ? slot : center;
                return true;
            }
            return false;
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

            bool inModeTeam = mode.Teams.Any(t => t != null && t.teamIndex == bot.TeamIndex);
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
        /// Тело сменилось (смерть → призрак, возрождение, скин) — в том же кадре: компоненты бота и ноги прежнего
        /// тела. Иначе новое тело кадр стояло бы головой на полу у корня (на базе) и зона спавна приняла бы призрака.
        /// </summary>
        private void OnAvatarSpawned(PlayerController avatar)
        {
            if (avatar == null || !NetworkServer.active) return;

            PlayerSession bot = _bots.Find(b => b != null && b.ActiveAvatar == avatar);
            if (bot == null) return;

            EnsureBody(bot, avatar);
        }

        private void EnsureBody(PlayerSession bot, PlayerController avatar)
        {
            if (avatar == null) return;

            BotMind mind = MindOf(bot);
            var body = avatar.GetComponent<BotBody>();
            if (body == null)
            {
                body = avatar.gameObject.AddComponent<BotBody>();

                // Тело сменилось на этой же карте — бот продолжает с того места, куда дошёл.
                if (mind.HasPlace && mind.SceneHandle == SceneManager.GetActiveScene().handle)
                    body.Place(mind.Feet, mind.Yaw);
            }

            if (avatar.GetComponent<BotNavigator>() == null) avatar.gameObject.AddComponent<BotNavigator>();
            if (avatar.GetComponent<BotGunner>() == null) avatar.gameObject.AddComponent<BotGunner>();

            mind.LastBody = avatar;
        }

        private static string TeamName(PlayerSession session)
        {
            TeamData team = session.Team;
            return team != null ? team.Name : "нет";
        }
    }
}
