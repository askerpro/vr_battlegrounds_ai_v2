using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.EditorTools.VersionControl;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    ///     Переносит размеры зон карманов, подобранные в Play Mode (обычно — в шлеме, с
    ///     <c>AnchorZonesDebugView</c>), в префаб аватара. Unity откатывает правки компонентов при
    ///     выходе из Play Mode — без этого подобранные цифры пришлось бы переписывать руками.
    ///
    ///     <para>
    ///     Как работает. Пункт меню в Play Mode снимает значения со своего аватара
    ///     (<see cref="UxrAvatar.LocalAvatar" />) в <see cref="SessionState" />; после выхода из Play
    ///     Mode они записываются в префаб, найденный по <see cref="UxrAvatar.PrefabGuid" />. Запись
    ///     откладывается намеренно: сохранять ассеты посреди игры — лишний риск.
    ///     </para>
    ///
    ///     <para>
    ///     Сопоставление — по имени якоря, не по пути: в рантайме body IK UltimateXR переподвешивает
    ///     кости под <c>Dummy Forward</c>, и пути живого аватара с префабом не совпадают. Имена
    ///     карманов (<c>Anchor_Back</c>, <c>Anchor_Hip_R</c>, <c>MagazinePocket</c>) — контракт
    ///     проекта и уникальны в аватаре. Прокси и коробка хвата берутся по ссылкам самого якоря в
    ///     префабе (<c>GrabProxy</c>, <c>GrabProximityBox</c>).
    ///     </para>
    ///
    ///     Что переносится: <c>Max Place Distance</c> якорей аватара; у их прокси — <c>Max
    ///     Distance Grab</c> каждой точки хвата и размеры <c>BoxCollider</c> в режиме
    ///     <c>BoxConstrained</c>. Значения ложатся переопределениями в префаб этого аватара — у
    ///     разных моделей разные тела, общий префаб кармана не трогается.
    /// </summary>
    [InitializeOnLoad]
    internal static class PocketZonesPrefabWriter
    {
        private const string MenuPath   = "Tools/VR Battlegrounds/Avatars/Save Pocket Zones To Prefab";
        private const string PendingKey = "VrBattlegrounds.PendingPocketZones";
        private const float  Tolerance  = 1e-5f;

        [Serializable]
        private class Snapshot
        {
            public string        prefabPath;
            public List<Pocket> pockets = new List<Pocket>();
        }

        [Serializable]
        private class Pocket
        {
            public string          anchorName;
            public float           maxPlaceDistance;
            public List<GrabPoint> grabPoints = new List<GrabPoint>();
        }

        [Serializable]
        private class GrabPoint
        {
            public float   maxDistanceGrab;
            public bool    hasBox;
            public Vector3 boxCenter;
            public Vector3 boxSize;
        }

        static PocketZonesPrefabWriter()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    ApplyPending();
                }
            };
        }

        [MenuItem(MenuPath, true)]
        private static bool CaptureValidate() => EditorApplication.isPlaying && UxrAvatar.LocalAvatar != null;

        [MenuItem(MenuPath)]
        private static void Capture()
        {
            UxrAvatar avatar     = UxrAvatar.LocalAvatar;
            string    prefabPath = AssetDatabase.GUIDToAssetPath(avatar.PrefabGuid);

            if (string.IsNullOrEmpty(prefabPath))
            {
                EditorUtility.DisplayDialog("Зоны карманов", $"У аватара '{avatar.name}' не найден префаб (PrefabGuid '{avatar.PrefabGuid}').", "OK");
                return;
            }

            var snapshot = new Snapshot { prefabPath = prefabPath };

            foreach (UxrGrabbableObjectAnchor anchor in avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                // Гнездо магазина оружия, лежащего в кармане, — не карман аватара.
                if (!VrBattlegrounds.Interaction.AnchorRole.IsAvatarPocket(anchor)) continue;

                var pocket = new Pocket { anchorName = anchor.name, maxPlaceDistance = anchor.MaxPlaceDistance };

                UxrGrabbableObject proxy = anchor.GrabProxy;
                if (proxy != null)
                {
                    for (int i = 0; i < proxy.GrabPointCount; i++)
                    {
                        UxrGrabPointInfo point = proxy.GetGrabPoint(i);
                        BoxCollider      box   = point.GrabProximityMode == UxrGrabProximityMode.BoxConstrained ? point.GrabProximityBox : null;

                        pocket.grabPoints.Add(new GrabPoint
                        {
                            maxDistanceGrab = point.MaxDistanceGrab,
                            hasBox          = box != null,
                            boxCenter       = box != null ? box.center : Vector3.zero,
                            boxSize         = box != null ? box.size : Vector3.zero
                        });
                    }
                }

                snapshot.pockets.Add(pocket);
            }

            SessionState.SetString(PendingKey, JsonUtility.ToJson(snapshot));
            Debug.Log($"[PocketZonesPrefabWriter] Сняты зоны {snapshot.pockets.Count} карманов с '{avatar.name}'. " +
                      $"Запишутся в '{prefabPath}' после выхода из Play Mode.");
        }

        private static void ApplyPending()
        {
            string json = SessionState.GetString(PendingKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            SessionState.EraseString(PendingKey);

            Snapshot   snapshot = JsonUtility.FromJson<Snapshot>(json);
            GameObject contents = PrefabUtility.LoadPrefabContents(snapshot.prefabPath);
            var        report   = new StringBuilder();
            int        changed  = 0;

            try
            {
                UxrGrabbableObjectAnchor[] anchors = contents.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true);

                foreach (Pocket pocket in snapshot.pockets)
                {
                    UxrGrabbableObjectAnchor[] matches = anchors.Where(a => a.name == pocket.anchorName).ToArray();
                    if (matches.Length != 1)
                    {
                        report.AppendLine($"  '{pocket.anchorName}': в префабе найдено {matches.Length} якорей с таким именем — пропущено");
                        continue;
                    }

                    changed += ApplyPocket(matches[0], pocket, report);
                }

                if (changed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, snapshot.prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // SaveAsPrefabAsset затирает _assetId значением Mirror — вернуть канон.
            if (changed > 0)
            {
                NetworkAssetIdNormalizer.Normalize(new[] { snapshot.prefabPath }, false);
            }

            Debug.Log($"[PocketZonesPrefabWriter] '{snapshot.prefabPath}': изменено значений — {changed}.\n{report}");
        }

        private static int ApplyPocket(UxrGrabbableObjectAnchor anchor, Pocket pocket, StringBuilder report)
        {
            int changed = 0;

            if (Mathf.Abs(anchor.MaxPlaceDistance - pocket.maxPlaceDistance) >= Tolerance)
            {
                report.AppendLine($"  {anchor.name}: Max Place Distance {anchor.MaxPlaceDistance:0.###} → {pocket.maxPlaceDistance:0.###}");
                anchor.MaxPlaceDistance = pocket.maxPlaceDistance;
                changed++;
            }

            UxrGrabbableObject proxy = anchor.GrabProxy;
            if (proxy == null) return changed;

            for (int i = 0; i < pocket.grabPoints.Count && i < proxy.GrabPointCount; i++)
            {
                GrabPoint        saved = pocket.grabPoints[i];
                UxrGrabPointInfo point = proxy.GetGrabPoint(i);

                if (Mathf.Abs(point.MaxDistanceGrab - saved.maxDistanceGrab) >= Tolerance)
                {
                    report.AppendLine($"  {anchor.name} → {proxy.name} [точка {i}]: Max Distance Grab {point.MaxDistanceGrab:0.###} → {saved.maxDistanceGrab:0.###}");
                    point.MaxDistanceGrab = saved.maxDistanceGrab;
                    EditorUtility.SetDirty(proxy);
                    changed++;
                }

                BoxCollider box = point.GrabProximityBox;
                if (saved.hasBox && box != null &&
                    ((box.center - saved.boxCenter).sqrMagnitude >= Tolerance || (box.size - saved.boxSize).sqrMagnitude >= Tolerance))
                {
                    report.AppendLine($"  {anchor.name} → {box.name}: коробка {box.size} → {saved.boxSize}, центр {box.center} → {saved.boxCenter}");
                    box.center = saved.boxCenter;
                    box.size   = saved.boxSize;
                    changed++;
                }
            }

            return changed;
        }
    }
}
