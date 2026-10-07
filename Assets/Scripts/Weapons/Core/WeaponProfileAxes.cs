using System;

namespace VrBattlegrounds.Weapons.Core
{
    /// <summary>Режим огня спуска. Берётся из <c>UxrShotCycle</c> при настройке хоста, в профиле не дублируется.</summary>
    public enum WeaponFireMode { Semi, Auto, Manual }

    /// <summary>Как устроен боезапас. <see cref="MagazineOnly"/> — бывший LegacyAmmo: патронника нет.</summary>
    public enum WeaponAmmoCapability { MagazineOnly, DetachableMagazineChamber, FixedStoreChamber }

    /// <summary>Есть ли у оружия ручной ход затвора/помпы.</summary>
    public enum WeaponPhysicalCapability { NoAction, ActionTravel }

    /// <summary>Кто досылает патрон: только рука, вставка магазина или первое нажатие спуска (без выстрела).</summary>
    public enum WeaponChamberPolicy { ManualReturn, AutoOnMagazineInsert, TriggerAssistPrepareOnly }

    /// <summary>Поза Action после последнего патрона: на задержке или в покое.</summary>
    public enum WeaponEmptyPose { HoldOpen, ReturnToRest }

    /// <summary>
    /// Оси профиля одного ствола и числа его rig (план п. 3.1). Неизменяемый снимок, который хост
    /// собирает при настройке из профиля, SDK-спуска и проверенного rig. Машина других настроек не читает.
    ///
    /// Прогрессы и <see cref="Epsilon"/> — в долях хода покой→зад (0 — покой, 1 — зад).
    /// Скорости возврата — в единицах исполнителя позы, машина их только передаёт.
    /// </summary>
    public readonly struct WeaponProfileAxes
    {
        public readonly WeaponFireMode FireMode;
        public readonly WeaponAmmoCapability Ammo;
        public readonly WeaponPhysicalCapability Physical;
        public readonly WeaponChamberPolicy Policy;
        public readonly WeaponEmptyPose EmptyPose;

        /// <summary>Порог извлечения: минимальный прогресс всех обязательных деталей (бывший <c>_slideThreshold</c>).</summary>
        public readonly float ExtractionGate;

        /// <summary>Допуск движения ручки в долях хода.</summary>
        public readonly float Epsilon;

        /// <summary>Скорость пружины после отпускания ручки посреди ручного цикла.</summary>
        public readonly float SpringReturnSpeed;

        /// <summary>Скорость возврата при подготовке без руки (вставка магазина, TriggerAssist, бот).</summary>
        public readonly float AutoReturnSpeed;

        public readonly bool HasFireClip;
        public readonly float FireClipDuration;
        public readonly bool HasEmptyClip;
        public readonly float EmptyClipDuration;

        /// <summary>Время Empty-клипа, в которое Action стоит в проверенной задней позе. Меньше нуля — не проверено.</summary>
        public readonly float EmptyRearTime;

        /// <summary>Хост нашёл привязанный fixed store и окно приёма патрона (<c>CartridgeIntake</c>).</summary>
        public readonly bool HasFixedStoreIntake;

        public WeaponProfileAxes(WeaponFireMode fireMode, WeaponAmmoCapability ammo, WeaponPhysicalCapability physical,
            WeaponChamberPolicy policy, WeaponEmptyPose emptyPose,
            float extractionGate, float epsilon, float springReturnSpeed, float autoReturnSpeed,
            bool hasFireClip = false, float fireClipDuration = 0f,
            bool hasEmptyClip = false, float emptyClipDuration = 0f, float emptyRearTime = -1f,
            bool hasFixedStoreIntake = false)
        {
            FireMode = fireMode; Ammo = ammo; Physical = physical; Policy = policy; EmptyPose = emptyPose;
            ExtractionGate = extractionGate; Epsilon = epsilon;
            SpringReturnSpeed = springReturnSpeed; AutoReturnSpeed = autoReturnSpeed;
            HasFireClip = hasFireClip; FireClipDuration = fireClipDuration;
            HasEmptyClip = hasEmptyClip; EmptyClipDuration = emptyClipDuration; EmptyRearTime = emptyRearTime;
            HasFixedStoreIntake = hasFixedStoreIntake;
        }

        /// <summary>Затвор остаётся на задержке (HoldOpen имеет смысл только при ручном ходе).</summary>
        public bool HoldsOpen => Physical == WeaponPhysicalCapability.ActionTravel && EmptyPose == WeaponEmptyPose.HoldOpen;

        /// <summary>
        /// Недопустимые сочетания осей (план п. 3.1) и непроверенные числа rig. Машина с таким
        /// профилем не создаётся: ошибка конфигурации видна при настройке, а не в бою.
        /// </summary>
        public static bool TryValidate(in WeaponProfileAxes a, out string error)
        {
            error = null;
            if (!Enum.IsDefined(typeof(WeaponFireMode), a.FireMode) || !Enum.IsDefined(typeof(WeaponAmmoCapability), a.Ammo) ||
                !Enum.IsDefined(typeof(WeaponPhysicalCapability), a.Physical) || !Enum.IsDefined(typeof(WeaponChamberPolicy), a.Policy) ||
                !Enum.IsDefined(typeof(WeaponEmptyPose), a.EmptyPose))
                error = "Профиль содержит неизвестное значение оси.";
            else if (a.Ammo == WeaponAmmoCapability.MagazineOnly && a.Policy != WeaponChamberPolicy.AutoOnMagazineInsert)
                error = "MagazineOnly не имеет патронника: допустима только политика AutoOnMagazineInsert.";
            else if (a.Physical == WeaponPhysicalCapability.NoAction && a.Policy == WeaponChamberPolicy.ManualReturn)
                error = "NoAction не поддерживает ManualReturn: дослать рукой нечем.";
            else if (a.Physical == WeaponPhysicalCapability.NoAction && a.EmptyPose == WeaponEmptyPose.HoldOpen)
                error = "NoAction не поддерживает HoldOpen: удерживать нечего.";
            else if (a.Ammo == WeaponAmmoCapability.FixedStoreChamber && !a.HasFixedStoreIntake)
                error = "FixedStoreChamber требует привязанного fixed store и окна приёма патрона.";
            else if (a.HasFireClip && !Positive(a.FireClipDuration))
                error = "Fire-клип должен иметь положительную длительность.";
            else if (a.HasEmptyClip && !Positive(a.EmptyClipDuration))
                error = "Empty-клип должен иметь положительную длительность.";
            else if (a.Physical == WeaponPhysicalCapability.ActionTravel &&
                     (!Finite(a.ExtractionGate) || a.ExtractionGate <= 0f || a.ExtractionGate > 1f))
                error = "Порог извлечения должен лежать в (0; 1].";
            else if (a.Physical == WeaponPhysicalCapability.ActionTravel && (!Positive(a.Epsilon) || a.Epsilon >= 0.5f))
                error = "Допуск хода должен лежать в (0; 0,5).";
            else if (a.Physical == WeaponPhysicalCapability.ActionTravel && (!Positive(a.SpringReturnSpeed) || !Positive(a.AutoReturnSpeed)))
                error = "Скорости возврата должны быть положительными.";
            else if (a.HoldsOpen && (!a.HasEmptyClip || !Finite(a.EmptyRearTime) || a.EmptyRearTime < 0f || a.EmptyRearTime > a.EmptyClipDuration))
                error = "HoldOpen требует Empty-клипа и проверенного времени задней позы внутри него.";
            return error == null;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Positive(float value) => Finite(value) && value > 0f;
    }
}
