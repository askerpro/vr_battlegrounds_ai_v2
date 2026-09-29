using UltimateXR.Animation.IK;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Реакция тела на попадание (T-37, пункт 2): торс коротко отклоняется от пули — чисто визуально.
    /// Позиция игрока, камера, кисти и оружие не меняются (требование пользователя).
    ///
    /// <para>
    /// <b>Как, чтобы руки не уехали.</b> Позвоночник ставит тело UltimateXR (<c>UxrBodyIK</c>) от головы, поэтому
    /// наклон до решения IK был бы перезаписан. Наклон кладётся после (событие <c>AvatarUpdated</c>, стадия
    /// <see cref="UxrUpdateStage.PostProcess"/>), и сразу решается IK рук этого аватара ещё раз — кисти
    /// возвращаются к контроллерам, оружие в ладони; наклоняются грудь, плечи и голова модели. Лишний проход IK
    /// рук — только у задетого аватара и только <see cref="Duration"/> секунд.
    /// </para>
    ///
    /// <para>
    /// <b>Кому.</b> Только чужим аватарам: своё тело, которое гнётся без движения игрока, в VR неприятно. Без
    /// сети: попадание симулирует каждая машина (<see cref="PlayerController.HitReceivedLocal"/>). Отменённый
    /// урон (разминка) — без реакции.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitReaction : MonoBehaviour
    {
        /// <summary>Длительность реакции, с.</summary>
        public const float Duration = 0.25f;

        /// <summary>Нарастание до пика, с.</summary>
        public const float Attack = 0.05f;

        public const float MinAngle = 3f;
        public const float MaxAngle = 8f;

        private UxrAvatar _avatar;
        private Transform _torso;
        private UxrArmIKSolver[] _arms;
        private Vector3 _axis;
        private float _angle;
        private float _startedAt = float.NegativeInfinity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe()
        {
            PlayerController.HitReceivedLocal -= OnHit;
            PlayerController.HitReceivedLocal += OnHit;
        }

        /// <summary>Доля наклона во времени: быстрое нарастание, затухание к <see cref="Duration"/>.</summary>
        public static float Curve(float t)
        {
            if (t <= 0f || t >= Duration) return 0f;
            if (t < Attack) return t / Attack;
            float k = 1f - (t - Attack) / (Duration - Attack);
            return k * k * (3f - 2f * k); // плавное затухание
        }

        /// <summary>Угол наклона по силе толчка (см. <see cref="DeathImpact.Magnitude"/>).</summary>
        public static float AngleFor(float impulseMagnitude) =>
            Mathf.Lerp(MinAngle, MaxAngle, Mathf.InverseLerp(DeathImpact.BaseImpulse, DeathImpact.MaxImpulse, impulseMagnitude));

        /// <summary>Ось наклона: торс уходит по направлению пули (горизонталь), вращение вокруг перпендикуляра.</summary>
        public static Vector3 AxisFor(Vector3 shotDirection)
        {
            Vector3 flat = new Vector3(shotDirection.x, 0f, shotDirection.z);
            if (flat.sqrMagnitude < 1e-6f) return Vector3.zero;
            return Vector3.Cross(Vector3.up, flat.normalized);
        }

        private static void OnHit(PlayerController target, UxrDamageEventArgs e)
        {
            if (target == null || e == null || e.IsCanceled || e.DamageType != UxrDamageType.ProjectileHit) return;

            UxrAvatar avatar = target.GetComponent<UxrAvatar>();
            if (avatar == null || avatar.AvatarMode == UxrAvatarMode.Local) return;

            DeathImpact impact = DeathImpact.From(e, target.transform.position + Vector3.up);
            if (!impact.HasImpulse) return;

            HitReaction reaction = target.GetComponent<HitReaction>();
            if (reaction == null) reaction = target.gameObject.AddComponent<HitReaction>();
            reaction.Begin(impact.Impulse);
        }

        /// <summary>Начать реакцию на толчок <paramref name="impulse"/>. Открыт для тестов.</summary>
        public void Begin(Vector3 impulse)
        {
            Vector3 axis = AxisFor(impulse);
            if (axis == Vector3.zero) return;

            _axis = axis;
            _angle = AngleFor(impulse.magnitude);
            _startedAt = Time.time;
        }

        public bool IsActive => Time.time - _startedAt < Duration;

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
            if (_avatar != null)
            {
                var rig = _avatar.AvatarRig;
                _torso = rig.UpperChest != null ? rig.UpperChest : rig.Chest != null ? rig.Chest : rig.Spine;
                _arms = GetComponentsInChildren<UxrArmIKSolver>(true);
            }
        }

        private void OnEnable()
        {
            if (_avatar != null) _avatar.AvatarUpdated += OnAvatarUpdated;
        }

        private void OnDisable()
        {
            if (_avatar != null) _avatar.AvatarUpdated -= OnAvatarUpdated;
        }

        private void OnAvatarUpdated(object sender, UxrAvatarUpdateEventArgs e)
        {
            if (e.UpdateStage != UxrUpdateStage.PostProcess || _torso == null || !IsActive) return;
            Apply(Curve(Time.time - _startedAt));
        }

        /// <summary>Наклонить торс на долю <paramref name="weight"/> и вернуть кисти IK рук. Открыт для тестов.</summary>
        public void Apply(float weight)
        {
            if (_torso == null || weight <= 0f) return;

            _torso.rotation = Quaternion.AngleAxis(_angle * weight, _axis) * _torso.rotation;

            if (_arms == null) return;
            foreach (UxrArmIKSolver arm in _arms)
                if (arm != null && arm.isActiveAndEnabled) arm.SolveIK();
        }
    }
}
