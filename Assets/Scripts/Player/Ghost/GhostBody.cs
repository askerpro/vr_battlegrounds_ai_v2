using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Универсальный призрак выбывшего игрока: шлем и корпус Cyborg и две кисти SmallHands из
    /// UltimateXR — без рук и без IK. Годится любому аватару: позу берёт у того, что есть у всех, —
    /// камеры (голова) и кистей <see cref="UxrAvatarHand"/>. Сам призрак — префаб
    /// <c>Prefabs/Player/Ghost/GhostBody</c> (<see cref="GhostModel"/>): смотреть и править там.
    /// Ссылку на него даёт <see cref="SpectatorController.GhostPrefab"/>.
    ///
    /// <para>
    /// <b>Зачем, а не копия тела.</b> Прежний призрак (Cyborg) — второй комплект всей геометрии
    /// аватара (14 скин-мешей, 83 тыс. треугольников) с прозрачным материалом: его надо собирать
    /// под каждый скин (у Optimized MEF его нет — выбывший просто исчезал), он скинится и
    /// перекрывает сам себя прозрачностью (дорого на Quest) и держится на IK всего тела.
    /// Здесь — два статичных меша (шлем, корпус; ~12 тыс. треугольников) и две кисти по 20 костей,
    /// один материал.
    /// </para>
    ///
    /// <para>
    /// <b>Кисти с пальцами</b> — потому что выбывший пользуется своим планшетом: позы кисти UltimateXR
    /// продолжает считать и у скрытого тела, призрак их повторяет. Коллайдеров у призрака нет:
    /// пули сквозь него проходят.
    /// </para>
    ///
    /// <para>
    /// Показывает и прячет его <see cref="SpectatorController"/>. Своя голова игроку не рисуется
    /// (камера внутри неё), кисти — рисуются: ими он работает с планшетом.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostBody : MonoBehaviour
    {
        private const float Alpha = 0.35f;

        /// <summary>Префаб призрака — из <see cref="SpectatorController.GhostPrefab"/>.</summary>
        public GhostModel Prefab { get; set; }

        private UxrAvatar _avatar;
        private GhostModel _model;

        public bool Visible => _model != null && _model.gameObject.activeSelf;

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
        }

        private void OnEnable() => UxrManager.AvatarsUpdated += OnAvatarsUpdated;

        private void OnDisable() => UxrManager.AvatarsUpdated -= OnAvatarsUpdated;

        private void OnDestroy()
        {
            if (_model != null) Destroy(_model.gameObject);
        }

        /// <summary>Показать или спрятать призрака. <paramref name="color"/> — цвет команды.</summary>
        public void SetVisible(bool visible, Color color)
        {
            if (visible) Build();
            if (_model == null) return;

            _model.gameObject.SetActive(visible);
            if (!visible) return;

            _model.SetColor(color, Alpha);
            _model.SetHeadVisible(_avatar == null || _avatar.AvatarMode != UxrAvatarMode.Local);
            _model.Pose(_avatar, transform);
        }

        private void OnAvatarsUpdated()
        {
            if (Visible) _model.Pose(_avatar, transform);
        }

        private void Build()
        {
            if (_model != null || _avatar == null) return;

            if (Prefab == null)
            {
                GameLog.Player.Warning($"[GhostBody] {name}: не назначен SpectatorController.GhostPrefab — призрака не будет.", this);
                return;
            }

            // Под аватаром — чтобы жить и умирать вместе с ним; позу призрак ставит сам в мировых координатах.
            _model = Instantiate(Prefab, transform, false);
            _model.name = "GhostBody";
            _model.gameObject.SetActive(false);
        }
    }
}
