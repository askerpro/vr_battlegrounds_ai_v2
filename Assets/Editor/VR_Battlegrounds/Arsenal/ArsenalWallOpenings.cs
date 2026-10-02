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
    /// <summary>Проёмы в блочных наружных стенах: меш и физика имеют одинаковые отверстия под станции.</summary>
    public static class ArsenalWallOpenings
    {
        public static string Apply(Scene scene)
        {
            var volumes = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SpawnZoneBoundaryOpening>(true)).ToArray();
            int changed = 0;
            foreach (var block in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SpawnZoneBoundaryBlock>(true)).ToArray())
            {
                var filter = block.GetComponent<MeshFilter>();
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null) continue;
                if (!block.SourceCaptured)
                {
                    if (block.transform.Find("OpeningBlocks") != null) throw new InvalidOperationException("Нужно восстановить исходное состояние блока " + block.name);
                    block.CaptureSourceState();
                }
                var matches = volumes.Where(v => v.WorldBounds.Intersects(renderer.bounds)).ToArray();
                if (matches.Length == 0)
                {
                    var oldParts = block.transform.Find("OpeningBlocks");
                    if (oldParts != null) UnityEngine.Object.DestroyImmediate(oldParts.gameObject);
                    block.RestoreSourceState();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    foreach (var sourceCollider in block.GetComponents<Collider>()) PrefabUtility.RecordPrefabInstancePropertyModifications(sourceCollider);
                    continue;
                }
                bool alongX = filter.transform.lossyScale.x > filter.transform.lossyScale.z;
                var ranges = new List<Vector2>();
                float top = -.5f;
                foreach (var volume in matches)
                {
                    var bounds = volume.WorldBounds;
                    float min = float.MaxValue, max = float.MinValue;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var local = filter.transform.InverseTransformPoint(corner);
                        float horizontal = alongX ? local.x : local.z;
                        min = Mathf.Min(min, horizontal); max = Mathf.Max(max, horizontal);
                        top = Mathf.Max(top, local.y);
                    }
                    ranges.Add(new Vector2(Mathf.Max(-.5f, min), Mathf.Min(.5f, max)));
                }
                top = Mathf.Clamp(top, -.5f, .5f);
                var parts = new List<Bounds>();
                if (top < .5f) parts.Add(new Bounds(new Vector3(0, (top + .5f) * .5f, 0), new Vector3(1, .5f - top, 1)));
                float cursor = -.5f;
                foreach (var range in ranges.OrderBy(r => r.x))
                {
                    if (range.x > cursor) parts.Add(Part(cursor, range.x, top, alongX));
                    cursor = Mathf.Max(cursor, range.y);
                }
                if (cursor < .5f) parts.Add(Part(cursor, .5f, top, alongX));
                var previous = block.transform.Find("OpeningBlocks");
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
                var group = new GameObject("OpeningBlocks").transform;
                group.SetParent(block.transform, false);
                int index = 0;
                foreach (var part in parts)
                {
                    var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    segment.name = "BoundaryBlock_" + index++;
                    segment.transform.SetParent(group, false);
                    segment.transform.localPosition = part.center;
                    segment.transform.localScale = part.size;
                    segment.GetComponent<Renderer>().sharedMaterials = renderer.sharedMaterials;
                }
                VrBattlegrounds.Editor.GameTagsTool.ApplyToHierarchy(group.gameObject, scene.path, new VrBattlegrounds.Editor.GameTagsTool.Result());
                renderer.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                foreach (var box in filter.GetComponents<BoxCollider>()) { box.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(box); }
                var collider = filter.GetComponent<MeshCollider>();
                if (collider != null) { collider.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(collider); }
                changed++;
            }
            return scene.name + ": стены с проёмами " + changed;
        }


        private static Bounds Part(float min, float max, float top, bool x)
        {
            var center = new Vector3(0, (top - .5f) * .5f, 0);
            var size = new Vector3(1, top + .5f, 1);
            if (x) { center.x = (min + max) * .5f; size.x = max - min; }
            else { center.z = (min + max) * .5f; size.z = max - min; }
            return new Bounds(center, size);
        }

    }
}
