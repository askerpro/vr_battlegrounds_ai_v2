using System.Collections.Generic;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Переносит баланс из <see cref="WeaponInfo" /> в префабы оружия и магазинов (T-38): урон вблизи и на
    /// предельной дистанции, пробитие стен (T-41), скорость и предельную дальность снаряда (если заданы в балансе) каждого
    /// выстрела (основного и дробинок), частоту спуска и режим огня (автомат/полуавтомат; ручной цикл механизма не меняется),
    /// число дробинок, ёмкость и
    /// заряд магазина, картину накопленной отдачи (<see cref="RecoilAccumulator" />) и толчок отдачи SDK у каждого спуска
    /// (из CS2 <c>recoil_magnitude</c>, <see cref="RecoilPattern.FromCs2" />), разлёт дробин (<see cref="WeaponSpread" />, конус дроби
    /// <see cref="ShotgunPellets" />). Недостающие компоненты ставятся. Руками эти поля в префабах не правятся — расхождение ловит <c>WeaponBalanceTests</c>.
    /// Оружие без баланса (<see cref="WeaponInfo.HasBalance" /> — сэмплы SDK) не трогается.
    /// </summary>
    public static class WeaponBalanceApplier
    {
        private const string WeaponsFolder = "Assets/Data/Weapons";

        private static void OpenWorkbench() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenTab(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Catalog);

        public static void ApplyAll() => Apply(System.Linq.Enumerable.Select(AssetDatabase.FindAssets("t:WeaponInfo", new[] { WeaponsFolder }), guid => AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid))));

        /// <summary>Применяет существующие формулы только к объявленному набору WeaponInfo.</summary>
        public static string Apply(IEnumerable<WeaponInfo> weapons)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Баланс нельзя менять в Play Mode.");
            var changed = new List<string>();
            foreach (var info in weapons)
            {
                if (info == null || !info.HasBalance || info.WeaponPrefab == null) continue;

                if (ApplyWeapon(info)) changed.Add(info.WeaponPrefab.name);
                if (ApplyInstalledMagazines(info) && !changed.Contains(info.WeaponPrefab.name)) changed.Add(info.WeaponPrefab.name);
                if (ApplyMagazine(info)) changed.Add(info.MagazinePrefab.name);
            }

            // ApplyWeapon/ApplyMagazine адресно сохраняют только объявленные префабы.
            // Общий SaveAssets здесь мог записать несохранённые изменения чужих материалов/данных.
            string report = changed.Count == 0
                ? "Баланс оружия: префабы уже совпадают с WeaponInfo."
                : $"Баланс оружия применён: {string.Join(", ", changed)}.";
            GameLog.WeaponSystem.Info(report);
            return report;
        }

        private static bool ApplyWeapon(WeaponInfo info)
        {
            GameObject prefab = info.WeaponPrefab;
            var weapon = prefab.GetComponent<UxrFirearmWeapon>();
            var source = prefab.GetComponent<UxrProjectileSource>();
            if (weapon == null || source == null)
            {
                GameLog.WeaponSystem.Warning($"{info.name}: префаб {prefab.name} без UxrFirearmWeapon/UxrProjectileSource — баланс не применён.", prefab);
                return false;
            }

            // Компонент — первым: LoadPrefabContents читает префаб с диска, несохранённые правки ниже потерялись бы.
            bool dirty = EnsureComponent<RecoilAccumulator>(prefab);
            dirty |= EnsureComponent<WeaponSpread>(prefab, info.Pellets > 1 && prefab.GetComponent<ShotgunPellets>() != null);
            prefab = info.WeaponPrefab;
            weapon = prefab.GetComponent<UxrFirearmWeapon>();
            source = prefab.GetComponent<UxrProjectileSource>();
            var shotIndices = new List<int>();

            var weaponSo = new SerializedObject(weapon);
            SerializedProperty triggers = weaponSo.FindProperty("_triggers");
            int frequency = WeaponInfo.ShotFrequency(info.FireRate);
            RecoilPattern recoil = info.Recoil;
            int cycle = (int)(info.FullAuto ? UxrShotCycle.FullyAutomatic : UxrShotCycle.SemiAutomatic);
            for (int i = 0; i < triggers.arraySize; i++)
            {
                SerializedProperty trigger = triggers.GetArrayElementAtIndex(i);
                shotIndices.Add(trigger.FindPropertyRelative("_projectileShotIndex").intValue);
                dirty |= SetInt(trigger.FindPropertyRelative("_maxShotFrequency"), frequency);
                // Режим огня — баланс (решение 2026-10-23): автомат/полуавтомат по WeaponInfo.FullAuto. Ручной цикл (помпа,
                // болт) — свойство механизма, его ставит сборщик по рецепту, баланс его не меняет; противоречие паспорта
                // (FullAuto у ручного механизма) сборка отклоняет.
                SerializedProperty cycleType = trigger.FindPropertyRelative("_cycleType");
                if (cycleType.intValue == (int)UxrShotCycle.ManualReload)
                {
                    if (info.FullAuto)
                        GameLog.WeaponSystem.Warning($"{info.name}: автоматический огонь в балансе, а у {prefab.name} механизм с ручным " +
                                                     "циклом — режим огня не изменён (пересборка такой паспорт отклоняет).", info);
                }
                else dirty |= SetInt(cycleType, cycle);
                // Толчок SDK — видимый и короткий: гаснет до следующего выстрела (см. RecoilPattern.SdkDuration).
                dirty |= SetFloat(trigger.FindPropertyRelative("_recoilAngleTwoHands"), recoil.SdkAngle(oneHand: false));
                dirty |= SetFloat(trigger.FindPropertyRelative("_recoilAngleOneHand"), recoil.SdkAngle(oneHand: true));
                dirty |= SetVector(trigger.FindPropertyRelative("_recoilOffsetTwoHands"), new Vector3(0f, 0f, -recoil.SdkOffset(oneHand: false)));
                dirty |= SetVector(trigger.FindPropertyRelative("_recoilOffsetOneHand"), new Vector3(0f, 0f, -recoil.SdkOffset(oneHand: true)));
                dirty |= SetFloat(trigger.FindPropertyRelative("_recoilDurationSeconds"), RecoilPattern.SdkDuration(1f / frequency));
            }
            weaponSo.ApplyModifiedPropertiesWithoutUndo();

            var pellets = prefab.GetComponent<ShotgunPellets>();
            if (pellets != null)
            {
                var pelletsSo = new SerializedObject(pellets);
                dirty |= SetInt(pelletsSo.FindProperty("_pellets"), info.Pellets);
                dirty |= SetFloat(pelletsSo.FindProperty("_spreadDegrees"), WeaponAccuracy.ToDegrees(info.Spread.Spread));
                pelletsSo.ApplyModifiedPropertiesWithoutUndo();
                shotIndices.Add(pellets.PelletShotIndex);
            }
            else if (info.Pellets > 1)
            {
                GameLog.WeaponSystem.Warning($"{info.name}: {info.Pellets} дробинок по балансу, а у {prefab.name} нет ShotgunPellets.", prefab);
            }

            // Скорость и дальность снаряда — из баланса (решение 2026-10-23); не заданные остаются за префабом
            // до миграции WeaponProjectileMigration.CaptureFromPrefabs. Пока предупреждение: сборщик без доноров ставит
            // дефолт SDK (30 м/с, 300 м), и ствол, пересобранный до миграции его WeaponInfo, остался бы с ним.
            // Отказом это станет, когда миграция — обязательный шаг (все WeaponInfo каталога с заданными полями).
            if (!info.HasProjectileSpeed || !info.HasProjectileMaxDistance)
                GameLog.WeaponSystem.Warning($"{info.name}: в балансе не заданы " +
                                             $"{(info.HasProjectileSpeed ? "" : "скорость ")}{(info.HasProjectileMaxDistance ? "" : "дальность ")}снаряда — " +
                                             $"у {prefab.name} оставлены значения префаба.", info);

            var sourceSo = new SerializedObject(source);
            SerializedProperty shots = sourceSo.FindProperty("_shotTypes");
            foreach (int index in new HashSet<int>(shotIndices))
            {
                if (index < 0 || index >= shots.arraySize) continue;
                SerializedProperty shot = shots.GetArrayElementAtIndex(index);
                if (info.HasProjectileSpeed) dirty |= SetFloat(shot.FindPropertyRelative("_projectileSpeed"), info.ProjectileSpeed);
                if (info.HasProjectileMaxDistance) dirty |= SetFloat(shot.FindPropertyRelative("_projectileMaxDistance"), info.ProjectileMaxDistance);
                float maxDistance = shot.FindPropertyRelative("_projectileMaxDistance").floatValue;
                dirty |= SetFloat(shot.FindPropertyRelative("_projectileDamageNear"), info.Damage);
                dirty |= SetFloat(shot.FindPropertyRelative("_projectileDamageFar"),
                                  WeaponInfo.DamageAt(info.Damage, info.RangeModifier, maxDistance));
                dirty |= SetFloat(shot.FindPropertyRelative("_penetrationPower"), info.Penetration);
            }
            sourceSo.ApplyModifiedPropertiesWithoutUndo();

            dirty |= ApplyRecoil(info, weaponSo.FindProperty("_recoilAxes").objectReferenceValue);
            dirty |= ApplySpread(info);

            if (dirty) PrefabUtility.SavePrefabAsset(prefab);
            return dirty;
        }

        private static bool EnsureComponent<T>(GameObject prefab, bool required = true) where T : Component
        {
            if ((prefab.GetComponent<T>() != null) == required) return false;

            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (required) contents.AddComponent<T>();
                else Object.DestroyImmediate(contents.GetComponent<T>());
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return true;
        }

        private static bool ApplyRecoil(WeaponInfo info, Object axes)
        {
            GameObject prefab = info.WeaponPrefab;
            bool dirty = false;
            var so = new SerializedObject(prefab.GetComponent<RecoilAccumulator>());
            SerializedProperty target = so.FindProperty("_pattern");
            RecoilPattern recoil = info.Recoil;
            dirty |= SetFloat(target.FindPropertyRelative("_kickDegrees"), recoil.KickDegrees);
            dirty |= SetFloat(target.FindPropertyRelative("_maxPitchDegrees"), recoil.MaxPitchDegrees);
            dirty |= SetFloat(target.FindPropertyRelative("_maxYawDegrees"), recoil.MaxYawDegrees);
            dirty |= SetFloat(target.FindPropertyRelative("_recoveryTime"), recoil.RecoveryTime);
            dirty |= SetFloat(target.FindPropertyRelative("_oneHandMultiplier"), recoil.OneHandMultiplier);

            SerializedProperty axesProperty = so.FindProperty("_axes");
            if (axesProperty.objectReferenceValue != axes)
            {
                axesProperty.objectReferenceValue = axes;
                dirty = true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return dirty;
        }

        private static bool ApplySpread(WeaponInfo info)
        {
            var spread = info.WeaponPrefab.GetComponent<WeaponSpread>();
            if (spread == null) return false; // Пули летят по оси; компонент нужен только дробовикам.
            var so = new SerializedObject(spread);
            SerializedProperty target = so.FindProperty("_pattern");
            SerializedProperty source = new SerializedObject(info).FindProperty("_spread");
            bool dirty = false;
            dirty |= SetFloat(target.FindPropertyRelative("_spread"), source.FindPropertyRelative("_spread").floatValue);
            // Старые значения в WeaponInfo — справочные; у дроби ни базовой, ни накопленной неточности нет.
            foreach (string field in new[] { "_inaccuracyStand", "_inaccuracyMove", "_inaccuracyFire" })
                dirty |= SetFloat(target.FindPropertyRelative(field), 0f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return dirty;
        }

        private static bool ApplyMagazine(WeaponInfo info)
        {
            if (info.MagazinePrefab == null) return false;
            if (info.ReadinessProfile != null && info.ReadinessProfile.AmmoCapability == WeaponAmmoCapability.FixedStoreChamber)
            {
                if (info.MagazinePrefab.GetComponent<Cartridge>() == null || info.MagazinePrefab.GetComponent<UxrFirearmMag>() != null)
                    throw new System.InvalidOperationException("Tube ammunition must be a single shell: " + info.WeaponId);
                return false; // Полная ёмкость никогда не записывается во внешний shell.
            }
            var mag = info.MagazinePrefab.GetComponentInChildren<UxrFirearmMag>(true);
            if (mag == null) return false;

            var so = new SerializedObject(mag);
            bool dirty = SetInt(so.FindProperty("_capacity"), info.MagazineSize);
            dirty |= SetInt(so.FindProperty("_rounds"), info.MagazineSize);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (dirty) PrefabUtility.SavePrefabAsset(info.MagazinePrefab);
            return dirty;
        }

        // Стартовый магазин — вложенный экземпляр и может хранить старый override ёмкости.
        // Обновление внешнего префаба само по себе такой override не исправляет.
        private static bool ApplyInstalledMagazines(WeaponInfo info)
        {
            var installed = InstalledMagazines(info.WeaponPrefab);
            if (!System.Linq.Enumerable.Any(installed, mag => mag.Capacity != info.MagazineSize || mag.Rounds != info.MagazineSize)) return false;
            string path = AssetDatabase.GetAssetPath(info.WeaponPrefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var mag in InstalledMagazines(contents))
                {
                    var so = new SerializedObject(mag);
                    SetInt(so.FindProperty("_capacity"), info.MagazineSize);
                    SetInt(so.FindProperty("_rounds"), info.MagazineSize);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(mag);
                }
                if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null)
                    throw new System.InvalidOperationException("Не удалось сохранить стартовый магазин: " + path);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static IEnumerable<UxrFirearmMag> InstalledMagazines(GameObject root)
        {
            var triggers = new SerializedObject(root.GetComponent<UxrFirearmWeapon>()).FindProperty("_triggers");
            var anchors = new HashSet<Object>();
            for (int index = 0; index < triggers.arraySize; index++)
                anchors.Add(triggers.GetArrayElementAtIndex(index).FindPropertyRelative("_ammunitionMagAnchor").objectReferenceValue);
            foreach (var mag in root.GetComponentsInChildren<UxrFirearmMag>(true))
            {
                var grab = mag.GetComponent<UltimateXR.Manipulation.UxrGrabbableObject>();
                if (grab != null && grab.StartAnchor != null && anchors.Contains(grab.StartAnchor)) yield return mag;
            }
        }

        private static bool SetInt(SerializedProperty property, int value)
        {
            if (property == null || property.intValue == value) return false;
            property.intValue = value;
            return true;
        }

        private static bool SetVector(SerializedProperty property, Vector3 value)
        {
            if (property == null || (property.vector3Value - value).sqrMagnitude < 1e-10f) return false;
            property.vector3Value = value;
            return true;
        }

        private static bool SetFloat(SerializedProperty property, float value)
        {
            if (property == null || Mathf.Approximately(property.floatValue, value)) return false;
            property.floatValue = value;
            return true;
        }
    }
}
