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
                prefabToSpawn = _combatAvatarStrategy.GetPrefab(msg, _playerPrefab);
            }
            else
            {
                GameLog.Warning(GameSettings.Instance.LogLevelPlayer, "[AvatarManager] _combatAvatarStrategy is missing. Using fallback.");
            }

            if (prefabToSpawn == null)
            {
                GameLog.Error("[AvatarManager] Cannot spawn: prefabToSpawn is null.");
                return;
            }

            // Восстановление позиции
            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;

            // Пытаемся получить стартовую позицию из NetworkManager
            Transform startPos = NetworkManager.singleton.GetStartPosition();
            if (startPos != null)
            {
                spawnPos = startPos.position;
                spawnRot = startPos.rotation;
            }

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

                if (snapshot != null && snapshot.NeedsPhysicalRestore)
                {
                    avatarClass.RestoreHealth(snapshot.Health);
                }
            }

            session.ActiveAvatar = avatarClass;

            // Спавним аватар и отдаем авторитет игроку.
            // Теперь PlayerSession является PlayerObject, поэтому здесь используем обычный Spawn(avatar, conn).
            NetworkServer.Spawn(avatarInstance, conn);

            OnAvatarSpawned?.Invoke(avatarClass);
        }

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
            Vector3 spawnPos = oldAvatar != null ? oldAvatar.transform.position : Vector3.zero;
            Quaternion spawnRot = oldAvatar != null ? oldAvatar.transform.rotation : Quaternion.identity;

            GameObject newPlayerInstance = Instantiate(avatarPrefab, spawnPos, spawnRot);
            newPlayerInstance.name = $"{avatarPrefab.name} [connId={conn.connectionId}]";

            PlayerController newPc = newPlayerInstance.GetComponent<PlayerController>();
            if (newPc != null)
            {
                newPc.SessionNetId = session.netId;
            }

            session.ActiveAvatar = newPc;

            // Спавним новый физический аватар с авторитетом клиента
            NetworkServer.Spawn(newPlayerInstance, conn);

            if (oldAvatar != null)
            {
                NetworkServer.Destroy(oldAvatar.gameObject);
            }

            OnAvatarSpawned?.Invoke(newPc);
        }
    }
}
