using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Avatars;
using VrBattlegrounds.EditorTools.VersionControl;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Gameplay
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
    /// <c>Grabs</c> корня, иначе корень; уже существующий <c>Orig</c> — там, где он есть, <see cref="GrabsOf" />): рамка ладони руки пака в кадре клипа (<see cref="GripSample.PalmInBody" />) —
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
    /// <b>Деталь механики (эксперимент, решение 2026-10-23).</b> Все стволы с затвором или помпой (<c>Action != None</c>
    /// рецептов Hands и KINEMATION). Вид точки — имя граббабла детали: <c>Slide</c> или <c>Pump</c>. Поза
    /// <c>&lt;Пак&gt;_&lt;Предмет&gt;_&lt;Slide|Pump&gt;_orig</c>, snap — <c>&lt;оружие&gt;/&lt;Slide|Pump&gt;/Orig/&lt;поза&gt;/Left|Right</c>
    /// внутри граббабла, в осях детали <c>&lt;Slide|Pump&gt;/&lt;деталь&gt;</c>; вторая рука — зеркало по боковой оси корпуса
    /// <c>MeshContainer/&lt;корпус&gt;</c> (то же правило, что у хвата оружия). Затвор: кадр, где рука ведёт деталь, — середина
    /// самого длинного окна по всем клипам пака, где деталь сдвинута от покоя не меньше доли хода, ладонь у меша, рука
    /// жёстко идёт с деталью и деталь внутри окна движется (<see cref="SlidePoint" />). Помпа: кадр позы оружия t=0, левая
    /// рука; клип хода — только проверка жёсткости (<see cref="PumpPoint" />). Сбой — предупреждение, не отказ сборки.
    /// Отчёт поиска — <c>&lt;поза&gt;_sources.json</c> рядом с позой.
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

        // Поиск кадра с магазином в руке: единицы — мир клипа (у пака Hands руки крупнее реальных ~×1.4).
        private const float RigidMeters = 0.005f;   // сдвиг «ладонь → магазин» внутри окна от его первого кадра
        private const float RigidDegrees = 5f;
        private const float NearMesh = 0.06f;      // центр ладони — ближайшая точка меша магазина
        private const float NearOrigin = 0.10f;    // меш недоступен на CPU — начало детали
        private const float MinSeat = 0.02f;       // магазин вынут: сдвиг от места в первом кадре клипа
        private const int MinFrames = 3;
        private const int MaxPoints = 400;

        // Деталь механики: доли наибольшего сдвига детали от покоя по всем клипам пака (ход).
        private const float ActionSeatShare = 0.30f;  // деталь сдвинута: не меньше этой доли хода
        private const float ActionRangeShare = 0.20f; // деталь движется внутри окна (иначе — затворная задержка, рука рядом)
        private const float MinActionTravel = 0.005f; // меньше — деталь в клипах пака не ходит

        /// <summary>
        /// Помпа: ладонь дальше <see cref="NearMesh" />, но не дальше этого, — кадр годен, если рука жёстко идёт с помпой весь
        /// её ход (<see cref="PumpPointRule" />). Обоснование: единицы — мир клипа пака Hands, где руки ~×1.4 реальных, и
        /// центр ладони SDK лежит внутри кисти, а цевьё толстое — у FabarmSDASS в Aming_Idle 86 мм до ближайшей вершины при
        /// жёсткости 0,2 мм / 0,2° за 131 мм хода в Shot (правая рука на том же ходу смещается на 131 мм). Порог 100 мм — тот же,
        /// что <see cref="NearOrigin" /> (меш недоступен), ≈70 мм реальной руки: дальше кисть уже не обхватывает деталь.
        /// </summary>
        private const float PumpNear = 0.10f;
        private const string PumpPointRule = "помпа: ладонь <= 60 мм меша или <= 100 мм при жёстком ходе руки с помпой";

        private const string SlidePointName = "Slide";
        private const string PumpPointName = "Pump";

        private static readonly UxrHandSide[] Sides = { UxrHandSide.Left, UxrHandSide.Right };

        public static string OrigName(string pack, string item, string point) => $"{pack}_{item}_{point}{Suffix}";

        public static string PoseAssetPath(string pack, string name) => $"{Root}/{pack}/{name}.asset";

        /// <summary>
        /// Snap-точка руки <paramref name="side" /> точки <paramref name="point" /> (<c>Grip</c>, <c>Support</c>) в префабе
        /// предмета: <c>&lt;Grabs&gt;/Orig/*_&lt;point&gt;_orig/&lt;side&gt;</c>; нет — null. Нейтральная точка хвата для областей
        /// подсветки (<see cref="WeaponInteractionRecipes" />). Деталь механики (<c>Slide</c>, <c>Pump</c>): <paramref name="root" /> —
        /// её граббабл, <c>Orig</c> лежит прямо в нём.
        /// </summary>
        public static Transform FindSnap(Transform root, string point, UxrHandSide side)
        {
            Transform grabs = GrabsOf(root);
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
            public bool MirrorSnaps;                      // вторая рука — зеркало по боковой оси корпуса
            public string MirrorFramePath;                // корпус зеркала (деталь механики); null — сама деталь BodyPath (хват оружия)
            public string SnapRoot;                       // узел контейнера Orig (граббабл детали механики); null — GrabsOf корня
            public bool Experimental;                     // кадр выбран автоматически: сбой — предупреждение, не ошибка
            public Func<Transform, string> CheckBody;     // деталь префаба — та же, что в кадре пака; null — без проверки
            public readonly Dictionary<UxrHandSide, HandsPackPoseExtractor.Sample> Samples = new Dictionary<UxrHandSide, HandsPackPoseExtractor.Sample>();

            // Расчётная точка (деталь механики без источника в паке): рамка ладони руки SourceSide в мире префаба по
            // геометрии детали (Problem — почему не посчитана) и поза пальцев шаблона; вторая рука — зеркало по корпусу.
            public Func<Transform, (Vector3 Position, Quaternion Rotation, string Problem)> Computed;
            public UxrHandDescriptor ComputedLeft, ComputedRight;

            public string Name => OrigName(Pack, Item, Point);

            public UxrHandDescriptor Descriptor(UxrHandSide side) =>
                Computed != null ? side == UxrHandSide.Left ? ComputedLeft : ComputedRight
                : Samples.TryGetValue(side, out HandsPackPoseExtractor.Sample own) ? own.Hand : Samples[SourceSide].Hand.Mirrored();

            public IEnumerable<(UxrHandSide Side, bool Mirrored, GripSample Sample)> SnapSides()
            {
                foreach (UxrHandSide side in Sides)
                {
                    if (Samples.TryGetValue(side, out HandsPackPoseExtractor.Sample own)) yield return (side, false, GripSample.FromPack(own, side, SourceKind));
                    else if (MirrorSnaps) yield return (side, true, GripSample.FromPack(Samples[SourceSide], SourceSide, SourceKind));
                }
            }

            /// <summary>Подписи snap-точек для отчёта: рука и зеркало.</summary>
            public IEnumerable<(UxrHandSide Side, bool Mirrored)> SnapLabels() =>
                Computed != null ? Sides.Select(s => (s, s != SourceSide)) : SnapSides().Select(s => (s.Side, s.Mirrored));
        }

        /// <summary>Предмет пака: позы хвата оружия (рецепты импортёров или рецепт оружия), магазин и деталь механики.</summary>
        private sealed class Item
        {
            public string Pack, Name, WeaponPath;
            public KinemationWeaponRecipe Kin;
            public HandsPackWeaponRecipe Hands;      // рецепт оружия пака Hands: источник выведенных Grip/Support
            public MagazineSource Magazine;
            public ActionSource Action;
            public readonly List<HandsPackPoseRecipe> Grips = new List<HandsPackPoseRecipe>();
        }

        /// <summary>
        /// Деталь механики: затвор (<c>Slide</c>) или помпа (<c>Pump</c>). Деталь пака — рендерер Hands или кость KINEMATION
        /// (<see cref="HandsPackWeaponRecipe.ActionPart" />); в префабе — <c>&lt;Slide|Pump&gt;/&lt;Clean(деталь)&gt;</c>
        /// (<see cref="HandsPackWeaponBuilder" />, BuildAction).
        /// </summary>
        private sealed class ActionSource
        {
            public string Point, Part, PrefabPart, MirrorFrame;
            public string GripPrefabPart; // видимая ручка (ActionGripPart, иначе сама деталь) — опора расчётной точки
            public bool Pump;
            public HandsPackWeaponRecipe Recipe;
            public KinemationWeaponRecipe Kin;
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
            var imported = new HashSet<string>(HandsPackPoseImporter.Recipes.Select(r => r.Weapon), StringComparer.Ordinal);
            foreach (HandsPackWeaponRecipe r in HandsRecipes())
            {
                Item item = Get(HandsPack, HandsWeaponPath(r));
                item.Hands = r;
                // В Recharge рука держит дубликат патрона Shogun_Patron_mesh001 (исследование hand-rig-quality, shell-phase).
                item.Magazine = HandsMagazine(r, r.Name == FabarmName ? new[] { "Shogun_Patron_mesh001" } : null);
                // Grip/Support из рецепта оружия — только тем, у кого нет поз HandsPackPoseImporter.Recipes (AR15, FabarmSDASS):
                // принятые позы остальных стволов не трогаются, MEF-импорт (Recipes) этот источник не видит.
                if (!imported.Contains(item.WeaponPath)) item.Grips.AddRange(RecipeGrips(r));
                item.Action = ActionOf(r, null);
            }

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
                item.Action = ActionOf(k.Weapon, k);
            }

            return items.Values.OrderBy(i => i.Pack, StringComparer.Ordinal).ThenBy(i => i.Name, StringComparer.Ordinal).ToList();
        }

        private const string FabarmName = "FabarmSDASS";

        private static string HandsWeaponPath(HandsPackWeaponRecipe r) => $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.Name}.prefab";

        /// <summary>
        /// Рецепты пака Hands для экспорта: T-38 и набор 3+3. Источник магазинов, деталей механики и выведенных Grip/Support.
        /// </summary>
        private static IEnumerable<HandsPackWeaponRecipe> HandsRecipes() => WithStubs(HandsPackWeaponBuilder.T38.Concat(FullSetRecipes()));

        /// <summary>
        /// Полные рецепты набора 3+3 из <see cref="HandsPackWeaponBuilder" /> (решение 2026-10-23: AR15 и FabarmSDASS — на
        /// полный рецепт без доноров). Публичный рецепт вытесняет рецепт легаси того же префаба (<see cref="WithStubs" />).
        /// </summary>
        private static IEnumerable<HandsPackWeaponRecipe> FullSetRecipes() => new[] { HandsPackWeaponBuilder.AR15, HandsPackWeaponBuilder.ShotgunReal };

        /// <summary>
        /// Переданные рецепты и легаси-рецепты <see cref="LegacyStubs" /> для префабов, которых среди них нет; одноимённые
        /// префабы — первый рецепт.
        /// </summary>
        private static IEnumerable<HandsPackWeaponRecipe> WithStubs(IEnumerable<HandsPackWeaponRecipe> recipes) =>
            recipes.Concat(LegacyStubs()).GroupBy(HandsWeaponPath, StringComparer.Ordinal).Select(g => g.First());

        /// <summary>
        /// Набор 3+3 вне полного рецепта: только BrowningHiPower — его публичный рецепт легаси-сборки
        /// (<see cref="HandsPackLegacyRebuild.Browning" />, без снимков доноров); копия рецепта здесь больше не нужна.
        /// </summary>
        private static IEnumerable<HandsPackWeaponRecipe> LegacyStubs() => new[] { HandsPackLegacyRebuild.Browning };

        private static MagazineSource HandsMagazine(HandsPackWeaponRecipe r, string[] alternates) =>
            new MagazineSource
            {
                Item = r.Name, WeaponPath = HandsWeaponPath(r),
                MagazinePath = $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.MagazineName ?? r.Name + "_mag"}.prefab",
                Part = r.MagazinePart, Body = r.BodyPart, Folder = r.Folder, Point = r.MagazineIsInternal ? "Shell" : "Magazine",
                AlternateParts = alternates ?? Array.Empty<string>()
            };

        /// <summary>
        /// Grip/Support из рецепта оружия по образцу <see cref="KinemationWeaponBuilder.PoseRecipes" />: правая рука пака на
        /// рукояти (точка 0) и левая — вторая рука (точка 1) при <see cref="HandsPackWeaponRecipe.SupportGrip" />, кадр клипа
        /// позы t=0, корпус — <c>MeshContainer/&lt;BodyObjectName&gt;</c>. Меш рук и кость корпуса не названы: их берёт
        /// <see cref="RecipeGripSample" /> из модели пака.
        /// </summary>
        private static IEnumerable<HandsPackPoseRecipe> RecipeGrips(HandsPackWeaponRecipe r)
        {
            string weapon = HandsWeaponPath(r);
            string body = $"MeshContainer/{r.BodyObjectName}";
            yield return new HandsPackPoseRecipe
            {
                Pose = $"{HandsPack}_{r.Name}_Grip", Folder = r.Folder, Clip = r.PoseClip, Time = 0f, PackSide = UxrHandSide.Right,
                Weapon = weapon, BodyPath = body, GrabPoint = 0
            };
            if (!r.SupportGrip) yield break;
            yield return new HandsPackPoseRecipe
            {
                Pose = $"{HandsPack}_{r.Name}_Support", Folder = r.Folder, Clip = r.PoseClip, Time = 0f, PackSide = UxrHandSide.Left,
                Weapon = weapon, BodyPath = body, GrabPoint = 1
            };
        }

        /// <summary>Деталь механики рецепта; без затвора и помпы — null.</summary>
        private static ActionSource ActionOf(HandsPackWeaponRecipe r, KinemationWeaponRecipe k)
        {
            if (r.Action == HandsPackWeaponRecipe.ActionKind.None || string.IsNullOrEmpty(r.ActionPart)) return null;
            bool pump = r.Action == HandsPackWeaponRecipe.ActionKind.Pump;
            string point = pump ? PumpPointName : SlidePointName;
            return new ActionSource
            {
                Point = point, Part = r.ActionPart, PrefabPart = $"{point}/{HandsPackWeaponBuilder.Clean(r.ActionPart)}",
                GripPrefabPart = $"{point}/{HandsPackWeaponBuilder.Clean(r.ActionGripPart ?? r.ActionPart)}",
                MirrorFrame = $"MeshContainer/{r.BodyObjectName}", Pump = pump, Recipe = r, Kin = k
            };
        }

        // ── Вход ────────────────────────────────────────────────────────────
        // Экспорт — шаг сборщиков оружия и команды окна арсенала («Сборка» → «Экспортировать позы рук»), без меню.

        /// <summary>Все предметы обоих паков: позы, snap-точки, отчёты источников. Ошибка предмета не останавливает остальные.</summary>
        public static string ExportAll() => Export(Items(), false);

        /// <summary>
        /// Команда окна арсенала для выбранного оружия: позы и snap-точки его предмета без пересборки и без канона
        /// префаба (он не пересохранялся). Нет рецептов паков — исключение.
        /// </summary>
        public static string ExportWeapon(string weaponPrefab)
        {
            List<Item> items = Items().Where(i => i.WeaponPath == weaponPrefab).ToList();
            if (items.Count == 0) throw new InvalidOperationException($"[PackOrigPoses] {weaponPrefab}: рецептов паков нет — позы рук не экспортируются.");
            return Export(items, false);
        }

        /// <summary>Область записи экспорта выбранного оружия: папка поз, префабы оружия и магазина.</summary>
        public static string[] OutputPaths(string weaponPrefab) =>
            new[] { Root }.Concat(Items().Where(i => i.WeaponPath == weaponPrefab)
                                         .SelectMany(i => new[] { i.Grips.Count > 0 || i.Action != null ? i.WeaponPath : null, i.Magazine?.MagazinePath }))
                          .Where(p => p != null).Distinct().ToArray();

        /// <summary>Есть ли у префаба оружия рецепты паков для экспорта поз рук.</summary>
        public static bool HasSources(string weaponPrefab) => Items().Any(i => i.WeaponPath == weaponPrefab);

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
            new[] { Root }.Concat(Items().SelectMany(i => new[] { i.Grips.Count > 0 || i.Action != null ? i.WeaponPath : null, i.Magazine?.MagazinePath }))
                          .Where(p => p != null).Distinct().ToArray();

        /// <summary>Читаемые источники: папки паков.</summary>
        public static string[] InputPaths() =>
            new[]
            {
                HandsPackWeapon.PackModels, KinemationWeapon.Pack + "Animations", KinemationWeapon.Pack + "Prefabs/Weapons", KinemationPoseExtractor.Character,
                KinemationWeapon.ArtRoot, // вырезанные меши деталей KINEMATION: точки меша, если у меша пака нет данных на CPU
                ComputedSlideTemplate, ComputedPumpTemplate // позы-шаблоны расчётных точек деталей механики
            };

        private static string Export(List<Item> items, bool canonicalize)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Экспорт _orig запрещён в Play Mode.");
            var log = new ExportLog();
            var errors = new List<string>();
            var warnings = new List<string>(); // магазин и деталь механики — эксперимент: их сбой не останавливает сборку оружия
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

            string report = $"[PackOrigPoses] Экспорт поз рук: предметов {items.Count}, ошибок {errors.Count}, предупреждений эксперимента {warnings.Count}\n" +
                            log.Format() + (errors.Count > 0 ? "\nОшибки:\n" + string.Join("\n", errors) : "") +
                            (warnings.Count > 0 ? "\nЭксперимент (магазин, деталь механики):\n" + string.Join("\n", warnings) : "");
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

        /// <summary>Итог экспорта для лога и окна: куда записано и что изменилось.</summary>
        private sealed class ExportLog
        {
            public readonly SortedSet<string> Folders = new(StringComparer.Ordinal);
            public readonly List<string> Created = new(), Updated = new(), Unchanged = new();
            public readonly List<string> Reports = new();
            public readonly List<string> SnapsChanged = new(), SnapsUnchanged = new();
            public readonly List<string> Notes = new();

            public void Pose(string path, string status)
            {
                Folders.Add(Path.GetDirectoryName(path)?.Replace('\\', '/') + "/");
                if (status == PoseCreated) Created.Add(path);
                else if (status == PoseUnchanged) Unchanged.Add(path);
                else Updated.Add($"{path} ({status})");
            }

            public string Format()
            {
                var text = new StringBuilder();
                void Section(string title, ICollection<string> lines)
                {
                    if (lines.Count == 0) return;
                    text.Append($"{title} ({lines.Count}):\n");
                    foreach (string line in lines) text.Append("  ").Append(line).Append('\n');
                }
                Section("Папки поз", Folders);
                Section("Позы созданы", Created);
                Section("Позы обновлены", Updated);
                Section("Позы без изменений", Unchanged);
                Section("Отчёты источников (JSON)", Reports);
                Section($"Snap-точки {SnapFolder}/... добавлены или обновлены в префабах", SnapsChanged);
                Section($"Snap-точки {SnapFolder}/... без изменений в префабах", SnapsUnchanged);
                Section("Примечания", Notes);
                return text.ToString().TrimEnd('\n');
            }
        }

        private const string PoseCreated = "создана";
        private const string PoseUnchanged = "без изменений";
        private const string SnapsUnchangedPrefix = "snap-точки без изменений";

        private static void ExportItem(Item item, bool canonicalize, ExportLog log, List<string> errors, List<string> warnings)
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
                    else log.Notes.Add($"{item.Pack}/{item.Name}: магазин — {report.magazineNote}");
                }
                catch (Exception e)
                {
                    report.magazineNote = "ошибка: " + e.Message;
                    warnings.Add($"{item.Pack}/{item.Name}: магазин — {e.Message}");
                }
            }

            ActionReport actionReport = null;
            if (item.Action != null)
            {
                // Эксперимент, как магазин: сбой не мешает позам хвата оружия и сборке.
                actionReport = new ActionReport
                {
                    pack = item.Pack, item = item.Name, weapon = item.WeaponPath, point = item.Action.Point, part = item.Action.Part,
                    prefabPart = item.Action.PrefabPart, mirrorFrame = item.Action.MirrorFrame
                };
                OrigPoint action = null;
                try
                {
                    action = item.Action.Pump ? PumpPoint(item, actionReport) : SlidePoint(item, actionReport);
                    if (action != null) actionReport.source = ActionSourcePack;
                    if (actionReport.warning != null) warnings.Add($"{item.Pack}/{item.Name}: {item.Action.Point} — {actionReport.warning}");
                }
                catch (Exception e)
                {
                    actionReport.note = "ошибка: " + e.Message;
                    warnings.Add($"{item.Pack}/{item.Name}: {item.Action.Point} — {e.Message}");
                }

                // Нет источника в паке (затвор ходит сам от отдачи, рука на детали не видна) — расчётная точка по геометрии
                // детали и поза-шаблон (решение пользователя 2026-10-23: хват пользователь доводит в редакторе).
                if (action == null)
                {
                    try
                    {
                        action = ComputedActionPoint(item, actionReport);
                        log.Notes.Add($"{item.Pack}/{item.Name}: {item.Action.Point} — {actionReport.note}");
                    }
                    catch (Exception e)
                    {
                        actionReport.computedNote = "ошибка: " + e.Message;
                        warnings.Add($"{item.Pack}/{item.Name}: {item.Action.Point} (расчётная точка) — {e.Message}");
                    }
                }

                if (action != null) points.Add(action);
            }

            foreach (OrigPoint point in points)
            {
                log.Pose(PoseAssetPath(point.Pack, point.Name), WritePose(point));
            }

            // Отдельная запись на префаб и вид точек: сбой экспериментальных snap-точек (деталь механики в префабе оружия)
            // откатывает только их запись, snap-точки хвата оружия остаются.
            var snapProblems = new Dictionary<string, string>();
            foreach (IGrouping<(string Prefab, bool Experimental), OrigPoint> prefab in points.GroupBy(p => (p.Prefab, p.Experimental))
                                                                                             .OrderBy(g => g.Key.Experimental))
            {
                string path = prefab.Key.Prefab;
                try
                {
                    string status = WriteSnaps(path, prefab.ToList(), canonicalize, snapProblems);
                    if (status.StartsWith(SnapsUnchangedPrefix, StringComparison.Ordinal)) log.SnapsUnchanged.Add($"{path} ({status})");
                    else log.SnapsChanged.Add($"{path} ({status})");
                }
                catch (Exception e)
                {
                    (prefab.Key.Experimental ? warnings : errors).Add($"{path}: snap-точки не записаны — {e.Message}");
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
                    snaps = point.SnapLabels().Select(s => $"{(point.SnapRoot != null ? point.SnapRoot + "/" : "")}{SnapFolder}/{point.Name}/{s.Side}" +
                                                           $"{(s.Mirrored ? " (зеркало)" : "")}").ToArray(),
                    check = point.Computed != null ? ComputedCheck
                          : point.Point == "Grip" || point.Point == "Support" ? null : "Эксперимент: кадр выбран автоматически — проверить глазами."
                });
                if (snapProblems.TryGetValue(point.Name, out string problem)) (point.Experimental ? warnings : errors).Add($"{point.Prefab}/{point.Name}: {problem}");
            }

            log.Reports.Add(WriteReport(item, report));
            if (actionReport != null) log.Reports.Add(WriteActionReport(actionReport));
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
                // Рецепт MEF-импорта называет меш рук и кость корпуса; выведенный из рецепта оружия (RecipeGrips) — нет.
                point.Samples[recipe.PackSide] = recipe.Mesh != null ? HandsPackPoseImporter.ExtractSample(recipe) : RecipeGripSample(item.Hands, recipe);
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

        /// <summary>
        /// Кадр Grip/Support по рецепту оружия: модель пака папки (как <see cref="HandsPackWeapon" />), корпус — рендерер
        /// <see cref="HandsPackWeaponRecipe.BodyPart" /> и его главная кость (как <c>HandsPackPoseImporter.ExtractSample</c> с
        /// явными именами).
        /// </summary>
        private static HandsPackPoseExtractor.Sample RecipeGripSample(HandsPackWeaponRecipe r, HandsPackPoseRecipe recipe)
        {
            if (r == null) throw new InvalidOperationException($"{recipe.Pose}: нет рецепта оружия для кадра без меша рук");
            GameObject model = HandsModel(r.Folder);
            SkinnedMeshRenderer body = SkinOf(model, r.BodyPart);
            return HandsPackPoseExtractor.Extract(model, HandsClip(r.Folder, recipe.Clip), recipe.Time, recipe.PackSide, body.sharedMesh.name,
                                                  body.bones[MainBone(body)].name);
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

            // Без записи: нужны ли изменения.
            GameObject probe = PrefabUtility.LoadPrefabContents(prefabPath);
            int created, changed, same;
            try { PlaceSnaps(probe, points, problems, out created, out changed, out same); }
            finally { PrefabUtility.UnloadPrefabContents(probe); }
            if (created == 0 && changed == 0) return $"snap-точки без изменений ({same})";

            byte[] original = File.ReadAllBytes(prefabPath);
            bool applied = false;
            try
            {
                applied = true;
                // Запись — тем же путём, что сохранение префаба (LoadPrefabContents → SaveAsPrefabAsset), от устоявшегося
                // вида Unity: применение добавленных объектов к экземпляру в preview-сцене пересериализовало чужие
                // компоненты (AudioSource ImpactAudio у магазинов набора Hands), и сверка YAML отвергала запись. Канон
                // _assetId и флагов UXR — после каждого сохранения.
                Converge(prefabPath);
                string baseline = File.ReadAllText(prefabPath);
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    PlaceSnaps(root, points, problems, out created, out changed, out same);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                Canonicalize(prefabPath);

                // Допустимая разница — объекты Orig каждого контейнера точек и дописанные m_Children самих контейнеров.
                Transform saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).transform;
                var free = new HashSet<long>();
                var additive = new HashSet<long>();
                foreach (Transform container in points.Select(p => ContainerOf(saved, p)).Where(c => c != null).Distinct())
                {
                    Transform orig = container.Find(SnapFolder);
                    if (orig == null) continue;
                    foreach (Transform t in orig.GetComponentsInChildren<Transform>(true))
                    {
                        AddFileId(free, t);
                        AddFileId(free, t.gameObject);
                    }

                    AddFileId(additive, container);
                }

                string written = File.ReadAllText(prefabPath);
                if (written == baseline) throw new InvalidOperationException("применение не дошло до файла префаба");
                string problem = YamlProblems(baseline, written, free, additive);
                if (problem != null) throw new InvalidOperationException("в YAML не только объекты Orig — файл возвращён: " + problem);

                return $"snap-точки: новых объектов {created}, сдвинуто {changed}, без изменений {same}";
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
        }

        /// <summary>
        /// Контейнер snap-точек <c>Orig</c>: тот из <c>MeshContainer/Grabs</c>, <c>Grabs</c>, корня, где <c>Orig</c> уже есть;
        /// иначе первый существующий. Так <c>Grabs</c>, созданный позже у корня (snap-узлы аватаров инспектора UXR или
        /// <see cref="AvatarGripRegistration" />), не прячет прежний <c>Orig</c> корня и не порождает второй.
        /// </summary>
        internal static Transform GrabsOf(Transform root)
        {
            foreach (Transform container in new[] { root.Find("MeshContainer/Grabs"), root.Find("Grabs"), root })
            {
                if (container != null && container.Find(SnapFolder) != null) return container;
            }

            return root.Find("MeshContainer/Grabs") ?? root.Find("Grabs") ?? root;
        }

        /// <summary>
        /// Контейнер <c>Orig</c> точки: узел <see cref="OrigPoint.SnapRoot" /> (граббабл детали механики), иначе
        /// <see cref="GrabsOf" /> корня; нет узла — null.
        /// </summary>
        private static Transform ContainerOf(Transform root, OrigPoint point) => point.SnapRoot == null ? GrabsOf(root) : root.Find(point.SnapRoot);

        /// <summary>Snap-точки предмета в загруженном содержимом префаба: создать недостающие, сдвинуть отличающиеся.</summary>
        private static void PlaceSnaps(GameObject root, List<OrigPoint> points, Dictionary<string, string> problems,
            out int createdCount, out int changedCount, out int same)
        {
            var created = new HashSet<GameObject>();
            changedCount = 0;
            same = 0;
            foreach (OrigPoint point in points)
            {
                Transform container = ContainerOf(root.transform, point);
                Transform body = root.transform.Find(point.BodyPath);
                Transform mirror = point.MirrorFramePath != null ? root.transform.Find(point.MirrorFramePath) : null;
                string bodyProblem = container == null ? $"нет узла {point.SnapRoot}"
                    : body == null ? $"нет детали {point.BodyPath}"
                    : point.MirrorFramePath != null && mirror == null ? $"нет корпуса {point.MirrorFramePath} для зеркала второй руки"
                    : point.CheckBody?.Invoke(body);
                (Vector3 Position, Quaternion Rotation, string Problem) computed = default;
                if (bodyProblem == null && point.Computed != null)
                {
                    computed = point.Computed(root.transform);
                    bodyProblem = computed.Problem;
                }

                if (bodyProblem != null)
                {
                    problems[point.Name] = bodyProblem;
                    continue;
                }

                // Расчётная точка: рамка ладони SourceSide из геометрии детали, вторая рука — зеркало по корпусу.
                IEnumerable<(UxrHandSide Side, Vector3 Position, Quaternion Rotation)> poses = point.Computed != null
                    ? Sides.Select(side =>
                    {
                        if (side == point.SourceSide) return (side, computed.Position, computed.Rotation);
                        MirrorAcross(mirror, computed.Position, computed.Rotation, out Vector3 p, out Quaternion r);
                        return (side, p, r);
                    }).ToList()
                    : point.SnapSides().Select(s => SampleSnap(s.Side, s.Mirrored, s.Sample, body, mirror)).ToList();

                foreach (var (side, worldPosition, worldRotation) in poses)
                {
                    Transform orig = Child(container, SnapFolder, created);
                    Transform node = Child(Child(orig, point.Name, created), side.ToString(), created);
                    if (created.Contains(node.gameObject))
                    {
                        node.SetPositionAndRotation(worldPosition, worldRotation);
                    }
                    else if (!SamePose(root.transform, node, worldPosition, worldRotation))
                    {
                        node.SetPositionAndRotation(worldPosition, worldRotation);
                        changedCount++;
                    }
                    else
                    {
                        same++;
                    }
                }
            }

            createdCount = created.Count;
        }

        /// <summary>Рамка ладони руки по кадру пака в мире префаба: снятая рука — от детали, зеркальная — от детали или корпуса.</summary>
        private static (UxrHandSide Side, Vector3 Position, Quaternion Rotation) SampleSnap(UxrHandSide side, bool mirrored, GripSample sample, Transform body,
                                                                                         Transform mirror)
        {
            float k = body.lossyScale.x / sample.BodyScale;
            if (mirrored && mirror != null)
            {
                // Деталь механики: рамка ладони снятой руки на детали — в мир, затем зеркало по боковой оси корпуса.
                sample.PalmInBody(sample.Side, k, out Vector3 own, out Quaternion ownRotation);
                MirrorAcross(mirror, body.position + body.rotation * own, body.rotation * ownRotation, out Vector3 worldPosition, out Quaternion worldRotation);
                return (side, worldPosition, worldRotation);
            }

            sample.PalmInBody(side, k, out Vector3 position, out Quaternion rotation);
            return (side, body.position + body.rotation * position, body.rotation * rotation);
        }

        /// <summary>Пересохранить префаб до устойчивых байтов (с каноном после каждого сохранения).</summary>
        internal static void Converge(string prefabPath)
        {
            Canonicalize(prefabPath);
            for (int pass = 0; pass < 3; pass++)
            {
                byte[] before = File.ReadAllBytes(prefabPath);
                GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try { PrefabUtility.SaveAsPrefabAsset(contents, prefabPath); }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                Canonicalize(prefabPath);
                if (File.ReadAllBytes(prefabPath).SequenceEqual(before)) break;
            }
        }

        /// <summary>SaveAsPrefabAsset пишет _assetId = 0 и флаги UXR как вне префаба — вернуть канон на диске.</summary>
        internal static void Canonicalize(string prefabPath)
        {
            NetworkAssetIdNormalizer.Normalize(new[] { prefabPath }, false);
            UxrUniqueIdPersister.Normalize(prefabPath);
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

        /// <summary>
        /// Зеркало рамки ладони по боковой оси (x) корпуса <paramref name="frame" />: то же правило, что
        /// <see cref="GripSample.ForSide" /> для хвата оружия (x позиции — с обратным знаком, поворот — (x, −y, −z, w)), но в осях
        /// корпуса, а не детали: у детали механики своё начало и оси, её «боковая ось» — не плоскость симметрии оружия.
        /// </summary>
        private static void MirrorAcross(Transform frame, Vector3 worldPosition, Quaternion worldRotation, out Vector3 position, out Quaternion rotation)
        {
            Quaternion inverse = Quaternion.Inverse(frame.rotation);
            Vector3 local = inverse * (worldPosition - frame.position);
            Quaternion localRotation = inverse * worldRotation;
            local.x = -local.x;
            localRotation = new Quaternion(localRotation.x, -localRotation.y, -localRotation.z, localRotation.w);
            position = frame.position + frame.rotation * local;
            rotation = frame.rotation * localRotation;
        }

        private static void AddFileId(HashSet<long> ids, Object assetObject)
        {
            if (assetObject != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assetObject, out string _, out long id)) ids.Add(id);
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
                problems.Add($"изменён !u!{doc.Value.Class} &{doc.Key} ({FirstDifference(doc.Value.Lines, current.Lines)})");
            }

            foreach (KeyValuePair<long, (int Class, string[] Lines)> doc in now)
            {
                if (!was.ContainsKey(doc.Key) && !(free.Contains(doc.Key) && (doc.Value.Class == 1 || doc.Value.Class == 4)))
                    problems.Add($"новый !u!{doc.Value.Class} &{doc.Key} вне Orig");
            }

            return problems.Count == 0 ? null : string.Join("; ", problems.Take(8)) + (problems.Count > 8 ? $" … ещё {problems.Count - 8}" : "");
        }

        internal static Dictionary<long, (int Class, string[] Lines)> Documents(string text, out string header)
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
            // Хвостовая пустая строка — у последнего документа файла; новый объект Orig со случайным fileID может встать
            // в конец, и без этого прежний последний документ выглядел бы изменённым.
            foreach (long key in documents.Keys.ToList())
            {
                (int Class, string[] Lines) doc = documents[key];
                int length = doc.Lines.Length;
                while (length > 1 && doc.Lines[length - 1].Length == 0) length--;
                if (length != doc.Lines.Length) documents[key] = (doc.Class, doc.Lines.Take(length).ToArray());
            }

            header = string.Join("\n", preamble);
            return documents;
        }

        /// <summary>Первая различающаяся строка документа — для отчёта об отказе.</summary>
        internal static string FirstDifference(string[] was, string[] now)
        {
            for (int i = 0; i < Math.Max(was.Length, now.Length); i++)
            {
                string a = i < was.Length ? was[i].Trim() : "—", b = i < now.Length ? now[i].Trim() : "—";
                if (a != b) return $"строка {i}: «{a}» → «{b}»";
            }

            return "без различий строк";
        }

        internal static bool IsSubsequence(string[] part, string[] whole)
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
            void Scan(IEnumerable<(AnimationClip Clip, AnimationClip WeaponClip)> clips)
            {
                foreach (string part in new[] { m.Part }.Concat(m.AlternateParts))
                {
                    foreach ((AnimationClip clip, AnimationClip weaponClip) in clips)
                    {
                        List<Window>[] windows;
                        float rate = clip.frameRate > 0f ? clip.frameRate : 30f;
                        try
                        {
                            using ClipRig rig = m.Kin != null ? (ClipRig)new KinemationRig(m.Kin, clip, weaponClip, part) : new HandsRig(HandsModel(m.Folder), clip, part, m.Body);
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
            }

            Scan(ReloadClips(m));
            // Запасной клип KINEMATION — проверка магазина Mag_Check, только если в клипах перезарядки окна нет (TR15, решение 2026-10-23).
            bool magCheck = false;
            if (best == null && m.Kin != null)
            {
                Scan(KinemationPairs(m.Kin, name => name.Contains("Mag_Check")));
                magCheck = best != null;
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
                Experimental = true, SourceKind = m.Kin != null ? PackClipGripSource.Kinemation : PackClipGripSource.Hands
            };
            foreach (UxrHandSide side in holding) point.Samples[side] = MagazineSample(m, best, side);
            point.CheckBody = MagazineBodyCheck(m);
            report.magazineNote = $"выбран {best.Clip.name}{(best.WeaponClip != null ? " + " + best.WeaponClip.name : "")}, деталь {best.Part}, " +
                                  $"кадры {best.Start}–{best.End} ({best.StartTime:F3}–{best.EndTime:F3} с), середина {best.MidTime:F3} с; " +
                                  $"держит: {string.Join(", ", holding)}." +
                                  (magCheck ? " Запасной клип Mag_Check: в клипах перезарядки окна нет." : "") + " Эксперимент — проверить глазами.";
            return point;
        }

        /// <summary>Кадр руки с деталью теми же экстракторами, что хват оружия: деталь — «корпус» кадра.</summary>
        private static HandsPackPoseExtractor.Sample MagazineSample(MagazineSource m, Window w, UxrHandSide side)
        {
            if (m.Kin != null)
            {
                return KinemationPoseExtractor.Extract(w.Clip, w.MidTime, side, PackPrefab(m.Kin), w.Part,
                                                       m.Kin.RestClip != null ? WeaponClip(m.Kin, m.Kin.RestClip) : null, w.WeaponClip);
            }

            GameObject model = HandsModel(m.Folder);
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
        private static Func<Transform, string> MagazineBodyCheck(MagazineSource m) => PartBodyCheck(m.Kin, m.Folder, m.Part);

        /// <summary>
        /// Деталь префаба — меш той же детали пака: у KINEMATION вырезанный меш <c>&lt;ствол&gt;_&lt;Clean(деталь)&gt;</c>
        /// (<see cref="KinemationWeapon.MeshOf" />), у Hands — меш рендерера детали модели пака.
        /// </summary>
        private static Func<Transform, string> PartBodyCheck(KinemationWeaponRecipe kin, string folder, string part)
        {
            string expected = kin != null ? $"{kin.Weapon.Name}_{KinemationWeapon.Clean(part)}" : SkinOf(HandsModel(folder), part).sharedMesh.name;
            return body =>
            {
                Mesh mesh = body.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                return mesh != null && mesh.name == expected ? null : $"деталь {body.name} префаба — не меш {expected} ({(mesh != null ? mesh.name : "нет меша")})";
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
            foreach ((AnimationClip Clip, AnimationClip WeaponClip) pair in KinemationPairs(m.Kin, name => name.Contains("_Reload"))) yield return pair;
        }

        /// <summary>
        /// Пары клипов KINEMATION: клип рук <c>A_FP_&lt;ствол&gt;_*</c> и синхронный клип оружия <c>A_W_&lt;ствол&gt;_*</c>
        /// (Tactical = Tac), оба с именем по <paramref name="filter" />; клип рук без пары не берётся.
        /// </summary>
        private static IEnumerable<(AnimationClip Clip, AnimationClip WeaponClip)> KinemationPairs(KinemationWeaponRecipe k, Func<string, bool> filter)
        {
            string folder = $"{KinemationWeapon.Pack}Animations/{k.AnimFolder}/";
            List<IGrouping<string, AnimationClip>> weapon = Clips(folder + "Weapon").Where(c => filter(c.name)).GroupBy(c => ReloadKey(c.name, "A_W_")).ToList();
            foreach (IGrouping<string, AnimationClip> character in Clips(folder + "Character").Where(c => filter(c.name)).GroupBy(c => c.name)
                                                                                          .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AnimationClip clip = PreferModel(character);
                IGrouping<string, AnimationClip> pair = weapon.FirstOrDefault(w => w.Key == ReloadKey(clip.name, "A_FP_"));
                if (pair != null) yield return (clip, PreferModel(pair));
            }
        }

        private static string ReloadKey(string name, string prefix) => (name.StartsWith(prefix) ? name.Substring(prefix.Length) : name).Replace("Tactical", "Tac");

        // ── Деталь механики ─────────────────────────────────────────────────

        /// <summary>Кадры одного клипа детали механики.</summary>
        private sealed class ClipScan
        {
            public AnimationClip Clip, WeaponClip;
            public float Rate;
            public bool Mesh;
            public List<Frame> Frames;
        }

        /// <summary>
        /// Затвор: кадр, где рука ведёт деталь, по всем клипам пака (у KINEMATION — пары клипов рук и оружия). Сдвиг детали —
        /// от покоя (клип покоя оружия), ход — наибольший сдвиг по всем клипам. Окно руки — подряд кадры, где деталь сдвинута
        /// не меньше <see cref="ActionSeatShare" /> хода, ладонь у меша детали и рука жёстко идёт с ней; окно, в котором деталь
        /// почти не движется (размах меньше <see cref="ActionRangeShare" /> хода — затворная задержка, рука просто рядом), не
        /// берётся. Выбор — середина самого длинного окна, как у магазина.
        /// </summary>
        private static OrigPoint SlidePoint(Item item, ActionReport report)
        {
            ActionSource a = item.Action;
            var scans = new List<ClipScan>();
            foreach ((AnimationClip clip, AnimationClip weaponClip) in ActionClips(a))
            {
                float rate = clip.frameRate > 0f ? clip.frameRate : 30f;
                try
                {
                    using ClipRig rig = ActionRig(a, clip, weaponClip);
                    scans.Add(new ClipScan
                    {
                        Clip = clip, WeaponClip = weaponClip, Rate = rate, Mesh = rig.Points.Length > 0, Frames = ScanFrames(rig, clip.length, rate, rig.RestSeat)
                    });
                }
                catch (Exception e)
                {
                    report.scan.Add(new ActionScanReport { clip = clip.name, weaponClip = weaponClip?.name, result = "ошибка: " + e.Message });
                }
            }

            if (scans.Count == 0)
            {
                report.note = report.scan.Count == 0 ? "в паке нет клипов для этой детали" : "клипы детали не прочитаны (см. scan)";
                return null;
            }

            float travel = scans.SelectMany(s => s.Frames).Max(f => f.Seat);
            report.travelMm = Round(travel * 1000f);
            if (travel < MinActionTravel)
            {
                report.note = $"деталь в клипах пака не ходит: наибольший сдвиг от покоя {travel * 1000f:F1} мм";
                return null;
            }

            float minSeat = travel * ActionSeatShare, minRange = travel * ActionRangeShare;
            report.minSeatMm = Round(minSeat * 1000f);
            report.minRangeMm = Round(minRange * 1000f);
            // Два яруса: окна, где деталь движется (рука её ведёт), и запасные — рука на сдвинутой детали жёстко с ней, но
            // деталь в окне стоит (AK105: рукоятка взвода удержана в заднем положении). Запасные — только если первых нет:
            // на затворной задержке такое окно бывает ложным (рука рядом), поэтому кадр помечается в отчёте.
            Window best = null, bestStill = null;
            List<Window> bestClip = null, bestStillClip = null;
            foreach (ClipScan scan in scans)
            {
                var windows = new List<Window>();
                var still = new List<Window>();
                for (int s = 0; s < Sides.Length; s++)
                {
                    List<Window> all = Windows(scan.Frames, Sides[s], scan.Mesh ? NearMesh : NearOrigin, minSeat);
                    foreach (Window w in all)
                    {
                        w.Clip = scan.Clip;
                        w.WeaponClip = scan.WeaponClip;
                        w.Part = a.Part;
                        w.Rate = scan.Rate;
                    }

                    List<Window> moving = all.Where(w => w.SeatRangeMm >= minRange * 1000f).ToList();
                    List<Window> standing = all.Except(moving).ToList();
                    int index = s;
                    Window top = moving.OrderByDescending(w => w.Frames).ThenBy(w => w.RigidMm).FirstOrDefault();
                    Window topStill = standing.OrderByDescending(w => w.Frames).ThenBy(w => w.RigidMm).FirstOrDefault();
                    ActionScanReport row = top != null ? ActionScanReport.Of(top, "окно")
                        : topStill != null ? ActionScanReport.Of(topStill, "окно без движения детали (запасное)")
                        : new ActionScanReport { clip = scan.Clip.name, weaponClip = scan.WeaponClip?.name, side = Sides[s].ToString(), result = "нет окна" };
                    row.distance = scan.Mesh ? "меш" : "начало детали";
                    row.minPalmMm = Round(scan.Frames.Where(f => f.Seat >= minSeat).Select(f => f.Palm[index] * 1000f).DefaultIfEmpty(-1f).Min());
                    row.maxSeatMm = Round(scan.Frames.Max(f => f.Seat) * 1000f);
                    report.scan.Add(row);
                    windows.AddRange(moving);
                    still.AddRange(standing);
                }

                Window candidate = windows.OrderByDescending(w => w.EndTime - w.StartTime).ThenBy(w => w.RigidMm).FirstOrDefault();
                if (Longer(candidate, best))
                {
                    best = candidate;
                    bestClip = windows;
                }

                Window candidateStill = still.OrderByDescending(w => w.EndTime - w.StartTime).ThenBy(w => w.RigidMm).FirstOrDefault();
                if (Longer(candidateStill, bestStill))
                {
                    bestStill = candidateStill;
                    bestStillClip = still;
                }
            }

            bool standby = false;
            if (best == null && bestStill != null)
            {
                best = bestStill;
                bestClip = bestStillClip;
                standby = true;
            }

            if (best == null)
            {
                report.note = "ни в одном клипе рука не держит деталь жёстко (см. scan)";
                return null;
            }

            // Вторая рука — снятая с клипа, только если в том же клипе и кадре тоже ведёт деталь; иначе — зеркало.
            UxrHandSide[] holding = Sides.Where(s => s == best.Side || bestClip.Any(w => w.Side == s && w.Start <= best.Mid && best.Mid <= w.End)).ToArray();
            OrigPoint point = ActionOrigPoint(item, best.Clip, best.WeaponClip, best.MidTime, best.Rate, best.Side);
            foreach (UxrHandSide side in holding) point.Samples[side] = ActionSample(a, best.Clip, best.WeaponClip, best.MidTime, side);
            report.note = $"выбран {best.Clip.name}{(best.WeaponClip != null ? " + " + best.WeaponClip.name : "")}, " +
                          $"кадры {best.Start}–{best.End} ({best.StartTime:F3}–{best.EndTime:F3} с), середина {best.MidTime:F3} с, сдвиг {best.SeatMm:F0} мм " +
                          $"из хода {travel * 1000f:F0} мм; держит: {string.Join(", ", holding)}." +
                          (standby ? " Запасное окно: рука жёстко на сдвинутой детали, но деталь в окне не движется." : "") +
                          " Эксперимент — проверить глазами.";
            return point;
        }

        /// <summary>
        /// Помпа: кадр позы оружия t=0 (<see cref="HandsPackWeaponRecipe.PoseClip" />), левая рука — вторая рука пака держит
        /// цевьё в прицеливании (решение 2026-10-23). Клип хода (<see cref="HandsPackWeaponRecipe.ActionClip" />) проверяет,
        /// что рука идёт с помпой жёстко. Ладонь у меша (<see cref="NearMesh" />) — кадр берётся; дальше, до
        /// <see cref="PumpNear" />, — только если рука жёстко идёт с помпой весь её ход (<see cref="PumpPointRule" />).
        /// Ладонь у меша, но не жёстко — кадр записывается с предупреждением.
        /// </summary>
        private static OrigPoint PumpPoint(Item item, ActionReport report)
        {
            ActionSource a = item.Action;
            if (a.Kin != null)
            {
                report.note = "помпа KINEMATION: правило кадра не задано";
                return null;
            }

            const UxrHandSide side = UxrHandSide.Left;
            const int s = 0;
            HandsPackWeaponRecipe r = a.Recipe;
            GameObject model = HandsModel(r.Folder);
            AnimationClip pose = HandsClip(r.Folder, r.PoseClip);
            float palm;
            bool mesh;
            using (var poseRig = new HandsRig(model, pose, a.Part, r.BodyPart))
            {
                palm = ScanFrames(poseRig, 0f, 30f)[0].Palm[s];
                mesh = poseRig.Points.Length > 0;
            }

            string distance = mesh ? "меш" : "начало детали";
            bool near = palm <= (mesh ? NearMesh : NearOrigin);
            bool rigid = false, drives = false;
            string rigidity = "клипа хода нет — жёсткость не проверена";
            if (!string.IsNullOrEmpty(r.ActionClip))
            {
                AnimationClip shot = HandsClip(r.Folder, r.ActionClip);
                float rate = shot.frameRate > 0f ? shot.frameRate : 30f;
                using var shotRig = new HandsRig(model, shot, a.Part, r.BodyPart);
                List<Frame> frames = ScanFrames(shotRig, shot.length, rate);
                float mm = frames.Max(f => Vector3.Distance(f.RelativePosition[s], frames[0].RelativePosition[s])) * 1000f;
                float deg = frames.Max(f => Quaternion.Angle(f.RelativeRotation[s], frames[0].RelativeRotation[s]));
                float stroke = frames.Max(f => f.Seat) * 1000f;
                float palmMax = frames.Max(f => f.Palm[s]);
                rigid = mm <= RigidMeters * 1000f && deg <= RigidDegrees;
                drives = rigid && stroke >= MinActionTravel * 1000f && palmMax <= PumpNear;
                report.scan.Add(new ActionScanReport
                {
                    clip = shot.name, side = side.ToString(), distance = distance, result = rigid ? "жёстко" : "не жёстко", frames = frames.Count,
                    rigidMm = Round(mm), rigidDeg = Round(deg), seatRangeMm = Round(stroke), palmMm = Round(palmMax * 1000f)
                });
                rigidity = $"в {shot.name} рука {(rigid ? "идёт с помпой жёстко" : "не идёт с помпой жёстко")}: {mm:F1} мм / {deg:F1}° за ход {stroke:F0} мм";
            }

            bool accepted = near || palm <= PumpNear && drives;
            report.scan.Insert(0, new ActionScanReport
            {
                clip = pose.name, side = side.ToString(), distance = distance, frames = 1, palmMm = Round(palm * 1000f),
                result = near ? "кадр позы" : accepted ? $"кадр позы: ладонь дальше {NearMesh * 1000f:F0} мм, но рука ведёт помпу весь ход" : "ладонь не у детали"
            });
            if (!accepted)
            {
                report.note = $"в кадре позы {pose.name} t=0 левая ладонь в {palm * 1000f:F0} мм от детали и не ведёт помпу жёстко ({rigidity}) — не на помпе";
                return null;
            }

            if (near && !rigid && !string.IsNullOrEmpty(r.ActionClip)) report.warning = $"{rigidity} — кадр позы записан, проверить глазами";
            OrigPoint point = ActionOrigPoint(item, pose, null, 0f, pose.frameRate, side);
            point.Samples[side] = ActionSample(a, pose, null, 0f, side);
            report.note = $"кадр позы {pose.name} t=0, левая рука, ладонь в {palm * 1000f:F0} мм от детали; {rigidity}. Эксперимент — проверить глазами.";
            return point;
        }

        /// <summary>Точка детали механики: snap — в граббабле детали, в осях детали; вторая рука — зеркало по корпусу.</summary>
        private static OrigPoint ActionOrigPoint(Item item, AnimationClip clip, AnimationClip weaponClip, float time, float rate, UxrHandSide source)
        {
            ActionSource a = item.Action;
            return new OrigPoint
            {
                Pack = item.Pack, Item = item.Name, Point = a.Point, Prefab = item.WeaponPath, BodyPath = a.PrefabPart, SnapRoot = a.Point,
                MirrorFramePath = a.MirrorFrame, MirrorSnaps = true, Experimental = true, Clip = clip.name, WeaponClip = weaponClip?.name, Time = time,
                FrameRate = rate, SourceSide = source, SourceKind = a.Kin != null ? PackClipGripSource.Kinemation : PackClipGripSource.Hands,
                CheckBody = PartBodyCheck(a.Kin, a.Recipe.Folder, a.Part)
            };
        }

        // ── Расчётная точка детали механики ─────────────────────────────────

        public const string ActionSourcePack = "pack";
        public const string ActionSourceComputed = "computed";
        private const string ComputedCheck = "Расчётная точка, проверить глазами: в паке нет кадра руки на детали — snap по геометрии детали, поза — шаблон SDK.";

        /// <summary>Поза-шаблон расчётной точки затвора — SDK Cyborg <c>Grab</c> (её сейчас временно носят записи Cyborg на затворах).</summary>
        public const string ComputedSlideTemplate = "Assets/ThirdParty/UltimateXR/Runtime/Art/Avatars/Cyborg/HandPoses/Grab.asset";

        /// <summary>Поза-шаблон расчётной точки помпы — SDK Cyborg <c>GrabClaw</c> (её носят временные записи Cyborg на помпе FabarmSDASS и затворе AR15).</summary>
        public const string ComputedPumpTemplate = "Assets/ThirdParty/UltimateXR/Runtime/Art/Avatars/Cyborg/HandPoses/GrabClaw.asset";

        private const float PalmOffset = 0.02f; // центр ладони SDK над поверхностью хвата (толщина ладони), м мира префаба
        private const float RearInset = 0.02f;  // от заднего торца детали вдоль хода, м; не больше 30 % длины детали

        /// <summary>
        /// Расчётная точка детали механики без источника в паке (решение пользователя 2026-10-23: snap ставится расчётно, хват
        /// доводится в редакторе). Рука — левая (вторая рука держит деталь), правая — зеркало по боковой оси корпуса.
        /// Поза пальцев — шаблон SDK (<see cref="ComputedSlideTemplate" />, <see cref="ComputedPumpTemplate" />); у позы Blend —
        /// смесь открытой и закрытой с <see cref="AvatarGripRegistration.PoseBlend" />, как её показывает запись хвата.
        /// Геометрия — <see cref="ComputeActionPalm" />.
        /// </summary>
        private static OrigPoint ComputedActionPoint(Item item, ActionReport report)
        {
            ActionSource a = item.Action;
            string templatePath = a.Pump ? ComputedPumpTemplate : ComputedSlideTemplate;
            var template = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(templatePath) ?? throw new FileNotFoundException($"нет позы-шаблона {templatePath}");
            UxrHandDescriptor Hand(UxrHandSide side)
            {
                if (template.PoseType == UxrHandPoseType.Fixed) return template.GetHandDescriptor(side);
                var hand = new UxrHandDescriptor();
                hand.CopyFrom(template.GetHandDescriptor(side, UxrBlendPoseType.OpenGrip));
                hand.InterpolateTo(template.GetHandDescriptor(side, UxrBlendPoseType.ClosedGrip), AvatarGripRegistration.PoseBlend);
                return hand;
            }

            report.source = ActionSourceComputed;
            report.check = ComputedCheck;
            report.computedTemplate = templatePath + (template.PoseType == UxrHandPoseType.Fixed ? "" : $" (Blend, смесь {AvatarGripRegistration.PoseBlend})");
            report.note = $"расчётная точка: {(report.note != null ? "в паке — " + report.note + "; " : "")}snap по геометрии {a.GripPrefabPart}, поза — шаблон " +
                          $"{Path.GetFileNameWithoutExtension(templatePath)}. Проверить глазами.";
            return new OrigPoint
            {
                Pack = item.Pack, Item = item.Name, Point = a.Point, Prefab = item.WeaponPath, BodyPath = a.PrefabPart, SnapRoot = a.Point,
                MirrorFramePath = a.MirrorFrame, MirrorSnaps = false, Experimental = true, SourceSide = UxrHandSide.Left, SourceKind = ActionSourceComputed,
                ComputedLeft = Hand(UxrHandSide.Left), ComputedRight = Hand(UxrHandSide.Right),
                Computed = root =>
                {
                    var palm = ComputeActionPalm(root, a, out string rule);
                    report.computedRule = rule;
                    return palm;
                }
            };
        }

        /// <summary>
        /// Рамка ладони левой руки на детали механики по геометрии префаба (мир содержимого префаба).
        /// <list type="bullet">
        /// <item>Ось хода <c>pull</c> (от покоя к оттянутой детали): ограничения перемещения граббабла детали
        /// (<c>TranslationLimitsMin + Max</c> в осях его родителя — тот ход, что поставила сборка), иначе
        /// <see cref="HandsPackWeaponRecipe.ActionTravelAxis" />, иначе назад по стволу (−Z корня).</item>
        /// <item>Оси оружия вокруг хода: верх <c>U</c> — Y корня без составляющей хода, бок <c>L</c> = pull × U (≈ +X корня).</item>
        /// <item>Опора — габарит меша ручки (<c>ActionGripPart</c>, иначе детали; нет меша — все меши граббабла) по осям pull/U/L.
        /// Затвор: задний торец (на <see cref="RearInset" /> внутрь, не больше 30 % длины) по ходу, верх, середина по боку; ладонь
        /// смотрит вниз. Помпа: середина по ходу, низ, середина по боку; ладонь смотрит вверх. Явный контакт ручки
        /// <see cref="HandsPackWeaponRecipe.ActionGripContact" /> (в осях меша ручки) заменяет опору, наружу — ближайшая из ±U, ±L
        /// к направлению от центра габарита к контакту.</item>
        /// <item>Центр ладони — опора + наружу × <see cref="PalmOffset" />; универсальные оси кисти: вверх (тыл) — наружу, вперёд
        /// (пальцы) — поперёк хода: ±(pull × наружу), к +L, а у бокового хвата — вниз.</item>
        /// </list>
        /// </summary>
        private static (Vector3 Position, Quaternion Rotation, string Problem) ComputeActionPalm(Transform root, ActionSource a, out string rule)
        {
            rule = null;
            Transform grabbable = root.Find(a.Point);
            if (grabbable == null) return (default, default, $"нет граббабла {a.Point}");

            Vector3 pull = Vector3.zero;
            string axis = null;
            if (grabbable.TryGetComponent(out UxrGrabbableObject g))
            {
                Vector3 travel = g.TranslationLimitsMin + g.TranslationLimitsMax;
                if (travel.sqrMagnitude > 1e-10f)
                {
                    pull = (grabbable.parent != null ? grabbable.parent : root).TransformDirection(travel);
                    axis = $"ход граббабла {travel.ToString("F3")}";
                }
            }

            if (pull.sqrMagnitude < 1e-10f && a.Recipe.ActionTravelAxis.HasValue)
            {
                pull = root.TransformDirection(a.Recipe.ActionTravelAxis.Value);
                axis = "ось хода рецепта";
            }

            if (pull.sqrMagnitude < 1e-10f)
            {
                pull = -root.forward;
                axis = "назад по стволу (хода нет)";
            }

            pull.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(root.up, pull);
            up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.ProjectOnPlane(root.forward, pull).normalized;
            Vector3 side = Vector3.Cross(pull, up).normalized;

            Transform grip = root.Find(a.GripPrefabPart) ?? root.Find(a.PrefabPart);
            MeshFilter[] filters = grip != null && grip.TryGetComponent(out MeshFilter own) && own.sharedMesh != null
                ? new[] { own }
                : grabbable.GetComponentsInChildren<MeshFilter>(true)
                           .Where(f => f.sharedMesh != null && !f.GetComponentsInParent<Transform>(true).Any(t => t.name == SnapFolder || t.name == "Grabs")).ToArray();
            if (filters.Length == 0) return (default, default, $"у {a.GripPrefabPart} и граббабла {a.Point} нет меша");

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (MeshFilter filter in filters)
            {
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = filter.transform.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y,
                                                                                  (i & 4) == 0 ? b.min.z : b.max.z));
                    var c = new Vector3(Vector3.Dot(corner, pull), Vector3.Dot(corner, up), Vector3.Dot(corner, side));
                    min = Vector3.Min(min, c);
                    max = Vector3.Max(max, c);
                }
            }

            Vector3 center = (min + max) * 0.5f;
            Vector3 World(Vector3 c) => pull * c.x + up * c.y + side * c.z;
            Vector3 contact, outward;
            string anchor;
            if (a.Recipe.ActionGripContact.HasValue && grip != null)
            {
                contact = grip.TransformPoint(a.Recipe.ActionGripContact.Value);
                Vector3 d = contact - World(center);
                float du = Vector3.Dot(d, up), ds = Vector3.Dot(d, side);
                outward = Mathf.Abs(du) < 1e-4f && Mathf.Abs(ds) < 1e-4f ? up : Mathf.Abs(du) >= Mathf.Abs(ds) ? up * Mathf.Sign(du) : side * Mathf.Sign(ds);
                anchor = $"контакт ручки рецепта {a.Recipe.ActionGripContact.Value.ToString("F3")}";
            }
            else if (a.Pump)
            {
                contact = World(new Vector3(center.x, min.y, center.z));
                outward = -up;
                anchor = "низ детали, середина по ходу";
            }
            else
            {
                float length = max.x - min.x;
                contact = World(new Vector3(max.x - Mathf.Min(RearInset, 0.3f * length), max.y, center.z));
                outward = up;
                anchor = "верх у заднего торца детали";
            }

            Vector3 fingers = Vector3.Cross(pull, outward).normalized;
            if (Mathf.Abs(Vector3.Dot(fingers, side)) > 0.5f ? Vector3.Dot(fingers, side) < 0f : Vector3.Dot(fingers, up) > 0f) fingers = -fingers;
            string support = grip != null && filters.Length == 1 ? GripExtractor.PathOf(filters[0].transform, root) : "меши граббабла";
            rule = $"ось хода — {axis}; опора — {anchor} ({support}, габарит по ходу/верху/боку {((max - min) * 1000f).ToString("F0")} мм); " +
                   $"ладонь в {PalmOffset * 1000f:F0} мм наружу, пальцы поперёк хода";
            return (contact + outward * PalmOffset, Quaternion.LookRotation(fingers, outward), null);
        }

        /// <summary>Окно длиннее (при равной длине — жёстче) прежнего лучшего — то же правило, что у магазина.</summary>
        private static bool Longer(Window candidate, Window best) =>
            candidate != null && (best == null || candidate.EndTime - candidate.StartTime > best.EndTime - best.StartTime + 1e-4f ||
                                  Mathf.Abs(candidate.EndTime - candidate.StartTime - (best.EndTime - best.StartTime)) <= 1e-4f && candidate.RigidMm < best.RigidMm);

        /// <summary>Клипы детали: у Hands — все клипы папки пака, у KINEMATION — все пары клипов рук и оружия.</summary>
        private static IEnumerable<(AnimationClip Clip, AnimationClip WeaponClip)> ActionClips(ActionSource a) =>
            a.Kin != null
                ? KinemationPairs(a.Kin, _ => true)
                : Clips(HandsPackWeapon.PackModels + a.Recipe.Folder + "/Animations").GroupBy(c => c.name).Select(PreferModel)
                                                                                      .OrderBy(c => c.name, StringComparer.Ordinal)
                                                                                      .Select(c => (Clip: c, WeaponClip: (AnimationClip)null));

        /// <summary>Риг детали: покой — клип покоя оружия (у Hands — <see cref="HandsPackWeaponRecipe.RestClip" />, если он есть в паке).</summary>
        private static ClipRig ActionRig(ActionSource a, AnimationClip clip, AnimationClip weaponClip) =>
            a.Kin != null
                ? new KinemationRig(a.Kin, clip, weaponClip, a.Part)
                : new HandsRig(HandsModel(a.Recipe.Folder), clip, a.Part, a.Recipe.BodyPart, HandsClipOrNull(a.Recipe.Folder, a.Recipe.RestClip));

        /// <summary>Кадр руки с деталью механики теми же экстракторами, что хват оружия: деталь — «корпус» кадра.</summary>
        private static HandsPackPoseExtractor.Sample ActionSample(ActionSource a, AnimationClip clip, AnimationClip weaponClip, float time, UxrHandSide side)
        {
            if (a.Kin != null)
            {
                return KinemationPoseExtractor.Extract(clip, time, side, PackPrefab(a.Kin), a.Part,
                                                       a.Kin.RestClip != null ? WeaponClip(a.Kin, a.Kin.RestClip) : null, weaponClip);
            }

            GameObject model = HandsModel(a.Recipe.Folder);
            SkinnedMeshRenderer part = SkinOf(model, a.Part);
            return HandsPackPoseExtractor.Extract(model, clip, time, side, part.sharedMesh.name, part.bones[MainBone(part)].name);
        }

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
            public float SeatRangeMm; // размах сдвига детали внутри окна (деталь механики: рука её ведёт, а не лежит рядом)
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
        /// от центра ладони до детали и сдвиг детали от её места на корпусе в первом кадре (<paramref name="rest" /> — от места
        /// покоя: деталь механики).
        /// </summary>
        private static List<Frame> ScanFrames(ClipRig rig, float length, float rate, Vector3? rest = null)
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
                if (i == 0) seat0 = rest ?? seat;
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
        private static List<Window> Windows(List<Frame> frames, UxrHandSide side, float near, float minSeat = MinSeat)
        {
            int s = side == UxrHandSide.Left ? 0 : 1;
            bool Holding(Frame f) => f.Palm[s] <= near && f.Seat >= minSeat;
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
                    float seatMin = float.MaxValue, seatMax = float.MinValue;
                    for (int k = i; k <= end; k++)
                    {
                        window.RigidMm = Mathf.Max(window.RigidMm, Vector3.Distance(frames[k].RelativePosition[s], m.RelativePosition[s]) * 1000f);
                        window.RigidDeg = Mathf.Max(window.RigidDeg, Quaternion.Angle(frames[k].RelativeRotation[s], m.RelativeRotation[s]));
                        seatMin = Mathf.Min(seatMin, frames[k].Seat);
                        seatMax = Mathf.Max(seatMax, frames[k].Seat);
                    }

                    window.SeatRangeMm = (seatMax - seatMin) * 1000f;

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
            public Vector3 RestSeat;                          // место детали на корпусе в покое (как сдвиг в ScanFrames)

            public abstract void Sample(float time);
            public abstract Matrix4x4 Part();
            public abstract Matrix4x4 Body();
            public abstract void Dispose();

            /// <summary>Место детали на корпусе сейчас: начало детали в жёсткой рамке корпуса (та же величина, что в ScanFrames).</summary>
            protected Vector3 SeatNow() => (Rigid(Body()).inverse * Rigid(Part())).GetColumn(3);

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

            /// <param name="model">Модель рук с оружием папки пака (<see cref="HandsModel" />)</param>
            /// <param name="body">Рендерер корпуса</param>
            /// <param name="rest">
            /// Клип покоя для <see cref="ClipRig.RestSeat" /> (деталь механики; как <see cref="HandsPackWeapon.Measure" />) — на
            /// отдельном экземпляре: кадры клипа снимаются от позы модели, как в экстракторе. null — поза модели.
            /// </param>
            public HandsRig(GameObject model, AnimationClip clip, string part, string body, AnimationClip rest = null)
            {
                _clip = clip;
                _instance = Object.Instantiate(model);
                _instance.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    Dictionary<string, Transform> bones = Bones(_instance);
                    SetupArms(s => HandsPackPoseExtractor.BuildArm(bones, s));
                    SkinnedMeshRenderer partRenderer = SkinOf(_instance, part);
                    SkinnedMeshRenderer bodyRenderer = SkinOf(_instance, body);
                    int partBone = MainBone(partRenderer), bodyBone = MainBone(bodyRenderer);
                    (_part, _partBind) = (partRenderer.bones[partBone], partRenderer.sharedMesh.bindposes[partBone]);
                    (_body, _bodyBind) = (bodyRenderer.bones[bodyBone], bodyRenderer.sharedMesh.bindposes[bodyBone]);
                    Points = MeshPoints(partRenderer.sharedMesh, -1, Matrix4x4.identity);
                    RestSeat = rest != null ? RestSeatOf(model, rest, part, body) : SeatNow();
                }
                catch
                {
                    Object.DestroyImmediate(_instance);
                    throw;
                }
            }

            private static Vector3 RestSeatOf(GameObject model, AnimationClip rest, string part, string body)
            {
                using var restRig = new HandsRig(model, rest, part, body);
                restRig.Sample(0f);
                return restRig.SeatNow();
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
                    // Меш пака без данных на CPU — вырезанный меш детали проекта: он в тех же осях кости без масштаба
                    // (KinemationWeapon.MeshOf), что Part().
                    if (Points.Length == 0) Points = SavedMeshPoints(k, part);
                    RestSeat = SeatNow();
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

            /// <summary>Кость детали — по правилу KinemationWeapon (<see cref="KinemationPoseExtractor.PartBone" />), не первый Transform с именем.</summary>
            private static Transform Find(GameObject root, string name) =>
                KinemationPoseExtractor.PartBone(root, name) ?? throw new KeyNotFoundException($"{root.name}: нет кости {name}");

            private static Vector3[] SavedMeshPoints(KinemationWeaponRecipe k, string part) =>
                MeshPoints(AssetDatabase.LoadAssetAtPath<Mesh>($"{KinemationWeapon.ArtRoot}/{k.Weapon.Name}/Meshes/{k.Weapon.Name}_{KinemationWeapon.Clean(part)}.asset"),
                           -1, Matrix4x4.identity);

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
        private static GameObject HandsModel(string packFolder)
        {
            string folder = HandsPackWeapon.PackModels + packFolder;
            string path = AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => !p.Contains("@"));
            return path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : throw new FileNotFoundException($"В {folder} нет модели без '@'");
        }

        /// <summary>
        /// Клип папки пака Hands по имени: <c>Modelas/&lt;папка&gt;/Animations</c>, иначе вся папка (как
        /// <see cref="HandsPackWeapon.Clip" />); одноимённые — take FBX.
        /// </summary>
        private static AnimationClip HandsClip(string packFolder, string name) =>
            HandsClipOrNull(packFolder, name) ?? throw new ArgumentException($"В {packFolder} нет клипа '{name}'");

        private static AnimationClip HandsClipOrNull(string packFolder, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (string folder in new[] { HandsPackWeapon.PackModels + packFolder + "/Animations", HandsPackWeapon.PackModels + packFolder })
            {
                AnimationClip[] clips = AssetDatabase.IsValidFolder(folder) ? Clips(folder).Where(c => c.name == name).ToArray() : Array.Empty<AnimationClip>();
                if (clips.Length > 0) return PreferModel(clips);
            }

            return null;
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

        [Serializable]
        private sealed class ActionReport
        {
            public string pack, item, weapon, point, part, prefabPart, mirrorFrame, source, check, note, warning;
            public string computedTemplate, computedRule, computedNote; // расчётная точка: шаблон позы, правило геометрии, сбой
            public float travelMm, minSeatMm, minRangeMm;
            public List<ActionScanReport> scan = new List<ActionScanReport>();
        }

        [Serializable]
        private sealed class ActionScanReport
        {
            public string clip, weaponClip, side, result, distance;
            public int frames, startFrame, endFrame;
            public float startTime, endTime, midTime, rigidMm, rigidDeg, palmMm, seatMm, seatRangeMm, minPalmMm, maxSeatMm;

            public static ActionScanReport Of(Window w, string result) =>
                new ActionScanReport
                {
                    clip = w.Clip.name, weaponClip = w.WeaponClip?.name, side = w.Side.ToString(), result = result, frames = w.Frames, startFrame = w.Start,
                    endFrame = w.End, startTime = Round(w.StartTime), endTime = Round(w.EndTime), midTime = Round(w.MidTime), rigidMm = Round(w.RigidMm),
                    rigidDeg = Round(w.RigidDeg), palmMm = Round(w.PalmMm), seatMm = Round(w.SeatMm), seatRangeMm = Round(w.SeatRangeMm)
                };
        }

        /// <summary>Отчёт без времени и статусов записи: меняется только вместе с источником.</summary>
        private static string WriteReport(Item item, SourceReport report) =>
            WriteJson($"{Root}/{item.Pack}/{item.Pack}_{item.Name}{Suffix}_sources.json", item.Pack, report);

        /// <summary>
        /// Отчёт поиска кадра детали механики — свой файл рядом с позой (<c>&lt;поза&gt;_sources.json</c>): схема отчётов
        /// предметов без деталей механики не меняется.
        /// </summary>
        private static string WriteActionReport(ActionReport report) =>
            WriteJson($"{Root}/{report.pack}/{OrigName(report.pack, report.item, report.point)}_sources.json", report.pack, report);

        private static string WriteJson(string path, string pack, object report)
        {
            KinemationWeapon.EnsureFolder($"{Root}/{pack}");
            string json = JsonUtility.ToJson(report, true).Replace("\r\n", "\n") + "\n";
            if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == json) return $"{path}: без изменений";
            File.WriteAllText(path, json, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            return $"{path}: записан";
        }
    }
}
