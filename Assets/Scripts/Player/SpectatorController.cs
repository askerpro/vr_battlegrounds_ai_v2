using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Режим наблюдателя — выбывший игрок (погиб или выбыл в конце боя): тело спрятано, вместо него
    /// универсальный призрак (<see cref="GhostBody"/>), хитбоксы выключены — пули сквозь выбывшего
    /// проходят. Состояние — <c>SyncVar</c>; применяется на каждой машине, включая выделенный
    /// сервер, где хук не вызывается, а пули считаются именно там.
    /// </summary>
    public class SpectatorController : NetworkBehaviour
    {
        [Header("References")]
        [Tooltip("Тело аватара — прячется у выбывшего.")]
        public GameObject Geo = null;

        [Tooltip("Части тела вне Geo — висят на костях и не могут лежать под ним (часы на предплечье). Прячутся вместе с Geo.")]
        public GameObject[] ExtraGeo = new GameObject[0];

        [Tooltip("Префаб призрака (Prefabs/Player/Ghost/GhostBody). Назначен в PlayerBase — все аватары наследуют.")]
        public GhostModel GhostPrefab = null;

        [Tooltip("Устарело: призрак-копия тела (Cyborg). Больше не показывается — вместо него GhostBody.")]
        public GameObject Ghost = null;

        [SyncVar(hook = nameof(OnSpectatingChanged))]
        private bool _isSpectating = false;

        /// <summary>Твёрдые коллайдеры, выключенные на время наблюдения, — чтобы вернуть ровно их.</summary>
        private readonly List<Collider> _disabledColliders = new List<Collider>();

        private GhostBody _ghostBody;
        private bool? _applied;

        public void ToggleSpectator()
        {
            if (!isServer) return;
            SetSpectating(!_isSpectating);
        }

        public bool IsSpectating()
        {
            return _isSpectating;
        }

        [Server]
        public void SetSpectating(bool value)
        {
            if (_isSpectating == value) return;

            GameLog.Player.Info($"[SpectatorController] {name}: режим наблюдения {(value ? "включен" : "выключен")} (сервер)", this);
            _isSpectating = value;

            // Хук SyncVar на выделенном сервере не вызывается — применяем сами (на хосте повтор безвреден).
            Apply(value);
        }

        public void StartSpectating()
        {
            if (!isServer) return;
            SetSpectating(true);
        }

        public void EndSpectating()
        {
            if (!isServer) return;
            SetSpectating(false);
        }

        private void OnSpectatingChanged(bool oldValue, bool newValue)
        {
            GameLog.Player.Verbose($"[SpectatorController] {name}: SyncVar наблюдателя изменено {oldValue} -> {newValue}", this);
            Apply(newValue);
        }

        private void Awake()
        {
            // Отклик своему игроку: вибрация при гибели, чёрно-белый вид вне своей зоны. Сам
            // работает только у локального аватара — у остальных молчит.
            if (GetComponent<GhostViewEffect>() == null) gameObject.AddComponent<GhostViewEffect>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Apply(_isSpectating);
        }

        private void Apply(bool spectating)
        {
            if (_applied == spectating) return;
            _applied = spectating;

            GameLog.Player.Verbose($"[SpectatorController] {name}: обновление визуализации (spectating={spectating})", this);

            if (Geo != null) Geo.SetActive(!spectating);
            foreach (GameObject part in ExtraGeo)
            {
                if (part != null) part.SetActive(!spectating);
            }
            if (Ghost != null) Ghost.SetActive(false);

            SetHitboxes(!spectating);

            // Компонент добавляется сам; префаб призрака — GhostPrefab из PlayerBase.
            if (_ghostBody == null) _ghostBody = GetComponent<GhostBody>();
            if (_ghostBody == null) _ghostBody = gameObject.AddComponent<GhostBody>();
            _ghostBody.Prefab = GhostPrefab;
            _ghostBody.SetVisible(spectating, TeamColor());
        }

        /// <summary>
        /// Хитбоксы — твёрдые коллайдеры аватара: пуля UltimateXR ищет попадание с
        /// <c>QueryTriggerInteraction.Ignore</c>. Триггеры (зона головы, карманы, якоря) не трогаем.
        /// </summary>
        private void SetHitboxes(bool enabled)
        {
            if (!enabled)
            {
                _disabledColliders.Clear();
                foreach (Collider collider in GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger || !collider.enabled) continue;
                    collider.enabled = false;
                    _disabledColliders.Add(collider);
                }
                return;
            }

            foreach (Collider collider in _disabledColliders)
            {
                if (collider != null) collider.enabled = true;
            }
            _disabledColliders.Clear();
        }

        private Color TeamColor()
        {
            var player = GetComponent<PlayerController>();
            TeamData team = player != null ? player.Team : null;
            return team != null ? team.color : Color.white;
        }
    }
}
