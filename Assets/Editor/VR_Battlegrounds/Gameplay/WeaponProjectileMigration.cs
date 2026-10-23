using System.Collections.Generic;
using System.Text;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Одноразовая миграция (решение 2026-10-23): скорость и предельная дальность снаряда переходят в баланс
    /// <see cref="WeaponInfo" />. Для каждого оружия с балансом читает основной выстрел префаба
    /// (<c>_shotTypes[0]</c>) и записывает его скорость и дальность в <see cref="WeaponInfo" /> — только в поля, которые
    /// ещё не заданы (0). Префабы не меняются; дальше их пишет <see cref="WeaponBalanceApplier" />.
    /// Меню нет — вызов через execute_code: <c>WeaponProjectileMigration.CaptureFromPrefabs()</c>.
    /// </summary>
    public static class WeaponProjectileMigration
    {
        private const string WeaponsFolder = "Assets/Data/Weapons";

        /// <summary>
        /// Переносит скорость и дальность основного выстрела префабов в незаданные поля <see cref="WeaponInfo" />,
        /// адресно сохраняет изменённые ассеты и возвращает отчёт по стволам. Расхождение выстрелов (дробинки,
        /// второй спуск) с основным — в отчёте: после Apply Weapon Balance они получат значения основного.
        /// </summary>
        public static string CaptureFromPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Баланс нельзя менять в Play Mode.");

            var report = new StringBuilder();
            int written = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { WeaponsFolder }))
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || !info.HasBalance || info.WeaponPrefab == null) continue;

                var source = info.WeaponPrefab.GetComponent<UxrProjectileSource>();
                if (source == null || source.ShotTypes == null || source.ShotTypes.Count == 0)
                {
                    report.AppendLine($"{info.name}: у {info.WeaponPrefab.name} нет UxrProjectileSource с выстрелами — пропущено.");
                    continue;
                }

                UxrShotDescriptor main = source.ShotTypes[0];
                var so = new SerializedObject(info);
                bool speedWritten = CaptureIfUnset(so.FindProperty("_projectileSpeed"), main.ProjectileSpeed);
                bool distanceWritten = CaptureIfUnset(so.FindProperty("_projectileMaxDistance"), main.ProjectileMaxDistance);
                so.ApplyModifiedPropertiesWithoutUndo();
                if (speedWritten || distanceWritten)
                {
                    EditorUtility.SetDirty(info);
                    AssetDatabase.SaveAssetIfDirty(info);
                    written++;
                }

                report.Append($"{info.name} ({info.WeaponPrefab.name}): скорость {main.ProjectileSpeed:0.##} м/с")
                      .Append(speedWritten ? "" : $" [не записано, в WeaponInfo {info.ProjectileSpeed:0.##}]")
                      .Append($", дальность {main.ProjectileMaxDistance:0.##} м")
                      .Append(distanceWritten ? "" : $" [не записано, в WeaponInfo {info.ProjectileMaxDistance:0.##}]");
                foreach (string mismatch in Mismatches(info.WeaponPrefab, source, main))
                    report.Append("; ").Append(mismatch);
                report.AppendLine(".");
            }

            report.Insert(0, $"Снаряд в баланс: записано в {written} WeaponInfo.\n");
            string text = report.ToString().TrimEnd();
            GameLog.WeaponSystem.Info(text);
            return text;
        }

        // Пишет значение префаба только в незаданное поле (0): уже заданный баланс миграция не перезаписывает.
        private static bool CaptureIfUnset(SerializedProperty property, float value)
        {
            if (property == null || property.floatValue > 0f || value <= 0f) return false;
            property.floatValue = value;
            return true;
        }

        // Выстрелы спусков и дробинок, чья скорость или дальность расходятся с основным (сейчас у дроби — 60 м
        // константой сборщика): после применения баланса они станут как основной.
        private static IEnumerable<string> Mismatches(GameObject prefab, UxrProjectileSource source, UxrShotDescriptor main)
        {
            var indices = new SortedDictionary<int, string>();
            var weapon = prefab.GetComponent<UxrFirearmWeapon>();
            if (weapon != null)
            {
                SerializedProperty triggers = new SerializedObject(weapon).FindProperty("_triggers");
                for (int i = 0; i < triggers.arraySize; i++)
                {
                    int index = triggers.GetArrayElementAtIndex(i).FindPropertyRelative("_projectileShotIndex").intValue;
                    if (index != 0) indices[index] = "спуск " + i;
                }
            }
            var pellets = prefab.GetComponent<ShotgunPellets>();
            if (pellets != null) indices[pellets.PelletShotIndex] = "дробинки";

            foreach (var pair in indices)
            {
                if (pair.Key < 0 || pair.Key >= source.ShotTypes.Count) continue;
                UxrShotDescriptor shot = source.ShotTypes[pair.Key];
                bool same = Mathf.Approximately(shot.ProjectileSpeed, main.ProjectileSpeed) &&
                            Mathf.Approximately(shot.ProjectileMaxDistance, main.ProjectileMaxDistance);
                // Дробинки показываются всегда: их дальность раньше задавал сборщик, не донор.
                if (same && pellets == null) continue;
                if (same && pair.Key != pellets.PelletShotIndex) continue;
                yield return $"{(same ? "" : "РАСХОЖДЕНИЕ: ")}{pair.Value} (выстрел {pair.Key}) — " +
                             $"{shot.ProjectileSpeed:0.##} м/с, {shot.ProjectileMaxDistance:0.##} м";
            }
        }
    }
}
