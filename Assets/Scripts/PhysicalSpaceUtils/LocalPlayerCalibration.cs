using System;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.PhysicalSpaceUtils
{
    /// <summary>
    /// Что эта машина знает о калибровке <b>своего</b> игрока между сессиями (T-50).
    ///
    /// <para>
    /// Не владелец и не применитель: значение игрока принадлежит сессии на сервере
    /// (<c>PlayerSession</c>), к аватару его ставит <see cref="AvatarCalibrationApplier" />. Запись
    /// нужна только там, где сессии нет или она ещё не создана: игрок приносит свою калибровку в
    /// сообщении подключения (переподключение, другой сервер), а процедура
    /// (<see cref="PhysicalSpaceSyncManager" />) считает новый замер от текущего значения.
    /// </para>
    ///
    /// <para>
    /// Два входа, оба явные. <see cref="Submit" /> — новый замер процедуры: своя сессия предсказывает
    /// его и отправляет серверу. <see cref="Adopt" /> — ответ сервера (принято, обрезано или отклонено):
    /// запись становится равной значению сессии, поэтому отклонённый замер не уйдёт повторно.
    /// После перезапуска приложения записи нет (<see cref="HasValue" /> = false), и клиент ничего
    /// не публикует поверх снимка сервера — он перенимает значение сессии.
    /// </para>
    /// </summary>
    public static class LocalPlayerCalibration
    {
        private static PlayerCalibration s_current;

        /// <summary>Последнее известное значение своего игрока. Без записи — <see cref="PlayerCalibration.None" />.</summary>
        public static PlayerCalibration Current => s_current;

        /// <summary>Машина знает калибровку своего игрока: замеряла сама или получила от сервера.</summary>
        public static bool HasValue { get; private set; }

        /// <summary>Процедура сняла новый замер. Подписчик — своя сессия (предсказание и запрос серверу).</summary>
        public static event Action<PlayerCalibration> Submitted;

        /// <summary>Новый результат процедуры калибровки.</summary>
        public static void Submit(PlayerCalibration measured)
        {
            // Отказ не должен портить принятую запись: при следующем подключении память
            // имеет приоритет над снимком сервера. Правило конечности остаётся общим.
            if (!PlayerCalibrationRules.TryNormalize(measured, out _))
            {
                GameLog.PhysicalSpace.Warning("[LocalPlayerCalibration] Нечисловой замер отклонён; принятая калибровка сохранена.");
                return;
            }

            s_current = measured;
            HasValue = true;
            GameLog.PhysicalSpace.Info($"[LocalPlayerCalibration] Новый замер: {measured}.");
            Submitted?.Invoke(measured);
        }

        /// <summary>Сервер решил: запись принимает значение сессии.</summary>
        public static void Adopt(PlayerCalibration decided)
        {
            if (HasValue && s_current == decided) return;

            s_current = decided;
            HasValue = true;
            GameLog.PhysicalSpace.Verbose($"[LocalPlayerCalibration] Значение сервера: {decided}.");
        }

        /// <summary>Последняя поза своего корня для подключения; пол/рост/флаг остаются решением сессии.</summary>
        public static void RecordPlacement(PlayerPlacement placement, PlayerCalibration? source = null)
        {
            if (!PlayerPlacement.TryNormalize(placement, out PlayerPlacement valid)) return;
            s_current = (source ?? s_current).WithPlacement(valid);
            HasValue = true;
        }

        /// <summary>Забыть запись: новый процесс, тесты.</summary>
        public static void Reset()
        {
            s_current = PlayerCalibration.None;
            HasValue = false;
        }

        // Без перезагрузки домена статика переживает выход из Play Mode — «перезапуск» должен её стирать.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            Reset();
            Submitted = null;
        }
    }
}
