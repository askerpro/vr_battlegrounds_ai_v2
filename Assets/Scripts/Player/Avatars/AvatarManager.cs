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

        [Server]
        public void SpawnAvatar(NetworkConnectionToClient conn, GamePlayerConnectMessage msg, SessionSnapshot snapshot, PlayerSession session)
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

            // Точка спавна: зона своей команды, запасной вариант — точка Mirror.
            // Раньше здесь спрашивался только NetworkManager.GetStartPosition(), а на картах
            // проекта нет ни одного NetworkStartPosition — то есть первичный спавн тоже
            // приземлялся в начало координат (см. WPN-03).
            AvatarSpawnPoint spawnPoint = AvatarSpawnPointResolver.Resolve(session.Team, session);
            LogSpawnPoint("SpawnAvatar", session, spawnPoint);

            Vector3 spawnPos = spawnPoint.Position;
            Quaternion spawnRot = spawnPoint.Rotation;

            // Снимок восстановления и сохранённая позиция бьют точку спавна: игрок
            // переподключился и обязан вернуться туда, где был, а не на базу.
            if (snapshot != null && snapshot.NeedsPhysicalRestore)
            {
                spawnPos = snapshot.Position;
                spawnRot = snapshot.Rotation;
            }
            else if (msg.hasSavedPosition)
            {
                spawnPos = msg.savedPosition;
                spawnRot = msg.savedRotation;
            }

            GameObject avatarInstance = Instantiate(prefabToSpawn, spawnPos, spawnRot);
            avatarInstance.name = $"{prefabToSpawn.name} [connId={conn.connectionId}]";

            PlayerController avatarClass = avatarInstance.GetComponent<PlayerController>();
            if (avatarClass != null)
            {
                avatarClass.SessionNetId = session.netId;
                avatarClass.AvatarPlayerName = session.PlayerName;

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
        /// <item><b>Смена команды.</b> Старый аватар жив, но игрок теперь на другой стороне.
        ///       Оставить его на месте — значит поставить в чужую базу: зона спавна
        ///       противника засчитала бы его как «в зоне» (<c>TeamSpawnZone</c> считает всех,
        ///       кто внутри), а до своей базы пришлось бы идти через всю карту. Поэтому
        ///       смена команды переносит на точку спавна <b>новой</b> команды.</item>
        /// </list>
        /// </summary>
        [Server]
        public void ChangeAvatar(NetworkConnectionToClient conn, PlayerSession session, int teamId, int avatarId)
        {
            TeamData teamData = TeamRegistry.Instance.GetByIndex(teamId);
            if (teamData == null) return;

            GameObject avatarPrefab = teamData.GetAvatarPrefab(avatarId);
            if (avatarPrefab == null) return;

            // Команду сравниваем до записи в сессию: после неё разницы уже не видно.
            bool teamChanged = session.TeamIndex != teamId;

            // Обновляем сессию (логически данные хранятся в сессии)
            session.TeamIndex = teamId;
            session.AvatarIndex = avatarId;

            // Находим текущий активный аватар, чтобы забрать его координаты и потом уничтожить
            PlayerController oldAvatar = session.ActiveAvatar;

            Vector3 spawnPos;
            Quaternion spawnRot;

            if (oldAvatar != null && !teamChanged)
            {
                spawnPos = oldAvatar.transform.position;
                spawnRot = oldAvatar.transform.rotation;
            }
            else
            {
                // Откалиброванное место восстанавливаем ровно в одном случае — когда
                // аватара не осталось, то есть после смены карты. Смена команды физическим
                // событием не является: игрок как стоял в комнате, так и стоит, — но увести
                // его из чужой базы всё равно нужно, и там ветка калибровки не спрашивается.
                PlayerSession restorePlaceFor = oldAvatar == null ? session : null;

                AvatarSpawnPoint spawnPoint = AvatarSpawnPointResolver.Resolve(teamData, restorePlaceFor);
                LogSpawnPoint(oldAvatar == null ? "ChangeAvatar/после смены карты" : "ChangeAvatar/смена команды",
                              session, spawnPoint);

                spawnPos = spawnPoint.Position;
                spawnRot = spawnPoint.Rotation;
            }

            GameObject newPlayerInstance = Instantiate(avatarPrefab, spawnPos, spawnRot);
            newPlayerInstance.name = $"{avatarPrefab.name} [connId={conn.connectionId}]";

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

            if (oldAvatar != null)
            {
                NetworkServer.Destroy(oldAvatar.gameObject);
            }

            OnAvatarSpawned?.Invoke(newPc);
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

            GameLog.Player.Warning(
                $"[AvatarManager] {stage}: {who} — у команды '{team.displayName}' нет зоны спавна на сцене " +
                $"'{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}', а NetworkStartPosition на карте " +
                "не нашлось. Аватар создан в начале координат — скорее всего внутри геометрии.");
        }
    }
}
