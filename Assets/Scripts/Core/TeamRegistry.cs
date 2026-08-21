using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds
{
    /// <summary>
    /// Реестр всех команд игры. Хранится в Resources/TeamRegistry.asset —
    /// загружается автоматически без ссылок в сцене.
    ///
    /// Создать: ПКМ в Project -> Create -> VrBattlegrounds -> Team Registry
    /// После создания сохранить asset в Assets/Resources/TeamRegistry.asset.
    ///
    /// Индекс 0 зарезервирован для "нет команды" (null-значение по сети).
    /// </summary>
    [CreateAssetMenu(
        fileName = "TeamRegistry",
        menuName  = "VR Battlegrounds/Team Registry")]
    public class TeamRegistry : ScriptableObject
    {
        #region Singleton

        private static TeamRegistry s_instance;

        /// <summary>
        /// Глобальный экземпляр реестра.
        /// Загружается из Resources/TeamRegistry.asset.
        /// </summary>
        public static TeamRegistry Instance
        {
            get
            {
                if (s_instance == null)
                    s_instance = Resources.Load<TeamRegistry>(nameof(TeamRegistry));

                if (s_instance == null)
                    GameLog.Error("[TeamRegistry] Файл Resources/TeamRegistry.asset не найден. Создайте его через Create -> VrBattlegrounds -> Team Registry.");

                return s_instance;
            }
        }

        #endregion

        [Tooltip("Все команды игры. teamIndex каждой команды должен быть уникальным и >= 1.")]
        public TeamData[] teams = new TeamData[0];

        /// <summary>Возвращает TeamData по teamIndex. Null если не найдено (индекс 0 = нет команды).</summary>
        public TeamData GetByIndex(int teamIndex)
        {
            if (teamIndex == 0)
                return null;

            for (int i = 0; i < teams.Length; i++)
            {
                if (teams[i] != null && teams[i].teamIndex == teamIndex)
                    return teams[i];
            }

            GameLog.Match.Warning($"[TeamRegistry] Команда с teamIndex={teamIndex} не найдена.");
            return null;
        }

        /// <summary>Проверяет что все teamIndex уникальны и >= 1.</summary>
        public bool Validate(out string error)
        {
            for (int i = 0; i < teams.Length; i++)
            {
                if (teams[i] == null) { error = $"teams[{i}] = null"; return false; }
                if (teams[i].teamIndex < 1) { error = $"{teams[i].name}: teamIndex должен быть >= 1"; return false; }

                for (int j = i + 1; j < teams.Length; j++)
                {
                    if (teams[j] != null && teams[i].teamIndex == teams[j].teamIndex)
                    {
                        error = $"Дублирующийся teamIndex={teams[i].teamIndex} у {teams[i].name} и {teams[j].name}";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }
    }
}