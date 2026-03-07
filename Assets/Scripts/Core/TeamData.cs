using UnityEngine;

namespace VrBattlegrounds
{
    /// <summary>
    /// Данные команды игроков — название, иконка, цвет.
    /// Создать: ПКМ в Project -> Create -> VrBattlegrounds -> Team Data
    ///
    /// По сети передаётся только <see cref="teamIndex"/> (int через SyncVar).
    /// TeamData получается из <see cref="TeamRegistry"/> по индексу на всех клиентах.
    /// </summary>
    [CreateAssetMenu(
        fileName = "TeamData",
        menuName  = "VrBattlegrounds/Team Data")]
    public class TeamData : ScriptableObject
    {
        [Tooltip("Уникальный числовой идентификатор команды. Используется в SyncVar и для поиска в TeamRegistry.\n0 = зарезервировано для 'нет команды'.")]
        [Min(1)]
        public int teamIndex;

        [Tooltip("Отображаемое название команды в UI.")]
        public string displayName;

        [Tooltip("Иконка команды для UI.")]
        public Sprite icon;

        [Tooltip("Цвет команды для выделения игроков и UI.")]
        public Color color = Color.white;

        public override string ToString() => displayName;
    }
}