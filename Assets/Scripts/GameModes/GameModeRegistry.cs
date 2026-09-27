using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Реестр всех игровых режимов — <b>единственное место</b>, где <see cref="GameModeData"/>
    /// ищется по <c>modeId</c> (на клиенте режим сообщает только строку). Разминка тоже здесь,
    /// с флагом <see cref="GameModeData.isWarmup"/>: она не режим матча, и меню выбора режима
    /// берёт только <see cref="MatchModes"/>.
    /// Создать: ПКМ в Project → Create → VR Battlegrounds → Game Mode Registry.
    /// Один asset на проект — назначить в Inspector SessionManager и MenuSessionSetup.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GameModeRegistry",
        menuName  = "VR Battlegrounds/Game Mode Registry")]
    public class GameModeRegistry : ScriptableObject
    {
        [Tooltip("Все режимы проекта: разминка (isWarmup) и режимы матча.")]
        public GameModeData[] modes = new GameModeData[0];

        /// <summary>Найти режим по идентификатору. Возвращает null если не найден.</summary>
        public GameModeData GetById(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
                return null;

            foreach (GameModeData mode in modes)
            {
                if (mode != null && mode.modeId == modeId)
                    return mode;
            }
            return null;
        }

        /// <summary>Разминка — режим, с которого стартует любая карта. Null, если в реестре её нет.</summary>
        public GameModeData Warmup
        {
            get
            {
                foreach (GameModeData mode in modes)
                    if (mode != null && mode.isWarmup) return mode;
                return null;
            }
        }

        /// <summary>Режимы матча — то, что администратор выбирает в меню. Разминки среди них нет.</summary>
        public IEnumerable<GameModeData> MatchModes
        {
            get
            {
                foreach (GameModeData mode in modes)
                    if (mode != null && !mode.isWarmup) yield return mode;
            }
        }
    }
}
