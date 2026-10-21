using UnityEngine;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Ноги, курс и взгляд мозга бота — <b>намерение</b>, а не писатель позы. Аватар бота — обычный аватар игрока без
    /// владельца, которым управляет ввод устройства: голова пилота идёт к <see cref="Feet"/> и смотрит по
    /// <see cref="HeadRotation"/> (<see cref="BlazeInputSource"/>), корень не пишет никто; этот компонент ничего не пишет в
    /// трансформы. Отстала голова — источник ставит ноги к голове (<see cref="Place"/>): правда — тело.
    ///
    /// <para>
    /// <see cref="Feet"/> — где бот стоит: в бою — корень Blaze-прокси (<see cref="SetCombatLegsSource"/>), вне боя — шаги
    /// <see cref="BotNavigator"/> (<see cref="WalkTo"/>). Курс доворачивается к цели или по ходу, голова смотрит на цель,
    /// по ходу или осматривается (<see cref="HeadRotation"/>).
    /// </para>
    ///
    /// <para>
    /// Тело сменяется (смерть → призрак → возрождение, смена скина) — новое тело получает прежние ноги через
    /// <see cref="Place"/> от директора (<see cref="BotMind"/>), иначе призрак погибшего появлялся бы у себя на базе.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotBody : MonoBehaviour
    {
        private const float LookSwayDegrees = 25f;
        private const float LookSwayPeriod = 6f;
        private const float TurnDegreesPerSecond = 240f;

        private bool _placed;
        private Vector3 _feet;
        private float _yaw;
        private float _phase;

        private bool _hasLookTarget;
        private Vector3 _lookTarget;
        private bool _walking;
        private Vector3 _walkDirection;
        private Transform _combatLegs;
        private int _preparedFrame = -1;

        /// <summary>
        /// Источник ног и курса в бою — корень Blaze-прокси (<see cref="BotCombatDriver"/>); null — вне боя (навигатор).
        /// </summary>
        public void SetCombatLegsSource(Transform legs)
        {
            if (_combatLegs == legs) return;
            _combatLegs = legs;
            LookAt(null);
        }

        /// <summary>Точка пола под головой — где бот стоит. До первого кадра — корень.</summary>
        public Vector3 Feet => _placed ? _feet : transform.position;

        /// <summary>Куда развёрнут корпус, градусы вокруг вертикали.</summary>
        public float Yaw => _placed ? _yaw : transform.eulerAngles.y;

        /// <summary>Поворот корпуса.</summary>
        public Quaternion BodyRotation => Quaternion.Euler(0f, Yaw, 0f);

        /// <summary>Поставить бота: ноги и разворот (новое тело продолжает путь прежнего).</summary>
        public void Place(Vector3 feet, float yaw)
        {
            _placed = true;
            _feet = feet;
            _yaw = yaw;
        }

        /// <summary>Шаг ходьбы: новые ноги и направление движения (корпус доворачивается по ходу, если не целится).</summary>
        public void WalkTo(Vector3 feet, Vector3 direction)
        {
            EnsurePlaced();
            _feet = feet;
            direction.y = 0f;
            _walking = direction.sqrMagnitude > 1e-6f;
            if (_walking) _walkDirection = direction.normalized;
        }

        /// <summary>Остановиться: корпус больше не доворачивается по ходу.</summary>
        public void StopWalking() => _walking = false;

        /// <summary>Поворачивать корпус и голову к точке. null — смотреть по ходу или по сторонам.</summary>
        public void LookAt(Vector3? target)
        {
            _hasLookTarget = target.HasValue;
            if (target.HasValue) _lookTarget = target.Value;
        }

        private void Awake()
        {
            _phase = Random.value * LookSwayPeriod;
        }

        private void EnsurePlaced()
        {
            if (_placed) return;
            Place(transform.position, transform.eulerAngles.y);
        }

        /// <summary>
        /// Логика кадра без записи трансформов: ноги и курс (от Blaze-прокси в бою, иначе доворот). Один раз за кадр;
        /// зовёт <see cref="BlazeInputSource"/> перед кадром ввода.
        /// </summary>
        public void PrepareFrame()
        {
            if (_preparedFrame == Time.frameCount) return;
            _preparedFrame = Time.frameCount;

            EnsurePlaced();
            if (_combatLegs != null && _combatLegs.gameObject.activeInHierarchy)
            {
                Vector3 from = _feet;
                _feet = _combatLegs.position;
                _yaw = _combatLegs.eulerAngles.y;
                _walking = (_feet - from).sqrMagnitude > 1e-6f;
            }
            else Turn();
        }

        /// <summary>Куда смотрит голова из точки глаз: на цель, по ходу или осматривается по сторонам.</summary>
        public Quaternion HeadRotation(Vector3 eye)
        {
            Quaternion body = BodyRotation;
            if (_hasLookTarget && (_lookTarget - eye).sqrMagnitude > 0.01f)
                return Quaternion.LookRotation(_lookTarget - eye, Vector3.up);
            if (_walking) return body;

            float sway = Mathf.Sin((Time.time + _phase) * (2f * Mathf.PI / LookSwayPeriod)) * LookSwayDegrees;
            return body * Quaternion.Euler(0f, sway, 0f);
        }

        /// <summary>Корпус доворачивается к цели, иначе — по ходу. Корень не трогается.</summary>
        private void Turn()
        {
            Vector3 flat;
            if (_hasLookTarget) flat = _lookTarget - _feet;
            else if (_walking) flat = _walkDirection;
            else return;

            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;

            float wanted = Quaternion.LookRotation(flat, Vector3.up).eulerAngles.y;
            _yaw = Mathf.MoveTowardsAngle(_yaw, wanted, TurnDegreesPerSecond * Time.deltaTime);
        }
    }
}
