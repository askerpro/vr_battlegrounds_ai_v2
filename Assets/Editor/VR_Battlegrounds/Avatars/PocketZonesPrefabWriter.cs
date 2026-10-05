using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.EditorTools.VersionControl;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Arsenal;

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
    ///     Mode они остаются ожидающим снимком. Применение — явное действие в редакторе аватара,
    ///     с проверкой SHA исходного prefab, его зависимостей и метаданных.
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
            public string prefabGuid;
            public List<FileStamp> origin = new List<FileStamp>();
            public List<Pocket> pockets = new List<Pocket>();
        }

        [Serializable] private class FileStamp { public string path, sha; }
        private static Task<System.Collections.Generic.Dictionary<string, string>> captureTask;
        private static Snapshot capturing;
        public static bool IsCapturing => captureTask != null;
        public static bool HasPending => !string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""));
        public static string PendingPath => ReadPending()?.prefabPath;
        public static string[] PendingPaths
        {
            get
            {
                string prefix = Directory.GetParent(Application.dataPath).FullName + Path.DirectorySeparatorChar;
                return ReadPending()?.origin?.Select(s => s.path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? s.path.Substring(prefix.Length).Replace('\\', '/') : s.path).ToArray() ?? Array.Empty<string>();
            }
        }
        private static Snapshot ReadPending()
        {
            string json = SessionState.GetString(PendingKey, "");
            try { return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Snapshot>(json); }
            catch { return null; }
        }
        public static void Discard()
        {
            if (IsCapturing) throw new InvalidOperationException("Дождитесь завершения capture перед отбрасыванием снимка.");
            SessionState.EraseString(PendingKey);
        }
        public static string ValidateOrigin(Dictionary<string, string> current)
        {
            var snapshot = ReadPending();
            if (snapshot == null || snapshot.origin == null || snapshot.origin.Count == 0) return "Снимок не содержит SHA. Снимите зоны заново в Play Mode.";
            if (AssetDatabase.GUIDToAssetPath(snapshot.prefabGuid) != snapshot.prefabPath) return "Исходный prefab перемещён или заменён.";
            return snapshot.origin.All(s => current.TryGetValue(s.path, out var sha) && sha == s.sha) ? null : "Исходный prefab или его зависимости изменились после capture. Снимите зоны заново.";
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
                    if (HasPending) GameLog.Player.Info("[PocketZones] Снимок ожидает явного применения в редакторе аватара: " + PendingPath);
                }
            };
        }

        private static bool CaptureValidate() => EditorApplication.isPlaying && UxrAvatar.LocalAvatar != null;

        private static void Capture()
        {
            Capture(UxrAvatar.LocalAvatar);
        }

        public static void Capture(UxrAvatar avatar)
        {
            if (!EditorApplication.isPlaying || avatar == null || avatar != UxrAvatar.LocalAvatar)
                throw new InvalidOperationException("Capture разрешён только для локального живого аватара в Play Mode.");
            if (IsCapturing || HasPending) throw new InvalidOperationException("Сначала примените или отбросьте предыдущий снимок.");
            string    prefabPath = AssetDatabase.GUIDToAssetPath(avatar.PrefabGuid);

            if (string.IsNullOrEmpty(prefabPath))
            {
                throw new InvalidOperationException("У живого аватара не найден исходный prefab.");
            }
            Workbench.AvatarScopedActions.RequireOwnPrefab(prefabPath);

            var snapshot = new Snapshot { prefabPath = prefabPath, prefabGuid = avatar.PrefabGuid };

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

            if (snapshot.pockets.Count == 0 || snapshot.pockets.Select(p => p.anchorName).Distinct().Count() != snapshot.pockets.Count)
                throw new InvalidOperationException("Нет карманов либо их имена неоднозначны.");
            string workspace = Directory.GetParent(Application.dataPath).FullName;
            string[] paths = AssetDatabase.GetDependencies(prefabPath, true).Select(Workbench.AvatarScopedActions.SnapshotPath)
                .Concat(new[] { "Packages/manifest.json", "Packages/packages-lock.json" }).ToArray();
            capturing = snapshot;
            // Значения переживают reload даже до SHA. Неполный origin явно блокирует Apply вместо потери данных.
            SessionState.SetString(PendingKey, JsonUtility.ToJson(snapshot));
            captureTask = Task.Run(() => ArsenalFileSnapshot.Read(workspace, paths, CancellationToken.None));
            EditorApplication.update += FinishCapture;
        }

        private static void FinishCapture()
        {
            if (captureTask == null || !captureTask.IsCompleted) return;
            EditorApplication.update -= FinishCapture;
            try
            {
                capturing.origin = captureTask.GetAwaiter().GetResult().Select(p => new FileStamp { path = p.Key, sha = p.Value }).ToList();
                SessionState.SetString(PendingKey, JsonUtility.ToJson(capturing));
                GameLog.Player.Info($"[PocketZones] Снято карманов: {capturing.pockets.Count}. Ожидается явное применение к {capturing.prefabPath}.");
            }
            catch (Exception e) { GameLog.Player.Error("[PocketZones] Capture не сохранён: " + e.Message); }
            finally { captureTask = null; capturing = null; }
        }

        public static string ApplyPending()
        {
            string json = SessionState.GetString(PendingKey, string.Empty);
            if (EditorApplication.isPlayingOrWillChangePlaymode || string.IsNullOrEmpty(json)) throw new InvalidOperationException("Нет доступного edit-mode снимка.");

            Snapshot   snapshot = JsonUtility.FromJson<Snapshot>(json);
            GameObject contents = PrefabUtility.LoadPrefabContents(snapshot.prefabPath);
            var        report   = new StringBuilder();
            int        changed  = 0;

            try
            {
                UxrGrabbableObjectAnchor[] anchors = contents.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true);

                // Все адреса проверяются до первого изменения: частичное сопоставление запрещено.
                foreach (Pocket pocket in snapshot.pockets)
                {
                    var matches = anchors.Where(a => a.name == pocket.anchorName && VrBattlegrounds.Interaction.AnchorRole.IsAvatarPocket(a)).ToArray();
                    if (matches.Length != 1 || (matches[0].GrabProxy?.GrabPointCount ?? 0) != pocket.grabPoints.Count)
                        throw new InvalidOperationException("Структура кармана изменилась: " + pocket.anchorName);
                    for (int i = 0; i < pocket.grabPoints.Count; i++)
                        if (pocket.grabPoints[i].hasBox && matches[0].GrabProxy.GetGrabPoint(i).GrabProximityBox == null)
                            throw new InvalidOperationException("Коробка хвата потеряна: " + pocket.anchorName);
                }

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
                    Workbench.AvatarScopedActions.SetCanonicalAssetId(contents, snapshot.prefabPath);
                    if (!PrefabUtility.SaveAsPrefabAsset(contents, snapshot.prefabPath)) throw new InvalidOperationException("Prefab не сохранён; снимок оставлен ожидающим.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // SaveAsPrefabAsset затирает _assetId значением Mirror — вернуть канон.
            if (changed > 0)
            {
                Workbench.AvatarScopedActions.NormalizeAndVerify(snapshot.prefabPath);
            }

            SessionState.EraseString(PendingKey);
            return $"{snapshot.prefabPath}: изменено значений — {changed}.\n{report}";
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
