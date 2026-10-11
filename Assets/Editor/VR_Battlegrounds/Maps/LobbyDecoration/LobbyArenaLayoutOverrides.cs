using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Сценовые переопределения по окончательной геометрии шатра; игровые владельцы сохраняются.</summary>
    public static class LobbyArenaLayoutOverrides
    {
        public static Bounds[] Apply(Scene scene, Bounds arena, Bounds tent, out Bounds mirrorView)
        {
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var stations = all.Select(t => t.GetComponent<ArsenalEquipmentPoses>()).Where(p => p != null).ToArray();
            if (stations.Length != 2) throw new InvalidOperationException("Ожидались две существующие станции.");
            var sweeps = new List<Bounds>();
            foreach (var poses in stations)
            {
                var t = poses.transform;
                Undo.RecordObject(t, "Арсенал внутри шатра");
                t.position = new Vector3(arena.center.x + Mathf.Sign(t.position.x - arena.center.x) * 6.80f, t.position.y, t.position.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                sweeps.Add(MeasureSweep(poses));
            }
            foreach (string name in new[] { "Table_North", "Table_South" })
            {
                var t = all.Single(x => x.name == name);
                Undo.RecordObject(t, "Линия торца внутри физической арены");
                t.position = new Vector3(t.position.x, t.position.y, name == "Table_North" ? tent.max.z - .23f : tent.min.z + .23f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            }
            // Только неподвижный сценовый реквизит. Отрезаем выступающие концы, сохраняя исходные вырезы станций.
            foreach (string name in new[] { "Table_East", "Table_West" })
                foreach (var renderer in all.Single(x => x.name == name).GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.enabled || renderer.GetComponent<MeshFilter>() == null) continue;
                    Bounds b = renderer.bounds;
                    float min = Mathf.Max(b.min.z, tent.min.z + .46f), max = Mathf.Min(b.max.z, tent.max.z - .46f);
                    if (max <= min) continue;
                    var t = renderer.transform;
                    Undo.RecordObject(t, "Укоротить края столов по шатру");
                    var scale = t.localScale; scale.z *= (max - min) / b.size.z; t.localScale = scale;
                    var position = t.position; position.z = (max + min) * .5f; t.position = position;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                }
            var environment = scene.GetRootGameObjects().Single(r => r.name == "Environment").transform;
            var mirror = environment.Find("ArmoryRangeExistingGeometry/MirrorDisplayClean")
                ?? throw new InvalidOperationException("Не найдено существующее зеркало в сценовой геометрии стрельбища.");
            Undo.RecordObject(mirror, "Зеркало на восточную стену");
            mirror.SetPositionAndRotation(new Vector3(tent.max.x - .20f, arena.max.y + 1.445f, tent.center.z - 5.60f), Quaternion.Euler(0, -90, 0));
            PrefabUtility.RecordPrefabInstancePropertyModifications(mirror);
            mirrorView = new Bounds(new Vector3(tent.max.x - 1.05f, arena.max.y + 1.20f, tent.center.z - 5.60f), new Vector3(2.1f, 2.4f, 2.2f));
            var eastTable = all.Single(t => t.name == "Table_East");
            // В исходном столе только редакторский маркер SpawnZoneBoundaryBlock; физической формы и интерактивности нет.
            if (eastTable.GetComponentInChildren<VrBattlegrounds.LevelDesign.PhysicalArenaShape>() != null)
                throw new InvalidOperationException("Ниша зеркала затрагивает физическую форму; операция остановлена.");
            foreach (var block in eastTable.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "BoundaryBlock_0").ToArray())
            {
                var b = block.bounds;
                if (b.max.z <= -6.7f || b.min.z >= -4.5f) continue;
                foreach (var limits in new[] { new Vector2(b.min.z, -6.7f), new Vector2(-4.5f, b.max.z) })
                {
                    if (limits.y - limits.x < .05f) continue;
                    var piece = UnityEngine.Object.Instantiate(block.gameObject, block.transform.parent);
                    piece.name = "MirrorNicheTableRemainder";
                    Undo.RegisterCreatedObjectUndo(piece, "Остаток стола у зеркала");
                    var scale = piece.transform.localScale; scale.z *= (limits.y - limits.x) / b.size.z; piece.transform.localScale = scale;
                    var position = piece.transform.position; position.z = (limits.x + limits.y) * .5f; piece.transform.position = position;
                }
                Undo.DestroyObjectImmediate(block.gameObject);
            }
            foreach (var renderer in eastTable.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled).ToArray())
            {
                if (renderer.name == "BoundaryBlock_1") Undo.DestroyObjectImmediate(renderer.gameObject);
                else if (renderer.name == "MirrorNicheTableRemainder" && renderer.bounds.center.z > -5)
                    KeepInterval(renderer, renderer.bounds.min.z, -3.67f);
            }
            var westTable = all.Single(t => t != null && t.name == "Table_West");
            foreach (var renderer in westTable.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled).ToArray())
            {
                if (renderer.name == "BoundaryBlock_0") KeepInterval(renderer, renderer.bounds.min.z, -6.55f);
                else if (renderer.name == "BoundaryBlock_1") KeepInterval(renderer, 5.10f, renderer.bounds.max.z);
            }
            foreach (var target in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ShootingTarget>(true)).ToArray())
                if (target.transform.position.z < arena.center.z)
                    Undo.DestroyObjectImmediate(target.gameObject);
            // Один ряд на 15 м от открытого торца; игровые экземпляры и их ссылки не заменяются.
            var row = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ShootingTarget>(true))
                .OrderBy(t => t.name, StringComparer.Ordinal).ToArray();
            if (row.Length != 5) throw new InvalidOperationException("Для нового стрелкового ряда нужны пять существующих мишеней.");
            for (int i = 0; i < row.Length; i++)
            {
                var target = row[i];
                Undo.RecordObject(target.transform, "Ряд мишеней в 15 м от шатра");
                target.transform.rotation = Quaternion.Euler(0, 180, 0);
                var plate = target.Pivot.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Plate");
                var desired = new Vector3(arena.center.x + (i - 2) * 3f, plate.transform.position.y, tent.max.z + 15f);
                target.transform.position += desired - plate.transform.position;
                PrefabUtility.RecordPrefabInstancePropertyModifications(target.transform);
            }
            // Панель перекрывала четыре направления из правой части линии. Её владелец пишет только MPB/подпись.
            var rangeScreen = all.Single(t => t != null && t.name == "RangeMonitorNorth");
            Undo.RecordObject(rangeScreen, "Функциональный экран сбоку от огня");
            rangeScreen.SetPositionAndRotation(new Vector3(arena.max.x + 1.25f, rangeScreen.position.y, tent.max.z + 3.02f), Quaternion.Euler(0, 40, 0));
            PrefabUtility.RecordPrefabInstancePropertyModifications(rangeScreen);
            return sweeps.ToArray();
        }

        private static void KeepInterval(MeshRenderer renderer, float min, float max)
        {
            Bounds b = renderer.bounds; min = Mathf.Max(min, b.min.z); max = Mathf.Min(max, b.max.z);
            if (max - min < .03f) { Undo.DestroyObjectImmediate(renderer.gameObject); return; }
            var t = renderer.transform; Undo.RecordObject(t, "Отдельные участки столов у стен");
            var scale = t.localScale; scale.z *= (max - min) / b.size.z; t.localScale = scale;
            var position = t.position; position.z = (min + max) * .5f; t.position = position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        public static Bounds MeasureSweep(ArsenalEquipmentPoses source)
        {
            var clone = UnityEngine.Object.Instantiate(source.gameObject);
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            clone.transform.localScale = source.transform.lossyScale;
            try
            {
                var poses = clone.GetComponent<ArsenalEquipmentPoses>();
                Bounds result = default; bool first = true;
                for (int sample = 0; sample <= 200; sample++)
                {
                    poses.Apply(sample / 200f);
                    foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                        if (renderer.enabled)
                        { if (first) { result = renderer.bounds; first = false; } else result.Encapsulate(renderer.bounds); }
                }
                result.Expand(.12f); // Запас для рук и мелкой геометрии, не заменяет runtime-проверку оружия.
                return result;
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }
    }
}
