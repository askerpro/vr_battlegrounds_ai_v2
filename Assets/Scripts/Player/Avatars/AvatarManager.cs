using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Managers;
using System;
using System.Linq;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Отвечает за инстанцирование, горячую замену и позиционирование физических аватаров (PlayerController)
    /// для подключенных сессий (PlayerSession). Содержит всю логику префабов, скинов и точек спавна.
    ///
    /// <para>
    /// Соединение <c>conn</c> может быть <c>null</c> — это сессия бота
    /// (<c>DevTools.Bots.BotDirector</c>): аватар спавнится без владельца, позу рассылает сервер.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(VrBattlegrounds.Managers.ManagerOrder.AvatarManager)]
    public class AvatarManager : MonoBehaviour
    {
        public static AvatarManager Instance { get; private set; }

        [Header("Roles & Prefabs")]
        [Tooltip("Базовый префаб игрока (fallback)")]
        [SerializeField] private GameObject _playerPrefab;

        [Tooltip("Стратегия спавна боевого VR-аватара")]
        [SerializeField] private AvatarSpawnStrategy _combatAvatarStrategy;

        public static event Action<PlayerController> OnAvatarSpawned;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Создаёт первый физический аватар только что подключившейся сессии.
        ///
        /// <para>
        /// <b>Откуда берётся позиция.</b> Правильных ответов три, и порядок между ними
        /// такой:
        /// </para>
        /// <list type="number">
        /// <item><b>Снимок восстановления на той же карте.</b> Игрок переподключился,
        ///       карта не менялась — вернуть его туда, где стоял, точнее любого пересчёта.
        ///       Как только карта сменилась, мировая точка снимка означает другое место
        ///       арены, и ветка не годится (см. <c>SessionSnapshot.CanRestorePlaceOn</c>).</item>
        /// <item><b>Место, заданное калибровкой.</b> Игрок принёс его с собой
        ///       в <c>GamePlayerConnectMessage</c> — в координатах якорей, — а разложил
        ///       по местам <c>PlayersManager.HandlePlayerConnect</c>. Ветка достижима
        ///       только для откалиброванного игрока: решает
        ///       <see cref="CalibratedSpawnRegistry"/>, тем же правилом, что при смене
        ///       карты (T-30).</item>
        /// <item><b>Зона своей команды.</b> До калибровки игра не знает, где игрок внутри
        ///       арены, и зона — разумное «где угодно».</item>
        /// </list>
        ///
        /// <para>
        /// Здесь стояла четвёртая ветка — <c>msg.hasSavedPosition</c>, — и она была
        /// находкой <b>CAL-02</b>: мировая позиция с прошлой карты, применяемая всем
        /// подряд. Ветка убрана целиком, а два законных случая остались за теми, кто
        /// ими и владеет: <c>SessionRecoveryManager</c> и <see cref="CalibratedSpawnRegistry"/>.
        /// </para>
        /// </summary>
        [Server]
        public void SpawnAvatar(NetworkConnectionToClient conn, SessionSnapshot snapshot, PlayerSession session)
        {
            GameObject prefabToSpawn = _playerPrefab;

            if (_combatAvatarStrategy != null)
            {
                // Для боевых игроков всегда используем эту стратегию (TeamAvatarStrategy)
                prefabToSpawn = _combatAvatarStrategy.GetPrefab(session, _playerPrefab);
            }
            else
            {
                GameLog.Player.Warning("[AvatarManager] _combatAvatarStrategy is missing. Using fallback.");
            }

            if (prefabToSpawn == null)
            {
                GameLog.Error("[AvatarManager] Cannot spawn: prefabToSpawn is null.");
                return;
            }

            // Точка спавна: место, заданное калибровкой, → зона своей команды →
            // точка Mirror. Раньше здесь спрашивался только NetworkManager.GetStartPosition(),
            // а на картах проекта нет ни одного NetworkStartPosition — то есть первичный
            // спавн тоже приземлялся в начало координат (см. WPN-03).
            AvatarSpawnPoint spawnPoint = AvatarSpawnPointResolver.Resolve(session.Team, session);
            LogSpawnPoint("SpawnAvatar", session, spawnPoint);

            Vector3 spawnPos = spawnPoint.Position;
            Quaternion spawnRot = spawnPoint.Rotation;

            // Снимок восстановления бьёт точку спавна, но только на своей карте: игрок
            // переподключился и обязан вернуться туда, где был, а не на базу.
            string currentMap = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (snapshot != null && snapshot.CanRestorePlaceOn(currentMap))
            {
                spawnPos = snapshot.Position;
                spawnRot = snapshot.Rotation;

                GameLog.Player.Info(
                    $"[AvatarManager] SpawnAvatar: {session.PlayerName} возвращён на своё место " +
                    $"из снимка сессии — карта '{currentMap}' не менялась, {spawnPos}.");
            }
            else if (snapshot != null && snapshot.NeedsPhysicalRestore)
            {
                GameLog.Player.Info(
                    $"[AvatarManager] SpawnAvatar: снимок {session.PlayerName} снят на карте " +
                    $"'{snapshot.CapturedOnMap}', а сервер уже на '{currentMap}' — мировая позиция " +
                    $"из снимка означала бы другое место арены (CAL-02). Точку выбрал резолвер: {spawnPoint}.");
            }

            GameObject avatarInstance = Instantiate(prefabToSpawn, spawnPos, spawnRot);
            avatarInstance.name = $"{prefabToSpawn.name} [{ServerAuthoredAvatar.OwnerLabel(conn)}]";

            // Без соединения — бот: тело двигает сервер (см. ServerAuthoredAvatar).
            if (conn == null) ServerAuthoredAvatar.Prepare(avatarInstance);

            PlayerController avatarClass = avatarInstance.GetComponent<PlayerController>();
            if (avatarClass != null)
            {
                avatarClass.SessionNetId = session.netId;
                avatarClass.AvatarPlayerName = session.PlayerName;

                // Здоровье возвращается независимо от карты: оно не про место.
                // Условие то же — игрок был жив, — но не про то, где он стоял.
                if (snapshot != null && snapshot.NeedsPhysicalRestore)
                {
                    avatarClass.RestoreHealth(snapshot.Health);
                }
            }

            // Спавним аватар и отдаем авторитет игроку.
            // Теперь PlayerSession является PlayerObject, поэтому здесь используем обычный Spawn(avatar, conn).
            NetworkServer.Spawn(avatarInstance, conn);

            // Связь проставляется строго ПОСЛЕ спавна: до него netId равен нулю,
            // и клиенты получили бы пустую ссылку на аватар.
            session.ActiveAvatar = avatarClass;

            // Вернувшийся живым после переподключения продолжает себя; остальные — новые в матче.
            Admit(avatarClass, continuesPrevious: snapshot != null && snapshot.NeedsPhysicalRestore);

            OnAvatarSpawned?.Invoke(avatarClass);
        }

        /// <summary>
        /// Пересоздаёт физический аватар сессии под новую команду и/или скин.
        ///
        /// <para>
        /// <b>Откуда берётся позиция.</b> Один вызов обслуживает три разных события, и
        /// правильный ответ у каждого свой (находка <b>WPN-03</b>):
        /// </para>
        /// <list type="bullet">
        /// <item><b>Смена скина внутри карты.</b> Старый аватар жив, команда та же —
        ///       позиция берётся у него. Телепортировать игрока за смену внешнего вида
        ///       нельзя: он стоит там, куда пришёл сам.</item>
        /// <item><b>Пересоздание после смены карты.</b> Старого аватара нет: Mirror
        ///       уничтожил его вместе со сценой, а <c>GameNetworkManager.OnServerReady</c>
        ///       зовёт этот метод заново. Брать позицию не у кого — нужна точка спавна.
        ///       Раньше в этой ветке стоял <c>Vector3.zero</c>, и все игроки материализовались
        ///       в начале координат карты, вплотную к реквизиту. У <b>откалиброванного</b>
        ///       игрока точка спавна не назначается вовсе: его место задано физически,
        ///       и сервер возвращает его туда же (<see cref="CalibratedSpawnRegistry"/>, T-30).</item>
        /// <item><b>Смена команды.</b> Старый аватар жив — позиция берётся у него, как при
        ///       смене скина. Игрок физически стоит в зале, его место задано калибровкой;
        ///       выбор команды — действие в меню, а не перенос (этап Б). Раньше (WPN-03)
        ///       смена команды переносила на точку спавна новой команды — это расклеивало
        ///       картинку с телом. В свою зону игрок теперь приходит сам.</item>
        /// </list>
        /// </summary>
        [Server]
        public void ChangeAvatar(NetworkConnectionToClient conn, PlayerSession session, int teamId, int avatarId)
        {
            TeamData teamData = TeamRegistry.Instance.GetByIndex(teamId);
            if (teamData == null) return;

            GameObject avatarPrefab = teamData.GetAvatarPrefab(avatarId);
            if (avatarPrefab == null) return;

            // Обновляем сессию (логически данные хранятся в сессии)
            session.TeamIndex = teamId;
            session.AvatarIndex = avatarId;

            // Находим текущий активный аватар, чтобы забрать его координаты и потом уничтожить
            PlayerController oldAvatar = session.ActiveAvatar;

            Vector3 spawnPos;
            Quaternion spawnRot;

            if (oldAvatar != null)
            {
                // Смена скина или команды: игрок стоит там, куда пришёл сам.
                spawnPos = oldAvatar.transform.position;
                spawnRot = oldAvatar.transform.rotation;
            }
            else
            {
                // Аватара не осталось — смена карты. Откалиброванное место восстанавливаем.
                AvatarSpawnPoint spawnPoint = AvatarSpawnPointResolver.Resolve(teamData, session);
                LogSpawnPoint("ChangeAvatar/после смены карты", session, spawnPoint);

                spawnPos = spawnPoint.Position;
                spawnRot = spawnPoint.Rotation;
            }

            GameObject newPlayerInstance = Instantiate(avatarPrefab, spawnPos, spawnRot);
            newPlayerInstance.name = $"{avatarPrefab.name} [{ServerAuthoredAvatar.OwnerLabel(conn)}]";

            if (conn == null) ServerAuthoredAvatar.Prepare(newPlayerInstance);

            PlayerController newPc = newPlayerInstance.GetComponent<PlayerController>();
            if (newPc != null)
            {
                newPc.SessionNetId = session.netId;
                newPc.AvatarPlayerName = session.PlayerName;
            }

            // Спавним новый физический аватар с авторитетом клиента
            NetworkServer.Spawn(newPlayerInstance, conn);

            // Только после спавна: netId нового аватара нужен клиентам, иначе связь
            // на них останется указывать на уже уничтоженный старый аватар.
            session.ActiveAvatar = newPc;

            // Без прежнего (смена карты) — в каком состоянии входит новый, решает режим.
            CarryLifeState(oldAvatar, newPc);
            Admit(newPc, continuesPrevious: oldAvatar != null);

            if (oldAvatar != null)
            {
                // Руки и снаряжение отпускаются до уничтожения: иначе UltimateXR остаётся
                // с захватом мёртвой руки, а сетевые предметы кобур и кармана гибнут в обход сети.
                AvatarTeardown.ReleaseBeforeDestroy(oldAvatar, "смена скина или команды");
                NetworkServer.Destroy(oldAvatar.gameObject);
            }

            OnAvatarSpawned?.Invoke(newPc);
        }

        /// <summary>
        /// Новый аватар продолжает прежнего: смена внешности или команды не оживляет выбывшего
        /// и не лечит раненого. Оба уже в сети; прежний — ещё не уничтожен.
        /// </summary>
        public static void CarryLifeState(PlayerController previous, PlayerController next)
        {
            if (previous == null || next == null) return;

            if (previous.IsAlive) next.RestoreHealth(previous.Health);
            else next.ServerEliminateSilently("новый аватар взамен выбывшего");
        }

        /// <summary>
        /// В каком состоянии новый аватар входит в игру, решает активный режим
        /// (<see cref="VrBattlegrounds.GameModes.GameMode.ServerAdmitAvatar"/>): в матче Elimination
        /// аватар без прошлого входит выбывшим. Аватар уже в сети — выбывание уходит клиентам.
        /// </summary>
        private static void Admit(PlayerController avatar, bool continuesPrevious)
        {
            if (avatar == null) return;
            VrBattlegrounds.GameModes.GameMode mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode : null;
            if (mode != null) mode.ServerAdmitAvatar(avatar, continuesPrevious);
        }

        /// <summary>
        /// Пишет в лог, откуда взялась точка спавна.
        ///
        /// <para>
        /// Начало координат — единственный уровень <c>Warning</c>, и только когда команда
        /// известна: значит на карте нет её зоны спавна, и игрок сейчас появится посреди
        /// геометрии. Команда без назначения (подключение в лобби до выбора стороны) —
        /// штатный случай, спрашивать зоны там не о чем.
        /// </para>
        /// </summary>
        private static void LogSpawnPoint(string stage, PlayerSession session, AvatarSpawnPoint point)
        {
            string who = session != null ? session.PlayerName : "(нет сессии)";

            if (point.Source != AvatarSpawnPointSource.WorldOrigin)
            {
                GameLog.Player.Info($"[AvatarManager] {stage}: {who} — {point}");
                return;
            }

            TeamData team = session != null ? session.Team : null;
            if (team == null)
            {
                GameLog.Player.Info(
                    $"[AvatarManager] {stage}: {who} — команда не назначена, точки спавна нет, ставим в начало координат.");
                return;
            }

            // Команда не из режима этой сцены (команда разминки на карте, а команду
            // матча ещё не выбрал) — штатное ожидание выбора, а не сбой карты: нейтральная
            // точка, откалиброванного всё равно ставит калибровка (этап Б).
            VrBattlegrounds.GameModes.GameMode mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode : null;
            if (mode == null || System.Array.IndexOf(mode.Teams, team) < 0)
            {
                GameLog.Player.Info(
                    $"[AvatarManager] {stage}: {who} — команда '{team.Name}' не из режима этой сцены " +
                    "(команда матча ещё не выбрана), ставим в нейтральную точку — начало координат.");
                return;
            }

            // Разминка на боевой карте: зон у команды «Разминка» там нет по построению
            // (зоны — у команд матча), это штатное состояние до «Начать матч».
            if (mode.IsWarmup)
            {
                GameLog.Player.Info(
                    $"[AvatarManager] {stage}: {who} — разминка, у команды '{team.Name}' на этой карте " +
                    "зоны нет — нейтральная точка, начало координат.");
                return;
            }

            GameLog.Player.Warning(
                $"[AvatarManager] {stage}: {who} — у команды '{team.Name}' нет зоны спавна на сцене " +
                $"'{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}', а NetworkStartPosition на карте " +
                "не нашлось. Аватар создан в начале координат — скорее всего внутри геометрии.");
        }
    }
}
