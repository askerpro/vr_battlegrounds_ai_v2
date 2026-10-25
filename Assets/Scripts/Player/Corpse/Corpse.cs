using System.Collections;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Тело погибшего — рэгдолл (T-35). Локальный объект каждой машины, не сетевой: сервер рассылает
    /// только «стань трупом + толчок» (<see cref="PlayerController"/>), и каждый клиент роняет свою
    /// копию от одного толчка. Позы костей не синхронизируются — для лежащего тела это не нужно.
    ///
    /// <para>
    /// Префаб генерируется из модели аватара (<c>Tools/VR Battlegrounds/Avatars/Build Corpses</c>):
    /// та же иерархия костей и меши, <c>Rigidbody</c> + <c>CharacterJoint</c> на гуманоидных костях,
    /// слой трупа (<see cref="CorpsePhysics"/>), ни сети, ни UltimateXR. На игру не влияет никак:
    /// сталкивается только со статичной геометрией карты, пули проходят насквозь.
    /// </para>
    ///
    /// <para>
    /// <b>Жизнь.</b> Падает <see cref="_settleSeconds"/> секунд (или пока все тела не уснут), затем
    /// замерзает: кости кинематические, коллайдеры выключены — дальше он ничего не стоит и ни с чем
    /// не взаимодействует. Лежит <see cref="_lingerSeconds"/> секунд, показывая силуэт ничьего
    /// снаряжения под собой (<see cref="LootXray"/>), и уходит под пол. Раньше — к следующему раунду и
    /// при смене режима; сверх <see cref="MaxCorpses"/> старейший исчезает. Смена карты уничтожает
    /// трупы вместе со сценой. Снаряжение погибшего отлетает от тела (<see cref="DeathDropEjection"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Corpse : MonoBehaviour
    {
        public const int MaxCorpses = 16;

        [Tooltip("Тела рэгдолла (заполняет сборщик).")]
        [SerializeField] private Rigidbody[] _bodies = new Rigidbody[0];

        [Tooltip("Кости и прочие узлы трупа и их пути от корня модели — по ним копируется поза аватара (заполняет сборщик).")]
        [SerializeField] private Transform[] _nodes = new Transform[0];
        [SerializeField] private string[] _nodePaths = new string[0];

        [Tooltip("Сколько секунд труп падает, прежде чем замёрзнуть.")]
        [SerializeField] private float _settleSeconds = 3f;

        [Tooltip("Сколько секунд замёрзший труп лежит, прежде чем уйти под пол.")]
        [SerializeField] private float _lingerSeconds = 12f;

        [Tooltip("За сколько секунд труп уходит под пол.")]
        [SerializeField] private float _sinkSeconds = 2f;

        /// <summary>На сколько метров труп опускается, уходя под пол.</summary>
        public const float SinkDepth = 1.2f;

        /// <summary>Радиус, в котором труп показывает силуэт ничьего снаряжения (<see cref="LootXray"/>).</summary>
        public const float LootRadius = 1.3f;

        private const float LootPingInterval = 0.25f;

        /// <summary>
        /// Таз ушёл от места появления по горизонтали дальше — подозрительно (труп «не на месте смерти»):
        /// лог с предупреждением и первыми контактами. По вертикали не считается: падение с роста само
        /// по себе даёт около метра.
        /// </summary>
        public const float SuspiciousShift = 1.5f;

        /// <summary>Сколько первых контактов падения запоминать для лога.</summary>
        private const int MaxNotedContacts = 5;

        private float _sunk;

        private Vector3 _launchHips;
        private float _launchTime;
        private List<string> _contacts;

        private static readonly List<Corpse> Alive = new List<Corpse>();
        private static bool _subscribed;

        public IReadOnlyList<Rigidbody> Bodies => _bodies;

        public bool IsFrozen { get; private set; }

        /// <summary>Сколько трупов лежит сейчас (для тестов и отладки).</summary>
        public static int Count => Alive.Count;

        /// <summary>
        /// Уронить: встать в позу модели аватара <paramref name="modelRoot"/>, отключить столкновения
        /// с телами игроков и снаряжением погибшего, толкнуть кость, ближайшую к точке попадания.
        /// <paramref name="owner"/> — погибший (его снаряжение и лог); null — без них (тест).
        /// </summary>
        public void Launch(Transform modelRoot, DeathImpact impact, PlayerController owner = null)
        {
            Register(this);

            if (modelRoot != null) CopyPose(modelRoot);

            // CopyPose двигал только Transform, а автосинхронизация в проекте выключена
            // (m_AutoSyncTransforms: 0): без этого PhysX видел бы бинд-позу в точке корня Instantiate —
            // OverlapSphere, ClosestPoint и AddForceAtPosition ниже считали бы от неё, а не от тела.
            Physics.SyncTransforms();

            int ignored = IgnoreEverythingButStaticWorld(owner, out int carried);

            foreach (Rigidbody body in _bodies)
            {
                if (body == null) continue;
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Rigidbody hit = null;
            if (impact.HasImpulse)
            {
                hit = NearestBody(impact.Point);
                if (hit != null) hit.AddForceAtPosition(impact.Impulse, impact.Point, ForceMode.Impulse);
            }

            _launchHips = HipsPosition;
            _launchTime = Time.time;
            LogLaunch(modelRoot, owner, impact, hit, ignored, carried);

            if (Application.isPlaying)
            {
                WatchContacts();
                StartCoroutine(FreezeWhenSettled());
            }
        }

        /// <summary>Застыть: кости кинематические, коллайдеры выключены.</summary>
        public void Freeze()
        {
            foreach (Rigidbody body in _bodies)
            {
                if (body == null) continue;
                body.isKinematic = true;
                foreach (Collider c in body.GetComponents<Collider>()) c.enabled = false;
            }
            IsFrozen = true;
        }

        /// <summary>Убрать все трупы (новый раунд, смена режима).</summary>
        public static void ClearAll(string reason)
        {
            if (Alive.Count == 0) return;
            GameLog.Player.Verbose($"[Corpse] Убрано трупов: {Alive.Count} ({reason}).");
            foreach (Corpse corpse in new List<Corpse>(Alive))
            {
                if (corpse != null) DestroyCorpse(corpse);
            }
            Alive.Clear();
        }

        /// <summary>Кость, ближайшая к точке попадания.</summary>
        public Rigidbody NearestBody(Vector3 point)
        {
            Rigidbody best = null;
            float bestDistance = float.MaxValue;
            foreach (Rigidbody body in _bodies)
            {
                if (body == null) continue;
                foreach (Collider c in body.GetComponents<Collider>())
                {
                    float d = (c.ClosestPoint(point) - point).sqrMagnitude;
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = body;
                    }
                }
            }
            return best;
        }

        private void CopyPose(Transform modelRoot)
        {
            transform.SetPositionAndRotation(modelRoot.position, modelRoot.rotation);
            transform.localScale = modelRoot.lossyScale;

            for (int i = 0; i < _nodes.Length && i < _nodePaths.Length; i++)
            {
                Transform node = _nodes[i];
                Transform source = modelRoot.Find(_nodePaths[i]);
                if (node == null || source == null) continue;
                node.localPosition = source.localPosition;
                node.localRotation = source.localRotation;
            }
        }

        /// <summary>Радиус вокруг таза, в котором труп при появлении ищет, с чем не сталкиваться.</summary>
        private const float IgnoreRadius = 4f;

        /// <summary>Таз трупа (первое тело рэгдолла); без тел — корень.</summary>
        private Vector3 HipsPosition =>
            _bodies.Length > 0 && _bodies[0] != null ? _bodies[0].transform.position : transform.position;

        /// <summary>
        /// Труп никак не влияет на игру: столкновения выключаются со всем подвижным рядом — частями
        /// игроков (хитбоксы труп не видит и так — у них свой слой, <see cref="CorpsePhysics"/>), оружием
        /// и предметами на <c>Default</c>, магазинами. Центр поиска — таз трупа, не корень: корень аватара
        /// при физической ходьбе стоит в стороне от тела. Снаряжение погибшего (в руках и кобурах,
        /// <see cref="DeathDropEjection.CollectCarried"/>) — явно, на любом расстоянии. Остаётся статичная
        /// геометрия карты — на неё он и падает. За время падения новых тел рядом не появится
        /// (возрождение — в закупке), а замёрзший труп коллайдеров не держит.
        /// </summary>
        /// <returns>Сколько чужих коллайдеров выключено; <paramref name="carried"/> — из них снаряжения погибшего.</returns>
        private int IgnoreEverythingButStaticWorld(PlayerController owner, out int carried)
        {
            carried = 0;
            var own = new List<Collider>();
            foreach (Rigidbody body in _bodies)
                if (body != null) own.AddRange(body.GetComponents<Collider>());
            if (own.Count == 0) return 0;

            var ignored = new HashSet<Collider>();

            if (owner != null)
            {
                var items = new List<UxrGrabbableObject>();
                DeathDropEjection.CollectCarried(owner, items);
                foreach (UxrGrabbableObject item in items)
                {
                    if (item == null) continue;
                    // Только включённые: на выключенном коллайдере IgnoreCollision падает с ошибкой.
                    foreach (Collider other in item.GetComponentsInChildren<Collider>())
                    {
                        if (other == null || !other.enabled || other.isTrigger || own.Contains(other) || !ignored.Add(other)) continue;
                        foreach (Collider mine in own) Physics.IgnoreCollision(mine, other, true);
                        carried++;
                    }
                }
            }

            foreach (Collider other in Physics.OverlapSphere(HipsPosition, IgnoreRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (other == null || own.Contains(other) || IsStaticWorld(other) || !ignored.Add(other)) continue;
                foreach (Collider mine in own) Physics.IgnoreCollision(mine, other, true);
            }

            return ignored.Count;
        }

        /// <summary>Статичный мир: без Rigidbody и не часть игрока.</summary>
        public static bool IsStaticWorld(Collider collider) =>
            collider.attachedRigidbody == null && collider.GetComponentInParent<PlayerController>() == null;

        private IEnumerator FreezeWhenSettled()
        {
            float until = Time.time + _settleSeconds;
            float nextPing = 0f;
            while (Time.time < until && !AllSleeping())
            {
                if (Time.time >= nextPing) { PingLootNearby(); nextPing = Time.time + LootPingInterval; }
                yield return null;
            }
            Freeze();
            LogSettled(Time.time - _launchTime);

            // Лежит — показывает силуэт снаряжения под собой, затем уходит под пол.
            until = Time.time + _lingerSeconds;
            while (Time.time < until)
            {
                PingLootNearby();
                yield return new WaitForSeconds(LootPingInterval);
            }
            while (!AdvanceSink(Time.deltaTime)) yield return null;
        }

        /// <summary>
        /// Силуэт ничьего снаряжения у трупа (<see cref="LootXray"/>): ствол под телом виден сквозь него.
        /// Только рядом с трупом — не «воллхак» по карте.
        /// </summary>
        public void PingLootNearby()
        {
            Vector3 center = _bodies.Length > 0 && _bodies[0] != null ? _bodies[0].position : transform.position;
            foreach (Collider c in Physics.OverlapSphere(center, LootRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                Rigidbody body = c != null ? c.attachedRigidbody : null;
                UltimateXR.Manipulation.UxrGrabbableObject item = body != null
                    ? body.GetComponent<UltimateXR.Manipulation.UxrGrabbableObject>()
                    : null;
                if (item != null) LootXray.Ping(item);
            }
        }

        /// <summary>
        /// Шаг ухода под пол: без прозрачности (дёшево на Quest) — труп опускается на <see cref="SinkDepth"/>
        /// за <see cref="_sinkSeconds"/> и исчезает. true — ушёл и уничтожен.
        /// </summary>
        public bool AdvanceSink(float deltaTime)
        {
            float step = Mathf.Min(SinkDepth - _sunk, SinkDepth * deltaTime / Mathf.Max(0.01f, _sinkSeconds));
            transform.position += Vector3.down * step;
            _sunk += step;

            if (_sunk < SinkDepth - 1e-4f) return false;

            Alive.Remove(this);
            DestroyCorpse(this);
            return true;
        }

        /// <summary>Контакт падающего тела (<see cref="CorpseContactProbe"/>): первые — в лог смещения.</summary>
        internal void NoteContact(Collider other)
        {
            if (IsFrozen || other == null) return;
            if (_contacts == null) _contacts = new List<string>(MaxNotedContacts);
            if (_contacts.Count >= MaxNotedContacts) return;

            string kind = IsStaticWorld(other) ? "статика" : "подвижный";
            string entry = $"{other.name} [{LayerMask.LayerToName(other.gameObject.layer)}, {kind}]";
            if (!_contacts.Contains(entry)) _contacts.Add(entry);
        }

        /// <summary>Слушать контакты костей на время падения: замёрзший труп коллайдеры выключает.</summary>
        private void WatchContacts()
        {
            foreach (Rigidbody body in _bodies)
            {
                if (body == null) continue;
                CorpseContactProbe probe = body.GetComponent<CorpseContactProbe>();
                if (probe == null) probe = body.gameObject.AddComponent<CorpseContactProbe>();
                probe.Bind(this);
            }
        }

        /// <summary>Лог появления — одна строка на труп, на каждой машине.</summary>
        private void LogLaunch(Transform modelRoot, PlayerController owner, DeathImpact impact, Rigidbody hit, int ignored, int carried)
        {
            if (!GameLog.Player.IsEnabled(LogLevel.Info)) return;

            Vector3 root = owner != null ? owner.transform.position : modelRoot != null ? modelRoot.position : transform.position;
            Vector3 rootToHips = _launchHips - root;
            rootToHips.y = 0f;
            UxrAvatar avatar = owner != null ? owner.GetComponent<UxrAvatar>() : null;
            string copy = avatar == null ? "неизвестно" : avatar.AvatarMode == UxrAvatarMode.Local ? "своя" : "чужая";
            string push = impact.HasImpulse
                ? $"толчок в {(hit != null ? hit.name : "никуда (нет костей)")}, точка={impact.Point:F2}, импульс={impact.Impulse.magnitude:F1}"
                : "без толчка";

            GameLog.Player.Info($"[Corpse] {name}: труп — root={root:F2}, hips={_launchHips:F2} " +
                                $"(от корня по горизонтали {rootToHips.magnitude:F2} м), {push}, " +
                                $"игнор столкновений: {ignored} коллайдеров (снаряжение погибшего — {carried}), копия={copy}.", this);
        }

        /// <summary>
        /// Лог заморозки: где лёг таз и насколько ушёл от места появления. Дальше <see cref="SuspiciousShift"/> —
        /// предупреждение с первыми контактами падения.
        /// </summary>
        private void LogSettled(float seconds)
        {
            Vector3 hips = HipsPosition;
            Vector3 shift = hips - _launchHips;
            float horizontal = new Vector2(shift.x, shift.z).magnitude;
            string text = $"[Corpse] {name}: застыл через {seconds:F1} с — hips={hips:F2}, смещение от старта " +
                          $"{shift.magnitude:F2} м (по горизонтали {horizontal:F2} м)";

            if (horizontal > SuspiciousShift)
            {
                string contacts = _contacts != null && _contacts.Count > 0 ? string.Join("; ", _contacts) : "нет";
                GameLog.Player.Warning($"{text} — по горизонтали больше {SuspiciousShift:F1} м, первые контакты: {contacts}.", this);
            }
            else
            {
                GameLog.Player.Info(text + ".", this);
            }
        }

        private bool AllSleeping()
        {
            foreach (Rigidbody body in _bodies)
                if (body != null && !body.IsSleeping()) return false;
            return true;
        }

        private void OnDestroy()
        {
            Alive.Remove(this);
        }

        private static void Register(Corpse corpse)
        {
            EnsureSubscribed();
            Alive.Remove(corpse);
            Alive.Add(corpse);
            while (Alive.Count > MaxCorpses)
            {
                Corpse oldest = Alive[0];
                Alive.RemoveAt(0);
                if (oldest != null) DestroyCorpse(oldest);
            }
        }

        private static void DestroyCorpse(Corpse corpse)
        {
            if (Application.isPlaying) Destroy(corpse.gameObject);
            else DestroyImmediate(corpse.gameObject);
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            EliminationMode.RoundPhaseChangedLocal += phase =>
            {
                if (phase == RoundPhase.Setup) ClearAll("новый раунд");
            };
            MapReferee.ActiveGameModeChangedLocal += _ => ClearAll("смена режима");
        }
    }
}
