using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Коллайдер-зона спавна конкретной команды.
    /// Отслеживает, кто из игроков вошёл, проверяет принадлежность к нужной командой, 
    /// может ответить на вопрос "Все ли живые игроки команды находятся в этой зоне?".
    /// Требует BoxCollider (isTrigger = true) на том же GameObject.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TeamSpawnZone : MonoBehaviour
    {
        [Tooltip("Команда, которой принадлежит эта зона")]
        [SerializeField] private TeamData _team;

        [Header("Debug View (ReadOnly)")]
        [SerializeField] private int _playersInZoneCount;

        /// <summary>Срабатывает когда игрок входит в зону. Передаётся сам контроллер игрока.</summary>
        public event Action<TeamSpawnZone, PlayerController> PlayerEntered;
        
        /// <summary>Срабатывает когда игрок покидает зону.</summary>
        public event Action<TeamSpawnZone, PlayerController> PlayerExited;

        private BoxCollider _boxCollider;
        
        // HashSet защищает от множественных коллайдеров одного игрока (например, если рэгдолл задел триггер 3 костями)
        private readonly HashSet<PlayerController> _playersInZone = new HashSet<PlayerController>();

        public TeamData Team => _team;

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();
            _boxCollider.isTrigger = true;

            if (_team == null)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelMatch, 
                    $"[TeamSpawnZone] У SpawnZone на объекте {gameObject.name} не назначена команда (_team).");
            }
            else
            {
                UpdateColor();
            }
        }

        private void OnValidate()
        {
            UpdateColor();
        }

        private void UpdateColor()
        {
            if (_team != null)
            {
                MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    // Create a mutable copy of the material so we don't accidentally modify the shared asset
                    if (Application.isPlaying)
                    {
                        meshRenderer.material.color = _team.color;
                    }
                    else
                    {
                        // In edit mode we shouldn't create material instances that leak, 
                        // but setting sharedMaterial color changes the asset for everyone.
                        // For a SpawnZone preview, using a MaterialPropertyBlock is safest and cleanest.
                        MaterialPropertyBlock block = new MaterialPropertyBlock();
                        meshRenderer.GetPropertyBlock(block);
                        block.SetColor("_BaseColor", _team.color);
                        block.SetColor("_Color", _team.color); // support both URP and standard shaders
                        meshRenderer.SetPropertyBlock(block);
                    }
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            
            // Если коллайдер не принадлежит игроку или игрок УЖЕ в списке — игнорируем
            if (player != null && _playersInZone.Add(player))
            {
                _playersInZoneCount = _playersInZone.Count;
                PlayerEntered?.Invoke(this, player);
                GameLog.Verbose(GameSettings.Instance.LogLevelMatch, 
                    $"[TeamSpawnZone] Игрок {player.name} вошёл в зону '{name}'");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            
            // Пытаемся удалить. Если игрока там и не было (удалился другим коллайдером), Remove вернёт false
            if (player != null && _playersInZone.Remove(player))
            {
                _playersInZoneCount = _playersInZone.Count;
                PlayerExited?.Invoke(this, player);
                GameLog.Verbose(GameSettings.Instance.LogLevelMatch, 
                    $"[TeamSpawnZone] Игрок {player.name} покинул зону '{name}'");
            }
        }

        /// <summary>Возвращает копию списка всех игроков, физически находящихся в зоне.</summary>
        public List<PlayerController> GetPlayersInZone()
        {
            return new List<PlayerController>(_playersInZone);
        }

        /// <summary>Возвращает игроков, которые находятся в зоне и чья команда совпадает с _team.</summary>
        public List<PlayerController> GetTeamPlayersInZone()
        {
            List<PlayerController> result = new List<PlayerController>();
            if (_team == null) return result;

            foreach (var p in _playersInZone)
            {
                if (p.Team == _team)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>Возвращает всех ЖИВЫХ игроков из PlayersManager требуемой команды, которых физически НЕТ в этой зоне.</summary>
        public List<PlayerController> GetTeamPlayersNotInZone()
        {
            List<PlayerController> result = new List<PlayerController>();
            if (_team == null || PlayersManager.Instance == null) return result;

            IEnumerable<PlayerController> allAliveInTeam = PlayersManager.Instance.GetAlivePlayers(_team);
            foreach (var alivePlayer in allAliveInTeam)
            {
                if (!_playersInZone.Contains(alivePlayer))
                {
                    result.Add(alivePlayer);
                }
            }
            
            return result;
        }

        /// <summary>Проверяет, все ли ЖИВЫЕ члены команды находятся в этом триггере.</summary>
        public bool AreAllTeamPlayersInZone()
        {
            if (_team == null || PlayersManager.Instance == null) return false;
            
            var notInZone = GetTeamPlayersNotInZone();
            return notInZone.Count == 0;
        }
    }
}
