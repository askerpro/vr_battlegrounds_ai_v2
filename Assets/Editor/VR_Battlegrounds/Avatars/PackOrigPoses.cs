using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Gameplay;
using VrBattlegrounds.EditorTools.VersionControl;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Библиотека исходных поз паков (<c>_orig</c>): поза кисти руки пака в кадре клипа и место её ладони на предмете —
    /// источник для будущих аватаров. Сборщик оружия из паков готовит только их; позы и хваты аватаров
    /// (<c>_handPoses</c>, записи <c>UxrGripPoseInfo</c>) здесь не пишутся — это отдельные инструменты
    /// (<see cref="HandsPackPoseImporter" /> по явной команде, Grip Extraction).
    ///
    /// <para>
    /// <b>Поза.</b> <see cref="UxrHandPoseAsset" /> <c>&lt;Пак&gt;_&lt;Предмет&gt;_&lt;Точка&gt;_orig</c> в
    /// <c>Assets/Art/HandPoses/Orig/&lt;Пак&gt;/</c>: дескриптор кисти пака в универсальных осях SDK, как его снимают
    /// <see cref="HandsPackPoseExtractor" /> и <see cref="KinemationPoseExtractor" /> (три фаланги, без пястных). Рука, не
    /// снятая с клипа, — зеркало снятой (правило <c>HandsPackPoseImporter.CreatePoseAsset</c>). Предмет — имя префаба
    /// оружия, не имя прежней позы (<c>HandsPack_Gun_Grip</c> → <c>HandsPack_BrowningHiPower_Grip_orig</c>). Ассет
    /// перезаписывается на месте (GUID прежний), только если дескриптор источника изменился.
    /// </para>
    ///
    /// <para>
    /// <b>Snap-точка.</b> Пустой <see cref="Transform" /> без компонентов
    /// <c>&lt;Grabs&gt;/Orig/&lt;имя позы&gt;/Left|Right</c> в префабе предмета (<c>Grabs</c> — <c>MeshContainer/Grabs</c>, иначе
    /// <c>Grabs</c> корня, иначе корень): рамка ладони руки пака в кадре клипа (<see cref="GripSample.PalmInBody" />) —
    /// начало в центре ладони SDK, оси — универсальные оси кисти. От граббера аватара не зависит: align аватара потом —
    /// snap · (рамка ладони → граббер) его кисти. В записи хватов и <c>UxrGrabbableObject</c> не входит.
    /// Запись — только добавленные объекты и их трансформы через экземпляр в превью-сцене
    /// (<see cref="PrefabUtility.ApplyAddedGameObject" />), без пересохранения префаба целиком; YAML сверяется до и после
    /// (<see cref="YamlProblems" />), лишнее изменение откатывает файл.
    /// </para>
    ///
    /// <para>
    /// <b>Магазин (эксперимент).</b> Клипы перезарядки пака: кадр, где рука держит магазин, — середина самого длинного
    /// окна, в котором магазин вынут из гнезда, близок к ладони и жёстко идёт с ней (<see cref="ScanFrames" />). Поза
    /// <c>…_Magazine_orig</c> (у дробовиков — <c>…_Shell_orig</c>), snap — в префабе магазина в осях детали <c>Mesh</c>.
    /// Snap-точки только у рук, которые держат магазин в клипе (зеркало детали без боковой оси не определено).
    /// </para>
    ///
    /// <para>
    /// Источники и окна — <c>&lt;Пак&gt;_&lt;Предмет&gt;_orig_sources.json</c> рядом с позами. Массовый экспорт —
    /// <see cref="ExportAll" /> (меню и окно арсенала), при сборке — <see cref="ExportForWeapon" />.
    /// </para>
    /// </summary>
    public static class PackOrigPoses
    {
        public const string Root = "Assets/Art/HandPoses/Orig";
        public const string Suffix = "_orig";
        public const string SnapFolder = "Orig";
        public const string HandsPack = "HandsPack";
        public const string Kinemation = "Kinemation";

        private const string MenuPath = "Tools/VR Battlegrounds/Avatars/Экспортировать исходные позы паков (_orig)";

        // Поиск кадра с магазином в руке: единицы — мир клипа (у пака Hands руки крупнее реальных ~×1.4).
        private const float RigidMeters = 0.005f;   // сдвиг «ладонь → магазин» внутри окна от его первого кадра
        private const float RigidDegrees = 5f;
        private const float NearMesh = 0.06f;      // центр ладони — ближайшая точка меша магазина
        private const float NearOrigin = 0.10f;    // меш недоступен на CPU — начало детали
        private const float MinSeat = 0.02f;       // магазин вынут: сдвиг от места в первом кадре клипа
        private const int MinFrames = 3;
        private const int MaxPoints = 400;

        private static readonly UxrHandSide[] Sides = { UxrHandSide.Left, UxrHandSide.Right };

        public static string OrigName(string pack, string item, string point) => $"{pack}_{item}_{point}{Suffix}";

        public static string PoseAssetPath(string pack, string name) => $"{Root}/{pack}/{name}.asset";

        /// <summary>
        /// Snap-точка руки <paramref name="side" /> точки <paramref name="point" /> (<c>Grip</c>, <c>Support</c>) в префабе
        /// предмета: <c>&lt;Grabs&gt;/Orig/*_&lt;point&gt;_orig/&lt;side&gt;</c>; нет — null. Нейтральная точка хвата для областей
        /// подсветки (<see cref="WeaponInteractionRecipes" />).
        /// </summary>
        public static Transform FindSnap(Transform root, string point, UxrHandSide side)
        {
            Transform grabs = root.Find("MeshContainer/Grabs") ?? root.Find("Grabs") ?? root;
            Transform orig = grabs.Find(SnapFolder);
            if (orig == null) return null;
            string suffix = $"_{point}{Suffix}";
            foreach (Transform pose in orig)
            {
                if (pose.name.EndsWith(suffix, StringComparison.Ordinal) && pose.Find(side.ToString()) is Transform snap) return snap;
            }

            return null;
        }

        // ── Точки ───────────────────────────────────────────────────────────

        /// <summary>Поза и snap одного места хвата: кадр клипа и руки, снятые с него.</summary>
        private sealed class OrigPoint
        {
            public string Pack, Item, Point, Prefab, BodyPath, Clip, WeaponClip, SourceKind;
            public float Time, FrameRate;
            public UxrHandSide SourceSide;
            public bool MirrorSnaps;                      // хват оружия: вторая рука — зеркало по боковой оси корпуса
            public Func<Transform, string> CheckBody;     // деталь префаба — та же, что в кадре пака; null — без проверки
            public readonly Dictionary<UxrHandSide, HandsPackPoseExtractor.Sample> Samples = new Dictionary<UxrHandSide, HandsPackPoseExtractor.Sample>();

            public string Name => OrigName(Pack, Item, Point);

            public UxrHandDescriptor Descriptor(UxrHandSide side) =>
                Samples.TryGetValue(side, out HandsPackPoseExtractor.Sample own) ? own.Hand : Samples[SourceSide].Hand.Mirrored();

            public IEnumerable<(UxrHandSide Side, bool Mirrored, GripSample Sample)> SnapSides()
            {
                foreach (UxrHandSide side in Sides)
                {
                    if (Samples.TryGetValue(side, out HandsPackPoseExtractor.Sample own)) yield return (side, false, GripSample.FromPack(own, side, SourceKind));
                    else if (MirrorSnaps) yield return (side, true, GripSample.FromPack(Samples[SourceSide], SourceSide, SourceKind));
                }
            }
        }

        /// <summary>Предмет пака: позы хвата оружия (рецепты импортёров) и магазин.</summary>
        private sealed class Item
        {
            public string Pack, Name, WeaponPath;
            public KinemationWeaponRecipe Kin;
            public MagazineSource Magazine;
            public readonly List<HandsPackPoseRecipe> Grips = new List<HandsPackPoseRecipe>();
        }

        /// <summary>Магазин (патрон) предмета: деталь пака и префаб магазина, где её меш — дочерний <c>Mesh</c>.</summary>
        private sealed class MagazineSource
        {
            public string Item, WeaponPath, MagazinePath, Part, Body, Point, Folder;
            public string[] AlternateParts = Array.Empty<string>(); // та же геометрия другим мешем (патрон в руке дробовика)
            public KinemationWeaponRecipe Kin;
        }

        private static List<Item> Items()
        {
            var items = new Dictionary<string, Item>();
            Item Get(string pack, string weapon)
            {
                string key = pack + "|" + weapon;
                if (!items.TryGetValue(key, out Item item))
                    items[key] = item = new Item { Pack = pack, Name = Path.GetFileNameWithoutExtension(weapon), WeaponPath = weapon };
                return item;
            }

            foreach (HandsPackPoseRecipe recipe in HandsPackPoseImporter.Recipes) Get(HandsPack, recipe.Weapon).Grips.Add(recipe);
            foreach (MagazineSource magazine in HandsMagazines()) Get(HandsPack, magazine.WeaponPath).Magazine = magazine;
            foreach (KinemationWeaponRecipe k in KinemationWeaponBuilder.All)
            {
                Item item = Get(Kinemation, KinemationWeaponBuilder.PrefabPath(k));
                item.Kin = k;
                item.Grips.AddRange(KinemationWeaponBuilder.PoseRecipes(k));
                item.Magazine = new MagazineSource
                {
                    Item = item.Name, WeaponPath = item.WeaponPath, MagazinePath = KinemationWeaponBuilder.MagazinePath(k), Part = k.Weapon.MagazinePart,
                    Body = k.Weapon.BodyPart, Point = k.Weapon.MagazineIsInternal ? "Shell" : "Magazine", Kin = k
                };
            }

            return items.Values.OrderBy(i => i.Pack, StringComparer.Ordinal).ThenBy(i => i.Name, StringComparer.Ordinal).ToList();
        }

        private static IEnumerable<MagazineSource> HandsMagazines()
        {
            foreach (HandsPackWeaponRecipe r in HandsPackWeaponBuilder.T38) yield return HandsMagazine(r, null);
            // Набор 3+3 HandsPackLegacyRebuild: те же папка, корпус и магазин, что в его рецептах (они закрыты и читают baseline).
            yield return HandsMagazine(new HandsPackWeaponRecipe
            {
                Name = "BrowningHiPower", PrefabFolder = "BrowningHiPower", MagazineName = "BrowningHiPower_Magazine", Folder = "Hands_Gun",
                BodyPart = "Base_mesh", MagazinePart = "Magazine_mesh"
            }, null);
            yield return HandsMagazine(new HandsPackWeaponRecipe
            {
                Name = "AR15", PrefabFolder = "AR15", MagazineName = "AR15_Magazine", Folder = "Hands_Automatic_Rifle03",
                BodyPart = "Rifle_Body_Mesh", MagazinePart = "Rifle_Magazine_Mesh"
            }, null);
            // В Recharge рука держит дубликат патрона Shogun_Patron_mesh001 (исследование hand-rig-quality, shell-phase).
            yield return HandsMagazine(HandsPackWeaponBuilder.ShotgunReal, new[] { "Shogun_Patron_mesh001" });
        }

        private static MagazineSource HandsMagazine(HandsPackWeaponRecipe r, string[] alternates) =>
            new MagazineSource
            {
                Item = r.Name, WeaponPath = $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.Name}.prefab",
                MagazinePath = $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.MagazineName ?? r.Name + "_mag"}.prefab",
                Part = r.MagazinePart, Body = r.BodyPart, Folder = r.Folder, Point = r.MagazineIsInternal ? "Shell" : "Magazine",
                AlternateParts = alternates ?? Array.Empty<string>()
            };

        // ── Вход ────────────────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        private static void ExportAllFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                GameLog.Debug.Warning("[PackOrigPoses] Экспорт — только в редакторе вне Play Mode и компиляции.");
                return;
            }

            if (!EditorUtility.DisplayDialog("Исходные позы паков (_orig)",
                    $"Позы _orig → {Root}/<Пак>/, snap-точки Orig → префабы оружия и магазинов паков.\n" +
                    "Позы и хваты аватаров не меняются. Неизменённый источник не перезаписывается.", "Экспортировать", "Отмена"))
                return;

            ExportAll();
        }

        /// <summary>Все предметы обоих паков: позы, snap-точки, отчёты источников. Ошибка предмета не останавливает остальные.</summary>
        public static string ExportAll() => Export(Items(), false);

        /// <summary>
        /// Шаг сборки ствола: позы и snap-точки его предмета (оружие и магазин). Сборка только что пересохранила префабы —
        /// сначала канон <c>_assetId</c> и флагов UXR на диске, чтобы сверка YAML видела лишь объекты <c>Orig</c>.
        /// Ошибка — исключение после записи остального.
        /// </summary>
        public static string ExportForWeapon(string weaponPrefab)
        {
            List<Item> items = Items().Where(i => i.WeaponPath == weaponPrefab).ToList();
            if (items.Count == 0) return $"[PackOrigPoses] {weaponPrefab}: рецептов паков нет — _orig не экспортируется.";
            return Export(items, true);
        }

        /// <summary>Область записи массового экспорта: папка поз и префабы оружия и магазинов.</summary>
        public static string[] OutputPaths() =>
            new[] { Root }.Concat(Items().SelectMany(i => new[] { i.Grips.Count > 0 ? i.WeaponPath : null, i.Magazine?.MagazinePath }))
                          .Where(p => p != null).Distinct().ToArray();

        /// <summary>Читаемые источники: папки паков.</summary>
        public static string[] InputPaths() =>
            new[] { HandsPackWeapon.PackModels, KinemationWeapon.Pack + "Animations", KinemationWeapon.Pack + "Prefabs/Weapons", KinemationPoseExtractor.Character };

        private static string Export(List<Item> items, bool canonicalize)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Экспорт _orig запрещён в Play Mode.");
            var log = new List<string>();
            var errors = new List<string>();
            var warnings = new List<string>(); // магазин — эксперимент: его сбой не останавливает сборку оружия
            foreach (Item item in items)
            {
                try
                {
                    ExportItem(item, canonicalize, log, errors, warnings);
                }
                catch (Exception e)
                {
                    errors.Add($"{item.Pack}/{item.Name}: {e.Message}");
                }
            }

            string report = $"[PackOrigPoses] Предметов: {items.Count}, ошибок: {errors.Count}, предупреждений магазина: {warnings.Count}\n" +
                            string.Join("\n", log) + (errors.Count > 0 ? "\nОшибки:\n" + string.Join("\n", errors) : "") +
                            (warnings.Count > 0 ? "\nМагазин (эксперимент):\n" + string.Join("\n", warnings) : "");
            if (errors.Count > 0)
            {
                GameLog.Debug.Error(report);
                if (canonicalize) throw new InvalidOperationException(report);
            }
            else if (warnings.Count > 0)
            {
                GameLog.Debug.Warning(report);
            }
            else
            {
                GameLog.Debug.Info(report);
            }

            return report;
        }

        private static void ExportItem(Item item, bool canonicalize, List<string> log, List<string> errors, List<string> warnings)
        {
            var report = new SourceReport { pack = item.Pack, item = item.Name, weapon = item.WeaponPath };
            var points = new List<OrigPoint>();
            foreach (HandsPackPoseRecipe recipe in item.Grips) points.Add(GripPoint(item, recipe));

            if (item.Magazine != null)
            {
                // Эксперимент: его сбой не мешает позам хвата оружия.
                try
                {
                    OrigPoint magazine = MagazinePoint(item, report);
                    if (magazine != null) points.Add(magazine);
                    else log.Add($"  {item.Pack}/{item.Name}: магазин — {report.magazineNote}");
                }
                catch (Exception e)
                {
                    report.magazineNote = "ошибка: " + e.Message;
                    warnings.Add($"{item.Pack}/{item.Name}: магазин — {e.Message}");
                }
            }

            foreach (OrigPoint point in points)
            {
                string status = WritePose(point);
                log.Add($"  {PoseAssetPath(point.Pack, point.Name)}: {status}");
            }

            var snapProblems = new Dictionary<string, string>();
            foreach (IGrouping<string, OrigPoint> prefab in points.GroupBy(p => p.Prefab))
            {
                try
                {
                    string status = WriteSnaps(prefab.Key, prefab.ToList(), canonicalize, snapProblems);
                    log.Add($"  {prefab.Key}: {status}");
                }
                catch (Exception e)
                {
                    (prefab.All(p => !p.MirrorSnaps) ? warnings : errors).Add($"{prefab.Key}: snap-точки не записаны — {e.Message}");
                }
            }

            foreach (OrigPoint point in points)
            {
                report.points.Add(new PointReport
                {
                    name = point.Name, point = point.Point, asset = PoseAssetPath(point.Pack, point.Name), prefab = point.Prefab, body = point.BodyPath,
                    clip = point.Clip, weaponClip = point.WeaponClip, sourceKind = point.SourceKind, sourceSide = point.SourceSide.ToString(),
                    time = Round(point.Time), frame = Round(point.Time * point.FrameRate),
                    heldBy = Sides.Where(point.Samples.ContainsKey).Select(s => s.ToString()).ToArray(),
                    snaps = point.SnapSides().Select(s => $"{SnapFolder}/{point.Name}/{s.Side}{(s.Mirrored ? " (зеркало)" : "")}").ToArray(),
                    check = point.Point == "Grip" || point.Point == "Support" ? null : "Эксперимент: кадр выбран автоматически — проверить глазами."
                });
                if (snapProblems.TryGetValue(point.Name, out string problem)) (point.MirrorSnaps ? errors : warnings).Add($"{point.Prefab}/{point.Name}: {problem}");
            }

            log.Add($"  {WriteReport(item, report)}");
        }

        private static OrigPoint GripPoint(Item item, HandsPackPoseRecipe recipe)
        {
            var point = new OrigPoint
            {
                Pack = item.Pack, Item = item.Name, Point = recipe.Pose.Substring(recipe.Pose.LastIndexOf('_') + 1), Prefab = recipe.Weapon,
                BodyPath = recipe.BodyPath, SourceSide = recipe.PackSide, MirrorSnaps = true
            };
            if (item.Kin == null)
            {
                point.SourceKind = PackClipGripSource.Hands;
                point.Clip = recipe.Clip;
                point.Time = recipe.Time;
                point.Samples[recipe.PackSide] = HandsPackPoseImporter.ExtractSample(recipe);
            }
            else
            {
                // Те же входы, что KinemationWeapon.GripSample, без модели ствола: она пишет меши деталей и Read/Write источников.
                KinemationWeaponRecipe k = item.Kin;
                AnimationClip clip = CharacterPoseClip(k);
                point.SourceKind = PackClipGripSource.Kinemation;
                point.Clip = clip.name;
                point.FrameRate = clip.frameRate;
                point.Samples[recipe.PackSide] = KinemationPoseExtractor.Extract(clip, 0f, recipe.PackSide, PackPrefab(k), k.Weapon.BodyPart,
                                                                                 k.RestClip != null ? WeaponClip(k, k.RestClip) : null);
            }

            return point;
        }

        // ── Поза ────────────────────────────────────────────────────────────

        private static string WritePose(OrigPoint point)
        {
            string folder = $"{Root}/{point.Pack}";
            KinemationWeapon.EnsureFolder(folder);
            string path = PoseAssetPath(point.Pack, point.Name);
            UxrHandDescriptor left = point.Descriptor(UxrHandSide.Left);
            UxrHandDescriptor right = point.Descriptor(UxrHandSide.Right);

            var existing = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(path);
            if (existing != null && existing.PoseType == UxrHandPoseType.Fixed && existing.Version == UxrHandPoseAsset.CurrentVersion &&
                Same(existing.HandDescriptorLeft, left) && Same(existing.HandDescriptorRight, right))
                return "без изменений";

            var asset = ScriptableObject.CreateInstance<UxrHandPoseAsset>();
            asset.Version = UxrHandPoseAsset.CurrentVersion;
            asset.PoseType = UxrHandPoseType.Fixed;
            asset.HandDescriptorLeft = left;
            asset.HandDescriptorRight = right;
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return "создана";
            }

            // Источник изменился: перезапись на месте, GUID прежний.
            EditorUtility.CopySerialized(asset, existing);
            existing.name = point.Name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(asset);
            AssetDatabase.SaveAssetIfDirty(existing);
            return "обновлена: дескриптор источника изменился";
        }

        private static bool Same(UxrHandDescriptor a, UxrHandDescriptor b) => a != null && b != null && a.Equals(b);

        // ── Snap-точки ──────────────────────────────────────────────────────

        /// <summary>
        /// Snap-точки точек одного префаба. Экземпляр в превью-сцене, новые объекты — <see cref="PrefabUtility.ApplyAddedGameObject" />,
        /// сдвинутые — <see cref="PrefabUtility.ApplyObjectOverride" /> только их трансформа. Префаб целиком не пересохраняется:
        /// <c>_uxrUniqueId</c>, флаги и <c>_assetId</c> остальных компонентов на диске не трогаются. После записи YAML
        /// сверяется с прежним (<see cref="YamlProblems" />); лишнее изменение — откат файла и ошибка.
        /// </summary>
        private static string WriteSnaps(string prefabPath, List<OrigPoint> points, bool canonicalize, Dictionary<string, string> problems)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) throw new FileNotFoundException($"Нет префаба {prefabPath}");
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == prefabPath)
                throw new InvalidOperationException("префаб открыт в Prefab Mode — закройте его и повторите");

            if (canonicalize)
            {
                NetworkAssetIdNormalizer.Normalize(new[] { prefabPath }, false);
                UxrUniqueIdPersister.Normalize(prefabPath);
            }

            byte[] original = File.ReadAllBytes(prefabPath);
            Scene scene = EditorSceneManager.NewPreviewScene();
            bool applied = false;
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
                Transform grabs = root.transform.Find("MeshContainer/Grabs") ?? root.transform.Find("Grabs") ?? root.transform;
                Transform orig = grabs.Find(SnapFolder);
                var created = new HashSet<GameObject>();
                var changed = new List<Transform>();
                int same = 0;

                foreach (OrigPoint point in points)
                {
                    Transform body = root.transform.Find(point.BodyPath);
                    string bodyProblem = body == null ? $"нет детали {point.BodyPath}" : point.CheckBody?.Invoke(body);
                    if (bodyProblem != null)
                    {
                        problems[point.Name] = bodyProblem;
                        continue;
                    }

                    foreach (var (side, _, sample) in point.SnapSides())
                    {
                        sample.PalmInBody(side, body.lossyScale.x / sample.BodyScale, out Vector3 position, out Quaternion rotation);
                        Vector3 worldPosition = body.position + body.rotation * position;
                        Quaternion worldRotation = body.rotation * rotation;

                        orig = orig != null ? orig : Child(grabs, SnapFolder, created);
                        Transform node = Child(Child(orig, point.Name, created), side.ToString(), created);
                        if (created.Contains(node.gameObject))
                        {
                            node.SetPositionAndRotation(worldPosition, worldRotation);
                        }
                        else if (!SamePose(root.transform, node, worldPosition, worldRotation))
                        {
                            node.SetPositionAndRotation(worldPosition, worldRotation);
                            changed.Add(node);
                        }
                        else
                        {
                            same++;
                        }
                    }
                }

                // Корни добавленного: объекты, чей родитель уже был в префабе.
                List<GameObject> added = created.Where(g => !created.Contains(g.transform.parent.gameObject)).ToList();
                if (added.Count == 0 && changed.Count == 0) return $"snap-точки без изменений ({same})";

                applied = true;
                foreach (GameObject go in added) PrefabUtility.ApplyAddedGameObject(go, prefabPath, InteractionMode.AutomatedAction);
                foreach (Transform node in changed) PrefabUtility.ApplyObjectOverride(node, prefabPath, InteractionMode.AutomatedAction);
                AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));

                var free = new HashSet<long>();
                foreach (Transform t in orig.GetComponentsInChildren<Transform>(true))
                {
                    AddFileId(free, t, prefabPath);
                    AddFileId(free, t.gameObject, prefabPath);
                }

                var additive = new HashSet<long>();
                AddFileId(additive, grabs, prefabPath);

                string written = File.ReadAllText(prefabPath);
                if (written == Encoding.UTF8.GetString(original)) throw new InvalidOperationException("применение не дошло до файла префаба");
                string problem = YamlProblems(Encoding.UTF8.GetString(original), written, free, additive);
                if (problem != null) throw new InvalidOperationException("в YAML не только объекты Orig — файл возвращён: " + problem);

                return $"snap-точки: новых объектов {created.Count}, сдвинуто {changed.Count}, без изменений {same}";
            }
            catch
            {
                if (applied)
                {
                    File.WriteAllBytes(prefabPath, original);
                    AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                }

                throw;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Transform Child(Transform parent, string name, HashSet<GameObject> created)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            created.Add(go);
            return go.transform;
        }

        private static bool SamePose(Transform root, Transform node, Vector3 worldPosition, Quaternion worldRotation)
        {
            GripLocalPose current = GripLocalPose.Of(root, node);
            Vector3 position = root.InverseTransformPoint(worldPosition);
            Quaternion rotation = Quaternion.Inverse(root.rotation) * worldRotation;
            return Vector3.Distance(current.Position, position) <= 1e-5f && Quaternion.Angle(current.Rotation, rotation) <= 1e-3f;
        }

        private static void AddFileId(HashSet<long> ids, Object instanceObject, string prefabPath)
        {
            Object source = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(instanceObject, prefabPath);
            if (source != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long id)) ids.Add(id);
        }

        private static readonly Regex DocHeader = new Regex(@"^--- !u!(\d+) &(-?\d+)");

        /// <summary>
        /// Разница YAML префаба допустима, только если она — объекты <c>Orig</c>: новые или изменённые документы GameObject и
        /// Transform из <paramref name="free" /> и дописанные строки (<c>m_Children</c>) в документах <paramref name="additive" />.
        /// Иначе — описание лишнего.
        /// </summary>
        private static string YamlProblems(string before, string after, HashSet<long> free, HashSet<long> additive)
        {
            Dictionary<long, (int Class, string[] Lines)> was = Documents(before, out string wasHeader);
            Dictionary<long, (int Class, string[] Lines)> now = Documents(after, out string nowHeader);
            var problems = new List<string>();
            if (wasHeader != nowHeader) problems.Add("заголовок YAML");
            foreach (KeyValuePair<long, (int Class, string[] Lines)> doc in was)
            {
                if (!now.TryGetValue(doc.Key, out (int Class, string[] Lines) current))
                {
                    problems.Add($"удалён !u!{doc.Value.Class} &{doc.Key}");
                    continue;
                }

                if (current.Lines.SequenceEqual(doc.Value.Lines)) continue;
                if (free.Contains(doc.Key) && (current.Class == 1 || current.Class == 4)) continue;
                if (additive.Contains(doc.Key) && IsSubsequence(doc.Value.Lines, current.Lines)) continue;
                problems.Add($"изменён !u!{doc.Value.Class} &{doc.Key}");
            }

            foreach (KeyValuePair<long, (int Class, string[] Lines)> doc in now)
            {
                if (!was.ContainsKey(doc.Key) && !(free.Contains(doc.Key) && (doc.Value.Class == 1 || doc.Value.Class == 4)))
                    problems.Add($"новый !u!{doc.Value.Class} &{doc.Key} вне Orig");
            }

            return problems.Count == 0 ? null : string.Join("; ", problems.Take(8)) + (problems.Count > 8 ? $" … ещё {problems.Count - 8}" : "");
        }

        private static Dictionary<long, (int Class, string[] Lines)> Documents(string text, out string header)
        {
            var documents = new Dictionary<long, (int, string[])>();
            var preamble = new List<string>();
            List<string> current = null;
            long id = 0;
            int type = 0;
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                Match match = DocHeader.Match(line);
                if (match.Success)
                {
                    if (current != null) documents[id] = (type, current.ToArray());
                    type = int.Parse(match.Groups[1].Value);
                    id = long.Parse(match.Groups[2].Value);
                    current = new List<string> { line };
                }
                else if (current != null) current.Add(line);
                else preamble.Add(line);
            }

            if (current != null) documents[id] = (type, current.ToArray());
            header = string.Join("\n", preamble);
            return documents;
        }

        private static bool IsSubsequence(string[] part, string[] whole)
        {
            int i = 0;
            foreach (string line in whole)
            {
                if (i < part.Length && part[i] == line) i++;
            }

            return i == part.Length;
        }

        // ── Магазин ─────────────────────────────────────────────────────────

        private static OrigPoint MagazinePoint(Item item, SourceReport report)
        {
            MagazineSource m = item.Magazine;
            Window best = null;
            List<Window> bestClip = null;
            foreach (string part in new[] { m.Part }.Concat(m.AlternateParts))
            {
                foreach ((AnimationClip clip, AnimationClip weaponClip) in ReloadClips(m))
                {
                    List<Window>[] windows;
                    float rate = clip.frameRate > 0f ? clip.frameRate : 30f;
                    try
                    {
                        using ClipRig rig = m.Kin != null ? (ClipRig)new KinemationRig(m.Kin, clip, weaponClip, part) : new HandsRig(m, clip, part);
                        List<Frame> frames = ScanFrames(rig, clip.length, rate);
                        windows = Sides.Select(s => Windows(frames, s, rig.Points.Length > 0 ? NearMesh : NearOrigin)).ToArray();
                        for (int s = 0; s < Sides.Length; s++)
                        {
                            Window top = windows[s].OrderByDescending(w => w.Frames).ThenBy(w => w.RigidMm).FirstOrDefault();
                            report.magazineScan.Add(top != null
                                ? top.Report(clip, weaponClip, part, rig.Points.Length > 0)
                                : new ScanReport
                                {
                                    clip = clip.name, weaponClip = weaponClip?.name, part = part, side = Sides[s].ToString(), distance = rig.Points.Length > 0 ? "меш" : "начало детали",
                                    result = "нет окна", minPalmMm = Round(frames.Where(f => f.Seat >= MinSeat).Select(f => f.Palm[s] * 1000f).DefaultIfEmpty(-1f).Min()),
                                    seatMm = Round(frames.Max(f => f.Seat) * 1000f)
                                });
                            foreach (Window w in windows[s])
                            {
                                w.Clip = clip;
                                w.WeaponClip = weaponClip;
                                w.Part = part;
                                w.Rate = rate;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        report.magazineScan.Add(new ScanReport { clip = clip.name, weaponClip = weaponClip?.name, part = part, result = "ошибка: " + e.Message });
                        continue;
                    }

                    Window candidate = windows.SelectMany(w => w).OrderByDescending(w => w.EndTime - w.StartTime).ThenBy(w => w.RigidMm).FirstOrDefault();
                    if (candidate != null && (best == null || candidate.EndTime - candidate.StartTime > best.EndTime - best.StartTime + 1e-4f ||
                                              Mathf.Abs(candidate.EndTime - candidate.StartTime - (best.EndTime - best.StartTime)) <= 1e-4f && candidate.RigidMm < best.RigidMm))
                    {
                        best = candidate;
                        bestClip = windows.SelectMany(w => w).ToList();
                    }
                }
            }

            if (best == null)
            {
                report.magazineNote = report.magazineScan.Count == 0
                    ? "в паке нет клипов перезарядки для этой детали"
                    : "ни в одном клипе перезарядки рука не держит деталь жёстко (см. magazineScan)";
                return null;
            }

            // Вторая рука — только если в том же клипе и кадре тоже держит деталь.
            UxrHandSide[] holding = Sides.Where(s => s == best.Side || bestClip.Any(w => w.Side == s && w.Start <= best.Mid && best.Mid <= w.End)).ToArray();
            var point = new OrigPoint
            {
                Pack = item.Pack, Item = item.Name, Point = m.Point, Prefab = m.MagazinePath, BodyPath = "Mesh", Clip = best.Clip.name,
                WeaponClip = best.WeaponClip?.name, Time = best.MidTime, FrameRate = best.Rate, SourceSide = best.Side, MirrorSnaps = false,
                SourceKind = m.Kin != null ? PackClipGripSource.Kinemation : PackClipGripSource.Hands
            };
            foreach (UxrHandSide side in holding) point.Samples[side] = MagazineSample(m, best, side);
            point.CheckBody = MagazineBodyCheck(m);
            report.magazineNote = $"выбран {best.Clip.name}{(best.WeaponClip != null ? " + " + best.WeaponClip.name : "")}, деталь {best.Part}, " +
                                  $"кадры {best.Start}–{best.End} ({best.StartTime:F3}–{best.EndTime:F3} с), середина {best.MidTime:F3} с; " +
                                  $"держит: {string.Join(", ", holding)}. Эксперимент — проверить глазами.";
            return point;
        }

        /// <summary>Кадр руки с деталью теми же экстракторами, что хват оружия: деталь — «корпус» кадра.</summary>
        private static HandsPackPoseExtractor.Sample MagazineSample(MagazineSource m, Window w, UxrHandSide side)
        {
            if (m.Kin != null)
            {
                return KinemationPoseExtractor.Extract(w.Clip, w.MidTime, side, PackPrefab(m.Kin), w.Part,
                                                       m.Kin.RestClip != null ? WeaponClip(m.Kin, m.Kin.RestClip) : null, null, w.WeaponClip);
            }

            GameObject model = HandsModel(m);
            SkinnedMeshRenderer part = SkinOf(model, w.Part);
            HandsPackPoseExtractor.Sample sample = HandsPackPoseExtractor.Extract(model, w.Clip, w.MidTime, side, part.sharedMesh.name,
                                                                                  part.bones[MainBone(part)].name);
            if (w.Part == m.Part) return sample;

            // Дубликат детали: та же геометрия со сдвигом центра меша d — кадр детали префаба = кадр дубликата · T(d).
            Mesh primary = SkinOf(model, m.Part).sharedMesh;
            Vector3 d = part.sharedMesh.bounds.center - primary.bounds.center;
            if (part.sharedMesh.vertexCount != primary.vertexCount || (part.sharedMesh.bounds.size - primary.bounds.size).magnitude > 1e-4f * primary.bounds.size.magnitude)
                throw new InvalidOperationException($"{w.Part}: геометрия не совпадает с {m.Part} — snap в осях детали префаба не определён");
            sample.BodyPosition += sample.BodyRotation * (d * sample.BodyScale);
            return sample;
        }

        /// <summary>Деталь <c>Mesh</c> префаба магазина — меш той же детали пака (иначе оси snap не те).</summary>
        private static Func<Transform, string> MagazineBodyCheck(MagazineSource m)
        {
            string expected = m.Kin != null ? $"{m.Kin.Weapon.Name}_{KinemationWeapon.Clean(m.Part)}" : SkinOf(HandsModel(m), m.Part).sharedMesh.name;
            return body =>
            {
                Mesh mesh = body.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                return mesh != null && mesh.name == expected ? null : $"деталь Mesh префаба — не меш {expected} ({(mesh != null ? mesh.name : "нет меша")})";
            };
        }

        private static IEnumerable<(AnimationClip Clip, AnimationClip WeaponClip)> ReloadClips(MagazineSource m)
        {
            if (m.Kin == null)
            {
                foreach (AnimationClip clip in Clips(HandsPackWeapon.PackModels + m.Folder + "/Animations")
                             .Where(c => Regex.IsMatch(c.name, "re(a)?load|recharge", RegexOptions.IgnoreCase))
                             .GroupBy(c => c.name).Select(PreferModel).OrderBy(c => c.name, StringComparer.Ordinal))
                    yield return (clip, null);
                yield break;
            }

            // Клип рук A_FP_<ствол>_Reload_* и синхронный клип оружия A_W_<ствол>_Reload_* (Tactical = Tac).
            string folder = $"{KinemationWeapon.Pack}Animations/{m.Kin.AnimFolder}/";
            List<IGrouping<string, AnimationClip>> weapon = Clips(folder + "Weapon").Where(c => c.name.Contains("_Reload")).GroupBy(c => ReloadKey(c.name, "A_W_")).ToList();
            foreach (IGrouping<string, AnimationClip> character in Clips(folder + "Character").Where(c => c.name.Contains("_Reload")).GroupBy(c => c.name)
                                                                                          .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AnimationClip clip = PreferModel(character);
                IGrouping<string, AnimationClip> pair = weapon.FirstOrDefault(w => w.Key == ReloadKey(clip.name, "A_FP_"));
                if (pair != null) yield return (clip, PreferModel(pair));
            }
        }

        private static string ReloadKey(string name, string prefix) => (name.StartsWith(prefix) ? name.Substring(prefix.Length) : name).Replace("Tactical", "Tac");

        // ── Поиск кадра ─────────────────────────────────────────────────────

        private sealed class Frame
        {
            public float Time, Seat;
            public readonly Vector3[] RelativePosition = new Vector3[2];
            public readonly Quaternion[] RelativeRotation = new Quaternion[2];
            public readonly float[] Palm = new float[2];
        }

        private sealed class Window
        {
            public UxrHandSide Side;
            public int Start, End, Mid;
            public float StartTime, EndTime, MidTime, Rate, RigidMm, RigidDeg, PalmMm, SeatMm;
            public AnimationClip Clip, WeaponClip;
            public string Part;
            public int Frames => End - Start + 1;

            public ScanReport Report(AnimationClip clip, AnimationClip weaponClip, string part, bool mesh) =>
                new ScanReport
                {
                    clip = clip.name, weaponClip = weaponClip?.name, part = part, side = Side.ToString(), result = "окно", distance = mesh ? "меш" : "начало детали",
                    frames = Frames, startFrame = Start, endFrame = End, startTime = Round(StartTime), endTime = Round(EndTime), midTime = Round(MidTime),
                    rigidMm = Round(RigidMm), rigidDeg = Round(RigidDeg), palmMm = Round(PalmMm), seatMm = Round(SeatMm)
                };
        }

        /// <summary>
        /// Кадры клипа с шагом кадра: деталь в рамке ладони каждой руки (центр ладони SDK, универсальные оси кисти), расстояние
        /// от центра ладони до детали и сдвиг детали от её места на корпусе в первом кадре.
        /// </summary>
        private static List<Frame> ScanFrames(ClipRig rig, float length, float rate)
        {
            int count = Mathf.FloorToInt(length * rate + 1e-3f) + 1;
            var frames = new List<Frame>(count);
            Vector3 seat0 = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                float time = Mathf.Min(i / rate, length);
                rig.Sample(time);
                Matrix4x4 part = rig.Part();
                Matrix4x4 rigid = Rigid(part);
                Vector3 seat = (Rigid(rig.Body()).inverse * rigid).GetColumn(3);
                if (i == 0) seat0 = seat;
                var frame = new Frame { Time = time, Seat = Vector3.Distance(seat, seat0) };
                for (int s = 0; s < 2; s++)
                {
                    Vector3 palm = HandsPackPoseExtractor.PalmCenterWorld(rig.Arms[s].Hand);
                    Quaternion hand = rig.HandAxes[s].UniversalRotation;
                    frame.RelativePosition[s] = Quaternion.Inverse(hand) * ((Vector3)rigid.GetColumn(3) - palm);
                    frame.RelativeRotation[s] = Quaternion.Inverse(hand) * rigid.rotation;
                    frame.Palm[s] = rig.Points.Length == 0
                        ? Vector3.Distance(palm, rigid.GetColumn(3))
                        : rig.Points.Min(p => Vector3.Distance(palm, part.MultiplyPoint3x4(p)));
                }

                frames.Add(frame);
            }

            return frames;
        }

        /// <summary>Окна руки: подряд кадры, где деталь вынута, близка к ладони и не сдвигается в рамке ладони.</summary>
        private static List<Window> Windows(List<Frame> frames, UxrHandSide side, float near)
        {
            int s = side == UxrHandSide.Left ? 0 : 1;
            bool Holding(Frame f) => f.Palm[s] <= near && f.Seat >= MinSeat;
            bool Steady(Frame a, Frame b) => Vector3.Distance(a.RelativePosition[s], b.RelativePosition[s]) <= RigidMeters &&
                                            Quaternion.Angle(a.RelativeRotation[s], b.RelativeRotation[s]) <= RigidDegrees;

            var windows = new List<Window>();
            int i = 0;
            while (i < frames.Count)
            {
                if (!Holding(frames[i]))
                {
                    i++;
                    continue;
                }

                int end = i;
                while (end + 1 < frames.Count && Holding(frames[end + 1]) && Steady(frames[i], frames[end + 1])) end++;
                if (end - i + 1 >= MinFrames)
                {
                    int mid = (i + end) / 2;
                    Frame m = frames[mid];
                    var window = new Window
                    {
                        Side = side, Start = i, End = end, Mid = mid, StartTime = frames[i].Time, EndTime = frames[end].Time, MidTime = m.Time,
                        PalmMm = m.Palm[s] * 1000f, SeatMm = m.Seat * 1000f
                    };
                    for (int k = i; k <= end; k++)
                    {
                        window.RigidMm = Mathf.Max(window.RigidMm, Vector3.Distance(frames[k].RelativePosition[s], m.RelativePosition[s]) * 1000f);
                        window.RigidDeg = Mathf.Max(window.RigidDeg, Quaternion.Angle(frames[k].RelativeRotation[s], m.RelativeRotation[s]));
                    }

                    windows.Add(window);
                }

                i = end + 1;
            }

            return windows;
        }

        /// <summary>Временный риг клипа: руки с осями по позе модели до клипа (как в экстракторах), деталь и корпус.</summary>
        private abstract class ClipRig : IDisposable
        {
            public readonly UxrAvatarArm[] Arms = new UxrAvatarArm[2];
            public readonly UxrUniversalLocalAxes[] HandAxes = new UxrUniversalLocalAxes[2];
            public Vector3[] Points = Array.Empty<Vector3>(); // точки меша детали в осях Part(); пусто — меш недоступен на CPU

            public abstract void Sample(float time);
            public abstract Matrix4x4 Part();
            public abstract Matrix4x4 Body();
            public abstract void Dispose();

            protected void SetupArms(Func<UxrHandSide, UxrAvatarArm> build)
            {
                for (int s = 0; s < 2; s++)
                {
                    Arms[s] = build(Sides[s]);
                    HandsPackPoseExtractor.SolveAxes(Arms[s].Hand, Sides[s], out HandAxes[s], out UxrUniversalLocalAxes _);
                }
            }
        }

        /// <summary>Пак Hands: руки и оружие — один FBX, деталь — свой скин-рендерер, место — кость × bindpose (как корпус в экстракторе).</summary>
        private sealed class HandsRig : ClipRig
        {
            private readonly GameObject _instance;
            private readonly AnimationClip _clip;
            private readonly Transform _part, _body;
            private readonly Matrix4x4 _partBind, _bodyBind;

            public HandsRig(MagazineSource m, AnimationClip clip, string part)
            {
                _clip = clip;
                _instance = Object.Instantiate(HandsModel(m));
                _instance.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    Dictionary<string, Transform> bones = Bones(_instance);
                    SetupArms(s => HandsPackPoseExtractor.BuildArm(bones, s));
                    SkinnedMeshRenderer partRenderer = SkinOf(_instance, part);
                    SkinnedMeshRenderer bodyRenderer = SkinOf(_instance, m.Body);
                    int partBone = MainBone(partRenderer), bodyBone = MainBone(bodyRenderer);
                    (_part, _partBind) = (partRenderer.bones[partBone], partRenderer.sharedMesh.bindposes[partBone]);
                    (_body, _bodyBind) = (bodyRenderer.bones[bodyBone], bodyRenderer.sharedMesh.bindposes[bodyBone]);
                    Points = MeshPoints(partRenderer.sharedMesh, -1, Matrix4x4.identity);
                }
                catch
                {
                    Object.DestroyImmediate(_instance);
                    throw;
                }
            }

            public override void Sample(float time) => _clip.SampleAnimation(_instance, time);
            public override Matrix4x4 Part() => _part.localToWorldMatrix * _partBind;
            public override Matrix4x4 Body() => _body.localToWorldMatrix * _bodyBind;
            public override void Dispose() => Object.DestroyImmediate(_instance);
        }

        /// <summary>
        /// KINEMATION: персонаж и оружие пака на <see cref="KinemationPoseExtractor.WeaponBone" /> — как в экстракторе, клип рук
        /// и синхронный клип оружия в одном времени поверх покоя. Деталь — кость оружия без масштаба.
        /// </summary>
        private sealed class KinemationRig : ClipRig
        {
            private readonly GameObject _character, _weapon;
            private readonly AnimationClip _clip, _weaponClip;
            private readonly Animator _animator;
            private readonly Transform _part, _body;

            public KinemationRig(KinemationWeaponRecipe k, AnimationClip clip, AnimationClip weaponClip, string part)
            {
                _clip = clip;
                _weaponClip = weaponClip;
                GameObject prefab = PackPrefab(k);
                _character = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KinemationPoseExtractor.Character));
                _character.hideFlags = HideFlags.HideAndDontSave;
                _weapon = Object.Instantiate(prefab);
                _weapon.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    Dictionary<string, Transform> bones = Bones(_character);
                    SetupArms(s => KinemationPoseExtractor.BuildArm(bones, s));
                    if (!bones.TryGetValue(KinemationPoseExtractor.WeaponBone, out Transform gun))
                        throw new KeyNotFoundException($"Нет кости {KinemationPoseExtractor.WeaponBone}");
                    _weapon.transform.SetParent(gun, false);
                    _weapon.transform.localPosition = Vector3.zero;
                    _weapon.transform.localRotation = KinemationPoseExtractor.WeaponRotationOffset(prefab);
                    _weapon.transform.localScale = Vector3.one;
                    _animator = _weapon.GetComponentInChildren<Animator>(true) ?? throw new KeyNotFoundException($"{prefab.name}: нет аниматора оружия");
                    if (k.RestClip != null) WeaponClip(k, k.RestClip).SampleAnimation(_animator.gameObject, 0f);
                    KinemationWeapon.SampleFullMagazines(_weapon, _animator);
                    _part = Find(_weapon, part);
                    _body = Find(_weapon, k.Weapon.BodyPart);
                    Points = BonePoints(_weapon, _part);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public override void Sample(float time)
            {
                _clip.SampleAnimation(_character, time);
                _weaponClip.SampleAnimation(_animator.gameObject, Mathf.Min(time, _weaponClip.length));
            }

            public override Matrix4x4 Part() => Rigid(_part.localToWorldMatrix);
            public override Matrix4x4 Body() => Rigid(_body.localToWorldMatrix);

            public override void Dispose()
            {
                if (_weapon != null) Object.DestroyImmediate(_weapon);
                if (_character != null) Object.DestroyImmediate(_character);
            }

            private static Transform Find(GameObject root, string name) =>
                root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) ?? throw new KeyNotFoundException($"{root.name}: нет кости {name}");

            /// <summary>Вершины скина, у которых кость детали главная, в её осях без масштаба (как <c>KinemationWeapon.MeshOf</c>).</summary>
            private static Vector3[] BonePoints(GameObject weapon, Transform bone)
            {
                foreach (SkinnedMeshRenderer r in weapon.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    int index = Array.IndexOf(r.bones, bone);
                    if (index < 0 || r.sharedMesh == null) continue;
                    Vector3[] points = MeshPoints(r.sharedMesh, index, Rigid(bone.localToWorldMatrix).inverse * bone.localToWorldMatrix * r.sharedMesh.bindposes[index]);
                    if (points.Length > 0) return points;
                }

                return Array.Empty<Vector3>();
            }
        }

        private static Vector3[] MeshPoints(Mesh mesh, int boneIndex, Matrix4x4 toPart)
        {
            if (mesh == null || !mesh.isReadable) return Array.Empty<Vector3>();
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0 || vertices.Length != mesh.vertexCount) return Array.Empty<Vector3>();
            BoneWeight[] weights = boneIndex >= 0 ? mesh.boneWeights : null;
            var points = new List<Vector3>();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (weights == null || weights[i].boneIndex0 == boneIndex) points.Add(toPart.MultiplyPoint3x4(vertices[i]));
            }

            int step = Mathf.Max(1, points.Count / MaxPoints);
            return points.Where((p, i) => i % step == 0).ToArray();
        }

        // ── Источники ───────────────────────────────────────────────────────

        private static Dictionary<string, Transform> Bones(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

        /// <summary>Модель рук с оружием пака Hands — как у <see cref="HandsPackWeapon" />: модель папки без '@'.</summary>
        private static GameObject HandsModel(MagazineSource m)
        {
            string folder = HandsPackWeapon.PackModels + m.Folder;
            string path = AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => !p.Contains("@"));
            return path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : throw new FileNotFoundException($"В {folder} нет модели без '@'");
        }

        private static SkinnedMeshRenderer SkinOf(GameObject root, string name) =>
            root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == name && r.sharedMesh != null)
            ?? throw new KeyNotFoundException($"{root.name}: нет детали {name}");

        private static int MainBone(SkinnedMeshRenderer r)
        {
            var count = new int[r.bones.Length];
            foreach (BoneWeight w in r.sharedMesh.boneWeights) count[w.boneIndex0]++;
            int best = 0;
            for (int i = 1; i < count.Length; i++)
                if (count[i] > count[best]) best = i;
            return best;
        }

        private static GameObject PackPrefab(KinemationWeaponRecipe k) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{KinemationWeapon.Pack}Prefabs/Weapons/{k.PackPrefab}.prefab")
            ?? throw new FileNotFoundException($"Нет префаба пака {k.PackPrefab}");

        private static IEnumerable<AnimationClip> Clips(string folder) =>
            AssetDatabase.FindAssets("t:AnimationClip", new[] { folder })
                         .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                         .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).Distinct();

        /// <summary>Как <c>KinemationWeapon.PoseClip</c>.</summary>
        private static AnimationClip CharacterPoseClip(KinemationWeaponRecipe k) =>
            Clips($"{KinemationWeapon.Pack}Animations/{k.AnimFolder}/Character").FirstOrDefault(c => c.name == k.PoseClip)
            ?? throw new ArgumentException($"В {k.AnimFolder}/Character нет клипа '{k.PoseClip}'");

        /// <summary>Клип оружия по имени; одноимённые — тот, на который ссылается аниматор префаба пака (как <c>KinemationWeapon.Clip</c>), иначе take FBX.</summary>
        private static AnimationClip WeaponClip(KinemationWeaponRecipe k, string name)
        {
            AnimationClip[] candidates = Clips($"{KinemationWeapon.Pack}Animations/{k.AnimFolder}/Weapon").Where(c => c.name == name).ToArray();
            if (candidates.Length == 1) return candidates[0];
            Animator animator = PackPrefab(k).GetComponentInChildren<Animator>(true);
            AnimationClip[] referenced = animator == null || animator.runtimeAnimatorController == null ? Array.Empty<AnimationClip>()
                : animator.runtimeAnimatorController.animationClips.Where(candidates.Contains).Distinct().ToArray();
            if (referenced.Length == 1) return referenced[0];
            AnimationClip[] models = candidates.Where(IsModelClip).ToArray();
            if (models.Length == 1) return models[0];
            throw new ArgumentException($"{k.AnimFolder}: клип оружия '{name}' не найден или неоднозначен ({candidates.Length})");
        }

        /// <summary>Take из FBX, а не одноимённая заглушка <c>.anim</c>.</summary>
        private static AnimationClip PreferModel(IEnumerable<AnimationClip> clips) => clips.FirstOrDefault(IsModelClip) ?? clips.First();

        private static bool IsModelClip(AnimationClip clip) => AssetDatabase.GetAssetPath(clip).EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        private static Matrix4x4 Rigid(Matrix4x4 m) => Matrix4x4.TRS(m.GetColumn(3), m.rotation, Vector3.one);

        private static float Round(float value) => Mathf.Round(value * 1000f) / 1000f;

        // ── Отчёт источников ────────────────────────────────────────────────

        [Serializable]
        private sealed class SourceReport
        {
            public string pack, item, weapon, magazineNote;
            public List<PointReport> points = new List<PointReport>();
            public List<ScanReport> magazineScan = new List<ScanReport>();
        }

        [Serializable]
        private sealed class PointReport
        {
            public string name, point, asset, prefab, body, clip, weaponClip, sourceKind, sourceSide, check;
            public float time, frame;
            public string[] heldBy, snaps;
        }

        [Serializable]
        private sealed class ScanReport
        {
            public string clip, weaponClip, part, side, result, distance;
            public int frames, startFrame, endFrame;
            public float startTime, endTime, midTime, rigidMm, rigidDeg, palmMm, seatMm, minPalmMm;
        }

        /// <summary>Отчёт без времени и статусов записи: меняется только вместе с источником.</summary>
        private static string WriteReport(Item item, SourceReport report)
        {
            string path = $"{Root}/{item.Pack}/{item.Pack}_{item.Name}{Suffix}_sources.json";
            KinemationWeapon.EnsureFolder($"{Root}/{item.Pack}");
            string json = JsonUtility.ToJson(report, true).Replace("\r\n", "\n") + "\n";
            if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == json) return $"{path}: без изменений";
            File.WriteAllText(path, json, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            return $"{path}: записан";
        }
    }
}
