using System.Collections.Generic;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Состояние матча на паузе: что нужно, чтобы «Продолжить» вернул матч с того же места.
    ///
    /// <para>
    /// <b>Почему снимок, а не приостановленный экземпляр режима.</b> На паузе карта в разминке,
    /// а режим на карте один: живой Elimination рядом с разминкой отвечал бы на те же вопросы
    /// (оружие, арсенал, урон), держал бы подписки на смерти и подключения и слал бы свои
    /// <c>SyncVar</c>. Снимок — несколько чисел, режим спавнится заново тем же путём, что и
    /// при «Начать матч». Хранит его <c>MapReferee</c> карты: пауза — состояние матча
    /// на этой карте, со сменой карты она теряет смысл.
    /// </para>
    /// </summary>
    public sealed class PauseSnapshot
    {
        /// <summary>Режим, который продолжится.</summary>
        public string ModeId = "";

        /// <summary>
        /// Счёт команд режима (<c>teamIndex</c> → раунды за карту или фраги). У режима с раундами —
        /// без прерванного раунда: он сыграется заново.
        /// </summary>
        public readonly Dictionary<int, int> TeamScores = new Dictionary<int, int>();

        /// <summary>Номер раунда, который сыграется заново после «Продолжить»; 0 — раунды не начинались.</summary>
        public int RoundToReplay;

        /// <summary>Сколько времени оставалось у матча с таймером (Respawn), секунды.</summary>
        public float TimeRemaining;
    }
}
