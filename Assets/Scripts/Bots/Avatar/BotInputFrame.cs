using UnityEngine;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Намерения бота на один кадр в режиме <see cref="BotBodyMode.PlayerFlow"/> — то, что у человека приходит
    /// со шлема и контроллеров: поза корня (точка пола и курс), голова в пространстве трекинга (локально к пивоту
    /// <c>UxrAvatar.CameraController</c>, как HMD), мировые позы двух кистей, спуск, хват, сброс.
    /// Собирает <see cref="BotRuntimeController"/> в Update, исполняют <see cref="BotAvatarDriver"/> (позы) и
    /// <see cref="BotHands"/> (хват/сброс). Значимый тип без ссылок на сцену — кадр не аллоцирует.
    /// </summary>
    public struct BotInputFrame
    {
        /// <summary>Номер кадра Unity, в котором собран.</summary>
        public int Frame;

        public Vector3 RootPosition;
        public Quaternion RootRotation;

        /// <summary>Камера в пространстве пивота камеры (метры, как трекинг HMD).</summary>
        public Vector3 HeadPosition;
        public Quaternion HeadRotation;

        /// <summary>Мировая поза кости кисти (<c>UxrAvatar.LeftHandBone</c>); false — кисть не пишется.</summary>
        public bool HasLeftHand;
        public Pose LeftHand;
        public bool HasRightHand;
        public Pose RightHand;

        /// <summary>Спуск: Blaze попросил выстрел (исполняет <see cref="BotGunner"/> после всех стадий).</summary>
        public bool Trigger;

        /// <summary>Хват основной рукояти правой рукой: кисть подведена к снапу.</summary>
        public bool GrabPrimary;

        /// <summary>Хват поддержки левой рукой: основная уже в руке, кисть на снапе поддержки.</summary>
        public bool GrabSupport;

        /// <summary>Сброс: в руке предмет, который не является выбранным оружием.</summary>
        public bool Release;
    }
}
