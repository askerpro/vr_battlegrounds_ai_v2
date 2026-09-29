using System.Collections.Generic;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>
    /// Всё, что экран «Обзор» (T-33) знает о текущей ситуации, — простыми данными, без ссылок
    /// на сетевые объекты. Заполняет его читатель реплицированного состояния (<c>MapReferee</c>,
    /// режим, <c>Series</c>, сессии, аватары), а <see cref="OverviewBuilder"/> превращает
    /// в <see cref="OverviewSnapshot"/>. Так логика «что показать» проверяется EditMode-тестом
    /// без Mirror и сцены — <c>OverviewBuilderTests</c>.
    /// </summary>
    public sealed class OverviewInput
    {
        /// <summary>Есть сетевая сессия (клиент или хост). Нет — контекст «Нет подключения».</summary>
        public bool Online;

        /// <summary>Загружено лобби (контекст меню <c>Lobby</c>): матча здесь не бывает.</summary>
        public bool IsLobby;

        /// <summary>Состояние карты — <c>MapReferee.CurrentState</c> (SyncVar).</summary>
        public MapState MapState = MapState.Warmup;

        /// <summary>Название карты для шапки.</summary>
        public string MapTitle = "";

        /// <summary>Название активного режима матча для шапки; пусто — разминка.</summary>
        public string ModeTitle = "";

        /// <summary>Кто смотрит на планшет.</summary>
        public OverviewViewer Viewer = new OverviewViewer();

        /// <summary>Команды активного режима со счётом карты (<c>GameMode.GetScore</c>).</summary>
        public List<OverviewTeamInput> Teams = new List<OverviewTeamInput>();

        /// <summary>Все сессии этой машины (<c>PlayerSession</c>), включая свою.</summary>
        public List<OverviewPlayerInput> Players = new List<OverviewPlayerInput>();

        /// <summary>Серия карт (<c>Series</c>); null — объекта серии нет.</summary>
        public OverviewSeriesInput Series;

        /// <summary>Выбор админа на следующую серию (<c>SessionManager</c>); null — не выбрано.</summary>
        public OverviewPlanInput Plan;

        /// <summary>Раунд Elimination; null — активный режим не Elimination.</summary>
        public EliminationRoundInput Elimination;

        /// <summary>Остаток матча Respawn в секундах; null — активный режим не Respawn.</summary>
        public float? MatchTimeRemaining;

        /// <summary>Минимум игроков для старта режима (<c>GameMode.MinPlayersToStart</c>).</summary>
        public int MinPlayersToStart;

        /// <summary>
        /// Политика: видит ли игрок хп соперников. По умолчанию да — решение пользователя
        /// (2026-09-29): арена физическая, игроки и так видят друг друга. Выключенная — у соперника
        /// только «жив / выбыл», как в таблице CS; своя команда, наблюдатель и игрок без команды
        /// видят хп всех всегда (<see cref="OverviewVisibility"/>).
        /// </summary>
        public bool ShowEnemyHealth = true;
    }

    /// <summary>Тот, кто смотрит на планшет.</summary>
    public sealed class OverviewViewer
    {
        /// <summary>Ключ сессии (<c>Series.PlayerKey</c>) — отметить свою строку.</summary>
        public string PlayerKey = "";

        /// <summary>Команда; <see cref="OverviewPlayerInput.NoTeam"/> — без команды.</summary>
        public int TeamIndex;

        public bool IsAdmin;

        /// <summary>Роль <c>GameRole.Spectator</c> — наблюдатель без аватара.</summary>
        public bool IsSpectatorRole;

        /// <summary>Выбыл в текущем раунде (призрак).</summary>
        public bool IsEliminated;
    }

    public sealed class OverviewTeamInput
    {
        public int Index;
        public string Name = "";

        /// <summary>Счёт карты: раунды (Elimination) или фраги (Respawn).</summary>
        public int MapScore;
    }

    public sealed class OverviewPlayerInput
    {
        /// <summary>Индекс «без команды» — как в <c>PlayerSession.TeamIndex</c>.</summary>
        public const int NoTeam = 0;

        /// <summary>Ключ сессии (<c>Series.PlayerKey</c>): стабилен, по нему View узнаёт строку.</summary>
        public string Key = "";
        public string Name = "";
        public int TeamIndex = NoTeam;
        public bool IsAdmin;
        public bool IsSpectatorRole;

        /// <summary>Аватар заспавнен. Нет — хп и «жив» не показываются.</summary>
        public bool HasAvatar;

        /// <summary><c>PlayerController.IsAlive</c> — <c>UxrActor.Life</c> через канал состояния UltimateXR.</summary>
        public bool IsAlive;

        /// <summary><c>PlayerController.Health</c>.</summary>
        public float Health;

        /// <summary><c>PlayerSession.ReadyState</c> (SyncVar).</summary>
        public bool IsReady;

        /// <summary><c>PlayerSession.IsCalibrated</c> (SyncVar).</summary>
        public bool IsCalibrated;

        /// <summary>Название выбранного скина (<c>AvatarData.displayName</c>); пусто — неизвестен.</summary>
        public string SkinName = "";

        /// <summary>Статистика текущей карты из <c>Series.PlayerStats</c>.</summary>
        public int Kills, Deaths, Assists;
    }

    public sealed class OverviewSeriesInput
    {
        public bool Running;

        /// <summary>Карта загружена без серии (<c>Series.IsAdHoc</c>).</summary>
        public bool AdHoc;

        public List<string> Maps = new List<string>();

        /// <summary><c>Series.CurrentIndex</c>; −1 — серия не идёт.</summary>
        public int CurrentIndex = -1;

        /// <summary>Победитель каждой сыгранной карты, −1 — ничья (<c>Series.Results</c>).</summary>
        public List<int> Results = new List<int>();

        /// <summary>Выиграно карт по командам (<c>Series.GetMapWins</c>).</summary>
        public Dictionary<int, int> MapWins = new Dictionary<int, int>();

        public bool IsLastMap => Running && CurrentIndex >= Maps.Count - 1;

        /// <summary>Итог текущей карты уже записан — карта сыграна.</summary>
        public bool CurrentMapDecided => (Running || AdHoc) && CurrentIndex >= 0 && Results.Count > CurrentIndex;
    }

    public sealed class OverviewPlanInput
    {
        public string ModeTitle = "";
        public List<string> Maps = new List<string>();
    }

    /// <summary>Состояние раунда Elimination — SyncVar режима плюс таймер, посчитанный локально.</summary>
    public sealed class EliminationRoundInput
    {
        public EliminationState State = EliminationState.WaitingForPlayers;
        public RoundPhase Phase = RoundPhase.Setup;
        public int RoundNumber;
        public int TotalRounds;
        public int RoundsToWin;
        public bool SidesSwapped;

        /// <summary>Остаток фазы — <c>RoundClock.TryGetTimeRemaining</c>.</summary>
        public float SecondsRemaining;

        public bool CountdownHeld;
    }
}
