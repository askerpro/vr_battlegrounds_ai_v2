using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Editor.Gameplay.SightSplit;
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
    /// <para>
    /// Родной прицел (раунд 2, 2026-10-20; запись таблицы с Kit = null): постоянное разделение по
    /// <see cref="SightSplitTable"/> — <see cref="SightPartSplitter.Apply"/> на содержимом префаба, меши корпуса и детали
    /// сохраняются ассетами по <see cref="SightPartSplitter.MeshAssetPath"/>, узлы <c>Sight{Role}Frame/Sight{Role}</c>
    /// с <see cref="SightAdjustablePart"/>, подъём рамок Scar; затем сдвиг подвижной детали из записи. Повторный
    /// прогон возвращает разделение к исходному мешу и делает его заново — результат тот же.
    /// </para>
    /// <para>
    /// Сборка ствола: сборщик пака → <see cref="AfterBuild"/> (родной прицел разделяется всегда, сдвиг — из записи;
    /// запись не применилась — отказ, сборка откатывается; комплект — из записи) → баланс.
    /// Кнопка стенда «Применить к префабу» вызывает тот же код (<see cref="Apply"/>).
    /// </para>
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

        /// <summary>Применить запись таблицы, если она есть; null — записи нет. Для сборщика — <see cref="AfterBuild"/>.</summary>
        public static string ApplyIfRecorded(WeaponInfo info)
        {
            WeaponSightTable table = WeaponSightTable.Load();
            return info != null && table != null && table.Find(info) != null ? Apply(info) : null;
        }

        /// <summary>
        /// Шаг прицела сборки ствола (<c>WeaponRebuildFinish</c> после сохранения префаба сборщиком): родной прицел —
        /// разделение и сдвиг из записи, комплект — из записи. Запись не применяется (нет её, другая дистанция, отпечаток
        /// ShotSource), спецификация разделения не совпала с мешем пака, второй прицел — исключение до записи прицела.
        /// null — у ствола нет ни родного прицела, ни записи.
        /// </summary>
        public static string AfterBuild(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new InvalidOperationException("Нет префаба " + prefabPath);
            WeaponInfo info = InfoOf(prefab);
            WeaponSightTable table = WeaponSightTable.Load();
            WeaponSightTable.Entry entry = info != null && table != null ? table.Find(info) : null;
            SightSplitSpec spec = NativeSpec(prefabPath);
            if (spec != null)
            {
                if (entry != null && entry.Kit != null)
                    throw new InvalidOperationException($"{prefabPath}: у ствола свой прицел, а в таблице комплект {entry.Kit.name} — пересвести на стенде");
                return ApplyNative(prefabPath, spec, entry);
            }
            return entry != null ? ApplyToPrefab(prefabPath, entry) : null;
        }

        /// <summary>Есть ли у префаба прицел: деталь <see cref="SightAdjustablePart"/> или сетка коллиматора.</summary>
        public static bool HasSight(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return prefab != null && (prefab.GetComponentsInChildren<SightAdjustablePart>(true).Length > 0
                                      || prefab.GetComponentsInChildren<WeaponOpticView>(true).Length > 0);
        }

        /// <summary>Спецификация родного прицела префаба; null — у ствола нет своего прицела в таблице разделения.</summary>
        public static SightSplitSpec NativeSpec(string prefabPath) => SightSplitTable.All.FirstOrDefault(s => s.PrefabPath == prefabPath);

        /// <summary>WeaponInfo, чей WeaponPrefab — этот префаб (первый найденный); null — нет.</summary>
        public static WeaponInfo InfoOf(GameObject prefab) => AssetDatabase.FindAssets("t:WeaponInfo").Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<WeaponInfo>).FirstOrDefault(i => i != null && i.WeaponPrefab == prefab);

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
            if (entry != null && entry.Kit == null) return PreflightNative(asset, prefabPath, entry);
            return Check(asset, entry, out _, out _);
        }

        public static string ApplyToPrefab(string prefabPath, WeaponSightTable.Entry entry)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Применение прицела — только в Edit Mode");
            if (entry != null && entry.Kit == null)
            {
                SightSplitSpec spec = NativeSpec(prefabPath) ?? throw new InvalidOperationException($"{prefabPath}: запись родного прицела, а ствола нет в SightSplitTable");
                PreflightPrefab(prefabPath, entry);
                return ApplyNative(prefabPath, spec, entry);
            }
            if (NativeSpec(prefabPath) != null) throw new InvalidOperationException($"{prefabPath}: у ствола свой прицел — комплект не ставится");
            PreflightPrefab(prefabPath, entry);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Check(root, entry, out Transform container, out Transform shotSource);
                ShotSourceFingerprint before = ShotSourceFingerprint.Capture(shotSource, root.transform);
                GameObject kit = ExistingKit(container);
                Transform builtInMoving = null; // родной прицел — ApplyNative
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

            RestoreAssetId(prefabPath);
            return Readback(prefabPath, entry);
        }

        /// <summary>SaveAsPrefabAsset пишет NetworkIdentity._assetId = 0; вернуть канон и сверить на диске, как Tr15OpticBuilder.</summary>
        private static void RestoreAssetId(string prefabPath)
        {
            VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { prefabPath }, false);
            uint canonical = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(prefabPath)));
            uint? onDisk = VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.ReadOnDisk(prefabPath);
            if (onDisk != canonical)
                throw new InvalidOperationException($"{prefabPath}: NetworkIdentity._assetId на диске {onDisk}, ожидается {canonical}");
        }

        // ── Родной прицел ───────────────────────────────────────

        private static string PreflightNative(GameObject asset, string prefabPath, WeaponSightTable.Entry entry)
        {
            SightSplitSpec spec = NativeSpec(prefabPath) ?? throw new InvalidOperationException($"{prefabPath}: ствола нет в SightSplitTable — родной прицел не записывается");
            var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
            if (settings == null) throw new InvalidOperationException("Нет настроек калибровки: " + SettingsPath);
            if (entry.ZeroDistance != settings.DefaultZeroDistance)
                throw new InvalidOperationException($"Запись сведена на {entry.ZeroDistance} м, текущая пристрелка {settings.DefaultZeroDistance} м — пересвести на стенде");
            Transform shotSource = FindShotSource(asset);
            string drift = entry.ShotSource.Compare(ShotSourceFingerprint.Capture(shotSource, asset.transform));
            if (entry.ShotSource.IsEmpty || drift != null)
                throw new InvalidOperationException("Отпечаток ShotSource не совпал (" + (drift ?? "пуст") + ") — пересвести на стенде");
            SightSplitPartSpec moving = spec.MovingPart;
            if (moving == null && entry.MovingOffset != 0f) throw new InvalidOperationException("Подвижной детали нет — сдвиг в записи должен быть 0");
            if (moving != null && (entry.MovingOffset < -moving.TravelDown - 1e-7f || entry.MovingOffset > moving.TravelUp + 1e-7f))
                throw new InvalidOperationException($"Сдвиг {entry.MovingOffset * 1000:0.00} мм вне хода −{moving.TravelDown * 1000:0.00}…+{moving.TravelUp * 1000:0.00} мм");
            return $"{asset.name}: родной прицел, сдвиг {entry.MovingOffset * 1000:+0.00;-0.00;0.00} мм, пристрелка {entry.ZeroDistance} м, ShotSource {shotSource.name}";
        }

        /// <summary>
        /// Постоянное разделение родного прицела и сдвиг подвижной детали из <paramref name="entry"/>. Записи нет, она на
        /// другую дистанцию или с чужим отпечатком ShotSource — отказ до записи (сдвиг 0 молча потерял бы калибровку).
        /// </summary>
        public static string ApplyNative(string prefabPath, SightSplitSpec spec, WeaponSightTable.Entry entry)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Применение прицела — только в Edit Mode");
            var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
            if (settings == null) throw new InvalidOperationException("Нет настроек калибровки: " + SettingsPath);
            float offset;
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            // Разделение — на второй временной копии; в основную копию переносятся только его результаты, существующие
            // узлы рамок и деталей обновляются на месте (их fileID в префабе не меняются — повторный прогон тот же).
            GameObject fresh = PrefabUtility.LoadPrefabContents(prefabPath);
            SightSplitApplyResult split = null;
            try
            {
                if (ExistingKit(FindContainer(root.transform)) != null || root.GetComponentsInChildren<WeaponOpticView>(true).Length > 0)
                    throw new InvalidOperationException($"{prefabPath}: у ствола свой прицел, а в префабе стоит комплект/коллиматор — снимите его");
                Transform shotSource = FindShotSource(root);
                ShotSourceFingerprint before = ShotSourceFingerprint.Capture(shotSource, root.transform);
                if (entry == null)
                    throw new InvalidOperationException($"{prefabPath}: родной прицел без записи в {WeaponSightTable.AssetPath} — свести на стенде и сохранить в таблицу");
                if (entry.ZeroDistance != settings.DefaultZeroDistance)
                    throw new InvalidOperationException($"{prefabPath}: запись сведена на {entry.ZeroDistance} м, текущая пристрелка {settings.DefaultZeroDistance} м — пересвести на стенде");
                string drift = entry.ShotSource.IsEmpty ? "отпечаток в записи пуст" : entry.ShotSource.Compare(before);
                if (drift != null)
                    throw new InvalidOperationException($"{prefabPath}: запись таблицы не применяется — отпечаток ShotSource не совпал ({drift}); " +
                                                        "сборка изменила точку выстрела — пересвести на стенде");
                offset = entry.MovingOffset;

                // TRS всех узлов вне прицела: рамки Sight*Frame пересоздаются, рамки Scar поднимаются (спецификация).
                var raise = new HashSet<Transform>(spec.Raises.Select(r => root.transform.Find(r.NodePath)).Where(t => t != null));
                var untouched = new Dictionary<Transform, Trs>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (!raise.Contains(t) && !InsideSightFrame(t, spec))
                        untouched[t] = new Trs { P = t.localPosition, R = t.localRotation, S = t.localScale, Parent = t.parent };

                split = SightPartSplitter.Apply(fresh, spec);
                Persist(spec, split);
                Sync(root, fresh, spec);
                SightSplitPartSpec movingSpec = spec.MovingPart;
                if (movingSpec != null)
                {
                    SightAdjustablePart moving = root.transform.Find($"{movingSpec.NodePath}/{movingSpec.FrameName}/{movingSpec.PartName}").GetComponent<SightAdjustablePart>();
                    if (!moving.IsWithinTravel(offset))
                        throw new InvalidOperationException($"Сдвиг {offset * 1000:0.00} мм вне хода −{moving.TravelDown * 1000:0.00}…+{moving.TravelUp * 1000:0.00} мм");
                    moving.transform.localPosition = moving.NeutralLocalPosition + Vector3.up * offset;
                }
                else if (offset != 0f) throw new InvalidOperationException("Подвижной детали нет — сдвиг в записи должен быть 0");

                foreach (var pair in untouched)
                    if (pair.Key == null || !Same(pair.Key, pair.Value))
                        throw new InvalidOperationException("Изменился узел вне прицела: " + (pair.Key != null ? pair.Key.name : "удалён"));
                if (before.Compare(ShotSourceFingerprint.Capture(shotSource, root.transform)) != null)
                    throw new InvalidOperationException("Цепочка ShotSource изменилась — отказ сохранения");
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                    throw new InvalidOperationException("Не удалось сохранить " + prefabPath);
            }
            finally
            {
                split?.DestroyMeshes(); // временные меши; сохранённые — отдельные объекты-ассеты
                PrefabUtility.UnloadPrefabContents(fresh);
                PrefabUtility.UnloadPrefabContents(root);
            }
            RestoreAssetId(prefabPath);
            return ReadbackNative(prefabPath, spec, offset);
        }

        /// <summary>
        /// Переносит разделение из копии <paramref name="fresh"/> в <paramref name="root"/>: меш узла, поза поднятых
        /// рамок, узлы <c>Sight{Role}Frame/Sight{Role}[/Sight{Role}Mesh]</c>. Существующие узлы той же формы обновляются на
        /// месте (TRS и сериализованные поля компонентов), иначе заменяются копией.
        /// </summary>
        private static void Sync(GameObject root, GameObject fresh, SightSplitSpec spec)
        {
            foreach (SightNodeRaise raise in spec.Raises)
            {
                Transform from = fresh.transform.Find(raise.NodePath), to = root.transform.Find(raise.NodePath);
                to.SetLocalPositionAndRotation(from.localPosition, from.localRotation);
            }
            foreach (SightSplitPartSpec part in spec.Parts)
            {
                Transform freshNode = fresh.transform.Find(part.NodePath), node = root.transform.Find(part.NodePath);
                MeshFilter freshFilter = freshNode.GetComponent<MeshFilter>(), filter = node.GetComponent<MeshFilter>();
                if (freshFilter != null && filter != null) filter.sharedMesh = freshFilter.sharedMesh;
                Transform freshFrame = freshNode.Find(part.FrameName), frame = node.Find(part.FrameName);
                if (frame != null && SameShape(freshFrame, frame)) CopyTree(freshFrame, frame);
                else
                {
                    if (frame != null) UnityEngine.Object.DestroyImmediate(frame.gameObject);
                    GameObject copy = UnityEngine.Object.Instantiate(freshFrame.gameObject, node, false);
                    copy.name = freshFrame.name;
                }
            }
        }

        private static bool SameShape(Transform a, Transform b)
        {
            if (a.name != b.name || a.childCount != b.childCount) return false;
            var ta = a.GetComponents<Component>().Select(c => c.GetType()).ToArray();
            var tb = b.GetComponents<Component>().Select(c => c != null ? c.GetType() : null).ToArray();
            if (!ta.SequenceEqual(tb)) return false;
            for (int i = 0; i < a.childCount; i++) if (!SameShape(a.GetChild(i), b.GetChild(i))) return false;
            return true;
        }

        private static void CopyTree(Transform from, Transform to)
        {
            to.SetLocalPositionAndRotation(from.localPosition, from.localRotation);
            to.localScale = from.localScale;
            to.gameObject.layer = from.gameObject.layer;
            var source = from.GetComponents<Component>();
            var target = to.GetComponents<Component>();
            for (int i = 0; i < source.Length; i++)
                if (!(source[i] is Transform)) EditorUtility.CopySerialized(source[i], target[i]);
            for (int i = 0; i < from.childCount; i++) CopyTree(from.GetChild(i), to.GetChild(i));
        }

        private static bool InsideSightFrame(Transform t, SightSplitSpec spec)
        {
            foreach (SightSplitPartSpec part in spec.Parts)
                for (Transform c = t; c != null; c = c.parent)
                    if (c.name == part.FrameName && c.parent != null && c.parent.GetComponent<SightAdjustablePart>() == null) return true;
            return false;
        }

        /// <summary>Меши корпуса и детали — ассетами (на месте прежних, GUID сохраняется); узлы ссылаются на ассеты.</summary>
        private static void Persist(SightSplitSpec spec, SightSplitApplyResult split)
        {
            foreach (var pair in split.Splits)
            {
                if (pair.Value.Parts.Count == 0) continue;
                SightSplitPartSpec extracted = pair.Value.Parts[0].Spec;
                pair.Key.GetComponent<MeshFilter>().sharedMesh = SaveMesh(pair.Value.Body, SightPartSplitter.MeshAssetPath(spec, extracted, true));
                foreach (SightPartMesh part in pair.Value.Parts)
                    split.PartRenderers[part.Spec].GetComponent<MeshFilter>().sharedMesh = SaveMesh(part.Mesh, SightPartSplitter.MeshAssetPath(spec, part.Spec, false));
            }
        }

        private static Mesh SaveMesh(Mesh source, string path)
        {
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            SightKitBuilder.EnsureFolder(folder);
            Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = asset == null;
            if (create && File.Exists(path)) throw new InvalidOperationException("Путь меша занят ассетом другого типа: " + path);
            if (create) asset = new Mesh();
            CopyMesh(source, asset);
            asset.name = Path.GetFileNameWithoutExtension(path); // CreateAsset всё равно назовёт по файлу — первый и повторный прогон одинаковы
            // В игре меш только рисуется: без CPU-копии (как импорт FBX пака).
            var so = new SerializedObject(asset);
            so.FindProperty("m_IsReadable").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (create) AssetDatabase.CreateAsset(asset, path);
            else EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        /// <summary>
        /// Копия меша через Mesh API (сырые вершинные потоки, индексы по подмешам, bounds). EditorUtility.CopySerialized
        /// для меша, собранного SetVertexBufferData в несколько потоков (меши пака Hands), теряет вершины — проверено 2026-10-20.
        /// </summary>
        private static void CopyMesh(Mesh source, Mesh target)
        {
            using (var data = Mesh.AcquireReadOnlyMeshData(source))
            {
                target.Clear(false);
                target.indexFormat = source.indexFormat;
                target.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < data[0].vertexBufferCount; stream++)
                {
                    var bytes = data[0].GetVertexData<byte>(stream);
                    target.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
                }
                target.subMeshCount = source.subMeshCount;
                for (int s = 0; s < source.subMeshCount; s++)
                    target.SetIndices(source.GetIndices(s), MeshTopology.Triangles, s, false);
                target.bounds = source.bounds;
            }
        }

        private static string ReadbackNative(string prefabPath, SightSplitSpec spec, float offset)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            SightParts.Find(asset, out var rear, out var front, out var moving);
            foreach (SightSplitPartSpec part in spec.Parts)
            {
                Transform node = asset.transform.Find(part.NodePath);
                if (node == null || node.Find(part.FrameName + "/" + part.PartName) == null)
                    throw new InvalidOperationException($"Readback: нет {part.NodePath}/{part.FrameName}/{part.PartName}");
                if (!part.Extract) continue;
                if (AssetDatabase.GetAssetPath(node.GetComponent<MeshFilter>().sharedMesh) != SightPartSplitter.MeshAssetPath(spec, part, true))
                    throw new InvalidOperationException("Readback: у узла " + part.NodePath + " не меш корпуса");
                var partMesh = node.Find($"{part.FrameName}/{part.PartName}/{part.MeshObjectName}")?.GetComponent<MeshFilter>();
                if (partMesh == null || AssetDatabase.GetAssetPath(partMesh.sharedMesh) != SightPartSplitter.MeshAssetPath(spec, part, false))
                    throw new InvalidOperationException("Readback: у детали " + part.Role + " не сохранённый меш");
            }
            if (moving != null && Mathf.Abs(moving.Offset - offset) > 1e-6f)
                throw new InvalidOperationException("Readback: сдвиг подвижной детали не совпал");
            return $"{prefabPath}: родной прицел разделён ({spec.WeaponId}), " +
                   (moving != null ? $"сдвиг {moving.Offset * 1000:+0.00;-0.00;0.00} мм" : "без подвижной детали");
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
            else throw new InvalidOperationException("Запись без комплекта — родной прицел (PreflightNative)");
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
