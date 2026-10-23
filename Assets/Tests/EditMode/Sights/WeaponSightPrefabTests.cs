using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Editor.Gameplay.SightSplit;
using VrBattlegrounds.Editor.Weapons.Sights;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Tests.Sights
{
    /// <summary>
    /// Принятая схема прицелов в игровых префабах (проверено пользователем в Unity и шлеме 2026-10-20, раунды 2–3):
    /// родной прицел разделён постоянно (узлы Sight{Role}Frame/Sight{Role}, меши-ассеты *_SightSplit / *_Sight{Role}),
    /// сдвиг подвижной детали — из WeaponSightTable; комплект — вложенный SightKit_* в позе записи, крепление сидит в
    /// пазу планки («Посадка в паз»); сборщики по умолчанию оставляют текущие позы хвата. Только чтение ассетов.
    /// </summary>
    public sealed class WeaponSightPrefabTests
    {
        private const float PointTolerance = .00005f;   // 0,05 мм
        private const float SeatTolerance = .0003f;     // контакт и ось, как у стенда
        private const float LugTolerance = .001f;       // упор от центра паза

        private static IEnumerable<string> NativeWeapons => SightSplitTable.All.Select(s => s.WeaponId);

        private static IEnumerable<string> KitWeapons
        {
            get
            {
                WeaponSightTable table = WeaponSightTable.Load();
                return table == null ? Enumerable.Empty<string>()
                    : table.Entries.Where(e => e != null && e.Kit != null && e.Weapon != null).Select(e => e.Weapon.WeaponId).ToArray();
            }
        }

        private static WeaponInfo InfoOf(GameObject prefab) =>
            AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<WeaponInfo>).FirstOrDefault(i => i != null && i.WeaponPrefab == prefab);

        [TestCaseSource(nameof(NativeWeapons))]
        public void NativeSight_SplitIsPersistent(string weaponId)
        {
            SightSplitSpec spec = SightSplitTable.Get(weaponId);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            Assert.That(prefab, Is.Not.Null, spec.PrefabPath);
            foreach (SightSplitPartSpec part in spec.Parts)
            {
                Transform node = prefab.transform.Find(part.NodePath);
                Assert.That(node, Is.Not.Null, $"{weaponId}: нет узла {part.NodePath}");
                Transform frame = node.Find(part.FrameName), detail = frame != null ? frame.Find(part.PartName) : null;
                Assert.That(detail, Is.Not.Null, $"{weaponId}: нет {part.NodePath}/{part.FrameName}/{part.PartName}");
                var component = detail.GetComponent<SightAdjustablePart>();
                Assert.That(component, Is.Not.Null, $"{weaponId} {part.Role}: нет SightAdjustablePart");
                Assert.That(component.Role, Is.EqualTo(part.Role));
                Assert.That(component.TravelDown, Is.EqualTo(part.TravelDown), $"{weaponId} {part.Role}: ход вниз");
                Assert.That(component.TravelUp, Is.EqualTo(part.TravelUp), $"{weaponId} {part.Role}: ход вверх");
                // Pivot в собранной позиции — на точке R/F спецификации (в мешах узла).
                Vector3 neutral = frame.TransformPoint(component.NeutralLocalPosition), point = node.TransformPoint(part.PointMesh);
                Assert.That((neutral - point).magnitude, Is.LessThan(PointTolerance), $"{weaponId} {part.Role}: pivot ≠ точке спецификации");
                if (!part.Extract) continue;

                MeshFilter body = node.GetComponent<MeshFilter>();
                Assert.That(AssetDatabase.GetAssetPath(body.sharedMesh), Is.EqualTo(SightPartSplitter.MeshAssetPath(spec, part, true)), $"{weaponId}: меш корпуса узла");
                Transform meshObject = detail.Find(part.MeshObjectName);
                Assert.That(meshObject, Is.Not.Null, $"{weaponId} {part.Role}: нет {part.MeshObjectName}");
                Assert.That(meshObject.GetComponent<SkinnedMeshRenderer>(), Is.Null, "деталь — статический меш");
                Assert.That(meshObject.GetComponent<MeshRenderer>(), Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(meshObject.GetComponent<MeshFilter>().sharedMesh), Is.EqualTo(SightPartSplitter.MeshAssetPath(spec, part, false)),
                            $"{weaponId} {part.Role}: меш детали");
                Mesh source = SightPartSplitter.SourceMesh(part);
                Assert.That(source, Is.Not.Null, $"{weaponId}: исходный меш {part.SourceMeshPath}:{part.SourceMeshLocalId}");
                Assert.That(body.sharedMesh.vertexCount, Is.EqualTo(source.vertexCount), "корпус сохраняет все вершины исходного меша");
            }
            Assert.That(prefab.GetComponentsInChildren<SightAdjustablePart>(true).Length, Is.EqualTo(2), $"{weaponId}: ровно две половины прицела");
        }

        [TestCaseSource(nameof(NativeWeapons))]
        public void NativeSight_TableOffsetApplied(string weaponId)
        {
            SightSplitSpec spec = SightSplitTable.Get(weaponId);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            WeaponInfo info = InfoOf(prefab);
            Assert.That(info, Is.Not.Null, $"{weaponId}: нет WeaponInfo префаба");
            WeaponSightTable.Entry entry = WeaponSightTable.Load()?.Find(info);
            Assert.That(entry, Is.Not.Null, $"{weaponId}: нет записи в {WeaponSightTable.AssetPath}");
            Assert.That(entry.Kit, Is.Null, $"{weaponId}: у ствола свой прицел — в таблице не комплект");
            Assert.That(entry.ShotSource.Compare(ShotSourceFingerprint.Capture(SightParts.FindShotSource(prefab), prefab.transform)), Is.Null,
                        $"{weaponId}: отпечаток ShotSource записи не совпадает с префабом — пересвести");
            SightParts.Find(prefab, out _, out _, out SightAdjustablePart moving);
            if (spec.MovingPart == null)
            {
                Assert.That(moving, Is.Null);
                Assert.That(entry.MovingOffset, Is.EqualTo(0f));
                return;
            }
            Assert.That(moving, Is.Not.Null);
            Assert.That(moving.Offset, Is.EqualTo(entry.MovingOffset).Within(1e-6f), $"{weaponId}: сдвиг детали ≠ записи таблицы");
            Assert.That(moving.IsWithinTravel(entry.MovingOffset), Is.True);
        }

        /// <summary>
        /// Автосведение пересборки (родной прицел без записи, решение пользователя 2026-10-23, проверено на worker:
        /// Scar −2,0254 мм) — тот же решатель, что «Свести» стенда: по разделённым деталям префаба оно повторяет сдвиг
        /// принятой записи таблицы. Содержимое префаба загружается и выгружается без сохранения.
        /// </summary>
        [TestCaseSource(nameof(NativeWeapons))]
        public void NativeSight_AutoZeroReproducesTableEntry(string weaponId)
        {
            SightSplitSpec spec = SightSplitTable.Get(weaponId);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            WeaponSightTable.Entry entry = WeaponSightTable.Load()?.Find(InfoOf(prefab));
            Assert.That(entry, Is.Not.Null, $"{weaponId}: нет записи в {WeaponSightTable.AssetPath}");
            // Писатель прицела — в Assembly-CSharp-Editor, на которую asmdef тестов сослаться не может: по имени типа.
            Type authoring = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VrBattlegrounds.Editor.Weapons.Sights.WeaponSightAuthoring")).FirstOrDefault(t => t != null);
            Assert.That(authoring, Is.Not.Null, "WeaponSightAuthoring");
            string settingsPath = (string)authoring.GetField("SettingsPath").GetValue(null);
            var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(settingsPath);
            Assert.That(settings, Is.Not.Null, settingsPath);
            Assert.That(entry.ZeroDistance, Is.EqualTo(settings.DefaultZeroDistance), $"{weaponId}: запись на другую дистанцию");
            System.Reflection.MethodInfo autoZero = authoring.GetMethod("AutoZero",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(autoZero, Is.Not.Null, "WeaponSightAuthoring.AutoZero");

            GameObject contents = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
            try
            {
                object[] args = { contents, SightParts.FindShotSource(contents), settings, spec.PrefabPath, null };
                float offset = (float)autoZero.Invoke(null, args);
                Assert.That(offset, Is.EqualTo(entry.MovingOffset).Within(1e-6f), $"{weaponId}: автосведение ≠ записи таблицы ({args[4]})");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [TestCaseSource(nameof(KitWeapons))]
        public void Kit_NestedInRecordedPose(string weaponId)
        {
            WeaponSightTable.Entry entry = WeaponSightTable.Load().Entries.Single(e => e.Weapon != null && e.Weapon.WeaponId == weaponId);
            GameObject prefab = entry.Weapon.WeaponPrefab;
            Transform container = prefab.transform.Find("MeshContainer");
            Assert.That(container, Is.Not.Null);
            var kits = container.Cast<Transform>().Where(t => t.name.StartsWith("SightKit_", StringComparison.Ordinal)).ToArray();
            Assert.That(kits.Length, Is.EqualTo(1), $"{weaponId}: один вложенный комплект под MeshContainer");
            Transform kit = kits[0];
            Assert.That(PrefabUtility.IsAnyPrefabInstanceRoot(kit.gameObject), Is.True, "комплект — вложенный префаб");
            Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(kit.gameObject), Is.EqualTo(entry.Kit), $"{weaponId}: комплект записи");
            Assert.That((kit.localPosition - entry.KitLocalPosition).magnitude, Is.LessThan(1e-6f), "поза комплекта из записи");
            Assert.That(Quaternion.Angle(kit.localRotation, entry.KitLocalRotation), Is.LessThan(1e-3f));
            Assert.That(SightKitScale.Problem(kit), Is.Null, "комплект в реальном размере");
            Assert.That(prefab.GetComponentsInChildren<SightAdjustablePart>(true).Count(p => !p.transform.IsChildOf(kit)), Is.EqualTo(0), "второго прицела нет");
            if (SightParts.IsOpticKit(entry.Kit)) return;
            SightParts.Find(kit.gameObject, out _, out _, out SightAdjustablePart moving);
            Assert.That(moving.Offset, Is.EqualTo(entry.MovingOffset).Within(1e-6f), $"{weaponId}: сдвиг детали комплекта ≠ записи");
        }

        [TestCaseSource(nameof(KitWeapons))]
        public void Kit_MountSeatedInRailSlot(string weaponId)
        {
            WeaponSightTable.Entry entry = WeaponSightTable.Load().Entries.Single(e => e.Weapon != null && e.Weapon.WeaponId == weaponId);
            GameObject prefab = entry.Weapon.WeaponPrefab;
            Transform kit = prefab.transform.Find("MeshContainer").Cast<Transform>().Single(t => t.name.StartsWith("SightKit_", StringComparison.Ordinal));
            Transform muzzle = SightParts.FindShotSource(prefab);
            Matrix4x4 frame = Matrix4x4.TRS(muzzle.position, muzzle.rotation, Vector3.one).inverse;
            var probe = new SurfaceProbe();
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null || !ActiveInPrefab(filter.transform)) continue;
                if (filter.name.Contains("Highlight") || filter.transform.IsChildOf(kit) || filter.GetComponentInParent<SightAdjustablePart>(true) != null) continue;
                probe.Add(filter.sharedMesh, frame * filter.transform.localToWorldMatrix);
            }
            var mounts = kit.GetComponentsInChildren<Transform>(true).Where(t => t.name == SightRailMount.MountName).ToArray();
            Assert.That(mounts.Length, Is.EqualTo(SightParts.IsOpticKit(entry.Kit) ? 1 : 2), $"{weaponId}: узлы крепления {SightRailMount.MountName}");
            foreach (Transform mount in mounts)
            {
                Vector3 p = frame.MultiplyPoint3x4(mount.position);
                SightRailInfo rail = SightRailMount.FindRail(probe, p.z);
                Assert.That(rail, Is.Not.Null, $"{weaponId} {mount.parent.name}: под креплением нет планки");
                Assert.That(p.y - rail.TopY, Is.InRange(-SeatTolerance, SeatTolerance), $"{weaponId} {mount.parent.name}: контакт не на вершинах зубцов ({rail})");
                Assert.That(p.x - rail.AxisX, Is.InRange(-SeatTolerance, SeatTolerance), $"{weaponId} {mount.parent.name}: не по оси планки");
                int slot = rail.NearestSlot(p.z);
                Assert.That(slot, Is.GreaterThanOrEqualTo(0), $"{weaponId} {mount.parent.name}: под креплением нет поперечных пазов");
                Assert.That(p.z - rail.SlotCenters[slot], Is.InRange(-LugTolerance, LugTolerance), $"{weaponId} {mount.parent.name}: упор не в пазу");
            }
        }

        /// <summary>activeInHierarchy у ассета префаба вне сцены всегда false — активность по activeSelf цепочки.</summary>
        private static bool ActiveInPrefab(Transform t)
        {
            for (; t != null; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        [TestCase("VrBattlegrounds.Editor.Gameplay.KinemationWeaponBuilder", "Build")]
        [TestCase("VrBattlegrounds.Editor.Gameplay.HandsPackWeaponBuilder", "BuildFull")]
        [TestCase("VrBattlegrounds.Editor.Gameplay.HandsPackLegacyRebuild", "RebuildAll")]
        public void Builders_KeepCurrentGripPosesByDefault(string typeName, string methodName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, typeName);
            var parameter = type.GetMethods().Where(m => m.Name == methodName)
                .SelectMany(m => m.GetParameters()).SingleOrDefault(p => p.ParameterType.Name == "WeaponGripPoseMode");
            Assert.That(parameter, Is.Not.Null, $"{typeName}.{methodName}: нет параметра режима поз хвата");
            Assert.That(parameter.HasDefaultValue, Is.True);
            Assert.That(parameter.DefaultValue.ToString(), Is.EqualTo("KeepCurrent"), $"{typeName}.{methodName}: по умолчанию «Оставить текущие позы»");
        }
    }
}
