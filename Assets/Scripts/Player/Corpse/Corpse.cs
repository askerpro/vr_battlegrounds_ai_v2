using System.Collections;
using System.Collections.Generic;
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
    /// не взаимодействует. Убирается к следующему раунду и при смене режима; сверх
    /// <see cref="MaxCorpses"/> старейший исчезает. Смена карты уничтожает трупы вместе со сценой.
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

        private static readonly List<Corpse> Alive = new List<Corpse>();
        private static bool _subscribed;

        public IReadOnlyList<Rigidbody> Bodies => _bodies;

        public bool IsFrozen { get; private set; }

        /// <summary>Сколько трупов лежит сейчас (для тестов и отладки).</summary>
        public static int Count => Alive.Count;

        /// <summary>
        /// Уронить: встать в позу модели аватара <paramref name="modelRoot"/>, отключить столкновения
        /// с телами игроков, толкнуть кость, ближайшую к точке попадания.
        /// </summary>
        public void Launch(Transform modelRoot, DeathImpact impact)
        {
            Register(this);

            if (modelRoot != null) CopyPose(modelRoot);
            IgnoreEverythingButStaticWorld();

            foreach (Rigidbody body in _bodies)
            {
                if (body == null) continue;
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            if (impact.HasImpulse)
            {
                Rigidbody hit = NearestBody(impact.Point);
                if (hit != null) hit.AddForceAtPosition(impact.Impulse, impact.Point, ForceMode.Impulse);
            }

            if (Application.isPlaying) StartCoroutine(FreezeWhenSettled());
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

        /// <summary>Радиус, в котором труп при появлении ищет, с чем не сталкиваться.</summary>
        private const float IgnoreRadius = 4f;

        /// <summary>
        /// Труп никак не влияет на игру: столкновения выключаются со всем подвижным рядом — телами
        /// игроков (их хитбоксы на том же слое, что и карта, <see cref="CorpsePhysics"/>), оружием,
        /// выпавшим из рук погибшего, магазинами и предметами. Остаётся статичная геометрия карты —
        /// на неё он и падает. За время падения новых тел рядом не появится (возрождение — в закупке),
        /// а замёрзший труп коллайдеров не держит.
        /// </summary>
        private void IgnoreEverythingButStaticWorld()
        {
            var own = new List<Collider>();
            foreach (Rigidbody body in _bodies)
                if (body != null) own.AddRange(body.GetComponents<Collider>());
            if (own.Count == 0) return;

            foreach (Collider other in Physics.OverlapSphere(transform.position, IgnoreRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (other == null || own.Contains(other) || IsStaticWorld(other)) continue;
                foreach (Collider mine in own) Physics.IgnoreCollision(mine, other, true);
            }
        }

        /// <summary>Статичный мир: без Rigidbody и не часть игрока.</summary>
        public static bool IsStaticWorld(Collider collider) =>
            collider.attachedRigidbody == null && collider.GetComponentInParent<PlayerController>() == null;

        private IEnumerator FreezeWhenSettled()
        {
            float until = Time.time + _settleSeconds;
            while (Time.time < until && !AllSleeping()) yield return null;
            Freeze();
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
