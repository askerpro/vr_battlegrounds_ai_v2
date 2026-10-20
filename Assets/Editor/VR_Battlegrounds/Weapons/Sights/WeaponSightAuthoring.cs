using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Единственный writer узлов механического прицела в префабах оружия: вставляет/обновляет экземпляр комплекта
    /// <c>SightKit_*</c> под <c>MeshContainer</c> (nested prefab) и выставляет из <see cref="WeaponSightTable"/> позу
    /// комплекта, положение половин вдоль планки и сдвиг подвижной детали. Остальные узлы (ShotSource и его цепочка,
    /// корень, детали оружия) не трогает: TRS сверяются до/после. Без записи в таблице — отказ.
    /// Комплект всегда в реальном размере: его localScale компенсирует масштаб MeshContainer (<see cref="SightKitScale"/>),
    /// поэтому ход и сдвиг подвижной детали — метры мира.
    /// Комплект-коллиматор (<see cref="SightParts.IsOpticKit"/>, раунд 1 2026-10-20): половин и подвижной детали нет;
    /// writer ставит комплект и привязывает сетку конечного фокуса экземпляра к ShotSource оружия и дистанции записи
    /// (<see cref="SightParts.BindOptic"/>). Второй прицел (механика или коллиматор вне комплекта) — отказ.
    /// С раунда 1 «Применить к префабу» включено для комплектов; встроенный (родной) прицел — следующим шагом.
    /// </summary>
    public static class WeaponSightAuthoring
    {
        public const string SettingsPath = "Assets/Data/Weapons/SightCalibration/WeaponSightCalibrationSettings.asset";
        public const string MeshContainerName = "MeshContainer";
        private const string WeaponPrefabRoot = "Assets/Prefabs/Weapons/";

        /// <summary>Read-only проверка записи таблицы и префаба оружия.</summary>
        public static string Preflight(WeaponInfo info)
        {
            Resolve(info, out string path, out WeaponSightTable.Entry entry);
            return PreflightPrefab(path, entry);
        }

        public static string Apply(WeaponInfo info)
        {
            Resolve(info, out string path, out WeaponSightTable.Entry entry);
            return ApplyToPrefab(path, entry);
        }

        /// <summary>Применить запись таблицы, если она есть (для сборщика после пересборки ствола); null — записи нет.</summary>
        public static string ApplyIfRecorded(WeaponInfo info)
        {
            WeaponSightTable table = WeaponSightTable.Load();
            return info != null && table != null && table.Find(info) != null ? Apply(info) : null;
        }

        private static void Resolve(WeaponInfo info, out string path, out WeaponSightTable.Entry entry)
        {
            if (info == null || info.WeaponPrefab == null) throw new InvalidOperationException("Нет WeaponInfo или его префаба");
            WeaponSightTable table = WeaponSightTable.Load();
            entry = table != null ? table.Find(info) : null;
            if (entry == null) throw new InvalidOperationException($"{info.WeaponId}: нет записи в {WeaponSightTable.AssetPath} — сначала свести и сохранить");
            path = AssetDatabase.GetAssetPath(info.WeaponPrefab);
        }

        /// <summary>Проверка префаба по пути (для временной копии в тестах инструмента). Ничего не пишет.</summary>
        public static string PreflightPrefab(string prefabPath, WeaponSightTable.Entry entry)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null || !prefabPath.StartsWith(WeaponPrefabRoot, StringComparison.Ordinal))
                throw new InvalidOperationException("Префаб оружия должен лежать в " + WeaponPrefabRoot + ": " + prefabPath);
            return Check(asset, entry, out _, out _);
        }

        public static string ApplyToPrefab(string prefabPath, WeaponSightTable.Entry entry)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Применение прицела — только в Edit Mode");
            PreflightPrefab(prefabPath, entry);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Check(root, entry, out Transform container, out Transform shotSource);
                ShotSourceFingerprint before = ShotSourceFingerprint.Capture(shotSource, root.transform);
                GameObject kit = ExistingKit(container);
                // Встроенный прицел: двигается только его подвижная деталь; у комплекта — только узлы комплекта.
                Transform builtInMoving = entry.Kit == null ? Moving(root).transform : null;
                bool optic = SightParts.IsOpticKit(entry.Kit);
                var untouched = Snapshot(root.transform, kit != null ? kit.transform : null, builtInMoving);

                if (entry.Kit != null)
                {
                    if (kit != null && PrefabUtility.GetCorrespondingObjectFromOriginalSource(kit) != entry.Kit)
                    {
                        UnityEngine.Object.DestroyImmediate(kit); // комплект заменён другим
                        kit = null;
                    }
                    if (kit == null) kit = (GameObject)PrefabUtility.InstantiatePrefab(entry.Kit, container);
                    kit.transform.localPosition = entry.KitLocalPosition;
                    kit.transform.localRotation = entry.KitLocalRotation;
                    SightKitScale.Apply(kit.transform); // lossyScale комплекта = 1 при любом масштабе корня оружия
                    if (!optic)
                    {
                        SetAlongRail(kit.transform.Find("Rear"), entry.RearAlongRail);
                        SetAlongRail(kit.transform.Find("Front"), entry.FrontAlongRail);
                    }
                }
                if (optic)
                {
                    var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
                    SightParts.BindOptic(SightParts.FindOptic(kit), SourceOf(root), entry.Weapon, settings, entry.ZeroDistance);
                }
                else
                {
                    SightAdjustablePart moving = Moving(kit != null ? kit : root);
                    moving.transform.localPosition = moving.NeutralLocalPosition + Vector3.up * entry.MovingOffset;
                }

                foreach (var pair in untouched)
                    if (pair.Key == null || !Same(pair.Key, pair.Value))
                        throw new InvalidOperationException("Изменился узел вне прицела: " + (pair.Key != null ? pair.Key.name : "удалён"));
                if (before.Compare(ShotSourceFingerprint.Capture(shotSource, root.transform)) != null)
                    throw new InvalidOperationException("Цепочка ShotSource изменилась — отказ сохранения");
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                    throw new InvalidOperationException("Не удалось сохранить " + prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            // SaveAsPrefabAsset пишет NetworkIdentity._assetId = 0; вернуть канон и сверить на диске, как Tr15OpticBuilder.
            VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { prefabPath }, false);
            uint canonical = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(prefabPath)));
            uint? onDisk = VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.ReadOnDisk(prefabPath);
            if (onDisk != canonical)
                throw new InvalidOperationException($"{prefabPath}: NetworkIdentity._assetId на диске {onDisk}, ожидается {canonical}");
            return Readback(prefabPath, entry);
        }

        // ── Проверки ────────────────────────────────────────────

        private static string Check(GameObject root, WeaponSightTable.Entry entry, out Transform container, out Transform shotSource)
        {
            if (entry == null) throw new InvalidOperationException("Нет записи таблицы");
            var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
            if (settings == null) throw new InvalidOperationException("Нет настроек калибровки: " + SettingsPath);
            if (!WeaponSightCalibrationSettings.IsValidDistance(entry.ZeroDistance) || entry.ZeroDistance != settings.DefaultZeroDistance)
                throw new InvalidOperationException($"Запись сведена на {entry.ZeroDistance} м, текущая пристрелка {settings.DefaultZeroDistance} м — свести заново");
            container = FindContainer(root.transform);
            shotSource = FindShotSource(root);
            string drift = entry.ShotSource.Compare(ShotSourceFingerprint.Capture(shotSource, root.transform));
            if (entry.ShotSource.IsEmpty || drift != null)
                throw new InvalidOperationException("Отпечаток ShotSource не совпал (" + (drift ?? "пуст") + ") — свести заново");

            GameObject kit = ExistingKit(container);
            var outside = root.GetComponentsInChildren<SightAdjustablePart>(true)
                .Where(p => kit == null || !p.transform.IsChildOf(kit.transform)).ToArray();
            var outsideOptics = root.GetComponentsInChildren<WeaponOpticView>(true)
                .Where(v => kit == null || !v.transform.IsChildOf(kit.transform)).ToArray();
            SightAdjustablePart moving;
            if (entry.Kit != null)
            {
                if (!IsKitPrefab(entry.Kit)) throw new InvalidOperationException("Комплект записи — не префаб SightKit_* из " + SightKitBuilder.PrefabFolder);
                SightKitScale.ParentScale(container); // неравномерный масштаб или скос MeshContainer — отказ
                if (outside.Length > 0 || outsideOptics.Length > 0)
                    throw new InvalidOperationException("У оружия уже есть прицел вне комплекта (" +
                        string.Join(", ", outside.Select(p => p.name).Concat(outsideOptics.Select(v => v.name))) + ") — два прицела недопустимы");
                if (SightParts.IsOpticKit(entry.Kit))
                {
                    if (entry.MovingOffset != 0f) throw new InvalidOperationException("У коллиматора нет подвижной детали — сдвиг в записи должен быть 0");
                    return $"{root.name}: {entry.Kit.name} (коллиматор), сетка на {entry.ZeroDistance} м, ShotSource {shotSource.name}";
                }
                moving = Moving(entry.Kit);
                Transform rear = entry.Kit.transform.Find("Rear"), front = entry.Kit.transform.Find("Front");
                if (rear == null || front == null) throw new InvalidOperationException("У комплекта нет половин Rear/Front");
            }
            else moving = Moving(root);
            if (!moving.IsWithinTravel(entry.MovingOffset))
                throw new InvalidOperationException($"Сдвиг {entry.MovingOffset * 1000:0.00} мм вне хода −{moving.TravelDown * 1000:0.00}…+{moving.TravelUp * 1000:0.00} мм");
            return $"{root.name}: {(entry.Kit != null ? entry.Kit.name : "встроенный прицел")}, сдвиг {entry.MovingOffset * 1000:+0.00;-0.00;0.00} мм, " +
                   $"пристрелка {entry.ZeroDistance} м, ShotSource {shotSource.name}";
        }

        public static Transform FindContainer(Transform root)
        {
            Transform direct = root.Find(MeshContainerName);
            if (direct != null) return direct;
            var found = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == MeshContainerName).ToArray();
            if (found.Length != 1) throw new InvalidOperationException($"{root.name}: узлов {MeshContainerName} {found.Length}, нужен 1");
            return found[0];
        }

        public static Transform FindShotSource(GameObject root) => SightParts.FindShotSource(root);

        /// <summary>Экземпляр комплекта под MeshContainer (не больше одного).</summary>
        public static GameObject ExistingKit(Transform container)
        {
            var kits = new List<GameObject>();
            foreach (Transform child in container)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) && IsKitPath(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject)))
                    kits.Add(child.gameObject);
            if (kits.Count > 1) throw new InvalidOperationException($"Под {container.name} несколько комплектов прицела");
            return kits.Count == 1 ? kits[0] : null;
        }

        public static bool IsKitPrefab(GameObject prefab) => prefab != null && IsKitPath(AssetDatabase.GetAssetPath(prefab));

        private static bool IsKitPath(string path) => !string.IsNullOrEmpty(path)
            && path.StartsWith(SightKitBuilder.PrefabFolder + "/", StringComparison.Ordinal)
            && Path.GetFileName(path).StartsWith("SightKit_", StringComparison.Ordinal);

        /// <summary>Ровно две SightAdjustablePart (Rear/Front), подвижная одна; возвращает подвижную.</summary>
        public static SightAdjustablePart Moving(GameObject scope)
        {
            var parts = scope.GetComponentsInChildren<SightAdjustablePart>(true);
            if (parts.Length != 2 || parts.Count(p => p.Role == SightPartRole.Rear) != 1 || parts.Count(p => p.Role == SightPartRole.Front) != 1)
                throw new InvalidOperationException($"{scope.name}: нужно ровно две SightAdjustablePart (Rear и Front), найдено {parts.Length}");
            var moving = parts.Where(p => p.IsAdjustable).ToArray();
            if (moving.Length != 1) throw new InvalidOperationException($"{scope.name}: подвижных деталей {moving.Length}, нужна 1");
            if (Quaternion.Angle(moving[0].transform.localRotation, Quaternion.identity) > .01f)
                throw new InvalidOperationException($"{moving[0].name}: ход только по локальной Y — поворот детали должен быть нулевым");
            return moving[0];
        }

        /// <summary>Источник выстрела оружия (тот, чей тип 0 даёт ShotSource), для привязки сетки коллиматора.</summary>
        private static UxrProjectileSource SourceOf(GameObject root) => root.GetComponentsInChildren<UxrProjectileSource>(true).Single();

        private static void SetAlongRail(Transform half, float z)
        {
            if (half == null) throw new InvalidOperationException("У экземпляра комплекта нет половины Rear/Front");
            Vector3 p = half.localPosition;
            half.localPosition = new Vector3(p.x, p.y, z);
        }

        private struct Trs { public Vector3 P, S; public Quaternion R; public Transform Parent; }

        private static Dictionary<Transform, Trs> Snapshot(Transform root, Transform excluded, Transform excludedNode)
        {
            var result = new Dictionary<Transform, Trs>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if ((excluded == null || !t.IsChildOf(excluded)) && t != excludedNode)
                    result[t] = new Trs { P = t.localPosition, R = t.localRotation, S = t.localScale, Parent = t.parent };
            return result;
        }

        private static bool Same(Transform t, Trs trs) => t.parent == trs.Parent && t.localPosition == trs.P
            && t.localRotation == trs.R && t.localScale == trs.S;

        private static string Readback(string prefabPath, WeaponSightTable.Entry entry)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Transform container = FindContainer(asset.transform);
            GameObject kit = ExistingKit(container);
            bool optic = SightParts.IsOpticKit(entry.Kit);
            if (entry.Kit != null)
            {
                if (kit == null || PrefabUtility.GetCorrespondingObjectFromOriginalSource(kit) != entry.Kit)
                    throw new InvalidOperationException("Readback: экземпляр комплекта не найден");
                if ((kit.transform.localPosition - entry.KitLocalPosition).magnitude > 1e-6f
                    || Quaternion.Angle(kit.transform.localRotation, entry.KitLocalRotation) > 1e-3f
                    || (!optic && Mathf.Abs(kit.transform.Find("Rear").localPosition.z - entry.RearAlongRail) > 1e-6f)
                    || (!optic && Mathf.Abs(kit.transform.Find("Front").localPosition.z - entry.FrontAlongRail) > 1e-6f))
                    throw new InvalidOperationException("Readback: поза комплекта не совпала с таблицей");
                string scale = SightKitScale.Problem(kit.transform);
                if (scale != null) throw new InvalidOperationException("Readback: " + scale);
            }
            Transform shotSource = FindShotSource(asset);
            if (entry.ShotSource.Compare(ShotSourceFingerprint.Capture(shotSource, asset.transform)) != null)
                throw new InvalidOperationException("Readback: отпечаток ShotSource изменился");
            if (optic)
            {
                string binding = SightParts.OpticBindingProblem(SightParts.FindOptic(kit), SourceOf(asset), entry.Weapon, entry.ZeroDistance);
                if (binding != null) throw new InvalidOperationException("Readback: " + binding);
                return $"{prefabPath}: комплект {entry.Kit.name} (коллиматор), сетка сведена на {entry.ZeroDistance} м";
            }
            SightAdjustablePart moving = Moving(kit != null ? kit : asset);
            if (Mathf.Abs(moving.Offset - entry.MovingOffset) > 1e-6f)
                throw new InvalidOperationException("Readback: сдвиг подвижной детали не совпал с таблицей");
            return $"{prefabPath}: комплект {(entry.Kit != null ? entry.Kit.name : "встроенный")}, сдвиг {moving.Offset * 1000:+0.00;-0.00;0.00} мм";
        }
    }
}
