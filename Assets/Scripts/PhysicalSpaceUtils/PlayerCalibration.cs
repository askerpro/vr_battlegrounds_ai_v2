using System;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.PhysicalSpaceUtils
{
    /// <summary>
    /// Калибровка игрока — <b>абсолютные</b> значения его физического пространства (T-50).
    ///
    /// <para>
    /// Единственный источник для всех производных величин аватара: пивот камеры, масштаб скелета,
    /// поля BodyIK, смещение рук. Их выводит <see cref="AvatarCalibrationApplier" /> на каждой машине
    /// для каждого аватара, поэтому здесь нет ни одной величины, зависящей от модели: рост — в метрах,
    /// а не отношением к <c>EyesBaseHeight</c> той модели, на которой калибровались (T-50 п. 3).
    /// </para>
    ///
    /// <para>
    /// Владелец — <c>PlayerSession</c> (SyncVar, единственный писатель — сервер). Поля публичные,
    /// потому что структуру сериализует Mirror (SyncVar и сообщение подключения).
    /// </para>
    /// </summary>
    [Serializable]
    public struct PlayerCalibration : IEquatable<PlayerCalibration>
    {
        /// <summary>На сколько поднят пивот камеры над базой префаба, метры. Ноль — пол не калибровался.</summary>
        public float FloorOffset;

        /// <summary>Рост глаз игрока над полом аватара, метры. Ноль — рост не калибровался.</summary>
        public float EyeHeight;

        /// <summary>Игрок откалибровал своё пространство по якорям карты: его место задано физически.</summary>
        public bool IsCalibrated;

        /// <summary>Единственная запись позы корня трекинга; кости/камера в неё не входят.</summary>
        public PlayerPlacement Placement;

        public PlayerCalibration(float floorOffset, float eyeHeight, bool isCalibrated)
        {
            FloorOffset = floorOffset;
            EyeHeight = eyeHeight;
            IsCalibrated = isCalibrated;
            Placement = PlayerPlacement.None;
        }

        /// <summary>Игрок не калибровался ничем.</summary>
        public static PlayerCalibration None => default(PlayerCalibration);

        /// <summary>Рост калибровался.</summary>
        public bool HasEyeHeight => EyeHeight > 0f;

        /// <summary>
        /// Масштаб скелета для модели с базовым ростом глаз <paramref name="eyesBaseHeight" />.
        /// Без калибровки роста или без известной базы модели — единица.
        /// </summary>
        public float ScaleFor(float eyesBaseHeight)
        {
            if (!HasEyeHeight || eyesBaseHeight <= 0f) return 1f;
            return EyeHeight / eyesBaseHeight;
        }

        public PlayerCalibration WithFloor(float floorOffset) => new PlayerCalibration(floorOffset, EyeHeight, IsCalibrated).WithPlacement(Placement);

        public PlayerCalibration WithEyeHeight(float eyeHeight) => new PlayerCalibration(FloorOffset, eyeHeight, IsCalibrated).WithPlacement(Placement);

        public PlayerCalibration WithCalibrated(bool isCalibrated) => new PlayerCalibration(FloorOffset, EyeHeight, isCalibrated).WithPlacement(Placement);

        public PlayerCalibration WithPlacement(PlayerPlacement placement)
        {
            PlayerCalibration result = this;
            result.Placement = placement;
            return result;
        }

        public bool Equals(PlayerCalibration other) =>
            FloorOffset.Equals(other.FloorOffset) && EyeHeight.Equals(other.EyeHeight) && IsCalibrated == other.IsCalibrated && Placement.Equals(other.Placement);

        public override bool Equals(object obj) => obj is PlayerCalibration other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = FloorOffset.GetHashCode();
                hash = hash * 397 ^ EyeHeight.GetHashCode();
                hash = hash * 397 ^ IsCalibrated.GetHashCode();
                return hash * 397 ^ Placement.GetHashCode();
            }
        }

        public static bool operator ==(PlayerCalibration a, PlayerCalibration b) => a.Equals(b);

        public static bool operator !=(PlayerCalibration a, PlayerCalibration b) => !a.Equals(b);

        public override string ToString() =>
            $"пол={FloorOffset:F3} м, рост глаз={(HasEyeHeight ? EyeHeight.ToString("F3") + " м" : "нет")}, по якорям={(IsCalibrated ? "да" : "нет")}";
    }

    /// <summary>
    /// Правила калибровки — <b>одно место</b> для всего проекта (T-50 этап 2).
    ///
    /// <para>
    /// Решает сервер (<c>PlayerSession.ServerAcceptCalibration</c>). Клиент вызывает те же функции,
    /// чтобы предсказать ответ сервера (нормализация) и не начинать заведомо отклонённую процедуру
    /// (запрет в бою) — это одна и та же функция, а не копия правила.
    /// </para>
    /// </summary>
    public static class PlayerCalibrationRules
    {
        /// <summary>
        /// Предел смещения пола по модулю, метры. Смещение двигает голову аватара, то есть точку
        /// попадания в неё; полтора метра с запасом перекрывают любую разницу полов.
        /// </summary>
        public const float MaxFloorOffset = 1.5f;

        /// <summary>
        /// Нижняя граница роста глаз, метры. Прежняя граница масштаба 0,5 давала 0,86–0,92 м
        /// на моделях проекта (EyesBaseHeight 1,71–1,84).
        /// </summary>
        public const float MinEyeHeight = 0.9f;

        /// <summary>
        /// Верхняя граница роста глаз, метры (прежний масштаб 1,5 — 2,57–2,76 м). Рост — это
        /// размер коллайдеров, то есть площадь попадания.
        /// </summary>
        public const float MaxEyeHeight = 2.5f;

        /// <summary>
        /// Приводит присланное значение к допустимому. Нечисловое отвергается целиком:
        /// NaN и бесконечность расходятся по матрицам трансформа.
        /// </summary>
        /// <returns><c>false</c> — принимать нельзя вовсе.</returns>
        public static bool TryNormalize(PlayerCalibration requested, out PlayerCalibration normalized)
        {
            if (!IsFinite(requested.FloorOffset) || !IsFinite(requested.EyeHeight) ||
                !PlayerPlacement.TryNormalize(requested.Placement, out PlayerPlacement placement))
            {
                normalized = PlayerCalibration.None;
                return false;
            }

            float floor = Mathf.Clamp(requested.FloorOffset, -MaxFloorOffset, MaxFloorOffset);

            // Ноль и меньше — «рост не калибровался», а не «очень маленький игрок».
            float eye = requested.EyeHeight > 0f ? Mathf.Clamp(requested.EyeHeight, MinEyeHeight, MaxEyeHeight) : 0f;

            normalized = new PlayerCalibration(floor, eye, requested.IsCalibrated).WithPlacement(placement);
            return true;
        }

        /// <summary>
        /// Изменение калибровки запрещено: живой игрок в бою режима без калибровки. Смещение координат
        /// посреди боя невозможно отличить от прохода через стену.
        /// </summary>
        public static bool IsLockedByCombat(GameMode mode, bool isEliminated, GameRole role)
        {
            return mode != null && !mode.PhysicalCalibrationEnabled && !isEliminated && role == GameRole.Player;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
