using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Снаряжение погибшего отлетает от тела (T-35), а не падает под труп. Толчок ставится в момент
    /// гибели на <b>каждой</b> машине (событие смерти — везде, <see cref="PlayerController"/>): у
    /// выпавшего оружия нет своего сетевого трансформа, физику после отпускания каждая машина считает
    /// сама — одинаковый толчок от одного и того же события даёт одинаковый отлёт.
    ///
    /// <para>
    /// Толкается предмет, как только он стал физическим: из руки — сразу при отпускании, из кобуры —
    /// когда придёт снятие с якоря (у клиента — чуть позже, каналом состояния). Не стал за
    /// <see cref="Deadline"/> секунд (подобрали, изъяли) — толчок отменяется.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeathDropEjection : MonoBehaviour
    {
        /// <summary>Горизонтальная скорость отлёта, м/с.</summary>
        public const float SideSpeed = 2.2f;

        /// <summary>Подброс вверх, м/с.</summary>
        public const float UpSpeed = 1.2f;

        public const float Deadline = 1.5f;

        private Vector3 _velocity;
        private float _until;

        /// <summary>Скорость отлёта: по горизонтали от центра тела к предмету (или вперёд тела), плюс вверх.</summary>
        public static Vector3 VelocityFor(Vector3 bodyCenter, Vector3 itemPosition, Vector3 bodyForward)
        {
            Vector3 away = itemPosition - bodyCenter;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                away = bodyForward;
                away.y = 0f;
            }
            if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;

            return away.normalized * SideSpeed + Vector3.up * UpSpeed;
        }

        /// <summary>
        /// Погиб <paramref name="body"/>: всё его снаряжение — в руках и в кобурах — получит толчок, как
        /// только станет физическим. Зовётся на каждой машине до отпускания.
        /// </summary>
        public static void ScheduleFor(PlayerController body)
        {
            if (body == null) return;

            Vector3 center = body.transform.position + Vector3.up;
            Vector3 forward = body.transform.forward;

            foreach (UxrGrabber grabber in body.GetComponentsInChildren<UxrGrabber>(true))
                Schedule(grabber != null ? grabber.GrabbedObject : null, center, forward);

            foreach (UxrGrabbableObjectAnchor anchor in body.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
                if (AnchorRole.IsAvatarPocket(anchor)) Schedule(anchor.CurrentPlacedObject, center, forward);
        }

        private static void Schedule(UxrGrabbableObject item, Vector3 center, Vector3 forward)
        {
            if (item == null || !EquipmentStrip.IsEquipment(item)) return;

            Rigidbody body = item.RigidBodySource;
            GameObject host = body != null ? body.gameObject : item.gameObject;

            DeathDropEjection ejection = host.GetComponent<DeathDropEjection>();
            if (ejection == null) ejection = host.AddComponent<DeathDropEjection>();
            ejection._velocity = VelocityFor(center, host.transform.position, forward);
            ejection._until = Time.time + Deadline;
        }

        /// <summary>Толкнуть, если предмет уже физический. true — толчок применён или отменён.</summary>
        public bool TryApply()
        {
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity += _velocity;
                return true;
            }
            return Time.time > _until;
        }

        private void FixedUpdate()
        {
            if (TryApply()) Destroy(this);
        }
    }
}
