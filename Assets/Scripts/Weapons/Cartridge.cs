using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UltimateXR.Avatar;
using UltimateXR.Core.StateSync;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Политика физической единицы патрона; SDK хранит единственный consumed marker.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(UxrFirearmAmmoUnit), typeof(UxrGrabbableObject))]
    public sealed class Cartridge : MonoBehaviour
    {
        [SerializeField] private string _ammoType;
        // Сколько таких патронов стоит одного магазина (ёмкость оружия): предел кармана на тип — в магазинах.
        [SerializeField] private int _magazineEquivalent = 1;
        private UxrFirearmAmmoUnit _unit;
        private UxrGrabbableObject _grab;
        private bool _hidden;
        private UxrGrabManager _manager;
        public UxrAvatar LastHandlingAvatar { get; private set; }
        public UxrGrabbableObjectAnchor LastPlacedAnchor { get; private set; }
        public string AmmoType => _ammoType;
        public int MagazineEquivalent => Mathf.Max(1, _magazineEquivalent);
        public UxrFirearmAmmoUnit Unit => _unit != null ? _unit : GetComponent<UxrFirearmAmmoUnit>();
        public UxrGrabbableObject Grabbable => _grab != null ? _grab : GetComponent<UxrGrabbableObject>();
        public bool IsAvailable => Unit != null && !Unit.HasBeenConsumed && isActiveAndEnabled;

        private void Awake() { _unit = GetComponent<UxrFirearmAmmoUnit>(); _grab = GetComponent<UxrGrabbableObject>(); }
        private void OnEnable()
        {
            if (UxrGrabManager.HasInstance && Unit != null && !Unit.HasBeenConsumed)
            { _manager = UxrGrabManager.Instance; _manager.StateChanged += HandleManipulation; }
            ProjectConsumedState();
        }
        private void OnDisable() { DisconnectHistory(); }
        private void Update() { ProjectConsumedState(); }
        private void DisconnectHistory()
        { if (_manager != null) _manager.StateChanged -= HandleManipulation; _manager = null; }
        private void HandleManipulation(object sender, UxrSyncEventArgs args)
        {
            if (!(args is UxrMethodInvokedSyncEventArgs method)) return;
            // Direct SDK event работает также для silent receiving replay; это evidence, не новый author.
            if ((method.MethodName == "GrabObject" || method.MethodName == "ReleaseObject") && method.Parameters.Length > 1 &&
                ReferenceEquals(method.Parameters[1], Grabbable) && method.Parameters[0] is UxrGrabber hand)
            { LastHandlingAvatar = hand.Avatar; LastPlacedAnchor = null; }
            if (method.MethodName == "PlaceObject" && method.Parameters.Length > 1 && ReferenceEquals(method.Parameters[0], Grabbable))
                LastPlacedAnchor = method.Parameters[1] as UxrGrabbableObjectAnchor;
        }

        private void ProjectConsumedState()
        {
            if (_hidden || Unit == null || !Unit.HasBeenConsumed) return;
            _hidden = true;
            DisconnectHistory();
            // Read-only projection без синхронизируемого IsGrabbable setter и без локального despawn.
            if (Grabbable != null) Grabbable.enabled = false;
            foreach (Collider collider in GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.isKinematic = true;
            }
            // Exact server/offline retirement ведёт receiver после закрытого SDK Place/commit.
        }
    }
}
