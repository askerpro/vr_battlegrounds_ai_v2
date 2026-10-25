using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Диагностика переносов своего аватара: одна строка на каждый перенос корня
    /// (<see cref="UxrAvatar.GlobalAvatarMoved"/>: всё, что идёт через <c>MoveAvatarTo</c> UltimateXR, —
    /// дуга со стика, телепорт из меню отладки). Пишет откуда и куда переехал корень, где голова,
    /// пол под новым местом и идёт ли ещё перенос UltimateXR; корень не на полу — <c>Warning</c>.
    ///
    /// <para>
    /// Только наблюдает: позицию не меняет. Мелкие сдвиги (поворот на месте, доводка калибровки)
    /// не пишутся — порог <see cref="MinTranslation"/>.
    /// </para>
    /// </summary>
    public static class LocalAvatarMoveLog
    {
        /// <summary>Переносы корня короче этого не пишутся.</summary>
        public const float MinTranslation = 0.3f;

        /// <summary>Последний записанный перенос — для строк других детекторов (<see cref="AvatarPoseSanityMonitor"/>).</summary>
        public static string LastMove { get; private set; } = "не было";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => LastMove = "не было";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Серверу без графики своего аватара не бывает.
            if (Application.isBatchMode) return;

            UxrAvatar.GlobalAvatarMoved -= OnAvatarMoved;
            UxrAvatar.GlobalAvatarMoved += OnAvatarMoved;
        }

        private static void OnAvatarMoved(object sender, UxrAvatarMoveEventArgs e)
        {
            var avatar = sender as UxrAvatar;
            if (avatar == null || avatar != UxrAvatar.LocalAvatar) return;

            Vector3 delta = e.NewPosition - e.OldPosition;
            bool vertical = Mathf.Abs(delta.y) > FloorProbe.Tolerance;
            if (!vertical && delta.magnitude < MinTranslation) return;

            Vector3 root = e.NewPosition;
            Vector3 head = avatar.CameraPosition;
            bool teleporting = UxrManager.HasInstance && UxrManager.Instance.IsTeleportingLocalAvatar;

            string floorText;
            bool offFloor;
            if (FloorProbe.TryFindFloor(root, out float floorY, out Collider floor))
            {
                floorText = $"пол '{floor.name}' y={floorY:F2}";
                offFloor = Mathf.Abs(root.y - floorY) > FloorProbe.Tolerance;
            }
            else
            {
                floorText = "пола (Ground) под корнем нет";
                offFloor = true;
            }

            string line = $"[LocalAvatarMoveLog] {avatar.name}: корень {e.OldPosition:F2} → {root:F2} (Δ={delta.magnitude:F2} м), " +
                          $"голова {head:F2} (над корнем {head.y - root.y:F2} м), {floorText}, перенос UXR идёт: {(teleporting ? "да" : "нет")}.";
            LastMove = $"{Time.unscaledTime:F1} с от запуска: {e.OldPosition:F2} → {root:F2}{(offFloor ? ", не на полу" : "")}";

            if (offFloor) GameLog.PhysicalSpace.Warning(line + " Корень не на полу.", avatar);
            else GameLog.PhysicalSpace.Info(line, avatar);
        }
    }
}
