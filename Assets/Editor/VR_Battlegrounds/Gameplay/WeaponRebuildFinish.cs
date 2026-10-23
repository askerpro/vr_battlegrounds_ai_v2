using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Weapons.Sights;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Рамка полной пересборки ствола из пака (<see cref="KinemationWeaponBuilder" />,
    /// <see cref="HandsPackWeaponBuilder.BuildFull" />, <see cref="HandsPackLegacyRebuild" />): всё или ничего.
    /// <see cref="Run" /> снимает копию файлов ствола (<see cref="WeaponRebuildTransaction" />), выполняет сборщик и общий
    /// конец, а при любом отказе возвращает файлы и бросает исключение с причиной. Общий конец — шаги, которые сборщик
    /// раньше оставлял отдельным командам; каждый выполняет его единственный писатель:
    /// <list type="number">
    /// <item>прицел — <see cref="WeaponSightAuthoring" />; отказ шага (спецификация разделения не совпала с мешем пака,
    /// запись таблицы не применилась, второй прицел, прицел без записи) — отказ всей пересборки;</item>
    /// <item>хост <c>WeaponSystem</c> вместо прежних компонентов механики — <see cref="WeaponSystemAuthoring.MigrateWeapons" />;</item>
    /// <item>баланс — <see cref="WeaponBalanceApplier" />;</item>
    /// <item>теги корней оружия и магазина — <see cref="VrBattlegrounds.Editor.GameTagsTool.ApplyToPrefab" />;</item>
    /// <item>флаги UXR-компонентов на диске — <see cref="VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister" />
    /// (обычно он срабатывает после импорта сам, но в фоне редактора отложенный вызов ждёт фокуса);</item>
    /// <item>последним — исходные позы пака и snap-точки <c>_orig</c> (<see cref="PackOrigPoses.ExportForWeapon" />):
    /// объекты поверх готовых префабов, без регистрации у аватаров.</item>
    /// </list>
    /// Хваты переносит <see cref="VrBattlegrounds.Editor.Avatars.WeaponGripCarryOver" /> (зовёт сборщик: место шага у
    /// сборщиков разное). Отклик магазина (звук падения, подсветка) ставит сама сборка.
    /// </summary>
    public static class WeaponRebuildFinish
    {
        private const string WeaponsData = "Assets/Data/Weapons";
        private static bool running;

        /// <summary>
        /// Пересборка ствола: <paramref name="build" /> (шаги сборщика до общего конца), затем общий конец. При отказе
        /// любого шага файлы ствола возвращаются к состоянию до сборки, исключение несёт причину и отчёт отката.
        /// </summary>
        /// <param name="extraPaths">Что сборка пишет помимо текущих префабов, их зависимостей, WeaponInfo и поз <c>_orig</c>.</param>
        public static void Run(string source, string weaponPath, string magazinePath, IEnumerable<string> extraPaths, Action build)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Пересборка запрещена в Play Mode.");
            if (running) throw new InvalidOperationException($"[{source}] {weaponPath}: вложенная пересборка не поддержана");

            string name = System.IO.Path.GetFileNameWithoutExtension(weaponPath);
            bool hadSight = WeaponSightAuthoring.HasSight(weaponPath);
            WeaponInfo info = InfoFor(weaponPath);
            var extras = (extraPaths ?? Enumerable.Empty<string>()).ToList();
            if (info != null) extras.Add(AssetDatabase.GetAssetPath(info));
            extras.Add(PackOrigPoses.Root);
            // Автосведение родного прицела без записи дописывает таблицу — отказ пересборки возвращает и её.
            extras.Add(WeaponSightTable.AssetPath);
            WeaponRebuildTransaction transaction = WeaponRebuildTransaction.Begin(new[] { weaponPath, magazinePath }, extras);
            // Сборка сохраняет только свою область. Несохранённое в ней до начала — чужая правка, которую сборка
            // записала бы на диск или затёрла; несохранённое вне области (материалы паков и т. п.) не мешает.
            string[] scope = transaction.Scope;
            string[] dirty = OwnAssetSave.Dirty(scope);
            if (dirty.Length > 0)
                throw new InvalidOperationException($"[{source}] {name}: в области пересборки есть несохранённые изменения — сохраните или отмените их:\n" +
                                                    string.Join("\n", dirty.Take(8)) + (dirty.Length > 8 ? "\n… ещё " + (dirty.Length - 8) : ""));
            GameLog.WeaponSystem.Info($"[{source}] {name}: копия до пересборки — {transaction.FileCount} файлов, {transaction.ByteCount / 1024} КБ");
            running = true;
            try
            {
                build();
                Complete(weaponPath, magazinePath, source, hadSight, scope);
                // Последним: snap-точки Orig — объекты поверх готовых префабов; отказ экспорта — отказ пересборки.
                GameLog.WeaponSystem.Info($"[{source}] {PackOrigPoses.ExportForWeapon(weaponPath)}");
                OwnAssetSave.Save(scope);
                string foreign = transaction.ReleaseForeign();
                if (foreign.Length > 0) GameLog.WeaponSystem.Warning($"[{source}] {name}: {foreign}");
            }
            catch (Exception exception)
            {
                string rollback;
                try { rollback = transaction.Rollback(); }
                catch (Exception failed) { rollback = "ОТКАТ НЕ УДАЛСЯ: " + failed.Message + " — вернуть файлы ствола из Git"; }
                string message = $"[{source}] {name}: пересборка отменена — {exception.Message}\n{rollback}";
                GameLog.WeaponSystem.Error(message, AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath));
                throw new InvalidOperationException(message, exception);
            }
            finally { running = false; }
        }

        private static void Complete(string weaponPath, string magazinePath, string source, bool hadSight, string[] scope)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(weaponPath);
            string sight;
            try { sight = WeaponSightAuthoring.AfterBuild(weaponPath); }
            catch (Exception e) { throw new InvalidOperationException("прицел не подготовлен: " + e.Message, e); }
            if (sight == null && hadSight)
                throw new InvalidOperationException($"прицел не подготовлен: у ствола был прицел, а в {WeaponSightTable.AssetPath} нет его записи " +
                                                    "и ствола нет в SightSplitTable — пересборка потеряла бы прицел");
            if (sight != null) GameLog.WeaponSystem.Info($"[{source}] прицел: {sight}");

            if (WeaponSystemAuthoring.EntryFor(name) == null)
            {
                GameLog.WeaponSystem.Warning($"[{source}] {name}: нет строки в WeaponSystemAuthoring.Weapons — хост WeaponSystem не переведён");
            }
            else
            {
                // Новый префаб (первая сборка ствола) после SaveAsPrefabAsset несёт _assetId 0, а нормализатор импорта
                // срабатывает только в delayCall — после этого вызова. Миграция сверяет канон до и после: выставить сейчас.
                VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { magazinePath, weaponPath }, false);
                Dictionary<string, object> migration = WeaponSystemAuthoring.MigrateWeapons(name);
                if (!(bool)migration["passed"])
                    throw new InvalidOperationException($"{name}: перевод на WeaponSystem не прошёл — " +
                                                        string.Join("; ", (IEnumerable<string>)migration["failures"]));
            }

            WeaponInfo info = InfoFor(weaponPath);
            if (info == null)
                GameLog.WeaponSystem.Warning($"[{source}] {name}: нет WeaponInfo в {WeaponsData} — баланс не применён");
            else
                GameLog.WeaponSystem.Info($"[{source}] {WeaponBalanceApplier.Apply(new[] { info })}");

            // Только область пересборки: общий SaveAssets записал бы и чужие несохранённые изменения.
            OwnAssetSave.Save(scope);
            // Теги — копия категории по компонентам; сборщик пишет корень заново без тега (пересобранный MP5K потерял
            // Weapon). Префаб читается с диска — после сохранения области. Магазин раньше оружия.
            foreach (string path in new[] { magazinePath, weaponPath })
            {
                if (string.IsNullOrEmpty(path)) continue;
                var tags = new VrBattlegrounds.Editor.GameTagsTool.Result();
                int retagged = VrBattlegrounds.Editor.GameTagsTool.ApplyToPrefab(path, tags);
                if (retagged > 0)
                {
                    GameLog.Debug.Info($"[{source}] {path}: тегов размечено {retagged}");
                    // Разметка сохраняет префаб через SaveAsPrefabAsset, а он пишет _assetId = 0 — вернуть канон
                    // (NetworkAssetIdOnDiskTests), как после записи хватов и прицела.
                    int ids = VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { path }, false);
                    if (ids > 0) GameLog.Debug.Info($"[{source}] {path}: _assetId возвращён после разметки тегов");
                }
                foreach (string conflict in tags.Conflicts) GameLog.WeaponSystem.Warning($"[{source}] {conflict}");
            }
            // SaveAsPrefabAsset пишет __isInPrefab: 0 — с такими флагами редактор перевыдаёт _uxrUniqueId (MPPM-02).
            // Магазин раньше оружия: база вложенного префаба.
            foreach (string path in new[] { magazinePath, weaponPath })
            {
                if (string.IsNullOrEmpty(path)) continue;
                string fixedFlags = VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister.Normalize(path);
                if (fixedFlags != null) GameLog.Debug.Info($"[{source}] {path}: {fixedFlags}");
            }
        }

        private static WeaponInfo InfoFor(string weaponPath) => AssetDatabase.FindAssets("t:WeaponInfo", new[] { WeaponsData })
            .Select(guid => AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid)))
            .FirstOrDefault(i => i != null && i.WeaponPrefab != null && AssetDatabase.GetAssetPath(i.WeaponPrefab) == weaponPath);
    }
}
