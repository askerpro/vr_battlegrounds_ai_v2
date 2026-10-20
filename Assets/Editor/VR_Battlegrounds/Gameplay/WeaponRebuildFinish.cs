using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Общий конец полной пересборки ствола из пака (<see cref="KinemationWeaponBuilder" />,
    /// <see cref="HandsPackWeaponBuilder.BuildFull" />, <see cref="HandsPackLegacyRebuild" />): шаги, которые
    /// сборщик раньше оставлял отдельным командам. Каждый шаг выполняет его единственный писатель:
    /// <list type="number">
    /// <item>прицел — <see cref="VrBattlegrounds.Editor.Weapons.Sights.WeaponSightAuthoring" />;</item>
    /// <item>хост <c>WeaponSystem</c> вместо прежних компонентов механики — <see cref="WeaponSystemAuthoring.MigrateWeapons" />;</item>
    /// <item>баланс — <see cref="WeaponBalanceApplier" />;</item>
    /// <item>флаги UXR-компонентов на диске — <see cref="VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister" />
    /// (обычно он срабатывает после импорта сам, но в фоне редактора отложенный вызов ждёт фокуса).</item>
    /// </list>
    /// Хваты переносит <see cref="VrBattlegrounds.Editor.Avatars.WeaponGripCarryOver" /> (зовёт сборщик: место шага у
    /// сборщиков разное). Отклик магазина (звук падения, подсветка) ставит сама сборка.
    /// </summary>
    public static class WeaponRebuildFinish
    {
        private const string WeaponsData = "Assets/Data/Weapons";

        public static void Complete(string weaponPath, string magazinePath, string source)
        {
            VrBattlegrounds.Editor.Weapons.Sights.WeaponSightAuthoring.AfterBuildLogged(weaponPath, source);

            string name = System.IO.Path.GetFileNameWithoutExtension(weaponPath);
            if (WeaponSystemAuthoring.EntryFor(name) == null)
            {
                GameLog.WeaponSystem.Warning($"[{source}] {name}: нет строки в WeaponSystemAuthoring.Weapons — хост WeaponSystem не переведён");
            }
            else
            {
                Dictionary<string, object> migration = WeaponSystemAuthoring.MigrateWeapons(name);
                if (!(bool)migration["passed"])
                    throw new InvalidOperationException($"{name}: перевод на WeaponSystem не прошёл — " +
                                                        string.Join("; ", (IEnumerable<string>)migration["failures"]));
            }

            WeaponInfo info = AssetDatabase.FindAssets("t:WeaponInfo", new[] { WeaponsData })
                .Select(guid => AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(i => i != null && i.WeaponPrefab != null && AssetDatabase.GetAssetPath(i.WeaponPrefab) == weaponPath);
            if (info == null)
                GameLog.WeaponSystem.Warning($"[{source}] {name}: нет WeaponInfo в {WeaponsData} — баланс не применён");
            else
                GameLog.WeaponSystem.Info($"[{source}] {WeaponBalanceApplier.Apply(new[] { info })}");

            AssetDatabase.SaveAssets();
            // SaveAsPrefabAsset пишет __isInPrefab: 0 — с такими флагами редактор перевыдаёт _uxrUniqueId (MPPM-02).
            // Магазин раньше оружия: база вложенного префаба.
            foreach (string path in new[] { magazinePath, weaponPath })
            {
                if (string.IsNullOrEmpty(path)) continue;
                string fixedFlags = VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister.Normalize(path);
                if (fixedFlags != null) GameLog.Debug.Info($"[{source}] {path}: {fixedFlags}");
            }
        }
    }
}
