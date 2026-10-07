using System;
using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Economy;
using VrBattlegrounds.Player;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Один настоящий магазин на отдельном якоре оружейного слота. Спавном владеет ArsenalMagazineSupply.</summary>
    [DisallowMultipleComponent]
    public sealed class ArsenalMagazineOffer : MonoBehaviour
    {
        [SerializeField] private FirearmSlotController _slot;
        [SerializeField] private UxrGrabbableObjectAnchor _anchor;
        [SerializeField] private MeshFilter _surface;
        [SerializeField] private Vector3 _surfaceNormalLocal = Vector3.back;
        private UxrGrabbableObject _assigned;
        private bool _subscribed;
        public FirearmSlotController Slot => _slot;
        public UxrGrabbableObjectAnchor Anchor => _anchor;
        public MeshFilter Surface => _surface;
        public Vector3 SurfaceNormal => _surface != null ? _surface.transform.TransformDirection(_surfaceNormalLocal).normalized : Vector3.zero;
        public UxrGrabbableObject CurrentItem => _assigned != null && _assigned.CurrentAnchor == _anchor ? _assigned : null;
        public event Action<ArsenalMagazineOffer, UxrManipulationEventArgs> Taken;

        public void Configure(FirearmSlotController slot, UxrGrabbableObjectAnchor anchor, MeshFilter surface, Vector3 surfaceNormalLocal)
        {
            Unsubscribe();
            _slot = slot; _anchor = anchor; _surface = surface; _surfaceNormalLocal = surfaceNormalLocal;
            if (isActiveAndEnabled && Application.isPlaying) Subscribe();
        }

        private void OnEnable() { if (Application.isPlaying) Subscribe(); }
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (_subscribed || _anchor == null) return;
            _anchor.Removed += HandleRemoved;
            // Запас выдаётся сервером; ручной возврат не создаёт новую запись склада и не заменяет его предмет.
            _anchor.AddPlacingValidator(RejectManualPlacement);
            _subscribed = true;
        }
        private static bool RejectManualPlacement(UxrGrabbableObject item) => false;
        private void Unsubscribe()
        {
            if (!_subscribed || _anchor == null) return;
            _anchor.Removed -= HandleRemoved;
            _anchor.RemovePlacingValidator(RejectManualPlacement);
            _subscribed = false;
        }

        /// <summary>Бесплатные боеприпасы: допуск стены и её владельца, без цены оружия.</summary>
        public bool AllowsGrab(UxrGrabber grabber)
        {
            ArsenalWallController wall = _slot != null ? _slot.Wall : null;
            if (wall == null || !wall.CanTrade) return false;
            if (MatchEconomy.Current == null) return true;
            PlayerController player = grabber != null && grabber.Avatar != null
                ? grabber.Avatar.GetComponentInParent<PlayerController>() : null;
            return wall.OwnerSessionNetId != 0 && player != null && player.Session != null &&
                   player.Session.netId == wall.OwnerSessionNetId;
        }

        public void Assign(UxrGrabbableObject item)
        {
            if (item == null || _anchor == null) return;
            _assigned = item;
            // Styled supply сохраняет физический worldScale, как SDK возврат оружия.
            item.transform.SetParent(_anchor.transform, ArsenalPresentationApplicator.Resolve(_slot).IsStyled);
            ArsenalPresentationApplicator.ApplyMagazine(this, item.transform);
            item.SetNetworkAnchor(_anchor);
            if (item.RigidBodySource != null)
            {
                // Скорость кинематического тела Unity не принимает (предупреждение в консоль); SetNetworkAnchor
                // мог уже перевести тело в kinematic.
                if (!item.RigidBodySource.isKinematic)
                {
                    item.RigidBodySource.linearVelocity = Vector3.zero;
                    item.RigidBodySource.angularVelocity = Vector3.zero;
                }
                item.RigidBodySource.isKinematic = true;
            }
        }

        public void RefreshAvailability()
        {
            UxrGrabbableObject item = CurrentItem;
            if (item == null || !StateEventAuthority.IsWorldAuthority) return;
            var wall = _slot != null ? _slot.Wall : null;
            bool available = wall != null && wall.CanTrade && (MatchEconomy.Current == null || wall.OwnerSessionNetId != 0);
            if (item.IsGrabbable != available) item.IsGrabbable = available;
        }

        private void HandleRemoved(object sender, UxrManipulationEventArgs e)
        {
            if (e.GrabbableObject != _assigned) return;
            // SDK уже завершил перепарентинг при GrabObject. При UseParenting=false убираем только остаточную связь со станцией.
            if (e.GrabbableObject.transform.IsChildOf(_anchor.transform))
                e.GrabbableObject.transform.SetParent(null, true);
            Taken?.Invoke(this, e);
        }

        /// <summary>Двигает только свободно выставляемый предмет по нормали до касания реальной поверхности.</summary>
        public void FitToSurface(Transform item)
        {
            if (_surface == null || _surface.sharedMesh == null || item == null) return;
            Vector3 normal = _surface.transform.TransformDirection(_surfaceNormalLocal).normalized;
            float plane = Support(_surface.sharedMesh.bounds, _surface.transform, normal, true);
            if (TrySupport(item, normal, false, out float nearest)) item.position += normal * (plane + .001f - nearest);
        }

        public static bool TrySupport(Transform root, Vector3 direction, bool maximum, out float value)
        {
            value = maximum ? float.NegativeInfinity : float.PositiveInfinity;
            bool found = false;
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
                if (mesh.sharedMesh != null && mesh.gameObject.activeInHierarchy &&
                    mesh.TryGetComponent<Renderer>(out var renderer) && renderer.enabled)
                {
                    float support = MeshSupport(mesh.sharedMesh, mesh.transform, direction, maximum);
                    value = maximum ? Mathf.Max(value, support) : Mathf.Min(value, support); found = true;
                }
            foreach (var mesh in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (mesh.sharedMesh != null && mesh.gameObject.activeInHierarchy && mesh.enabled)
                {
                    float support = Support(mesh.localBounds, mesh.transform, direction, maximum);
                    value = maximum ? Mathf.Max(value, support) : Mathf.Min(value, support); found = true;
                }
            return found;
        }

        private static float MeshSupport(Mesh mesh, Transform frame, Vector3 direction, bool maximum)
        {
            // Поворот магазина делает угол AABB пустым пространством. Прижимаем реальные вершины;
            // непрочитанный будущий источник сохраняет консервативную границу, а не пересечение.
            if (!mesh.isReadable) return Support(mesh.bounds, frame, direction, maximum);
            Vector3 localDirection = frame.localToWorldMatrix.transpose.MultiplyVector(direction);
            float offset = Vector3.Dot(frame.position, direction);
            float value = maximum ? float.NegativeInfinity : float.PositiveInfinity;
            foreach (Vector3 vertex in mesh.vertices)
            {
                float candidate = offset + Vector3.Dot(vertex, localDirection);
                value = maximum ? Mathf.Max(value, candidate) : Mathf.Min(value, candidate);
            }
            return value;
        }

        public static float Support(Bounds bounds, Transform frame, Vector3 direction, bool maximum)
        {
            float value = maximum ? float.NegativeInfinity : float.PositiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float candidate = Vector3.Dot(frame.TransformPoint(corner), direction);
                value = maximum ? Mathf.Max(value, candidate) : Mathf.Min(value, candidate);
            }
            return value;
        }
    }
}
