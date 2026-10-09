using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Исполнитель отклика взаимодействий — один на игру (<c>tasks/haptics-system/Details.md</c>, п. 0.4). Отклик — префаб-GO
    /// из компонентов Unity (<see cref="HapticPlayer" />, звук, подсветка…), выбранный по правилу «самое частное побеждает»:
    /// <see cref="InteractionFeedbackOverride" /> предмета → якоря → <see cref="InteractionFeedbackConfig" />.
    /// <list type="bullet">
    /// <item><b>Готовность</b> (состояние, только своя рука): пустая рука может взять предмет — кандидат хвата SDK
    /// (<c>UxrGrabManager.TryGetGrabCandidate</c>, SDK-патч 67): предмет в якоре или прокси — отклик якоря, свободный — «в
    /// досягаемости». Рука с предметом может положить его в якорь — кандидат якоря SDK (<see cref="AnchorPlacementReadiness" />).
    /// Пока состояние держится, экземпляр отклика включён у цели (якоря или предмета) с рукой в <see cref="HapticPlayer" />.</item>
    /// <item><b>События</b> (взят, уложен, отпущен): разовый отклик. Своя рука — весь префаб; чужой игрок — только слоты с
    /// <see cref="NetworkedFeedback" />.</item>
    /// </list>
    /// Своих расчётов готовности нет — ответ SDK, тот же, что решит grip. Экземпляры берутся из пула; пока экземпляр в пуле,
    /// он лежит под неактивным контейнером, поэтому включается только у цели, уже зная руку. Ставит себя сам; в batch mode — нет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionFeedback : MonoBehaviour
    {
        private sealed class Active
        {
            public GameObject Prefab;
            public Transform Target;
            public HapticHandRole Role;
            public GameObject Instance;
        }

        private sealed class OneShot
        {
            public GameObject Prefab;
            public GameObject Instance;
            public float Until;
        }

        private InteractionFeedbackConfig _config;
        private UxrGrabManager _manager;
        private AnchorPlacementReadiness _placement;
        private Transform _poolRoot;

        private readonly Active[] _ready = { new Active(), new Active() };
        private readonly List<OneShot> _oneShots = new List<OneShot>();
        private readonly Dictionary<GameObject, Stack<GameObject>> _pool = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly Dictionary<UxrGrabbableObjectAnchor, GameObject> _anchorFeedback = new Dictionary<UxrGrabbableObjectAnchor, GameObject>();
        private readonly Dictionary<UxrGrabbableObject, UxrGrabbableObjectAnchor> _proxyAnchors = new Dictionary<UxrGrabbableObject, UxrGrabbableObjectAnchor>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isBatchMode) return;
            if (FindAnyObjectByType<InteractionFeedback>() != null) return;

            var host = new GameObject(nameof(InteractionFeedback));
            DontDestroyOnLoad(host);
            host.AddComponent<InteractionFeedback>();
        }

        private void Awake()
        {
            _config = InteractionFeedbackConfig.Instance;
            var pool = new GameObject("Pool");
            pool.SetActive(false);
            pool.transform.SetParent(transform, false);
            _poolRoot = pool.transform;
        }

        private void OnEnable()
        {
            InteractionFeedbackConfig.Changed += OnFeedbackEdited;
            HapticPlayer.AssetEdited += OnFeedbackEdited;
        }

        private void OnDisable()
        {
            InteractionFeedbackConfig.Changed -= OnFeedbackEdited;
            HapticPlayer.AssetEdited -= OnFeedbackEdited;
            Subscribe(null);
            ReleaseAll();
        }

        private void Update()
        {
            Subscribe(UxrGrabManager.HasInstance ? UxrGrabManager.Instance : null);
            ExpireOneShots();

            UxrAvatar local = UxrAvatar.LocalAvatar;
            if (_manager == null || local == null || local.AvatarMode != UxrAvatarMode.Local)
            {
                ReleaseReady();
                return;
            }

            UpdateHand(local, UxrHandSide.Left);
            UpdateHand(local, UxrHandSide.Right);
        }

        // ---------- Готовность ----------

        private void UpdateHand(UxrAvatar avatar, UxrHandSide side)
        {
            UxrGrabber grabber = avatar.GetGrabber(side);
            Transform target = null;
            HapticHandRole role = HapticHandRole.Primary;
            GameObject prefab = grabber != null ? ResolveReady(grabber, out target, out role) : null;

            Active active = _ready[(int)side];
            if (prefab == active.Prefab && target == active.Target && role == active.Role && active.Instance != null) return;

            Release(active.Prefab, active.Instance);
            active.Prefab = prefab;
            active.Target = target;
            active.Role = role;
            active.Instance = prefab != null ? Spawn(prefab, target, side, role, local: true) : null;

            if (GameLog.Player.IsEnabled(LogLevel.Verbose))
                GameLog.Player.Verbose(prefab != null
                    ? $"[InteractionFeedback] {side}: готов — '{target.name}', отклик '{prefab.name}', {role}"
                    : $"[InteractionFeedback] {side}: готовности нет", this);
        }

        /// <summary>
        /// Отклик готовности руки, его цель и роль руки — или null, если рука ничего не может взять или положить с откликом.
        /// Роль Secondary — пустая рука тянется к предмету, который уже держит другая рука того же игрока (цевьё, затвор).
        /// </summary>
        private GameObject ResolveReady(UxrGrabber grabber, out Transform target, out HapticHandRole role)
        {
            target = null;
            role = HapticHandRole.Primary;

            if (grabber.GrabbedObject != null)
            {
                // Рука с предметом: якорь, куда SDK положит предмет при отпускании.
                foreach (UxrGrabbableObjectAnchor anchor in UxrGrabbableObjectAnchor.EnabledComponents)
                {
                    GameObject feedback = AnchorFeedback(anchor);
                    if (feedback == null || !_placement.TryGetReadyGrabber(anchor, out UxrGrabber ready) || ready != grabber) continue;
                    target = anchor.transform;
                    return feedback;
                }
                return null;
            }

            // Пустая рука: предмет, который возьмёт grip (свободный, в якоре или прокси якоря).
            if (!_manager.TryGetGrabCandidate(grabber, out UxrGrabbableObject item, out _)) return null;
            UxrGrabbableObjectAnchor owner = item.CurrentAnchor != null ? item.CurrentAnchor : ProxyAnchor(item);
            GameObject own = Override(item)?.Ready;

            if (owner != null)
            {
                target = owner.transform;
                return own != null ? own : AnchorFeedback(owner);
            }

            target = item.transform;
            UxrGrabber other = grabber.Avatar != null ? grabber.Avatar.GetGrabber(grabber.Side == UxrHandSide.Left ? UxrHandSide.Right : UxrHandSide.Left) : null;
            if (IsPartOfHeld(item, other != null ? other.GrabbedObject : null)) role = HapticHandRole.Secondary;
            return own != null ? own : _config.ItemInReach;
        }

        /// <summary>
        /// Предмет — то, что держит другая рука, или его часть (затвор, помпа, цевьё — дети оружия): тогда пустая рука
        /// тянется второй рукой к тому же предмету.
        /// </summary>
        internal static bool IsPartOfHeld(UxrGrabbableObject item, UxrGrabbableObject heldByOtherHand) =>
            item != null && heldByOtherHand != null && item.transform.IsChildOf(heldByOtherHand.transform);

        /// <summary>Отклик готовности якоря: своё у якоря → роль якоря в конфиге. Кэш: роль якоря за игру не меняется.</summary>
        private GameObject AnchorFeedback(UxrGrabbableObjectAnchor anchor)
        {
            if (!_anchorFeedback.TryGetValue(anchor, out GameObject feedback))
            {
                GameObject own = Override(anchor)?.Ready;
                feedback = own != null ? own : _config.AnchorReady(AnchorRole.Get(anchor));
                _anchorFeedback[anchor] = feedback;
            }
            return feedback;
        }

        /// <summary>Якорь, чей прокси — этот предмет (прокси кармана берётся вместо спрятанного содержимого).</summary>
        private UxrGrabbableObjectAnchor ProxyAnchor(UxrGrabbableObject item)
        {
            if (_proxyAnchors.TryGetValue(item, out UxrGrabbableObjectAnchor anchor) && anchor != null && anchor.GrabProxy == item) return anchor;
            anchor = null;
            foreach (UxrGrabbableObjectAnchor candidate in UxrGrabbableObjectAnchor.EnabledComponents)
            {
                if (candidate.GrabProxy != item) continue;
                anchor = candidate;
                break;
            }
            _proxyAnchors[item] = anchor;
            return anchor;
        }

        private static InteractionFeedbackOverride Override(Component component)
        {
            var own = component != null ? component.GetComponent<InteractionFeedbackOverride>() : null;
            return own != null ? own : null;
        }

        // ---------- События ----------

        private void Subscribe(UxrGrabManager manager)
        {
            if (manager == _manager) return;
            if (_manager != null)
            {
                _manager.ObjectGrabbed -= OnGrabbed;
                _manager.ObjectPlaced -= OnPlaced;
                _manager.ObjectReleased -= OnReleased;
            }
            _placement?.Dispose();
            _placement = null;
            _manager = manager;
            if (_manager != null)
            {
                _manager.ObjectGrabbed += OnGrabbed;
                _manager.ObjectPlaced += OnPlaced;
                _manager.ObjectReleased += OnReleased;
                _placement = new AnchorPlacementReadiness(_manager, a => a != null);
            }
        }

        private void OnGrabbed(object sender, UxrManipulationEventArgs e) =>
            PlayEvent(e, Override(e?.GrabbableObject)?.Grab, _config.ItemGrab, e?.GrabbableObject?.transform);

        private void OnPlaced(object sender, UxrManipulationEventArgs e) =>
            PlayEvent(e, Override(e?.GrabbableObject)?.Place, _config.ItemPlace, e?.GrabbableAnchor?.transform);

        private void OnReleased(object sender, UxrManipulationEventArgs e) =>
            PlayEvent(e, Override(e?.GrabbableObject)?.Release, _config.ItemRelease, e?.GrabbableObject?.transform);

        private void PlayEvent(UxrManipulationEventArgs e, GameObject own, GameObject fallback, Transform target)
        {
            // Перехват второй рукой и смена руки — не новое событие предмета.
            if (e == null || e.Grabber == null || !e.IsGrabbedStateChanged || target == null) return;
            GameObject prefab = own != null ? own : fallback;
            if (prefab == null) return;

            bool local = HapticService.IsLocalHand(e.Grabber);
            if (!local && prefab.GetComponentInChildren<NetworkedFeedback>(true) == null) return;
            GameObject instance = Spawn(prefab, target, e.Grabber.Side, HapticHandRole.Primary, local);
            _oneShots.Add(new OneShot { Prefab = prefab, Instance = instance, Until = Time.unscaledTime + _config.OneShotLifetime });
        }

        private void ExpireOneShots()
        {
            for (int i = _oneShots.Count - 1; i >= 0; i--)
            {
                OneShot shot = _oneShots[i];
                if (shot.Instance != null && Time.unscaledTime < shot.Until) continue;
                Release(shot.Prefab, shot.Instance);
                _oneShots.RemoveAt(i);
            }
        }

        // ---------- Пул ----------

        /// <summary>
        /// Экземпляр отклика у цели. Рука задаётся до включения (экземпляр лежит под неактивным пулом). Для события чужого
        /// игрока остаются активными только слоты с <see cref="NetworkedFeedback" />.
        /// </summary>
        private GameObject Spawn(GameObject prefab, Transform target, UxrHandSide side, HapticHandRole role, bool local)
        {
            GameObject instance = _pool.TryGetValue(prefab, out Stack<GameObject> stack) && stack.Count > 0
                ? stack.Pop()
                : Instantiate(prefab, _poolRoot, false);

            foreach (HapticPlayer player in instance.GetComponentsInChildren<HapticPlayer>(true))
            {
                if (local) player.Bind(side, role);
                else player.Unbind();
            }
            foreach (Transform child in instance.transform)
                child.gameObject.SetActive(local || child.GetComponent<NetworkedFeedback>() != null);

            instance.transform.SetParent(target, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
        }

        private void Release(GameObject prefab, GameObject instance)
        {
            if (instance == null) return;
            instance.transform.SetParent(_poolRoot, false);
            foreach (Transform child in instance.transform) child.gameObject.SetActive(true);
            if (prefab == null) Destroy(instance);
            else
            {
                if (!_pool.TryGetValue(prefab, out Stack<GameObject> stack)) _pool[prefab] = stack = new Stack<GameObject>();
                stack.Push(instance);
            }
        }

        private void ReleaseReady()
        {
            foreach (Active active in _ready)
            {
                Release(active.Prefab, active.Instance);
                active.Prefab = null;
                active.Target = null;
                active.Instance = null;
            }
        }

        private void ReleaseAll()
        {
            ReleaseReady();
            foreach (OneShot shot in _oneShots) Release(shot.Prefab, shot.Instance);
            _oneShots.Clear();
        }

        /// <summary>Правка конфига или префаба отклика в Play: пересоздать экземпляры, чтобы новые значения зазвучали сразу.</summary>
        private void OnFeedbackEdited(Object edited)
        {
            ReleaseAll();
            foreach (Stack<GameObject> stack in _pool.Values)
                foreach (GameObject instance in stack)
                    if (instance != null) Destroy(instance);
            _pool.Clear();
            _anchorFeedback.Clear();
        }
    }
}
