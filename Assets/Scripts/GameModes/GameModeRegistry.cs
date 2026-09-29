using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Реестр всех игровых режимов — <b>единственное место</b>, где <see cref="GameModeData"/>
    /// ищется по <c>modeId</c> (на клиенте режим сообщает только строку).
    ///
    /// <para>
    /// <b>Разминка — не режим каталога.</b> Её не выбирают и у неё нет команд: это состояние
    /// карты «режим матча не запущен или на паузе», которое <c>MapReferee</c> включает сам.
    /// Поэтому она отдельное поле <see cref="warmup"/>, а не элемент <see cref="modes"/>,
    /// и в списки режимов карт (<c>MapData.supportedModes</c>) не входит.
    /// </para>
    /// Создать: ПКМ в Project → Create → VR Battlegrounds → Game Mode Registry.
    /// Один asset на проект — назначить в Inspector SessionManager и MenuSessionSetup.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GameModeRegistry",
        menuName  = "VR Battlegrounds/Game Mode Registry")]
    public class GameModeRegistry : ScriptableObject
    {
        [Tooltip("Режимы матча — то, что администратор выбирает в меню. Разминки среди них нет.")]
        public GameModeData[] modes = new GameModeData[0];

        [Tooltip("Разминка: включается сама, когда режим матча не запущен или на паузе. Без команд.")]
        public GameModeData warmup;

        /// <summary>Найти режим по идентификатору. Возвращает null если не найден.</summary>
        public GameModeData GetById(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
                return null;

            if (warmup != null && warmup.modeId == modeId) return warmup;

            foreach (GameModeData mode in modes)
            {
                if (mode != null && mode.modeId == modeId)
                    return mode;
            }
            return null;
        }

        /// <summary>Разминка — с неё стартует любая карта. Null, если в реестре её нет.</summary>
        public GameModeData Warmup => warmup;

        /// <summary>Режимы матча — то, что администратор выбирает в меню. Разминки среди них нет.</summary>
        public IEnumerable<GameModeData> MatchModes
        {
            get
            {
                foreach (GameModeData mode in modes)
                    if (mode != null && mode != warmup) yield return mode;
            }
        }
    }
}
