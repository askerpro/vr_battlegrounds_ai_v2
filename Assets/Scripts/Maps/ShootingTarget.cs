using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Мишень для разминки: пуля в щит — щит вспыхивает и падает назад, полежав,
    /// поднимается сам. Живёт в лобби за краем арены.
    ///
    /// <para>
    /// <b>Откуда узнаёт о попадании.</b> Мишень не <c>UxrActor</c>: урон, смерть и
    /// репликация жизни ей не нужны. Пуля в «не актёра» кончается событием
    /// <see cref="UxrWeaponManager.NonActorImpacted"/> — его мишень и слушает, сверяя
    /// коллайдер попадания со своим щитом (<see cref="_pivot"/> и всё под ним).
    /// Попадание в стойку щит не роняет.
    /// </para>
    ///
    /// <para>
    /// <b>Сеть.</b> Компонент не сетевой, и это не упрощение, а следствие устройства
    /// UltimateXR: снаряд симулирует <b>каждая</b> машина (выстрел реплицируется,
    /// <c>UxrWeaponManager.UpdateProjectiles</c> роли не смотрит — см.
    /// <c>Docs/combat-networking.md</c>), значит и событие попадания приходит на каждой.
    /// Мишень неподвижна, и каждая машина роняет её сама, без сообщений по сети.
    /// Ограничение: подключившийся позже видит мишени поднятыми, а попадание «впритирку»
    /// к краю щита на разных машинах может засчитаться по-разному.
    /// </para>
    ///
    /// <para>
    /// Не хватается: <c>UxrGrabbableObject</c> здесь запрещён — тест поз хвата требует
    /// позу для каждого grabbable в сценах.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ShootingTarget : MonoBehaviour
    {
        /// <summary>Фактический контакт SDK со щитом до вспышки/падения; визуальные наблюдатели не управляют выстрелом.</summary>
        public static event System.Action<ShootingTarget,UxrProjectileSource,RaycastHit> ProjectileContact;
        private enum State
        {
            Up,
            Falling,
            Down,
            Rising
        }

        [Tooltip("Щит на шарнире. Вращается вокруг своей локальной оси X; попадание засчитывается только по коллайдерам под ним.")]
        [SerializeField] private Transform _pivot;

        [Tooltip("Что вспыхивает при попадании. Пусто — рендереры под шарниром.")]
        [SerializeField] private Renderer[] _flashRenderers;

        [SerializeField] private Color _flashColor = new Color(1f, 0.8f, 0.1f);

        [Min(0f)]
        [SerializeField] private float _flashDuration = 0.2f;
        [Tooltip("Выключено на стрельбище: щит остаётся неподвижным и принимает каждый выстрел.")]
        [SerializeField] private bool _fallOnHit = true;
        [SerializeField] private AudioSource _impactAudio;
        [SerializeField] private AudioClip[] _metalImpacts;
        private int _nextImpact;
        private float _lastImpactSound = float.NegativeInfinity;

        [Tooltip("На сколько градусов щит заваливается назад, от стрелка.")]
        [Range(0f, 90f)]
        [SerializeField] private float _fallAngle = 80f;

        [Min(0.01f)]
        [SerializeField] private float _fallDuration = 0.12f;

        [Tooltip("Сколько секунд щит лежит, прежде чем подняться.")]
        [Min(0f)]
        [SerializeField] private float _downTime = 2.5f;

        [Min(0.01f)]
        [SerializeField] private float _riseDuration = 0.6f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private State _state = State.Up;
        private float _stateTime;
        private float _flashLeft;
        private bool _subscribed;
        private bool _initialized;
        private Quaternion _restRotation;
        private MaterialPropertyBlock _block;

        /// <summary>Сколько раз щит был сбит с момента появления мишени.</summary>
        public int HitCount { get; private set; }

        /// <summary>Щит не стоит: падает, лежит или поднимается. Попадания в это время не считаются.</summary>
        public bool IsDown => _state != State.Up;

        /// <summary>Текущий наклон щита назад, градусы.</summary>
        public float Tilt { get; private set; }

        public Transform Pivot => _pivot;

        private void Awake()
        {
            if (_pivot == null)
                GameLog.WeaponSystem.Warning($"[ShootingTarget] У мишени '{name}' не назначен щит (_pivot) — она не будет падать.", this);

            EnsureInitialized();
        }

        /// <summary>
        /// Запоминает стоячее положение щита. Ленивое, а не в <see cref="Awake"/>: в тестах
        /// редактора <c>Awake</c> у добавленного компонента не вызывается.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_initialized || _pivot == null) return;

            _restRotation = _pivot.localRotation;

            if (_flashRenderers == null || _flashRenderers.Length == 0)
                _flashRenderers = _pivot.GetComponentsInChildren<Renderer>(true);

            _initialized = true;
        }

        /// <summary>Назначает щит. Для сборки мишени из кода (инструмент редактора, тесты).</summary>
        public void SetPivot(Transform pivot)
        {
            _pivot = pivot;
            _initialized = false;
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (_subscribed && UxrWeaponManager.HasInstance)
                UxrWeaponManager.Instance.NonActorImpacted -= OnNonActorImpacted;

            _subscribed = false;
        }

        private void Update()
        {
            // Менеджер оружия UltimateXR может появиться позже мишени (он создаётся
            // по первому обращению), поэтому подписка догоняет его здесь.
            if (!_subscribed) TrySubscribe();

            Step(Time.deltaTime);
        }

        private void TrySubscribe()
        {
            if (_subscribed || !UxrWeaponManager.HasInstance) return;

            UxrWeaponManager.Instance.NonActorImpacted += OnNonActorImpacted;
            _subscribed = true;
        }

        private void OnNonActorImpacted(object sender, UxrNonDamagingImpactEventArgs e)
        {
            if(_pivot!=null&&e.RaycastHit.collider!=null&&e.RaycastHit.collider.transform.IsChildOf(_pivot))
            {
                var handlers=ProjectileContact;
                if(handlers!=null)foreach(System.Action<ShootingTarget,UxrProjectileSource,RaycastHit> handler in handlers.GetInvocationList())
                    try{handler(this,e.ProjectileSource,e.RaycastHit);}catch(System.Exception exception){GameLog.WeaponSystem.Error("[ShootingTarget] Photo observer: "+exception,this);}
            }
            TryRegisterHit(e.RaycastHit.collider);
        }

        /// <summary>
        /// Засчитывает попадание, если <paramref name="hitCollider"/> — щит этой мишени
        /// и щит сейчас стоит. Возвращает, засчитано ли.
        /// </summary>
        public bool TryRegisterHit(Collider hitCollider)
        {
            if (_pivot == null || hitCollider == null) return false;
            if (!hitCollider.transform.IsChildOf(_pivot)) return false;
            if (_state != State.Up) return false;

            EnsureInitialized();

            HitCount++;
            _state = _fallOnHit ? State.Falling : State.Up;
            _stateTime = 0f;
            _flashLeft = _flashDuration;
            SetFlash(true);

            if(_impactAudio!=null&&_metalImpacts!=null&&_metalImpacts.Length>0&&Time.unscaledTime-_lastImpactSound>=.03f)
            {
                AudioClip clip=_metalImpacts[_nextImpact++%_metalImpacts.Length];
                if(clip!=null){_impactAudio.PlayOneShot(clip);_lastImpactSound=Time.unscaledTime;}
            }

            GameLog.WeaponSystem.Verbose($"[ShootingTarget] Попадание в '{name}', всего {HitCount}.", this);
            return true;
        }

        /// <summary>Шаг анимации щита и вспышки. Открыт для тестов: зовёт его <see cref="Update"/>.</summary>
        public void Step(float deltaTime)
        {
            if (_flashLeft > 0f)
            {
                _flashLeft -= deltaTime;
                if (_flashLeft <= 0f) SetFlash(false);
            }

            if (_pivot == null || _state == State.Up) return;

            EnsureInitialized();
            _stateTime += deltaTime;

            switch (_state)
            {
                case State.Falling:
                    Tilt = _fallAngle * Mathf.Clamp01(_stateTime / _fallDuration);
                    if (_stateTime >= _fallDuration) Enter(State.Down);
                    break;

                case State.Down:
                    Tilt = _fallAngle;
                    if (_stateTime >= _downTime) Enter(State.Rising);
                    break;

                case State.Rising:
                    Tilt = _fallAngle * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_stateTime / _riseDuration)));
                    if (_stateTime >= _riseDuration)
                    {
                        Tilt = 0f;
                        Enter(State.Up);
                    }
                    break;
            }

            // Минус — верх щита уходит к -Z мишени, то есть от стрелка: лицо мишени смотрит в +Z.
            _pivot.localRotation = _restRotation * Quaternion.Euler(-Tilt, 0f, 0f);
        }

        private void Enter(State state)
        {
            _state = state;
            _stateTime = 0f;
        }

        private void SetFlash(bool on)
        {
            if (_flashRenderers == null) return;

            if (_block == null) _block = new MaterialPropertyBlock();

            foreach (Renderer r in _flashRenderers)
            {
                if (r == null) continue;

                if (on)
                {
                    r.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, _flashColor);
                    _block.SetColor(ColorId, _flashColor);
                    r.SetPropertyBlock(_block);
                }
                else
                {
                    r.SetPropertyBlock(null);
                }
            }
        }
    }
}
