using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.DevTools.Bots
{
    /// <summary>
    /// Боты-противники для проверки сетевого матча в одиночку. Только сервер, только
    /// редактор и development-сборка (создаёт его <see cref="DebugOrchestrator"/> по
    /// <c>DebugBootstrapConfig.botCount</c> или пункт меню <c>Tools/VR Battlegrounds/Debug/Bots</c>).
    ///
    /// <para>
    /// <b>Бот для игры — обычный игрок.</b> У него настоящая <see cref="PlayerSession"/>
    /// в <see cref="PlayersManager"/> (без соединения) и настоящий аватар без владельца
    /// (<c>AvatarManager.SpawnAvatar(null, …)</c>). Поэтому попадания, урон, смерть,
    /// конец раунда, счёт и возрождение идут тем же кодом, что у людей, — ни одной ветки
    /// «если бот» в игровой логике нет.
    /// </para>
    ///
    /// <para>
    /// Сам директор делает за бота только то, что человек делает руками:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Команда.</b> В матче бот выбирает команду, где меньше людей
    ///       (<see cref="BotTeamChoice"/>), пока выбор открыт. Скин — тот, в который
    ///       можно попасть (<see cref="BotSkin"/>). После смены команды бот переносится
    ///       в зону новой команды: смена команды игрока его не двигает (этап Б).</item>
    /// <item><b>Готовность.</b> Живой бот сразу объявляет готовность — фаза закупки не ждёт его 45 с.</item>
    /// <item><b>Тело после смены карты.</b> Аватары людей пересоздаёт
    ///       <c>GameNetworkManager.OnServerReady</c> по соединению; у бота соединения нет,
    ///       и тело возвращает директор.</item>
    /// <item><b>Поза.</b> <see cref="BotBody"/> держит голову на росте человека — иначе зона
    ///       спавна бота не видит и он не возрождается.</item>
    /// <item><b>Стрельба.</b> <see cref="BotGunner"/> берёт оружие в руку и стреляет в фазе боя.</item>
    /// </list>
    /// </summary>
    public sealed class BotDirector : MonoBehaviour
    {
        public static BotDirector Instance { get; private set; }

        private readonly List<PlayerSession> _bots = new List<PlayerSession>();
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

        private void OnEnable() => Maps.SpawnSides.Changed += OnSidesChanged;

        private void OnDisable() => Maps.SpawnSides.Changed -= OnSidesChanged;

        /// <summary>
        /// Смена сторон: база команды теперь на другой стороне. Человек идёт туда сам, бот ходить
        /// не умеет — переносится (иначе не возродится: возрождение требует своей зоны).
        /// </summary>
        private void OnSidesChanged()
        {
            if (!NetworkServer.active) return;

            foreach (PlayerSession bot in _bots)
            {
                if (bot != null) MoveToOwnZone(bot);
            }
        }

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
                GameLog.Debug.Warning("[BotDirector] Бот не создан: сервер не поднят или нет PlayersManager/AvatarManager.");
                return null;
            }

            int number = _nextNumber++;
            PlayerSession session = players.CreateBotSession($"Бот {number}", TokenPrefix + number);
            if (session == null) return null;

            _bots.Add(session);

            // Команда — до тела: спавн берёт зону команды. Сами режимы команду не выдают
            // (у разминки команд нет, матч ждёт выбора) — выбираем.
            ChooseTeam(session, force: true);
            SpawnBody(session);

            GameLog.Debug.Info($"[BotDirector] Добавлен {session.PlayerName}: команда {TeamName(session)}, скин {session.AvatarIndex}.");
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

            PlayerController avatar = session.ActiveAvatar;
            PlayersManager.Instance?.UnregisterBot(session);

            if (!NetworkServer.active) return;

            if (avatar != null)
            {
                AvatarTeardown.ReleaseBeforeDestroy(avatar, "бот убран");
                NetworkServer.Destroy(avatar.gameObject);
            }
            NetworkServer.Destroy(session.gameObject);

            GameLog.Debug.Info($"[BotDirector] Убран {session.PlayerName}.");
        }

        // ── Тик ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!NetworkServer.active)
            {
                // Сервер остановлен: объекты уже убрал Mirror, из реестра — убираем сами.
                foreach (PlayerSession bot in _bots) PlayersManager.Instance?.UnregisterBot(bot);
                _bots.Clear();
                Destroy(gameObject);
                return;
            }

            if (NetworkServer.isLoadingScene) return;

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

                EnsureBody(bot.ActiveAvatar);

                if (bot.ActiveAvatar.IsAlive && !bot.ReadyState)
                {
                    bot.ServerSetReady(true, "бот готов сразу");
                }
            }
        }

        // ── Команда ─────────────────────────────────────────────────────────

        /// <summary>
        /// Ставит бота в команду режима по <see cref="BotTeamChoice"/>. В разминке не
        /// вмешивается — команду даёт режим. Пока выбор команды открыт, пересчитывает
        /// каждый тик: человек пришёл в команду бота — бот уходит. После закрытия выбора
        /// трогает только бота без команды режима (добавлен посреди матча).
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

            bool hadBody = bot.ActiveAvatar != null;
            SessionTeamAssigner.Apply(bot, desired, BotSkin.PickHittable(desired), "BotDirector");

            // Смена команды тело не двигает (игрок идёт в зону сам) — бот сам не ходит.
            if (hadBody) MoveToOwnZone(bot);
        }

        // ── Тело ────────────────────────────────────────────────────────────

        private static void SpawnBody(PlayerSession bot)
        {
            if (AvatarManager.Instance == null) return;

            AvatarManager.Instance.SpawnAvatar(null, null, bot);
            EnsureBody(bot.ActiveAvatar);
        }

        private static void EnsureBody(PlayerController avatar)
        {
            if (avatar == null) return;

            if (avatar.GetComponent<BotBody>() == null) avatar.gameObject.AddComponent<BotBody>();
            if (avatar.GetComponent<BotGunner>() == null) avatar.gameObject.AddComponent<BotGunner>();
        }

        private static void MoveToOwnZone(PlayerSession bot)
        {
            PlayerController avatar = bot.ActiveAvatar;
            if (avatar == null) return;

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(bot.Team);
            if (point.Source == AvatarSpawnPointSource.WorldOrigin) return;

            // Корень бота рассылает сервер (ServerToClient) — достаточно поставить его здесь.
            avatar.transform.SetPositionAndRotation(point.Position, point.Rotation);
            GameLog.Debug.Info($"[BotDirector] {bot.PlayerName} перенесён в зону команды: {point}.");
        }

        private static string TeamName(PlayerSession session)
        {
            TeamData team = session.Team;
            return team != null ? team.Name : "нет";
        }
    }
}
