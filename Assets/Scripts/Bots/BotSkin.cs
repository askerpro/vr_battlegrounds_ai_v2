using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Скин бота — тот, в который можно попасть.
    ///
    /// <para>
    /// Пуля UltimateXR ищет попадание лучом с <c>QueryTriggerInteraction.Ignore</c>, то есть
    /// только по <b>твёрдым</b> коллайдерам. У части скинов их нет вовсе (находка WPN-02:
    /// <c>Heavy_Soldier_Base_Avatar</c> и другие) — по такому боту пули пролетают насквозь,
    /// и проверка «попадания регистрируются» дала бы ложный отказ.
    /// </para>
    /// </summary>
    public static class BotSkin
    {
        /// <summary>
        /// Индекс первого скина команды с твёрдым коллайдером. Нет такого — 0 и
        /// предупреждение: бот встанет, но попасть в него нельзя.
        /// </summary>
        public static int PickHittable(TeamData team)
        {
            if (team == null || team.avatars == null) return 0;

            for (int i = 0; i < team.avatars.Count; i++)
            {
                if (HasSolidCollider(team.GetAvatarPrefab(i))) return i;
            }

            GameLog.Debug.Warning(
                $"[BotSkin] У команды '{team.Name}' нет скина с твёрдыми коллайдерами (WPN-02) — " +
                "бот встанет, но пули будут проходить сквозь него.");
            return 0;
        }

        public static bool HasSolidCollider(GameObject prefab)
        {
            if (prefab == null) return false;

            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.isTrigger) return true;
            }
            return false;
        }
    }
}
