using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Managers;
using VrBattlegrounds.GameModes;
using Mirror;

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

        [Header("Visibility Settings")]
        [Tooltip("Материал для эффекта X-ray (видимость сквозь стены)")]
        [SerializeField] private Material _xrayMaterial;

        [Header("Debug View (ReadOnly)")]
        [SerializeField] private int _playersInZoneCount;
        [SerializeField] private RoundState _currentRoundState;

        /// <summary>Срабатывает когда игрок входит в зону. Передаётся сам контроллер игрока.</summary>
        public event Action<TeamSpawnZone, PlayerController> PlayerEntered;

        /// <summary>Срабатывает когда игрок покидает зону (перестал быть полностью внутри или вышел совсем).</summary>
        public event Action<TeamSpawnZone, PlayerController> PlayerExited;

        private BoxCollider _boxCollider;
        private MeshRenderer _meshRenderer;
        private Material _originalMaterial;
        private PlayerController _localPlayer;

        // Игроки, которые физически касаются триггера (кандидаты на проверку полного входа)
        private readonly HashSet<PlayerController> _playersTouching = new HashSet<PlayerController>();

        // Игроки, чья голова ПОЛНОСТЬЮ внутри зоны. 
        // Именно этот список видят внешние скрипты через GetPlayersInZone() и события.
        private readonly HashSet<PlayerController> _playersInZone = new HashSet<PlayerController>();

        // Оптимизированная проверка полного нахождения:
        // Кешируем уменьшенный Bounds зоны (zone minus head half-extents).
        // Если центр камеры игрока лежит внутри _shrunkenBounds → голова гарантированно целиком внутри зоны.
        // Одна операция Bounds.Contains = 3 float-сравнения — дёшево для 10 игроков/кадр.
        private Bounds _shrunkenBounds;

        /// <summary>Половина размера коллайдера головы игрока (Camera BoxCollider). </summary>
        private static readonly Vector3 HeadHalfExtents = new Vector3(0.125f, 0.11f, 0.09f);

        // Кеш: PlayerController → Transform камеры, чтобы не делать GetComponentInChildren каждый Stay
        private readonly Dictionary<PlayerController, Transform> _cameraTransformCache =
            new Dictionary<PlayerController, Transform>();

        public TeamData Team => _team;

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();
            _boxCollider.isTrigger = true;
            _meshRenderer = GetComponent<MeshRenderer>();

            if (_meshRenderer != null)
            {
                _originalMaterial = _meshRenderer.sharedMaterial;
            }

            // Ensure there's a kinematic rigidbody so trigger events fire regardless of the player's rigidbody setup
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            // Предрассчитываем уменьшенный Bounds для быстрой проверки полного нахождения.
            // Bounds пересчитывается при Awake — объект должен быть уже на своей финальной позиции.
            RebuildShrunkenBounds();

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

        /// <summary>
        /// Пересчитывает уменьшенный Bounds зоны.
        /// Вызывать при изменении позиции/размера зоны (например при инициализации или ресайзе).
        /// </summary>
        private void RebuildShrunkenBounds()
        {
            Bounds zoneBounds = _boxCollider.bounds; // world-space AABB
            _shrunkenBounds = new Bounds(
                zoneBounds.center,
                zoneBounds.size - HeadHalfExtents * 2f // уменьшаем на полный размер головы
            );
        }

        private void OnEnable()
        {
            EliminationMode.OnRoundStateChangedLocal += OnRoundStateChanged;
            UpdateVisibility();
        }

        private void OnDisable()
        {
            EliminationMode.OnRoundStateChangedLocal -= OnRoundStateChanged;
            if (_localPlayer != null)
            {
                _localPlayer.PlayerDied -= OnLocalPlayerDied;
            }
        }

        private void Update()
        {
            // Пытаемся найти локального игрока, если ещё не нашли
            if (_localPlayer == null && NetworkClient.localPlayer != null)
            {
                _localPlayer = NetworkClient.localPlayer.GetComponent<PlayerController>();
                if (_localPlayer != null)
                {
                    _localPlayer.PlayerDied += OnLocalPlayerDied;
                    UpdateVisibility();
                }
            }
        }

        private void OnRoundStateChanged(RoundState newState)
        {
            _currentRoundState = newState;
            UpdateVisibility();
        }

        private void OnLocalPlayerDied(PlayerController player)
        {
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (_meshRenderer == null) return;

            // Базовая логика: 
            // - Вне активного раунда (ожидание, отсчёт, конец) — зона видна всем.
            // - В активном раунде — зона видна ТОЛЬКО мёртвым игрокам СВОЕЙ команды.

            bool isVisible = true;

            if (_currentRoundState == RoundState.Active)
            {
                // Если раунд активен, проверяем локального игрока
                if (_localPlayer != null)
                {
                    bool isDead = !_localPlayer.IsAlive;
                    bool isSameTeam = _localPlayer.Session.Team == _team;

                    // Видим только если мы мертвы и из этой же команды
                    isVisible = isDead && isSameTeam;
                }
                else
                {
                    // Если локальный игрок ещё не заспавнился в активном раунде — скрываем
                    isVisible = false;
                }
            }

            _meshRenderer.enabled = isVisible;

            if (isVisible)
            {
                // Если мы мертвы и видим зону в активном раунде — используем X-ray материал
                bool useXray = (_currentRoundState == RoundState.Active && _localPlayer != null && !_localPlayer.IsAlive);
                _meshRenderer.sharedMaterial = useXray && _xrayMaterial != null ? _xrayMaterial : _originalMaterial;

                // Перекрашиваем, если сменили материал
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
                    // Use MaterialPropertyBlock instead of .material to avoid leaking materials
                    // or throwing "Not allowed to access Renderer.material on prefab object" errors
                    // when editing prefabs in Play Mode.
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    meshRenderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", _team.color);
                    block.SetColor("_Color", _team.color); // support both URP and standard shaders
                    meshRenderer.SetPropertyBlock(block);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();

            // Если коллайдер не принадлежит игроку или игрок УЖЕ касается — игнорируем
            if (player != null && _playersTouching.Add(player))
            {
                // Кешируем Transform камеры один раз при первом контакте игрока с зоной
                if (!_cameraTransformCache.ContainsKey(player))
                {
                    Camera cam = player.GetComponentInChildren<Camera>(true);
                    if (cam != null)
                        _cameraTransformCache[player] = cam.transform;
                }
            }
        }

        private void OnTriggerStay(Collider other)
        {
            // Быстрая проверка полного нахождения: O(1) на игрока
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || !_playersTouching.Contains(player)) return;

            if (!_cameraTransformCache.TryGetValue(player, out Transform camT) || camT == null) return;

            bool isCurrentlyFullyInside = _shrunkenBounds.Contains(camT.position);
            bool wasAlreadyFullyInside = _playersInZone.Contains(player);

            if (isCurrentlyFullyInside && !wasAlreadyFullyInside)
            {
                // СОБЫТИЕ: Игрок зашёл ЦЕЛИКОМ
                _playersInZone.Add(player);
                _playersInZoneCount = _playersInZone.Count;

                PlayerEntered?.Invoke(this, player);

                GameLog.Verbose(GameSettings.Instance.LogLevelMatch,
                    $"[TeamSpawnZone] Игрок {player.name} вошёл в зону '{name}' (полное нахождение)");
            }
            else if (!isCurrentlyFullyInside && wasAlreadyFullyInside)
            {
                // СОБЫТИЕ: Игрок больше не внутри целиком (но всё ещё касается колайдером)
                _playersInZone.Remove(player);
                _playersInZoneCount = _playersInZone.Count;

                PlayerExited?.Invoke(this, player);

                GameLog.Verbose(GameSettings.Instance.LogLevelMatch,
                    $"[TeamSpawnZone] Игрок {player.name} покинул зону '{name}' (вышел из режима полного нахождения)");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();

            // Если игрок совсем перестал касаться триггера
            if (player != null && _playersTouching.Remove(player))
            {
                // Если он до этого момента считался "внутри целиком", вызываем событие выхода
                if (_playersInZone.Remove(player))
                {
                    _playersInZoneCount = _playersInZone.Count;
                    PlayerExited?.Invoke(this, player);

                    GameLog.Verbose(GameSettings.Instance.LogLevelMatch,
                        $"[TeamSpawnZone] Игрок {player.name} покинул зону '{name}' (вышел совсем)");
                }

                _cameraTransformCache.Remove(player);
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
                if (p.Session.Team == _team)
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

            IEnumerable<PlayerSession> allAliveInTeam = PlayersManager.Instance.GetAlivePlayers(_team);
            foreach (var session in allAliveInTeam)
            {
                var alivePlayer = session.ActiveAvatar;
                if (alivePlayer != null && !_playersInZone.Contains(alivePlayer))
                {
                    result.Add(alivePlayer);
                }
            }

            return result;
        }

        /// <summary>Проверяет, все ли ЖИВЫЕ члены команды находятся в этом триггере (касание).</summary>
        public bool AreAllTeamPlayersInZone()
        {
            if (_team == null || PlayersManager.Instance == null) return false;

            var notInZone = GetTeamPlayersNotInZone();
            return notInZone.Count == 0;
        }

        // ─── Полное нахождение (голова целиком внутри) ────────────────────────────

        /// <summary>
        /// Быстрая проверка: голова игрока (Camera BoxCollider) целиком внутри зоны.
        /// Алгоритм: центр камеры должен лежать в уменьшенном Bounds зоны (zone − head_size).
        /// Точность ~±HeadHalfExtents (5–12 см), скорость O(1).
        /// </summary>
        public bool IsPlayerFullyInZone(PlayerController player)
        {
            if (player == null || !_playersTouching.Contains(player)) return false;
            if (!_cameraTransformCache.TryGetValue(player, out Transform camT) || camT == null) return false;
            return _shrunkenBounds.Contains(camT.position);
        }

        /// <summary>
        /// Возвращает true, если все ЖИВЫЕ игроки команды полностью (головой) внутри зоны.
        /// Оптимизировано: проверяем текущий состав _playersInZone.
        /// </summary>
        public bool AreAllTeamPlayersFullyInZone()
        {
            if (_team == null || PlayersManager.Instance == null) return false;

            foreach (PlayerSession session in PlayersManager.Instance.GetAlivePlayers(_team))
            {
                var alive = session.ActiveAvatar;
                if (alive != null && !_playersInZone.Contains(alive))
                    return false;
            }
            return true;
        }

        /// <summary>Возвращает копию множества игроков, голова которых ПОЛНОСТЬЮ внутри зоны.</summary>
        public List<PlayerController> GetPlayersFullyInZone()
        {
            return new List<PlayerController>(_playersInZone);
        }
    }
}
