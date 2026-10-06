// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 37: настройки ног из клипов (раздел «Ноги» UxrStandardAvatarController).
// --------------------------------------------------------------------------------------------------------------------
using System;
using UnityEngine;

namespace UltimateXR.Animation.IK
{
    /// <summary>Приёмник предупреждений ног; игра направляет его в GameLog (SDK не зависит от игровой сборки).</summary>
    public static class UxrLegsDiagnostics
    {
        public static Action<string, UnityEngine.Object> WarningSink;
        public static void Warn(string message, UnityEngine.Object context) => WarningSink?.Invoke(message, context);
    }

    /// <summary>
    ///     VR Battlegrounds patch 37: настройки ног аватара — решатель ноги (<see cref="UxrLegIKSolver" />), клипы ходьбы на
    ///     копии рига и шаги (<see cref="UxrLegLocomotion" />). Хранятся в <c>UxrStandardAvatarController</c> рядом с IK
    ///     тела и рук; работает ими <see cref="UxrAnimatedLegs" />. Читаются каждый кадр — правка в Play видна сразу
    ///     (кроме копии рига и контроллера: они берутся при появлении копии).
    /// </summary>
    [Serializable]
    public sealed class UxrLegsSettings
    {
        #region Inspector Properties/Serialized Fields

        [Header("Решатель ноги")]
        [Tooltip("Куда смотрит колено: 0 — вперёд по тазу (колени всегда вперёд), 1 — за носком стопы (колено поворачивается вместе со стопой, например при шаге вбок). Обычно 0,5; меньше — колени меньше разворачиваются по ходу.")]
        [Range(0f, 1f)]
        public float kneeFollowsFoot = 0.5f;

        [Tooltip("Сколько ноги слушаются клипов: 1 — стопы ставит клип (обычно), 0 — ноги стоят в позе модели. Промежуточные значения — для отладки.")]
        [Range(0f, 1f)]
        public float legsWeight = 1f;

        [Header("Клипы ходьбы")]
        [Tooltip("Невидимая копия скелета этого аватара, на которой играют клипы ходьбы; с неё берутся позы стоп. Печётся утилитой «Tools/VR Battlegrounds/Avatars/Setup Legs», руками не менять.")]
        public GameObject locomotionRig;

        [Tooltip("Набор анимаций ходьбы (стойки без оружия / пистолет / винтовка, повороты на месте). Пусто — контроллер самой копии рига. Собирается утилитой «Setup Legs».")]
        public RuntimeAnimatorController locomotionController;

        [Tooltip("Резкий отрыв тела от ног, м, после которого ноги не догоняют шагом, а сразу переставляются под тело (телепорт, респаун). Обычно 1–2; меньше — ноги «прыгают» при быстрых рывках.")]
        public float teleportDistance = 1.5f;

        [Header("Стопы и пол")]
        [Tooltip("Высота лодыжки над подошвой ботинка, м (без масштаба аватара); своя у каждой модели, считается утилитой «Setup Legs» по мешу обуви. Больше — подошва висит над полом, меньше — уходит в пол. MEF: 0,135.")]
        public float ankleHeight = 0.135f;

        [Tooltip("Ставить подошву на пол. Клипы сняты на другом скелете, и без этого стопа может висеть или уходить в пол на несколько сантиметров. Обычно включено.")]
        public bool groundFeet = true;

        [Tooltip("Скорость стопы, м/с, ниже которой стопа считается стоящей на полу (тогда по ней выравнивается высота подошвы). Обычно 0,2–0,3; больше — подошва «прилипает» к полу и в начале шага.")]
        public float plantedSpeed = 0.25f;

        [Tooltip("Плавность выравнивания подошвы по полу, с. Обычно 0,05; больше — стопа медленнее садится на пол после шага.")]
        public float groundSmoothTime = 0.05f;

        [Header("Присед и сидение (Legs_Crouch: 0 стоя, 1 колено, 2 сидя)")]
        [Tooltip("Доля роста игрока: опускание головы меньше этой доли — стоя (Legs_Crouch 0). Обычно 0,12; меньше — присед начинается от лёгкого наклона.")]
        [Range(0f, 0.4f)]
        public float crouchDeadZone = 0.12f;

        [Tooltip("Сглаживание Legs_Crouch, с. Обычно 0,15; больше — поза догоняет голову медленнее.")]
        [Range(0.01f, 1f)]
        public float crouchSmoothTime = 0.15f;

        [Tooltip("С какого Legs_Crouch ноги ниже таза начинают брать локальную позу клипа вместо решателя ноги (полностью — с 1). Обычно 0,5.")]
        [Range(0f, 1f)]
        public float clipLegsStart = 0.5f;

        [Tooltip("Глубже этого Legs_Crouch шагов и поворотов нет, root motion выбрасывается. Обычно 0,5.")]
        [Range(0f, 2f)]
        public float locomotionCrouchLimit = 0.5f;

        [Header("Шаги")]
        [Tooltip("Когда и как переставлять ноги вслед за игроком (перенос локомоции VRIK Animated).")]
        public UxrLegLocomotion locomotion = new UxrLegLocomotion();

        #endregion
    }
}
