using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// До включения физики SDK выводит внутренний патрон в сохранённую безопасную позу.
    /// Якорь владеет подпиской: она покрывает стартовый и любой заменённый патрон.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UxrGrabbableObjectAnchor))]
    public sealed class WeaponInternalAmmoEjection : MonoBehaviour
    {
        [Tooltip("DropAlign патрона при выбросе; builder проверяет зазор с коллайдерами корпуса.")]
        [SerializeField] private Transform _ejectionPose;

        public Transform EjectionPose => _ejectionPose;

        private UxrGrabbableObjectAnchor _anchor;
        private UxrGrabbableObject _magazine;

        private void OnEnable()
        {
            _anchor = GetComponent<UxrGrabbableObjectAnchor>();
            RefreshMagazineSubscription();
        }

        private void OnDisable()
        {
            if (_magazine != null) _magazine.Removing -= HandleRemoving;
            _magazine = null;
        }

        private void OnTransformChildrenChanged()
        {
            if (isActiveAndEnabled) RefreshMagazineSubscription();
        }

        private void RefreshMagazineSubscription()
        {
            UxrGrabbableObject current = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<UxrFirearmMag>() != null)
                {
                    current = child.GetComponent<UxrGrabbableObject>();
                    break;
                }
            }
            if (current == _magazine) return;
            if (_magazine != null) _magazine.Removing -= HandleRemoving;
            _magazine = current;
            if (_magazine != null) _magazine.Removing += HandleRemoving;
        }

        private void HandleRemoving(object sender, UxrManipulationEventArgs e)
        {
            if (!e.IsEjected || e.GrabbableAnchor != _anchor || _ejectionPose == null) return;

            // SDK вызывает shell.Removing и при silent replay, до dynamic/unparent/collider.
            // Это локальное следствие одного Remove, а не новое синхронизируемое действие:
            // guard автора здесь помешал бы копиям применить ту же безопасную позу.
            e.GrabbableObject.transform.position += _ejectionPose.position - e.GrabbableObject.DropAlignTransform.position;
        }
    }
}
