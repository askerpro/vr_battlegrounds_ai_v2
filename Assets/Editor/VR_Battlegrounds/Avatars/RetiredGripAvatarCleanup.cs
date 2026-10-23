using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.EditorTools.VersionControl;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Снимает с оружия выведенный аватар хвата <c>MEF_Base_Avatar</c> (решение пользователя 2026-10-23; разбор —
    /// <c>tasks/weapon-batch-legacy-recipe/research/D-mef-cleanup-browning-mag.md</c>). Только префабы
    /// <see cref="WeaponsRoot" />; доноры сборки (<c>Assets/Art/Weapons/HandsPack/LegacyBuildDonors</c>) и сам префаб MEF
    /// не трогаются. Меню нет: вход — <see cref="DryRun" /> и <see cref="Apply()" /> (execute_code), оба возвращают отчёт.
    ///
    /// <para>
    /// Что делает с каждым префабом: удаляет записи хвата MEF всех точек (SerializedObject), переносит узлы Cyborg
    /// (<c>CyborgHandsBase</c>), лежавшие в папках MEF, в <c>&lt;родитель папки&gt;/CyborgHandsBase/&lt;точка&gt;</c> с
    /// сохранением мировой позы, удаляет папки <c>MEF_Base_Avatar</c> целиком и align MEF вне папок (вместе с опустевшими
    /// папками над ними, не выше <c>Grabs</c>). У <see cref="ExtraPointMagazine" /> удаляется вторая точка хвата
    /// <c>_additionalGrabPoints[0]</c> вместе с её записями (наследие сэмпла SDK, решение «убрать точку»).
    /// </para>
    ///
    /// <para>
    /// Порядок — вложенные раньше вкладывающих (магазин → ствол): у ствола после чистки его магазина снимаются ставшие
    /// пустыми overrides узлов папки MEF магазина (<c>PrefabUtility.RemoveUnusedOverrides</c>). После записи —
    /// <see cref="NetworkAssetIdNormalizer" /> и <see cref="UxrUniqueIdPersister" />.
    /// </para>
    ///
    /// <para>
    /// Сухой прогон делает всё то же на копиях в памяти (LoadPrefabContents) без сохранения. Любая неожиданность — ссылка
    /// на удаляемый узел не из записи MEF, чужие компоненты в папке MEF, override массивов хвата у вложенного префаба,
    /// компонент формы на удаляемой точке — проблема: <see cref="Apply()" /> тогда ничего не пишет. Apply сначала
    /// выполняет сухой прогон и сверяет его с цифрами исследования (<see cref="Apply(bool)" /> — без сверки).
    /// </para>
    /// </summary>
    public static class RetiredGripAvatarCleanup
    {
        public const string WeaponsRoot = "Assets/Prefabs/Weapons";

        /// <summary>Единственный магазин со второй точкой хвата (из донора <c>Gun_real_mag</c>).</summary>
        public const string ExtraPointMagazine = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower_Magazine.prefab";

        // Цифры исследования D (2026-10-23): префабы с данными MEF, записи, папки, overrides у стволов, узлы Cyborg в папках MEF.
        private const int ExpectedPrefabs     = 34;
        private const int ExpectedEntries     = 67;
        private const int ExpectedFolders     = 52;
        private const int ExpectedFolderNodes = 115;
        private const int ExpectedOverrides   = 40;
        private const int ExpectedMoved       = 6;
        private const int ExpectedLooseAligns = 26; // справочно: align MEF вне папок (затворы, помпы, слайды)

        private const string EntriesProperty = "_avatarGripPoseEntries";

        /// <summary>Сухой прогон: отчёт без записи префабов.</summary>
        public static string DryRun() => Run(false).Text;

        /// <summary>Применение со сверкой сухого прогона с цифрами исследования.</summary>
        public static string Apply() => Apply(false);

        /// <param name="ignoreResearchCounts">true — применить и при расхождении с цифрами исследования (проблемы всё равно блокируют).</param>
        public static string Apply(bool ignoreResearchCounts)
        {
            Summary dry = Run(false);
            if (dry.HasProblems || (!ignoreResearchCounts && dry.Mismatch))
            {
                string why = dry.HasProblems ? "есть проблемы" : "сухой прогон расходится с цифрами исследования";
                GameLog.Debug.Warning($"[RetiredGripAvatarCleanup] Не применено: {why}.");
                return $"НЕ ПРИМЕНЕНО: {why}. Префабы не менялись.\n\n" + dry.Text;
            }

            return Run(true).Text;
        }

        // ── Прогон ──────────────────────────────────────────────────────────────

        private sealed class Context
        {
            public string     MefGuid;
            public GameObject MefPrefab;
            public string     FolderName;
            public string     CyborgGuid;
            public string     CyborgFolderName;

            /// <summary>Префаб → исходные пути (с номером среди одноимённых) удалённых в нём поддеревьев.</summary>
            public readonly Dictionary<string, HashSet<string>> Deleted = new Dictionary<string, HashSet<string>>();
        }

        private sealed class Result
        {
            public string Path;
            public int    Posed, NoPose, Empty;   // свои записи MEF: с позой; без позы, с align; пустые
            public int    NestedEntries;          // записи MEF вложенных префабов (снимет их собственный проход)
            public int    ExtraPointEntries;      // все записи удалённой точки (MEF среди них — и в Empty/NoPose/Posed)
            public bool   ExtraPointRemoved;
            public int    Folders, FolderNodes, LooseAligns, SharedAligns, PrunedParents, Moved, SelectedReset;
            public int    OverridesRelated, OverridesRemoved;
            public readonly List<string> Problems = new List<string>();
            public readonly List<string> Notes    = new List<string>();

            public int  Entries  => Posed + NoPose + Empty;
            public bool HasData  => Entries > 0 || Folders > 0 || LooseAligns > 0;
            public bool Changed  => HasData || ExtraPointRemoved || Moved > 0 || SelectedReset > 0 || PrunedParents > 0 || OverridesRemoved > 0;
        }

        private sealed class Summary
        {
            public string Text;
            public bool   HasProblems;
            public bool   Mismatch;
        }

        /// <summary>Ссылка компонента префаба на объект префаба; <see cref="AvatarGuid" /> — у ссылки из записи хвата.</summary>
        private sealed class Ref
        {
            public Component Source;
            public string    Property;
            public string    AvatarGuid;
        }

        private static Summary Run(bool save)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException("RetiredGripAvatarCleanup: редактор занят Play/compile/import.");
            }

            string mefPath = HandsPackPoseImporter.MefBaseAvatarPath;
            string cyborgPath = GripTargetProfile.ResolveCyborgBasePath();
            var ctx = new Context
            {
                MefGuid          = HandsPackPoseImporter.MefBaseAvatarGuid,
                MefPrefab        = AssetDatabase.LoadAssetAtPath<GameObject>(mefPath),
                FolderName       = Path.GetFileNameWithoutExtension(mefPath),
                CyborgGuid       = GripTargetProfile.CyborgBaseGuid,
                CyborgFolderName = Path.GetFileNameWithoutExtension(cyborgPath)
            };
            if (AssetDatabase.AssetPathToGUID(cyborgPath) != ctx.CyborgGuid)
            {
                throw new InvalidOperationException($"RetiredGripAvatarCleanup: {cyborgPath} не CyborgHandsBase ({ctx.CyborgGuid}).");
            }

            var results = new List<Result>();
            foreach (string path in Ordered(ctx))
            {
                Result result = Process(path, save, ctx);
                results.Add(result);
                if (save && result.Problems.Count > 0) break; // остальное не пишется: отчёт покажет, что уже записано
            }

            return Format(results, save);
        }

        /// <summary>Затронутые префабы оружия (данные MEF и вкладывающие их) — вложенные раньше вкладывающих.</summary>
        private static List<string> Ordered(Context ctx)
        {
            string[] all = AssetDatabase.FindAssets("t:Prefab", new[] { WeaponsRoot })
                                        .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var direct = new HashSet<string>(all.Where(p =>
            {
                string text = File.ReadAllText(p);
                return text.Contains(ctx.MefGuid) || text.Contains("m_Name: " + ctx.FolderName);
            }));
            Dictionary<string, string[]> deps = all.ToDictionary(p => p, p => AssetDatabase.GetDependencies(p, false));
            var set = new HashSet<string>(direct.Concat(all.Where(p => deps[p].Any(direct.Contains))));

            var ordered = new List<string>();
            var done = new HashSet<string>();
            while (done.Count < set.Count)
            {
                List<string> ready = set.Where(p => !done.Contains(p) && deps[p].Where(d => d != p && set.Contains(d)).All(done.Contains))
                                        .OrderBy(p => p, StringComparer.Ordinal).ToList();
                if (ready.Count == 0) throw new InvalidOperationException("RetiredGripAvatarCleanup: цикл вложенных префабов.");
                ordered.AddRange(ready);
                done.UnionWith(ready);
            }

            return ordered;
        }

        private static Result Process(string path, bool save, Context ctx)
        {
            var r = new Result { Path = path };
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Dictionary<Transform, string> original = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => NodePath(root.transform, t));

                // Overrides — до правок: в сухом прогоне снятие записей вложенного магазина в памяти даёт свои overrides.
                Overrides(root, save, ctx, r);
                if (r.Problems.Count > 0) return r;

                List<Transform> folders = Folders(root, ctx);
                foreach (Transform folder in folders.Where(f => Own(f.gameObject)))
                {
                    r.Folders++;
                    r.FolderNodes += folder.GetComponentsInChildren<Transform>(true).Length - 1;
                }

                var candidates = new List<Transform>();
                RemoveEntries(root, path, save, ctx, r, candidates);
                if (r.Problems.Count > 0) return r;

                Dictionary<Object, List<Ref>> refs = References(root);
                var moves = new List<(Transform Node, Transform Folder)>();
                foreach (Transform folder in folders) CheckFolder(root, folder, refs, ctx, r, moves, original);
                if (r.Problems.Count > 0) return r;
                foreach ((Transform node, Transform folder) in moves.Distinct())
                {
                    Move(node, folder, ctx);
                    r.Moved++;
                    r.Notes.Add($"узел Cyborg {original[node]} → {NodePath(root.transform, node)}");
                }

                var delete = new List<Transform>(folders.Where(f => Own(f.gameObject)));
                foreach (Transform align in candidates.Where(t => t != null).Distinct())
                {
                    if (folders.Any(f => align.IsChildOf(f))) continue; // уходит вместе с папкой
                    if (Referenced(refs, align, true)) { r.SharedAligns++; continue; }
                    string problem = Content(align, true) ?? External(align, refs);
                    if (problem != null) { r.Problems.Add($"align {original[align]}: {problem}"); continue; }
                    if (!Own(align.gameObject)) continue; // align вложенного префаба удаляет его собственный проход
                    r.LooseAligns++;
                    delete.Add(align);
                }

                if (r.Problems.Count > 0) return r;

                var deleted = new HashSet<string>();
                foreach (Transform node in delete)
                {
                    if (node == null) continue;
                    deleted.Add(original[node]);
                    Transform parent = node.parent;
                    Object.DestroyImmediate(node.gameObject);
                    r.PrunedParents += PruneEmpty(parent, root.transform, original, deleted);
                }

                ctx.Deleted[path] = deleted;
                if (!save || !r.Changed) return r;

                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) r.Problems.Add("SaveAsPrefabAsset не сохранил префаб");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (save && r.Changed && r.Problems.Count == 0)
            {
                // SaveAsPrefabAsset пишет _assetId = 0 и флаги UXR «не в префабе» — вернуть канон.
                NetworkAssetIdNormalizer.Normalize(new[] { path }, false);
                string uxr = UxrUniqueIdPersister.Normalize(path);
                if (uxr != null) r.Notes.Add("UxrUniqueIdPersister: " + uxr);
                VerifySaved(path, ctx, r);
            }

            return r;
        }

        // ── Шаги ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Overrides вложенных префабов, чьи узлы MEF удалены их собственным проходом: сухой прогон считает overrides на
        /// эти узлы, применение снимает ставшие пустыми (<c>RemoveUnusedOverrides</c>). Override массивов хвата вложенного
        /// префаба — проблема: чистка вложенного его не снимет.
        /// </summary>
        private static void Overrides(GameObject root, bool save, Context ctx, Result r)
        {
            GameObject[] instances = root.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject)
                                         .Where(PrefabUtility.IsOutermostPrefabInstanceRoot).ToArray();
            var cleaned = new List<GameObject>();
            foreach (GameObject instance in instances)
            {
                string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
                PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instance) ?? Array.Empty<PropertyModification>();
                foreach (PropertyModification m in modifications)
                {
                    if (m.target is UxrGrabbableObject && (m.propertyPath.Contains(EntriesProperty) || m.propertyPath.StartsWith("_additionalGrabPoints", StringComparison.Ordinal)))
                    {
                        r.Problems.Add($"override {m.propertyPath} у вложенного {instance.name} ({source}): массивы хвата переопределены стволом");
                    }

                    if (!save && m.target != null && InDeleted(m.target, ctx)) r.OverridesRelated++;
                }

                if (ctx.Deleted.TryGetValue(source, out HashSet<string> gone) && gone.Count > 0) cleaned.Add(instance);
            }

            if (!save || cleaned.Count == 0 || r.Problems.Count > 0) return;
            int before = cleaned.Sum(i => (PrefabUtility.GetPropertyModifications(i) ?? Array.Empty<PropertyModification>()).Length);
            PrefabUtility.RemoveUnusedOverrides(cleaned.ToArray(), InteractionMode.AutomatedAction);
            int after = cleaned.Sum(i => (PrefabUtility.GetPropertyModifications(i) ?? Array.Empty<PropertyModification>()).Length);
            r.OverridesRemoved = before - after;
        }

        /// <summary>Цель override — узел вложенного префаба, удалённый его проходом (сухой прогон).</summary>
        private static bool InDeleted(Object target, Context ctx)
        {
            Transform t = target is Component c ? c.transform : (target as GameObject)?.transform;
            if (t == null || !ctx.Deleted.TryGetValue(AssetDatabase.GetAssetPath(target), out HashSet<string> deleted)) return false;
            string path = NodePath(t.root, t);
            return deleted.Any(d => path == d || path.StartsWith(d + "/", StringComparison.Ordinal));
        }

        /// <summary>
        /// Записи MEF всех точек; у <see cref="ExtraPointMagazine" /> — вторая точка целиком. Свои удаляются; у вложенного
        /// префаба в сухом прогоне снимаются в памяти (их запишет проход магазина), при записи их уже быть не должно.
        /// </summary>
        private static void RemoveEntries(GameObject root, string path, bool save, Context ctx, Result r, List<Transform> candidates)
        {
            foreach (UxrGrabbableObject grabbable in root.GetComponentsInChildren<UxrGrabbableObject>(true))
            {
                bool own = Own(grabbable);
                string where = NodePath(root.transform, grabbable.transform);
                var so = new SerializedObject(grabbable);

                SerializedProperty selected = so.FindProperty("_selectedAvatarForGrips");
                if (selected.objectReferenceValue != null && selected.objectReferenceValue == ctx.MefPrefab && own)
                {
                    // Выбор аватара в инспекторе хватов SDK: ссылка на выведенный префаб.
                    selected.objectReferenceValue = null;
                    r.SelectedReset++;
                }

                SerializedProperty additional = so.FindProperty("_additionalGrabPoints");
                if (own && path == ExtraPointMagazine && grabbable.transform == root.transform) RemoveExtraPoint(root, grabbable, additional, ctx, r, candidates);

                var lists = new List<(string Name, SerializedProperty Point)> { ("_grabPoint", so.FindProperty("_grabPoint")) };
                for (int i = 0; i < additional.arraySize; i++) lists.Add(($"_additionalGrabPoints[{i}]", additional.GetArrayElementAtIndex(i)));
                foreach ((string name, SerializedProperty point) in lists)
                {
                    if (point.FindPropertyRelative("_defaultGripPoseInfo._avatarPrefabGuid").stringValue == ctx.MefGuid)
                    {
                        r.Problems.Add($"{where}.{name}: запись по умолчанию — MEF");
                    }

                    SerializedProperty entries = point.FindPropertyRelative(EntriesProperty);
                    for (int k = entries.arraySize - 1; k >= 0; k--)
                    {
                        SerializedProperty entry = entries.GetArrayElementAtIndex(k);
                        if (entry.FindPropertyRelative("_avatarPrefabGuid").stringValue != ctx.MefGuid) continue;
                        if (!own && save)
                        {
                            r.Problems.Add($"{where}.{name}: вложенный префаб ещё хранит запись MEF — сначала его собственный проход");
                            continue;
                        }

                        if (own) Count(entry, r, candidates);
                        else r.NestedEntries++;
                        entries.DeleteArrayElementAtIndex(k);
                    }
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>Вторая точка хвата магазина Browning (<c>_additionalGrabPoints[0]</c>) вместе с её записями.</summary>
        private static void RemoveExtraPoint(GameObject root, UxrGrabbableObject grabbable, SerializedProperty additional, Context ctx, Result r,
            List<Transform> candidates)
        {
            if (additional.arraySize == 0)
            {
                r.Notes.Add("вторая точка хвата уже удалена");
                return;
            }

            if (additional.arraySize != 1)
            {
                r.Problems.Add($"точек хвата {additional.arraySize + 1}, ожидалось 2");
                return;
            }

            foreach (UxrGrabPointShape shape in root.GetComponentsInChildren<UxrGrabPointShape>(true))
            {
                int index = new SerializedObject(shape).FindProperty("_grabPointIndex._index").intValue;
                if (index >= 1) r.Problems.Add($"форма точки {shape.GetType().Name} на {NodePath(root.transform, shape.transform)} — точка {index}");
            }

            if (r.Problems.Count > 0) return;
            SerializedProperty entries = additional.GetArrayElementAtIndex(0).FindPropertyRelative(EntriesProperty);
            for (int k = 0; k < entries.arraySize; k++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(k);
                r.ExtraPointEntries++;
                if (entry.FindPropertyRelative("_avatarPrefabGuid").stringValue == ctx.MefGuid) Count(entry, r, candidates);
                else AddAligns(entry, candidates);
            }

            additional.DeleteArrayElementAtIndex(0);
            r.ExtraPointRemoved = true;
            r.Notes.Add($"удалена вторая точка хвата {grabbable.name} (_additionalGrabPoints[0]) и её записи: {r.ExtraPointEntries}");
        }

        private static void Count(SerializedProperty entry, Result r, List<Transform> candidates)
        {
            bool pose = entry.FindPropertyRelative("_handPose").objectReferenceValue != null;
            bool aligned = AddAligns(entry, candidates);
            if (pose) r.Posed++;
            else if (aligned) r.NoPose++;
            else r.Empty++;
        }

        private static bool AddAligns(SerializedProperty entry, List<Transform> candidates)
        {
            var left  = entry.FindPropertyRelative("_gripAlignTransformHandLeft").objectReferenceValue as Transform;
            var right = entry.FindPropertyRelative("_gripAlignTransformHandRight").objectReferenceValue as Transform;
            if (left != null) candidates.Add(left);
            if (right != null) candidates.Add(right);
            return left != null || right != null;
        }

        /// <summary>
        /// Папка MEF: в поддереве только Transform и snap-трансформы; ссылки извне — только записи Cyborg на листья (их узлы
        /// переносятся). У папки вложенного префаба проверяются лишь ссылки снаружи его экземпляра — остальное проверил его проход.
        /// </summary>
        private static void CheckFolder(GameObject root, Transform folder, Dictionary<Object, List<Ref>> refs, Context ctx, Result r,
            List<(Transform, Transform)> moves, Dictionary<Transform, string> original)
        {
            bool own = Own(folder.gameObject);
            GameObject instance = own ? null : PrefabUtility.GetOutermostPrefabInstanceRoot(folder.gameObject);
            if (own)
            {
                string content = Content(folder, false);
                if (content != null) r.Problems.Add($"папка {original[folder]}: {content}");
            }

            foreach ((Object target, Ref reference) in Incoming(folder, refs))
            {
                if (!own)
                {
                    if (PrefabUtility.GetOutermostPrefabInstanceRoot(reference.Source) == instance) continue;
                    r.Problems.Add($"папка вложенного {original[folder]}: {Describe(root.transform, reference)} → {target.name}");
                    continue;
                }

                var node = target as Transform;
                bool movable = reference.AvatarGuid == ctx.CyborgGuid && node != null && node != folder && node.childCount == 0 && Content(node, false) == null;
                if (movable) moves.Add((node, folder));
                else r.Problems.Add($"папка {original[folder]}: {Describe(root.transform, reference)} → {target.name}");
            }
        }

        /// <summary>
        /// Узел Cyborg из папки MEF — в <c>&lt;родитель папки&gt;/CyborgHandsBase/&lt;путь внутри папки&gt;</c>; недостающие узлы
        /// создаются с локальной позой исходных, мировая поза узла сохраняется.
        /// </summary>
        private static void Move(Transform node, Transform folder, Context ctx)
        {
            var chain = new List<Transform>();
            for (Transform p = node.parent; p != folder; p = p.parent) chain.Insert(0, p);
            Transform target = Child(folder.parent, ctx.CyborgFolderName, folder);
            foreach (Transform link in chain) target = Child(target, link.name, link);
            node.SetParent(target, true);
        }

        private static Transform Child(Transform parent, string name, Transform template)
        {
            Transform child = parent.Cast<Transform>().FirstOrDefault(c => c.name == name);
            if (child != null) return child;
            child = new GameObject(name) { layer = template.gameObject.layer }.transform;
            child.SetParent(parent, false);
            child.SetLocalPositionAndRotation(template.localPosition, template.localRotation);
            child.localScale = template.localScale;
            return child;
        }

        /// <summary>Опустевшие папки над удалённым узлом (только Transform, свои), не выше <c>Grabs</c> и корня.</summary>
        private static int PruneEmpty(Transform parent, Transform root, Dictionary<Transform, string> original, HashSet<string> deleted)
        {
            int pruned = 0;
            while (parent != null && parent != root && parent.name != "Grabs" && parent.childCount == 0 &&
                   parent.GetComponents<Component>().Length == 1 && Own(parent.gameObject))
            {
                Transform next = parent.parent;
                if (original.TryGetValue(parent, out string was)) deleted.Add(was);
                Object.DestroyImmediate(parent.gameObject);
                pruned++;
                parent = next;
            }

            return pruned;
        }

        private static void VerifySaved(string path, Context ctx, Result r)
        {
            string text = File.ReadAllText(path);
            if (text.Contains("_avatarPrefabGuid: " + ctx.MefGuid)) r.Problems.Add("после записи в файле остались записи MEF");
            if (text.Contains("m_Name: " + ctx.FolderName)) r.Problems.Add($"после записи в файле осталась папка {ctx.FolderName}");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;
            bool nested = prefab.GetComponentsInChildren<UxrGrabbableObject>(true)
                                .SelectMany(g => Enumerable.Range(0, g.GrabPointCount).Select(i => g.GetGrabPoint(i)))
                                .SelectMany(p => p.AvatarGripPoseEntries).Any(e => e.AvatarPrefabGuid == ctx.MefGuid);
            if (nested) r.Problems.Add("после записи у вложенного префаба остались записи MEF");
            if (prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == ctx.FolderName)) r.Problems.Add($"после записи осталась вложенная папка {ctx.FolderName}");
            if (path == ExtraPointMagazine && prefab.GetComponent<UxrGrabbableObject>().GrabPointCount != 1) r.Problems.Add("после записи у магазина не одна точка хвата");
        }

        // ── Ссылки и содержимое ─────────────────────────────────────────────────

        /// <summary>Все ссылки компонентов префаба (кроме Transform: m_Children/m_Father — иерархия) на объекты префаба.</summary>
        private static Dictionary<Object, List<Ref>> References(GameObject root)
        {
            var map = new Dictionary<Object, List<Ref>>();
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;
                var so = new SerializedObject(component);
                SerializedProperty property = so.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    Object value = property.objectReferenceValue;
                    Transform t = value is Component c ? c.transform : (value as GameObject)?.transform;
                    if (t == null || !t.IsChildOf(root.transform)) continue;
                    if (!map.TryGetValue(value, out List<Ref> list)) map[value] = list = new List<Ref>();
                    list.Add(new Ref { Source = component, Property = property.propertyPath, AvatarGuid = EntryGuid(so, property.propertyPath) });
                }
            }

            return map;
        }

        /// <summary>GUID аватара записи хвата, которой принадлежит свойство; null — не запись хвата.</summary>
        private static string EntryGuid(SerializedObject so, string propertyPath)
        {
            if (!(so.targetObject is UxrGrabbableObject)) return null;
            int at = propertyPath.IndexOf(EntriesProperty + ".Array.data[", StringComparison.Ordinal);
            if (at < 0) return null;
            int end = propertyPath.IndexOf(']', at);
            return so.FindProperty(propertyPath.Substring(0, end + 1) + "._avatarPrefabGuid")?.stringValue;
        }

        /// <summary>Ссылки на объекты поддерева <paramref name="node" /> из компонентов вне его.</summary>
        private static IEnumerable<(Object Target, Ref Reference)> Incoming(Transform node, Dictionary<Object, List<Ref>> refs)
        {
            IEnumerable<Object> targets = node.GetComponentsInChildren<Transform>(true)
                                              .SelectMany(t => t.GetComponents<Component>().Where(c => c != null).Cast<Object>().Append(t.gameObject));
            foreach (Object target in targets)
            {
                if (!refs.TryGetValue(target, out List<Ref> list)) continue;
                foreach (Ref reference in list.Where(x => x.Source != null && !x.Source.transform.IsChildOf(node))) yield return (target, reference);
            }
        }

        private static bool Referenced(Dictionary<Object, List<Ref>> refs, Transform node, bool gripOnly) =>
            Incoming(node, refs).Any(x => !gripOnly || x.Reference.AvatarGuid != null);

        private static string External(Transform node, Dictionary<Object, List<Ref>> refs)
        {
            (Object target, Ref reference) = Incoming(node, refs).FirstOrDefault();
            return reference == null ? null : $"ссылка {Describe(node.root, reference)} → {target.name}";
        }

        /// <summary>В поддереве только Transform и snap-трансформы; <paramref name="leaf" /> — без детей.</summary>
        private static string Content(Transform node, bool leaf)
        {
            if (leaf && node.childCount > 0) return $"у align {node.childCount} дочерних";
            foreach (Transform t in node.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c == null) return $"в поддереве компонент без скрипта на {t.name}";
                    if (!(c is Transform) && !(c is UxrGrabbableObjectSnapTransform)) return $"в поддереве {c.GetType().Name} на {t.name}";
                }
            }

            return null;
        }

        private static string Describe(Transform root, Ref reference) =>
            $"{reference.Source.GetType().Name} {NodePath(root, reference.Source.transform)}.{reference.Property}" +
            (reference.AvatarGuid != null ? $" (запись {reference.AvatarGuid})" : "");

        // ── Вспомогательное ─────────────────────────────────────────────────────

        /// <summary>Верхние папки MEF (папка в папке входит в верхнюю), свои и вложенных префабов.</summary>
        private static List<Transform> Folders(GameObject root, Context ctx)
        {
            List<Transform> named = root.GetComponentsInChildren<Transform>(true).Where(t => t != root.transform && t.name == ctx.FolderName).ToList();
            return named.Where(f => !named.Any(o => o != f && f.IsChildOf(o))).ToList();
        }

        /// <summary>Объект самого префаба, а не вложенного (добавленные в экземпляр — свои).</summary>
        private static bool Own(Object o) => !PrefabUtility.IsPartOfPrefabInstance(o);

        /// <summary>Путь от корня с номером среди одноимённых братьев (<c>имя#номер</c>).</summary>
        private static string NodePath(Transform root, Transform t)
        {
            var segments = new List<string>();
            for (Transform p = t; p != null && p != root; p = p.parent)
            {
                int ordinal = p.parent == null ? 0 : p.parent.Cast<Transform>().TakeWhile(c => c != p).Count(c => c.name == p.name);
                segments.Insert(0, $"{p.name}#{ordinal}");
            }

            return string.Join("/", segments);
        }

        // ── Отчёт ───────────────────────────────────────────────────────────────

        private static Summary Format(List<Result> results, bool save)
        {
            int prefabs  = results.Count(r => r.HasData);
            int entries  = results.Sum(r => r.Entries);
            int folders  = results.Sum(r => r.Folders);
            int nodes    = results.Sum(r => r.FolderNodes);
            int moved    = results.Sum(r => r.Moved);
            int related  = results.Sum(r => r.OverridesRelated);
            int removed  = results.Sum(r => r.OverridesRemoved);
            int overrides = save ? removed : related;
            bool problems = results.Any(r => r.Problems.Count > 0);
            bool mismatch = prefabs != ExpectedPrefabs || entries != ExpectedEntries || folders != ExpectedFolders ||
                            overrides != ExpectedOverrides || moved != ExpectedMoved;

            var text = new StringBuilder();
            text.AppendLine(save ? "RetiredGripAvatarCleanup — ПРИМЕНЕНИЕ" : "RetiredGripAvatarCleanup — СУХОЙ ПРОГОН (префабы не менялись)");
            text.AppendLine($"Префабов с данными MEF: {prefabs} (исследование {ExpectedPrefabs}); всего в проходе {results.Count}, записано {(save ? results.Count(r => r.Changed && r.Problems.Count == 0) : 0)}.");
            text.AppendLine($"Записей MEF: {entries} (исследование {ExpectedEntries}): с позой {results.Sum(r => r.Posed)}, без позы с align {results.Sum(r => r.NoPose)}, пустых {results.Sum(r => r.Empty)}; " +
                            $"у вложенных в памяти {results.Sum(r => r.NestedEntries)}.");
            text.AppendLine($"Папок MEF: {folders} (исследование {ExpectedFolders}), узлов в них {nodes} (исследование {ExpectedFolderNodes}, справочно); " +
                            $"align вне папок {results.Sum(r => r.LooseAligns)} (исследование {ExpectedLooseAligns}, справочно), общих с другими аватарами оставлено {results.Sum(r => r.SharedAligns)}, " +
                            $"опустевших папок {results.Sum(r => r.PrunedParents)}.");
            text.AppendLine($"Узлов Cyborg перенесено: {moved} (исследование {ExpectedMoved}); выбор аватара редактора сброшен: {results.Sum(r => r.SelectedReset)}.");
            text.AppendLine(save ? $"Overrides снято (RemoveUnusedOverrides): {removed} (исследование {ExpectedOverrides})."
                                 : $"Overrides на узлы MEF вложенных магазинов: {related} (исследование {ExpectedOverrides}).");
            text.AppendLine($"Вторая точка {ExtraPointMagazine}: {(results.Any(r => r.ExtraPointRemoved) ? "удалена" : "не удалялась")}.");
            text.AppendLine(problems ? "ПРОБЛЕМЫ: есть (ниже)." : "Проблем нет.");
            text.AppendLine(mismatch ? "Сверка с исследованием: РАСХОЖДЕНИЕ." : "Сверка с исследованием: совпало.");
            text.AppendLine();

            foreach (Result r in results)
            {
                text.AppendLine($"{r.Path}: записей {r.Entries} (поза {r.Posed}/без позы {r.NoPose}/пустых {r.Empty}), вложенных {r.NestedEntries}, " +
                                $"папок {r.Folders} ({r.FolderNodes} узл.), align {r.LooseAligns}, общих {r.SharedAligns}, перенос {r.Moved}, " +
                                $"overrides {(save ? r.OverridesRemoved : r.OverridesRelated)}{(r.ExtraPointRemoved ? ", точка 1 удалена" : "")}");
                foreach (string note in r.Notes) text.AppendLine("  · " + note);
                foreach (string problem in r.Problems) text.AppendLine("  ! " + problem);
            }

            if (save && problems)
            {
                text.AppendLine();
                text.AppendLine("ПРЕРВАНО: префаб с проблемой не записан, следующие не обрабатывались; записанные выше — в git (откат — git checkout).");
            }

            string report = text.ToString();
            GameLog.Debug.Info($"[RetiredGripAvatarCleanup] {(save ? "apply" : "dry-run")}: префабов {prefabs}, записей {entries}, папок {folders}, " +
                               $"перенос {moved}, overrides {overrides}, проблем {results.Sum(r => r.Problems.Count)}");
            return new Summary { Text = report, HasProblems = problems, Mismatch = mismatch };
        }
    }
}
