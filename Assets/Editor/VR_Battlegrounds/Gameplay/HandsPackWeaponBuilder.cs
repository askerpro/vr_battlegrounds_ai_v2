using System.Collections.Generic;
using System.Linq;
using Mirror;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Avatars;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Рецепт оружия из пака Hands Weapons Animations: что взять из пака и у каких префабов
    /// позаимствовать настройки. Всё, что можно вычислить, вычисляет <see cref="HandsPackWeaponBuilder" />.
    /// </summary>
    public sealed class HandsPackWeaponRecipe
    {
        /// <summary>Помпа (дробовик), затвор (автомат, пистолет, болтовка) или ничего (револьвер).</summary>
        public enum ActionKind { Pump, Slide, None }

        public string Name;
        public string PrefabFolder;    // Assets/Prefabs/Weapons/<PrefabFolder>
        public string Folder;          // папка оружия в паке: Hands_Shotgun
        public string BodyPart;        // рендерер корпуса
        public string BodyObjectName = "Base"; // стабильный путь корпуса у уже зарегистрированного оружия
        public string PoseClip;        // клип, где руки держат оружие: Aming_Idle
        public string ActionClip;      // клип, где ходят затвор/помпа и спуск: Shot
        public string RestClip = "Idle";
        public float TargetLength;     // габарит в метрах, по реальному прототипу
        public float FinalRootScale;   // 0 — без override; итоговый размер после расчёта authored child frames

        public string[] StaticParts;   // детали корпуса без движения
        public string[] ExcludedSourceParts; // FPS reload props вне игрового оружия; preflight обязан учесть явно
        public string MuzzlePart;      // деталь среза ствола: пламегаситель/глушитель; null — корпус
        public string TriggerPart;
        public string ActionPart;      // помпа или затвор; по нему меряется ход
        public string ActionGripPart;  // видимая ручка: независима от детали, по которой меряется ход
        public Vector3? ActionGripContact; // явный контакт в локальных осях меша ручки
        public Vector3? ActionTravelAxis; // явная ось линейной VR-тяги в осях готового корня; длина проецируется из источника
        public string[] ActionExtraParts; // детали, которые ходят вместе с затвором (рукоять затвора болтовки)
        public ActionKind Action;
        public string LoadingPart;     // деталь внешней точки приёма; null — приём на месте магазина пака
        public bool SupportGrip;       // вторая рука на корне (цевьё или поддержка пистолета) — точка 1 донора
        public UxrShotCycle? ShotCycle; // режим огня вместо донорского (болтовка — ManualReload: затвор после каждого выстрела)
        public bool Pellets;           // дробь без помпы (полуавтоматический дробовик); у помпового — всегда

        public string UxrTag;          // тег хвата корня: по нему карманы и слоты принимают оружие
        public string MagazineBase;    // шаблон магазина первой сборки (сеть, физика, звук); дальше шаблон — текущий магазин ствола
        public string MagazineName;    // прежнее имя ассета магазина; null — Name + "_mag"
        public string InteractionFolder; // ассеты областей подсветки; null — Art/Weapons/Interaction/<имя>
        public string MagazineGripDonor; // прежняя калибровка хвата MEF магазина; сборка его не читает (хваты аватаров не пишет)
        public string MagazinePart;    // меш магазина из пака
        public string[] MagazineExtraParts; // вынимаются вместе с магазином: патроны, гильзы барабана
        public string MagazineTag;     // тег хвата магазина: его принимают якорь и карманы
        public int MagazineCapacity;
        public bool MagazineIsInternal; // боезапас внутри корпуса: внешняя точка приёма отдельно от конечной позы
        public bool ManualLoading; // явный recipe opt-in, не вывод из имени/дроби/внутреннего магазина
        public string ManualLoadingProfile;
        // Явный профиль готовности съёмного магазина (патронник, ручной цикл, Empty-поза); пусто — legacy без ledger.
        public string ReadinessProfile;

        public string GripDonor;       // префаб, у которого берутся физика и поля хвата, якорь и затвор
        public string GripDonorBodyPath; // путь его корпуса в префабе
        // Папка, корпус и клип донора в паке — прежняя калибровка хвата MEF; сборка их не читает.
        public string GripDonorFolder; // его папка в паке
        public string GripDonorBody;   // рендерер его корпуса в паке
        public string GripDonorPoseClip;
        public string FirearmDonor;    // префаб, у которого берутся снаряд, звук выстрела, отдача
        public bool PreserveDonorGameplay; // пересборка существующего оружия: баланс и эффекты не заменяются defaults
        public uint NetworkAssetId;    // сетевой id зарегистрированного префаба до создания donor snapshot
        public uint MagazineAssetId;
        public string ShotAudio;       // свой звук выстрела вместо донорского (необязательно)
        public string LoadAudio;       // звук вставки патрона в окно приёма (ручное заряжание); магазины — общий звук
        public string TakeOutAudio;    // звук снятия магазина (необязательно; иначе донорский)
        public string SlideBackAudio;  // оттягивание затвора (необязательно; иначе донорский)
        public string SlideForwardAudio; // обратный ход затвора (необязательно; иначе донорский)
    }

    /// <summary>
    /// Собирает префаб оружия и его магазина по <see cref="HandsPackWeaponRecipe" />.
    ///
    /// <para>
    /// Геометрия и механика — из пака (<see cref="HandsPackWeapon" />): детали на точных местах,
    /// ход помпы/затвора и угол спуска из клипа. Настройки, которые не выводятся
    /// из пака (снаряд, звук, отдача, физика хвата), копируются у доноров по полям — без
    /// <c>_uxrUniqueId</c>, чтобы не делить id с донором.
    /// </para>
    ///
    /// <para>
    /// Записей хвата аватаров (<c>_avatarGripPoseEntries</c>) и их трансформов сборщик не пишет
    /// (решение 2026-10-22): точка отдачи, <see cref="MainGripAimLock" /> и области подсветки — по
    /// нейтральным точкам хвата, центрам ладоней рук пака (<see cref="PalmPoints" />). Хваты аватаров
    /// переносит <see cref="WeaponGripCarryOver" />, регистрирует отдельная команда.
    /// </para>
    ///
    /// <para>
    /// Сборщик делает только префабы. Регистрация — <c>WeaponInfo</c>, <c>spawnPrefabs</c>, теги,
    /// эталон размера — отдельные шаги скилла <c>/add-weapon</c>.
    /// </para>
    /// </summary>
    public static class HandsPackWeaponBuilder
    {
        private const string Tracer = "Assets/Prefabs/Weapons/Effects/Tracer_Default.prefab";
        private const string ImpactEffect = "Assets/Prefabs/Weapons/Effects/Impact_Concrete.prefab";
        private const string ImpactDecal = "Assets/Prefabs/Weapons/Effects/ImpactDecal_Concrete.prefab";
        private const string MuzzleEffect = "Assets/Prefabs/Weapons/Effects/Muzzle_Default.prefab";

        public static HandsPackWeaponRecipe ShotgunReal => new HandsPackWeaponRecipe
        {
            Name              = "FabarmSDASS",
            PrefabFolder      = "FabarmSDASS",
            MagazineName      = "FabarmSDASS_Ammo",
            Folder            = "Hands_Shotgun",
            BodyPart          = "Shogun_Base_mesh",
            PoseClip          = "Aming_Idle",
            ActionClip        = "Shot",
            TargetLength      = 0.72f, // Сохраняем принятый игровой размер FABARM SDASS; точный вариант не установлен.
            StaticParts       = new[] { "Shogun_Staple_mesh", "Shogun_Gate_mesh" },
            TriggerPart       = "Shogun_Triger_mesh",
            ActionPart        = "Shogun_Fore-End_mesh",
            Action            = HandsPackWeaponRecipe.ActionKind.Pump,
            LoadingPart       = "Shogun_Staple_mesh",
            UxrTag            = "Shotgun",
            MagazineBase      = MagShotgunBase,
            MagazinePart      = "Shogun_Patron_mesh",
            MagazineTag       = "MagShotgun",
            MagazineCapacity  = 8,
            MagazineIsInternal = true,
            ManualLoading = true,
            ManualLoadingProfile = "Assets/Data/Weapons/Profiles/ManualLoadFabarmReadiness.asset",
            GripDonor         = "Assets/Prefabs/Weapons/AR15/AR15.prefab",
            GripDonorFolder   = "Hands_Automatic_Rifle03",
            GripDonorBody     = "Rifle_Body_Mesh",
            GripDonorBodyPath = "MeshContainer/Rifle_Body_Mesh",
            GripDonorPoseClip = "Aim_Idle",
            // Сэмпл SDK Shotgun убран из арсенала (2026-10-20, Assets/Unused/Weapons): донор спуска — неизменный снимок
            // самого FABARM, как и в HandsPackLegacyRebuild (recipe.FirearmDonor = Donor(...)).
            FirearmDonor      = "Assets/Art/Weapons/HandsPack/LegacyBuildDonors/Shotgun_real.prefab",
            ShotAudio         = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/10Shotgun_Set/edit/shot.mp3",
            LoadAudio         = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/10Shotgun_Set/Reload_1.mp3"
        };

        private static void BuildShotgunReal()
        {
            VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenWeapon(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Build, "Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab");
            VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenMaintenance(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Build);
        }

        // ── Оружие T-38 ─────────────────────────────────────────────────────────
        // Длина — по руке пака: длина в паке × 0,707 (как у Gun_real), чтобы хват из клипа пака
        // ложился в ладонь. Доноры: длинноствольное — M16 (хват, снаряд, автоматический огонь),
        // пистолеты — Gun_real. Позы кисти снимаются отдельно — HandsPackPoseImporter.Recipes.

        private const string M16 = "Assets/Prefabs/Weapons/AR15/AR15.prefab";
        private const string GunReal = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower.prefab";
        // Шаблон магазина первой сборки нового ствола — готовый магазин паков того же типа (базы SDK удалены 2026-10-22).
        private const string MagRifleBase = "Assets/Prefabs/Weapons/Scar/Scar_mag.prefab";
        private const string MagPistolBase = "Assets/Prefabs/Weapons/PPK/PPK_mag.prefab";
        internal const string MagShotgunBase = "Assets/Prefabs/Weapons/Herrington/Herrington_mag.prefab";

        internal static HandsPackWeaponRecipe RifleDonor(HandsPackWeaponRecipe r)
        {
            r.GripDonor = M16;
            r.GripDonorFolder = "Hands_Automatic_Rifle03";
            r.GripDonorBody = "Rifle_Body_Mesh";
            r.GripDonorBodyPath = "MeshContainer/Rifle_Body_Mesh";
            r.GripDonorPoseClip = "Aim_Idle";
            r.FirearmDonor = M16;
            r.MagazineBase = MagRifleBase;
            r.Action = HandsPackWeaponRecipe.ActionKind.Slide;
            return r;
        }

        internal static HandsPackWeaponRecipe PistolDonor(HandsPackWeaponRecipe r)
        {
            r.GripDonor = GunReal;
            r.GripDonorFolder = "Hands_Gun";
            r.GripDonorBody = "Base_mesh";
            r.GripDonorBodyPath = "MeshContainer/Base";
            r.GripDonorPoseClip = "Aiming_Idle";
            r.FirearmDonor = GunReal;
            r.MagazineBase = MagPistolBase;
            r.Action = HandsPackWeaponRecipe.ActionKind.Slide;
            return r;
        }

        /// <summary>Автомат-карабин (роль AK-47): SCAR-L CQC.</summary>
        public static HandsPackWeaponRecipe Scar => RifleDonor(new HandsPackWeaponRecipe
        {
            Name = "Scar", PrefabFolder = "Scar", Folder = "Hands_Automatic_Rifle01", BodyPart = "Scar_Base_mesh",
            PoseClip = "Aiming_Idle", ActionClip = "Singl_Shot", RestClip = "idle",
            TargetLength = 0.635f, // SCAR-L CQC: 63.5 см; по руке пака 64.5
            StaticParts = new[] { "Scar_Detail_01_mesh", "Scar_Detail_02_mesh", "Scar_Detail_03_mesh", "Scar_Planck_mesh" },
            TriggerPart = "Scar_Triger_mesh", ActionPart = "Scar_Grip_mesh", SupportGrip = true,
            UxrTag = "BackWeapon", MagazinePart = "Scar_Magazine_mesh", MagazineTag = "MagScar", MagazineCapacity = 30
        });

        /// <summary>Пистолет-пулемёт одной рукой (роль MAC-10): Uzi. Вторая рука пака не на оружии — точки поддержки нет.</summary>
        public static HandsPackWeaponRecipe Uzi
        {
            get
            {
                HandsPackWeaponRecipe r = RifleDonor(new HandsPackWeaponRecipe
                {
                    Name = "Uzi", PrefabFolder = "Uzi", Folder = "Hands_Tommy_gun", BodyPart = "body_Mesh",
                    PoseClip = "Aiming_Idle", ActionClip = "Shot",
                    TargetLength = 0.35f, // Mini-Uzi со сложенным прикладом: 36 см; по руке пака 35
                    StaticParts = new string[0],
                    TriggerPart = "Trigger_mesh", ActionPart = "Reload_frame_mesh", SupportGrip = false,
                    UxrTag = "Gun", MagazinePart = "Magazine_Mesh", MagazineTag = "MagUzi", MagazineCapacity = 30
                });
                r.MagazineBase = MagPistolBase;
                return r;
            }
        }

        /// <summary>Пистолет-пулемёт (роль MP9): HK MP5K. Затвор в выстреле пака неподвижен — ход рукоятки взведения из перезарядки.</summary>
        public static HandsPackWeaponRecipe MP5K => RifleDonor(new HandsPackWeaponRecipe
        {
            Name = "MP5K", PrefabFolder = "MP5K", Folder = "Hands_Automatic_Rifle04", BodyPart = "Rifle04_Body_Mesh",
            PoseClip = "Idle_Aim", ActionClip = "Reload",
            TargetLength = 0.33f, // HK MP5K: 32.5 см
            StaticParts = new string[0],
            TriggerPart = "Rifle04_Trigger_Mesh", ActionPart = "Rifle04_Detail_Mesh", ActionTravelAxis = Vector3.back, SupportGrip = true,
            UxrTag = "BackWeapon", MagazinePart = "Rifle04_Magazine_Mesh", MagazineTag = "MagMP5K", MagazineCapacity = 30
        });

        /// <summary>Стартовый пистолет (роль Glock): Walther PPK. Патроны — часть магазина.</summary>
        public static HandsPackWeaponRecipe PPK => PistolDonor(new HandsPackWeaponRecipe
        {
            Name = "PPK", PrefabFolder = "PPK", Folder = "Hands_Gun02", BodyPart = "Gun02_Body_Mesh",
            PoseClip = "Aim_Idle", ActionClip = "Shot",
            TargetLength = 0.20f, // PPK 15.5 см, но модель пака крупнее: по руке пака 20 см
            StaticParts = new[] { "Gun02_Detail_02_Mesh" },
            TriggerPart = "Gun02_Trigger_Mesh", ActionPart = "Gun02_Detail_01_Mesh", SupportGrip = true,
            UxrTag = "Gun", MagazinePart = "Gun02_Magazine_Mesh", MagazineExtraParts = new[] { "Gun02_Bulets_Mesh" },
            MagazineTag = "MagPPK", MagazineCapacity = 20
        });

        /// <summary>
        /// Тяжёлый пистолет (роль R8): револьвер. Затвора нет, «магазин» — барабан с шестью патронами в каморах
        /// (гильзы пака <c>Gun_03_Sleeve_Mesh*</c> стоят в каморах и видны с казны), вставляется в гнездо
        /// барабана целиком. Наконечники <c>Gun_03_Bulet_Mesh*</c> висят на ускорителе заряжания в левой
        /// руке пака, не в барабане, — не берутся. Прицел (<c>Gun_03_Aim_Mesh</c>, <c>Glasses</c>) снят.
        /// </summary>
        public static HandsPackWeaponRecipe Revolver
        {
            get
            {
                HandsPackWeaponRecipe r = PistolDonor(new HandsPackWeaponRecipe
                {
                    Name = "Revolver", PrefabFolder = "Revolver", Folder = "Hands_Gun_03", BodyPart = "Gun_03_Body_Mesh",
                    PoseClip = "Idle_Aiming", ActionClip = "Shot",
                    TargetLength = 0.35f, // Raging Bull 6.5": ~31 см; по руке пака 35
                    StaticParts = new[] { "Gun_03_Drum_Detail_Mesh", "Gun_03_Striker_Mesh" },
                    TriggerPart = "Gun_03_Trigger_Mesh", SupportGrip = true,
                    UxrTag = "Gun", MagazinePart = "Gun_03_Drum_Mesh",
                    MagazineExtraParts = new[] { "Gun_03_Sleeve_Mesh", "Gun_03_Sleeve_Mesh001", "Gun_03_Sleeve_Mesh004",
                                                 "Gun_03_Sleeve_Mesh005", "Gun_03_Sleeve_Mesh006", "Gun_03_Sleeve_Mesh007" },
                    MagazineTag = "MagRevolver", MagazineCapacity = 6
                });
                r.Action = HandsPackWeaponRecipe.ActionKind.None;
                // Выстрел пака Hands (9Run_Set/Shot 4) — тот же, что у лёгких пистолетов, и звучал «как с глушителем»;
                // свой громкий — выстрел револьвера R08 из пака KINEMATION (KinemationWeaponBuilder.MakeRevolverShot).
                r.ShotAudio = KinemationWeaponBuilder.RevolverShotPath;
                return r;
            }
        }

        /// <summary>
        /// Тяжёлая винтовка (роль SSG 08), заглушка: болтовой механики у сборщика нет — затвор идёт по прямой,
        /// как у автомата (рукоять не поднимается), но огонь ручной: затвор после каждого выстрела.
        /// </summary>
        public static HandsPackWeaponRecipe SniperRifle
        {
            get
            {
                HandsPackWeaponRecipe r = RifleDonor(new HandsPackWeaponRecipe
                {
                    Name = "SniperRifle", PrefabFolder = "SniperRifle", Folder = "Hands_Sniper_Rifle", BodyPart = "Sniper_Rifle_Base_Mesh",
                    PoseClip = "Aiming_Idle", ActionClip = "Shot", RestClip = "Idel",
                    TargetLength = 1.15f, // AX-50: ~123 см; по руке пака 115
                    StaticParts = new string[0],
                    TriggerPart = "Sniper_Rifle_Triger_Mesh", ActionPart = "Sniper _Rifle_Gate_End_Mesh",
                    ActionExtraParts = new[] { "Sniper _Rifle_Gate_Mesh" }, SupportGrip = true,
                    UxrTag = "BackWeapon", MagazinePart = "Sniper_Rifle_Magazine_Mesh", MagazineTag = "MagSniper", MagazineCapacity = 10
                });
                r.ShotCycle = UxrShotCycle.ManualReload;
                return r;
            }
        }

        public static IEnumerable<HandsPackWeaponRecipe> T38 => new[] { Scar, Uzi, MP5K, PPK, Revolver, SniperRifle };

        private static void BuildT38() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenBuildSet(T38.Select(r => $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.Name}.prefab"));

        /// <summary>
        /// Полный маршрут одного T-38: сборка (точка отдачи и области хвата — по центрам ладоней пака,
        /// <see cref="PalmPoints" />) → перенос хватов из текущего префаба → подсветка магазина → прицел, хост
        /// WeaponSystem, баланс (<see cref="WeaponRebuildFinish" />) → исходные позы пака и snap-точки <c>_orig</c>
        /// (<see cref="PackOrigPoses" />). Позы и хваты аватаров не регистрируются — отдельная команда
        /// (<see cref="HandsPackPoseImporter.ImportFor" />, Grip Extraction). <paramref name="poses" /> — только
        /// <see cref="WeaponGripPoseMode.KeepCurrent" />.
        /// </summary>
        public static void BuildFull(HandsPackWeaponRecipe r, WeaponGripPoseMode poses = WeaponGripPoseMode.KeepCurrent)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Пересборка запрещена в Play Mode.");
            WeaponGripCarryOver.RequireBuilderMode(poses, "HandsPackWeaponBuilder");
            string path = $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.Name}.prefab";
            string magazine = $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.MagazineName ?? r.Name + "_mag"}.prefab";
            // Области: Interaction/<имя> (у стволов KINEMATION — папка ствола KINEMATION).
            var outputs = new List<string> { "Assets/Art/Weapons/Interaction/" + r.Name, KinemationFixReview.InteractionFolder(r.Name) };
            // Всё или ничего: отказ любого шага, включая прицел и _orig, возвращает файлы ствола к состоянию до сборки.
            WeaponRebuildFinish.Run("HandsPackWeaponBuilder", path, magazine, outputs, () =>
            {
                // Хваты снимаются до сборки: она перезаписывает оружие и магазин.
                WeaponGripCarryOver.Snapshot grips = WeaponGripCarryOver.Capture(path, magazine);
                Build(r);
                WeaponGripCarryOver.Restore(grips, poses, MagazineBaseGeometry(r), SdkSampleAvatars);
                // Подсветка выдаваемого отдельно магазина. Области хвата оружия сборка уже испекла по центрам ладоней
                // пака — перенесённые хваты аватаров их не меняют.
                WeaponInteractionReview.Apply(System.Array.Empty<string>(), new[] { magazine });
            });
        }

        /// <summary>
        /// Нейтральные точки хвата в осях корня <paramref name="root" />: центр ладони руки пака в кадре позы
        /// (определение SDK <c>UxrAvatarHand.GetPalmCenter</c>). [0] — правая рука на рукояти (точка 0), [1] — левая,
        /// вторая рука (точка 1). Та же величина, что начало snap-точек <c>Orig/&lt;поза&gt;_orig/Right|Left</c>
        /// (<see cref="PackOrigPoses" />, <see cref="GripSample.PalmInBody" />). От аватаров не зависит: по ним сборка
        /// ставит точку отдачи, решает о <see cref="MainGripAimLock" /> и печёт области подсветки
        /// (<see cref="WeaponInteractionRecipes" />). <paramref name="body" /> — корпус в префабе, тот же меш, что в паке;
        /// <paramref name="poseClip" /> — клип пака Hands (у KINEMATION клип позы задаёт модель).
        /// </summary>
        internal static Vector3[] PalmPoints(IWeaponModel pack, string poseClip, Transform root, Transform body)
        {
            Vector3 Palm(UxrHandSide side)
            {
                if (pack is IHandGripSource frames)
                {
                    HandsPackPoseExtractor.Sample sample = frames.GripSample(side);
                    GripSample.FromPack(sample, side, PackClipGripSource.Kinemation)
                              .PalmInBody(side, body.lossyScale.x / sample.BodyScale, out Vector3 position, out _);
                    return root.InverseTransformPoint(body.position + body.rotation * position);
                }

                return root.InverseTransformPoint(body.TransformPoint(((HandsPackWeapon)pack).PalmCenterInBody(side == UxrHandSide.Right)));
            }

            var hands = pack as HandsPackWeapon;
            hands?.Sample(null);
            hands?.Sample(poseClip);
            try
            {
                return new[] { Palm(UxrHandSide.Right), Palm(UxrHandSide.Left) };
            }
            finally
            {
                hands?.Sample(null);
            }
        }

        public static GameObject Build(HandsPackWeaponRecipe r)
        {
            using var pack = new HandsPackWeapon(r.Folder, r.BodyPart);
            return Build(r, pack);
        }

        /// <summary>
        /// Сборка из любого источника геометрии (<see cref="IWeaponModel" />): пак Hands или KINEMATION
        /// (<see cref="KinemationWeaponBuilder" />). <see cref="HandsPackWeaponRecipe.TargetLength" /> 0 — модель уже
        /// в реальном масштабе (KINEMATION), корень ×1.
        /// </summary>
        /// <param name="configureHost">
        /// false — хост WeaponSystem (и трубка ручного заряжания) не настраивается: клипы затвора ставит следующий шаг
        /// (KINEMATION — <see cref="KinemationFixReview" />, набор Hands — <c>HandsPackLegacyRebuild.InstallMotion</c>),
        /// и вызывающий настраивает хост после них (<see cref="ConfigureHost(string, HandsPackWeaponRecipe)" />). Без клипов
        /// профиль HoldOpen не настраивается (отказ «затвор без клипов»).
        /// </param>
        public static GameObject Build(HandsPackWeaponRecipe r, IWeaponModel pack, bool configureHost = true)
        {
            if (r.ManualLoading && AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(r.ManualLoadingProfile) == null)
                throw new System.InvalidOperationException("Не задан явный профиль tube reload: " + r.ManualLoadingProfile);
            if (!string.IsNullOrEmpty(r.ReadinessProfile) && (r.ManualLoading || AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(r.ReadinessProfile) == null))
                throw new System.InvalidOperationException("Профиль готовности не найден или конфликтует с ManualLoading: " + r.ReadinessProfile);
            if (r.FinalRootScale < 0f || float.IsNaN(r.FinalRootScale) || float.IsInfinity(r.FinalRootScale))
                throw new System.InvalidOperationException("Некорректный итоговый масштаб корня: " + r.Name);
            string folder = $"Assets/Prefabs/Weapons/{r.PrefabFolder}";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Prefabs/Weapons", r.PrefabFolder);

            Scene scene = EditorSceneManager.NewPreviewScene();

            try
            {
                // Масштаб корня: габарит корпуса в единицах меша → реальная длина.
                Mesh body = pack.MeshOf(r.BodyPart);
                float scale = r.TargetLength > 0f ? r.TargetLength / Mathf.Max(body.bounds.size.x, body.bounds.size.y, body.bounds.size.z) : 1f;

                Matrix4x4 bodyLocal = BodyPlacement(r, pack);

                GameObject mag = BuildMagazine(r, pack, bodyLocal, scale, folder, scene);
                GameObject weapon = BuildWeapon(r, pack, bodyLocal, scale, scene, mag, configureHost);

                ImpactSoundInstaller.Apply(weapon, false);
                // Override применяется последним: geometry/grab/ray child frames сохраняют authored local TRS.
                if (r.FinalRootScale > 0f) weapon.transform.localScale = Vector3.one * r.FinalRootScale;
                string path = $"{folder}/{r.Name}.prefab";
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(weapon, path);
                GameLog.Debug.Info($"[HandsPackWeaponBuilder] {path}: масштаб ×{weapon.transform.localScale.x:F3}, магазин {AssetDatabase.GetAssetPath(mag)}");
                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // ── Оружие ──────────────────────────────────────────────────────────────

        /// <summary>Общая для preflight и сборки система координат: ствол +Z, верх +Y, начало у спуска.</summary>
        internal static Matrix4x4 BodyPlacement(HandsPackWeaponRecipe r, IWeaponModel pack)
        {
            (Vector3 forward, Vector3 up) = pack.AimAxes(r.PoseClip);
            pack.Sample(null);
            Matrix4x4 rotation = Matrix4x4.Rotate(Quaternion.Inverse(Quaternion.LookRotation(forward, up)));
            Vector3 trigger = (rotation * pack.PartInBody(r.TriggerPart)).GetColumn(3);
            return Matrix4x4.Translate(new Vector3(0f, -trigger.y, -trigger.z)) * rotation;
        }

        /// <summary>Нулевой ход FBX не отменяет ручную механику immutable донора существующего оружия.</summary>
        internal static Vector3 ResolveActionTravel(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal, out bool usesDonor)
        {
            Vector3 travel = bodyLocal.MultiplyVector(pack.Measure(r.ActionPart, r.ActionClip, r.RestClip).Far);
            if (r.ActionTravelAxis.HasValue)
            {
                Vector3 axis = r.ActionTravelAxis.Value;
                if (!Finite(axis) || axis.sqrMagnitude < 1e-10f || MovingAxes(axis) != 1)
                    throw new System.InvalidOperationException($"{r.Name}: ось линейной VR-тяги должна иметь одну ненулевую координату.");
                // FPS Reload у MP5K также поднимает/поворачивает ручку в парковочную позу.
                // SDK ограничивает XYZ независимо, а feedback/возврат имеют одну ось: боковой box недопустим.
                travel = Vector3.Project(travel, axis);
                if (!Finite(travel) || travel.sqrMagnitude < 1e-10f)
                    throw new System.InvalidOperationException($"{r.Name}: исходный клип не содержит тяги по заданной оси.");
            }
            usesDonor = r.PreserveDonorGameplay && travel.magnitude < 1e-5f;
            if (!usesDonor) return travel;

            GameObject donor = AssetDatabase.LoadAssetAtPath<GameObject>(r.GripDonor);
            if (donor == null) throw new System.InvalidOperationException($"{r.Name}: нет immutable action donor: {r.GripDonor}");
            var main = donor.GetComponent<UxrGrabbableObject>();
            UxrGrabbableObject[] candidates = donor.GetComponentsInChildren<UxrGrabbableObject>(true)
                .Where(g => g != main && g.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Length == 0).ToArray();
            Transform body = donor.transform.Find(r.GripDonorBodyPath);
            if (candidates.Length != 1 || body == null || candidates[0].transform.parent == null)
                throw new System.InvalidOperationException($"{r.Name}: некорректный immutable action/body: {r.GripDonor}, {r.GripDonorBodyPath}");
            var action = candidates[0];
            if (!AutomaticWeaponSlideFeedback.TryGetSlideTravel(action, out Vector3 direction, out float length) ||
                !Finite(direction) || !Finite(length) || !Finite(action.TranslationLimitsMin) || !Finite(action.TranslationLimitsMax) ||
                (action.TranslationLimitsMin.sqrMagnitude > 1e-10f && action.TranslationLimitsMax.sqrMagnitude > 1e-10f) ||
                MovingAxes(direction * length) != 1)
                throw new System.InvalidOperationException($"{r.Name}: FBX не содержит хода {r.ActionPart}, donor не задаёт односторонний ручной ход от нуля: {r.GripDonor}");

            // SDK clamps в initial local-space и возвращает через InitialRelativeMatrix; reciprocalScale
            // компенсирует localScale action. В parent-space остаётся initial rotation, затем масштаб родителя.
            // Через корпус возвращаем в source body-space, затем в parent-space нового action (корень).
            Vector3 inBody = body.InverseTransformVector(action.transform.parent.TransformVector(action.transform.localRotation * (direction * length)));
            travel = bodyLocal.MultiplyVector(inBody);
            if (!Finite(travel) || travel.magnitude < 1e-5f || MovingAxes(travel) != 1)
                throw new System.InvalidOperationException($"{r.Name}: перенос immutable ручного хода в новый корень дал некорректный вектор: {travel}");
            return travel;
        }

        internal static (int Axis, float Degrees, bool UsesDonor) ResolveTriggerRotation(HandsPackWeaponRecipe r, PartMotion motion)
        {
            if (!r.PreserveDonorGameplay || Mathf.Abs(motion.MaxAngle) >= 1e-5f)
                return (DominantAxis(motion.RotationAxis), Mathf.Round(motion.MaxAngle) * Mathf.Sign(AxisComponent(motion.RotationAxis)), false);
            GameObject donor = AssetDatabase.LoadAssetAtPath<GameObject>(r.FirearmDonor);
            var firearm = donor == null ? null : donor.GetComponent<UxrFirearmWeapon>();
            if (firearm == null) throw new System.InvalidOperationException($"{r.Name}: нет immutable firearm donor: {r.FirearmDonor}");
            var triggers = new SerializedObject(firearm).FindProperty("_triggers");
            if (triggers.arraySize == 0) throw new System.InvalidOperationException($"{r.Name}: immutable donor не содержит спуска: {r.FirearmDonor}");
            var trigger = triggers.GetArrayElementAtIndex(0);
            int axis = trigger.FindPropertyRelative("_triggerRotationAxis._axis").intValue;
            float degrees = trigger.FindPropertyRelative("_triggerRotationDegrees").floatValue;
            if (axis < 0 || axis > 2 || !Finite(degrees) || Mathf.Abs(degrees) < 1e-5f)
                throw new System.InvalidOperationException($"{r.Name}: FBX не содержит поворота спуска, immutable donor не задаёт корректный угол/ось: {r.FirearmDonor}");
            return (axis, degrees, true);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        // RestrictLocalOffset задаёт box. Диагональный fallback расширил бы старый одномерный ход до box.
        private static int MovingAxes(Vector3 value) => (Mathf.Abs(value.x) >= 1e-5f ? 1 : 0) +
            (Mathf.Abs(value.y) >= 1e-5f ? 1 : 0) + (Mathf.Abs(value.z) >= 1e-5f ? 1 : 0);

        private static GameObject BuildWeapon(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal, float scale, Scene scene, GameObject magPrefab,
                                              bool configureHost)
        {
            var root = new GameObject(r.Name);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.localScale = Vector3.one * scale;

            GameObject gripDonor = AssetDatabase.LoadAssetAtPath<GameObject>(r.GripDonor);
            GameObject fireDonor = AssetDatabase.LoadAssetAtPath<GameObject>(r.FirearmDonor);

            var rb = root.AddComponent<Rigidbody>();
            CopyFields(gripDonor.GetComponent<Rigidbody>(), rb);
            var grabbable = root.AddComponent<UxrGrabbableObject>();
            var projectile = root.AddComponent<UxrProjectileSource>();
            var firearm = root.AddComponent<UxrFirearmWeapon>();
            var identity = root.AddComponent<NetworkIdentity>();
            if (r.PreserveDonorGameplay)
            {
                CopyFields(gripDonor.GetComponent<NetworkIdentity>(), identity);
                var network = new SerializedObject(identity);
                network.FindProperty("_assetId").longValue = r.NetworkAssetId;
                network.ApplyModifiedPropertiesWithoutUndo();
            }

            // Детали
            var container = new GameObject("MeshContainer").transform;
            container.SetParent(root.transform, false);
            var names = new Dictionary<string, string> { { r.BodyPart, r.BodyObjectName } };
            foreach (string p in r.StaticParts ?? new string[0]) names[p] = Clean(p);
            names[r.TriggerPart] = "Trigger";
            Dictionary<string, Transform> parts = pack.BuildParts(container, bodyLocal, names);
            parts[r.BodyPart].gameObject.AddComponent<MeshCollider>().convex = true;

            // Помпа/затвор — отдельный граббабл у корня, как Recoil у M16. У револьвера его нет.
            PartMotion triggerMotion = pack.Measure(r.TriggerPart, r.ActionClip, r.RestClip);
            if (r.Action != HandsPackWeaponRecipe.ActionKind.None)
                BuildAction(r, pack, bodyLocal, root, gripDonor, fireDonor);

            root.AddComponent<AnchoredItemCollisionIgnore>();
            root.AddComponent<OutOfWorldGuard>();

            // Ствол: дуло — передний край корпуса, ось — центр кольца вершин у дула (у помпового —
            // верхнего: ниже ствола подствольный магазин).
            // С отдельным пламегасителем/глушителем дуло — его срез (MuzzlePart).
            string muzzlePart = r.MuzzlePart ?? r.BodyPart;
            Bounds muzzle = MuzzleRing(pack.MeshOf(muzzlePart), parts[muzzlePart], r.Action == HandsPackWeaponRecipe.ActionKind.Pump || r.Pellets);
            var tip = Empty("Tip", container, new Vector3(muzzle.center.x, muzzle.center.y, muzzle.max.z));
            // Снаряд рождается у дула, на 1 см позади Tip: из глубины коробки след трассера
            // выходит сбоку от ствола при отдаче и движении руки (ShotOriginTests).
            var shotSource = Empty("ShotSource", container, new Vector3(muzzle.center.x, muzzle.center.y, muzzle.max.z - 0.01f / scale));

            // Ствол в стене не стреляет: проверка от казённой части (на уровне спуска, на оси
            // ствола) до среза (BarrelObstructionTests).
            var breech = Empty("BarrelCheck", container, new Vector3(muzzle.center.x, muzzle.center.y, parts[r.TriggerPart].localPosition.z));
            var obstruction = new SerializedObject(root.AddComponent<BarrelObstruction>());
            obstruction.FindProperty("_breech").objectReferenceValue = breech;
            obstruction.ApplyModifiedPropertiesWithoutUndo();

            // Якорь магазина, магазин вложен и вставляется в Awake.
            Vector3 anchorPosition;
            if (r.LoadingPart != null)
            {
                // Доступная снаружи точка приёма у окна заряжания; конечный alignment внутреннего
                // патрона задаётся отдельно по геометрии окна после создания вложенного магазина.
                Transform loading = parts[r.LoadingPart];
                Vector3 loadingCenter = container.InverseTransformPoint(loading.TransformPoint(loading.GetComponent<MeshFilter>().sharedMesh.bounds.center));
                Bounds magBounds = magPrefab.GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds;
                float magHalfHeight = Mathf.Min(magBounds.size.x, magBounds.size.y, magBounds.size.z) * 0.5f;
                anchorPosition = loadingCenter + Vector3.down * magHalfHeight;
            }
            else
            {
                // Съёмный магазин/барабан: якорь — ровно на месте детали в паке (центр её меша);
                // меш магазина в префабе отцентрован в том же месте (BuildMagazine).
                anchorPosition = MagazineCenter(r, pack, bodyLocal);
            }

            var anchorGo = Empty("MagAnchor", container, anchorPosition).gameObject;
            var anchor = anchorGo.AddComponent<UxrGrabbableObjectAnchor>();
            var anchorSo = new SerializedObject(anchor);
            CopyFields(gripDonor.GetComponentInChildren<UxrGrabbableObjectAnchor>(true), anchor, anchorSo);
            anchorSo.FindProperty("_compatibleTags").arraySize = 1;
            anchorSo.FindProperty("_compatibleTags").GetArrayElementAtIndex(0).stringValue = r.MagazineTag;
            foreach (string field in new[] { "_alignTransform", "_dropProximityTransform", "_activateOnCompatibleNear", "_activateOnPlaced", "_activateOnCompatibleNotNear", "_activateOnHandNearAndGrabbable", "_activateOnEmpty", "_grabProxy" })
                anchorSo.FindProperty(field).objectReferenceValue = null;
            anchorSo.FindProperty("_alignTransformUseSelf").boolValue = true;
            anchorSo.FindProperty("_dropProximityTransformUseSelf").boolValue = true;
            anchorSo.ApplyModifiedPropertiesWithoutUndo();
            // Источник со звуком вставки — копия донорского (AnchorSound играет его по Placed).
            // Не на Activate On Placed и без Play On Awake: AnchorActivationAudioTests.
            var source = anchorGo.AddComponent<AudioSource>();
            EditorUtility.CopySerialized(gripDonor.GetComponentInChildren<UxrGrabbableObjectAnchor>(true).GetComponent<AudioSource>(), source);
            source.playOnAwake = false;
            // Своего клипа у гнезда магазина нет — общий MagazineAnchorSoundDefaults. LoadAudio — только окну приёма
            // патронов (ручное заряжание, ManualLoadingAuthoring).
            source.clip = null;
            var sound = new SerializedObject(anchorGo.AddComponent<AnchorSound>());
            CopyFields(gripDonor.GetComponentInChildren<AnchorSound>(true), sound.targetObject, sound);
            sound.FindProperty("_source").objectReferenceValue = source;
            if (!string.IsNullOrEmpty(r.TakeOutAudio))
                sound.FindProperty("_takeOutClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(r.TakeOutAudio);
            sound.ApplyModifiedPropertiesWithoutUndo();

            var nested = (GameObject)PrefabUtility.InstantiatePrefab(magPrefab, anchorGo.transform);
            nested.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            nested.transform.localScale = Vector3.one;
            Object.DestroyImmediate(nested.GetComponent<NetworkIdentity>()); // Mirror не допускает вложенных
            var nestedGrab = new SerializedObject(nested.GetComponent<UxrGrabbableObject>());
            nestedGrab.FindProperty("_startAnchor").objectReferenceValue = anchor;
            nestedGrab.FindProperty("_rigidBodySource").objectReferenceValue = nested.GetComponent<Rigidbody>();
            nestedGrab.ApplyModifiedPropertiesWithoutUndo();
            if (r.MagazineIsInternal)
                WeaponInternalAmmoAlignment.ConfigureAnchor(anchor, parts[r.LoadingPart], nested.GetComponent<UxrGrabbableObject>());

            // Хват корня
            var so = new SerializedObject(grabbable);
            CopyFields(gripDonor.GetComponent<UxrGrabbableObject>(), grabbable, so);
            so.FindProperty("_rigidBodySource").objectReferenceValue = rb;
            so.FindProperty("_tag").stringValue = r.UxrTag;
            // Вторая рука — дополнительная точка донора (цевьё M16 или поддержка Gun_real).
            so.FindProperty("_additionalGrabPoints").arraySize = r.SupportGrip ? 1 : 0;
            UseGripAsProximity(so);
            so.FindProperty("_startAnchor").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Снаряд и спуск
            var ps = new SerializedObject(projectile);
            CopyFields(fireDonor.GetComponent<UxrProjectileSource>(), projectile, ps);
            ps.FindProperty("_weaponAnimator").objectReferenceValue = null;

            // Помповое — дробь: второй тип выстрела для дробинок, без вспышки у дула на каждую.
            SerializedProperty shotTypes = ps.FindProperty("_shotTypes");
            bool pellets = r.Action == HandsPackWeaponRecipe.ActionKind.Pump || r.Pellets;
            if (!r.PreserveDonorGameplay) shotTypes.arraySize = pellets ? 2 : 1;
            if (shotTypes.arraySize < (pellets ? 2 : 1))
                throw new System.InvalidOperationException($"{r.Name}: у immutable донора нет необходимых типов выстрела");

            for (int i = 0; i < shotTypes.arraySize; i++)
            {
                SerializedProperty shot = shotTypes.GetArrayElementAtIndex(i);
                if (i > 0 && !r.PreserveDonorGameplay) shot.boxedValue = shotTypes.GetArrayElementAtIndex(0).boxedValue;

                shot.FindPropertyRelative("_shotSource").objectReferenceValue = shotSource;
                shot.FindPropertyRelative("_tip").objectReferenceValue = tip;
                if (r.PreserveDonorGameplay) continue;

                // Вид выстрела оружия проекта: белый трассер и попадание без сэмплов SDK
                // (TracerVisibilityTests, ImpactEffectTests).
                shot.FindPropertyRelative("_projectilePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Tracer);
                shot.FindPropertyRelative("_prefabInstantiateOnImpact").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactEffect);
                shot.FindPropertyRelative("_prefabInstantiateOnImpactLife").floatValue = 2f;
                shot.FindPropertyRelative("_prefabScenarioImpactDecal").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UxrImpactDecal>(ImpactDecal);

                // Вспышка у дула — только частицы, привязана к оружию и масштабируется с ним
                // (MuzzleEffectTests). Дробинкам своя вспышка не нужна — она обнуляется ниже.
                shot.FindPropertyRelative("_prefabInstantiateOnTipWhenShot").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleEffect);
                shot.FindPropertyRelative("_prefabInstantiateOnTipParent").boolValue = true;
                shot.FindPropertyRelative("_prefabInstantiateOnTipLife").floatValue = 1f;

                if (pellets)
                {
                    // 8 дробинок × 14 — 112 в упор при здоровье 100 (ShotgunPelletsTests).
                    shot.FindPropertyRelative("_projectileDamageNear").floatValue = 14f;
                    shot.FindPropertyRelative("_projectileDamageFar").floatValue = 4f;
                    shot.FindPropertyRelative("_projectileMaxDistance").floatValue = 60f;
                    if (i > 0) shot.FindPropertyRelative("_prefabInstantiateOnTipWhenShot").objectReferenceValue = null;
                }
            }
            ps.ApplyModifiedPropertiesWithoutUndo();
            if (pellets)
            {
                var component = root.AddComponent<ShotgunPellets>();
                if (r.PreserveDonorGameplay) CopyFields(fireDonor.GetComponent<ShotgunPellets>(), component);
            }

            var fw = new SerializedObject(firearm);
            CopyFields(fireDonor.GetComponent<UxrFirearmWeapon>(), firearm, fw);
            var recoil = Empty("RecoilSourceAxes", container, Vector3.zero);
            fw.FindProperty("_recoilAxes").objectReferenceValue = recoil;
            SerializedProperty trig = fw.FindProperty("_triggers").GetArrayElementAtIndex(0);
            trig.FindPropertyRelative("_triggerGrabbable").objectReferenceValue = grabbable;
            trig.FindPropertyRelative("_grabbableGrabPointIndex").intValue = 0;
            trig.FindPropertyRelative("_triggerTransform").objectReferenceValue = parts[r.TriggerPart];
            var triggerRotation = ResolveTriggerRotation(r, triggerMotion);
            // При отсутствии source rotation CopyFields уже сохранил точные axis/degrees immutable донора.
            if (!triggerRotation.UsesDonor)
            {
                trig.FindPropertyRelative("_triggerRotationAxis._axis").intValue = triggerRotation.Axis;
                trig.FindPropertyRelative("_triggerRotationDegrees").floatValue = triggerRotation.Degrees;
            }
            trig.FindPropertyRelative("_ammunitionMagAnchor").objectReferenceValue = anchor;
            // Без затвора патрон досылать нечем: выстрел — прямо из барабана.
            if (r.Action == HandsPackWeaponRecipe.ActionKind.None)
                trig.FindPropertyRelative("_useHasReloadedForSemiAndFullAuto").boolValue = false;
            if (r.ShotCycle.HasValue)
                trig.FindPropertyRelative("_cycleType").enumValueIndex = (int)r.ShotCycle.Value;
            if (!string.IsNullOrEmpty(r.ShotAudio))
                trig.FindPropertyRelative("_shotAudio._clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(r.ShotAudio);
            fw.ApplyModifiedPropertiesWithoutUndo();

            // Нейтральные точки хвата — центры ладоней пака: правая — рукоять, левая — цевьё/поддержка. Записей
            // хвата аватаров сборка не пишет; точка отдачи — у правой ладони на рукояти.
            Vector3[] palms = PalmPoints(pack, r.PoseClip, root.transform, parts[r.BodyPart]);
            recoil.position = root.transform.TransformPoint(palms[0]);
            if (r.PreserveDonorGameplay)
            {
                if (fireDonor.TryGetComponent(out RecoilAccumulator donorRecoil))
                {
                    var target = root.AddComponent<RecoilAccumulator>();
                    CopyFields(donorRecoil, target);
                    var recoilSo = new SerializedObject(target);
                    recoilSo.FindProperty("_axes").objectReferenceValue = recoil;
                    recoilSo.ApplyModifiedPropertiesWithoutUndo();
                }
                if (fireDonor.TryGetComponent(out WeaponSpread donorSpread))
                    CopyFields(donorSpread, root.AddComponent<WeaponSpread>());
            }

            // Вторая рука пистолета обнимает первую: она не должна поворачивать оружие и браться
            // одна (GunTwoHandAimTests, SupportGripTests; порог — TwoHandGrabCases.SupportGripMaxGap).
            // Расстояние — между ладонями рук пака на рукояти и на второй точке.
            if (r.SupportGrip)
            {
                float gap = Vector3.Distance(root.transform.TransformPoint(palms[0]), root.transform.TransformPoint(palms[1]));
                if (gap < SupportGripMaxGap)
                {
                    root.AddComponent<MainGripAimLock>();
                    root.AddComponent<SupportGripRequiresMain>();
                }
            }

            WeaponInteractionRecipe interaction = WeaponInteractionRecipes.For(root, r, palms);
            if (r.InteractionFolder != null) interaction.AssetFolder = r.InteractionFolder;
            WeaponInteractionInstaller.Apply(root, interaction);
            if (configureHost) ConfigureHost(root, r);

            return root;
        }

        /// <summary>
        /// Хост WeaponSystem по профилю рецепта (у ручного заряжания — вместе с трубкой и приёмником патрона): единственный
        /// writer хоста при сборке. Вызывается, когда механика ствола (клипы затвора) уже стоит.
        /// </summary>
        private static void ConfigureHost(GameObject root, HandsPackWeaponRecipe r)
        {
            if (r.ManualLoading)
                ManualLoadingAuthoring.ConfigureWeapon(root, r.Name, r.MagazineCapacity,
                    AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(r.ManualLoadingProfile),
                    string.IsNullOrEmpty(r.LoadAudio) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(r.LoadAudio));
            else
            {
                // Профиль — из рецепта, иначе из таблицы волн WeaponSystem (переведённый ствол пересобирается сразу на машине).
                string readiness = !string.IsNullOrEmpty(r.ReadinessProfile) ? r.ReadinessProfile : WeaponSystemAuthoring.ProfilePathFor(r.Name);
                if (!string.IsNullOrEmpty(readiness))
                    WeaponSystemAuthoring.Configure(root, AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(readiness) ??
                        throw new System.InvalidOperationException("Нет профиля готовности " + readiness));
            }
        }

        /// <summary>Отложенный хост (<c>Build(..., configureHost: false)</c>) на сохранённом префабе после установки клипов затвора.</summary>
        public static void ConfigureHost(string path, HandsPackWeaponRecipe r)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ConfigureHost(root, r);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            // SaveAsPrefabAsset пишет _assetId = 0 — вернуть канон.
            VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { path }, false);
        }

        /// <summary>Помпа или затвор: ход из клипа либо сохранённая ручная механика immutable донора.</summary>
        private static void BuildAction(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal,
                                        GameObject root, GameObject gripDonor, GameObject fireDonor)
        {
            var action = new GameObject(r.Action == HandsPackWeaponRecipe.ActionKind.Pump ? "Pump" : "Slide");
            action.transform.SetParent(root.transform, false);
            var actionNames = new Dictionary<string, string> { { r.ActionPart, Clean(r.ActionPart) } };
            foreach (string p in r.ActionExtraParts ?? new string[0]) actionNames[p] = Clean(p);
            pack.BuildParts(action.transform, bodyLocal, actionNames);

            // Механика из клипа: полный ход — наибольшее смещение от покоя (у рычага взведения
            // MP5K путь не прямой, первое направление движения с ним не совпадает).
            Vector3 travel = ResolveActionTravel(r, pack, bodyLocal, out _);

            var actionGrab = action.AddComponent<UxrGrabbableObject>();
            action.AddComponent<GrabOnlyWhenParentHeld>();
            var ag = new SerializedObject(actionGrab);
            CopyFields(FindPart(gripDonor), actionGrab, ag);
            ag.FindProperty("_translationConstraintMode").enumValueIndex = (int)UxrTranslationConstraintMode.RestrictLocalOffset;
            ag.FindProperty("_translationLimitsMin").vector3Value = Vector3.Min(travel, Vector3.zero);
            ag.FindProperty("_translationLimitsMax").vector3Value = Vector3.Max(travel, Vector3.zero);
            ag.FindProperty("_rotationConstraintMode").enumValueIndex = (int)UxrRotationConstraintMode.RestrictLocalRotation;
            UseGripAsProximity(ag);

            // Донор — рукоятка затвора M16: она не наводит ствол и от винтовки не зависит. Помпу
            // держит вторая рука, она наводит ствол, как цевьё (так у сэмплового дробовика SDK).
            bool isPump = r.Action == HandsPackWeaponRecipe.ActionKind.Pump;
            ag.FindProperty("_controlParentDirection").boolValue = isPump;
            ag.FindProperty("_ignoreGrabbableParentDependency").boolValue = !isPump;
            ag.ApplyModifiedPropertiesWithoutUndo();

            if (isPump)
            {
                var pump = new SerializedObject(root.AddComponent<UxrShotgunPump>());
                CopyFields(fireDonor.GetComponent<UxrShotgunPump>(), pump.targetObject, pump);
                pump.FindProperty("_pump").objectReferenceValue = actionGrab;
                pump.FindProperty("_localPumpDirection").vector3Value = travel.normalized;
                pump.FindProperty("_localPumpOffset").vector3Value = travel;
                pump.ApplyModifiedPropertiesWithoutUndo();
                // С этапа drive помпой управляет хост WeaponSystem: компонент — только источник хода, порога и звуков
                // для WeaponSystemAuthoring.Configure, который его затем снимает. Включённый он считается legacy-помпой.
                ((UxrShotgunPump)pump.targetObject).enabled = false;

                // Помпа идёт за смещением руки с момента хвата, иначе запаздывает на промах хвата.
                var follow = new SerializedObject(root.AddComponent<PumpGrabFollow>());
                follow.FindProperty("_pump").objectReferenceValue = actionGrab;
                follow.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                var slide = new SerializedObject(root.AddComponent<AutomaticWeaponSlideFeedback>());
                var donorFeedback = gripDonor.GetComponent<AutomaticWeaponSlideFeedback>();
                if (donorFeedback == null)
                {
                    // Живой донор (AR15, BrowningHiPower) переведён на WeaponSystem и затвора AWSF больше не несёт —
                    // порог, возврат и звуки затвора берутся из его immutable снимка (LegacyBuildDonors). Иначе
                    // WeaponSystemAuthoring.Configure брал значения по умолчанию: порог 0,7 вместо 0,855 и без звуков.
                    var snapshot = AssetDatabase.LoadAssetAtPath<GameObject>(HandsPackLegacyRebuild.DonorSnapshot(r.GripDonor) ?? "");
                    donorFeedback = snapshot != null ? snapshot.GetComponent<AutomaticWeaponSlideFeedback>() : null;
                }
                CopyFields(donorFeedback, slide.targetObject, slide);
                slide.FindProperty("_slide").objectReferenceValue = actionGrab;
                // Свои звуки затвора (KINEMATION) — и для пустого, и для заряженного хода.
                foreach (var (field, audio) in new[] { ("_audioSlideBack", r.SlideBackAudio), ("_audioSlideBackWhenLoaded", r.SlideBackAudio),
                                                       ("_audioSlideForward", r.SlideForwardAudio), ("_audioSlideForwardWhenLoaded", r.SlideForwardAudio) })
                {
                    if (string.IsNullOrEmpty(audio)) continue;
                    slide.FindProperty(field + "._clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(audio);
                }
                slide.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Близость руки к точке хвата — от её же точки выравнивания (Use Self), как у доноров. CopyFields
        /// пропускает всё с <c>_grabProximityTransform</c> в пути, включая флаг, и у новой
        /// дополнительной точки он оставался выключенным при пустом трансформе: близость мерилась от
        /// корня, и вторая рука у револьвера выбирала основную точку (GunTwoHandGrabTests).
        /// </summary>
        private static void UseGripAsProximity(SerializedObject grabbable)
        {
            grabbable.FindProperty("_grabPoint._grabProximityTransformUseSelf").boolValue = true;
            SerializedProperty additional = grabbable.FindProperty("_additionalGrabPoints");
            for (int i = 0; i < additional.arraySize; i++)
                additional.GetArrayElementAtIndex(i).FindPropertyRelative("_grabProximityTransformUseSelf").boolValue = true;
        }

        /// <summary>Центр меша магазина пака в осях контейнера деталей — место якоря и начало координат магазина.</summary>
        private static Vector3 MagazineCenter(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal)
        {
            pack.Sample(null);
            return (bodyLocal * pack.PartInBody(r.MagazinePart)).MultiplyPoint3x4(pack.MeshOf(r.MagazinePart).bounds.center);
        }

        // ── Магазин ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Самостоятельный префаб магазина (не вариант): шаблон — текущий магазин ствола, а у первой сборки —
        /// <see cref="HandsPackWeaponRecipe.MagazineBase" /> (готовый магазин паков того же типа). Сеть, физика и звуки — от
        /// шаблона, геометрия — меш пака. Масштаб — как у оружия: вложенный магазин живёт под корнем оружия с ×1,
        /// выдаваемый должен быть того же размера. Корень сохраняет GUID и localFileID (внешние WeaponInfo/spawn-ссылки).
        /// </summary>
        private static GameObject BuildMagazine(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal, float scale, string folder, Scene scene)
        {
            string path = $"{folder}/{r.MagazineName ?? r.Name + "_mag"}.prefab";
            string template = MagazineTemplate(r);
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(template) ??
                throw new System.InvalidOperationException($"{r.Name}: нет шаблона магазина {template}");
            long rootId = AssetDatabase.LoadAssetAtPath<GameObject>(path) is GameObject current &&
                          AssetDatabase.TryGetGUIDAndLocalFileIdentifier(current, out string _, out long id) ? id : 0;
            var mag = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, scene);
            // Отделённая копия: магазин — не вариант базы SDK (базы удалены, решение пользователя 2026-10-22).
            // SaveAsPrefabAsset сопоставляет прежний корень по имени и сохраняет внешние WeaponInfo/spawn-ссылки.
            PrefabUtility.UnpackPrefabInstance(mag, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (template != path && !r.PreserveDonorGameplay) ClearTemplateGrips(mag);
            mag.name = r.MagazineName ?? r.Name + "_mag";
            mag.transform.localScale = Vector3.one * scale;
            if (r.PreserveDonorGameplay)
            {
                // Старые Animation/Animator не должны повторно двигать детали нового generic механизма.
                foreach (var animation in mag.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(animation);
                foreach (var animator in mag.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                var network = new SerializedObject(mag.GetComponent<NetworkIdentity>());
                network.FindProperty("_assetId").longValue = r.MagazineAssetId;
                network.ApplyModifiedPropertiesWithoutUndo();
            }

            // Геометрия базы уходит целиком, вместе с индикатором патронов, который на неё ссылается.
            foreach (var indicator in mag.GetComponents<MonoBehaviour>().Where(c => c != null && c.GetType().Name == "MagAmmoIndicator").ToList())
                Object.DestroyImmediate(indicator);
            // Объект геометрии базы (MagMachinegunGeo, MagGunGeo, MagShotGunGeo) уходит целиком, вместе с точками хвата
            // сэмпловых аватаров SDK внутри него (решение пользователя 2026-10-22). Поза руки магазина — snap-точка _orig
            // (PackOrigPoses); регистрация поз аватаров — отдельная команда, перенос хватов эти узлы не воссоздаёт.
            foreach (Transform geometry in BaseGeometry(mag.transform).ToList())
                Object.DestroyImmediate(geometry.gameObject);
            foreach (MeshFilter filter in mag.GetComponentsInChildren<MeshFilter>(true).ToList())
                if (filter != null) Object.DestroyImmediate(filter.gameObject);
            RemoveOrphanGripEntries(mag.GetComponent<UxrGrabbableObject>());

            // Меш пака: ориентирован как в оружии, центр — в начале координат магазина. Детали,
            // которые вынимаются вместе с ним (патроны, гильзы в каморах барабана), — на своих
            // местах относительно него.
            Vector3 center = MagazineCenter(r, pack, bodyLocal);
            GameObject meshGo = null;
            foreach (string part in new[] { r.MagazinePart }.Concat(r.MagazineExtraParts ?? new string[0]))
            {
                Matrix4x4 place = Matrix4x4.Translate(-center) * bodyLocal * pack.PartInBody(part);
                var go = new GameObject(part == r.MagazinePart ? "Mesh" : Clean(part));
                go.transform.SetParent(mag.transform, false);
                go.transform.SetLocalPositionAndRotation(place.GetColumn(3), place.rotation);
                go.transform.localScale = place.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = pack.MeshOf(part);
                go.AddComponent<MeshRenderer>().sharedMaterials = pack.MaterialsOf(part);
                if (part == r.MagazinePart) meshGo = go;
            }

            Mesh mesh = pack.MeshOf(r.MagazinePart);
            var col = meshGo.AddComponent<BoxCollider>();
            col.center = mesh.bounds.center;
            col.size = mesh.bounds.size;

            // У патрона ручного заряжания счётчик снимает ConfigureCartridge — в шаблоне-патроне его нет.
            var counter = mag.GetComponent<UxrFirearmMag>();
            if (counter == null) counter = mag.AddComponent<UxrFirearmMag>();
            var so = new SerializedObject(counter);
            so.FindProperty("_capacity").intValue = r.MagazineCapacity;
            so.FindProperty("_rounds").intValue = r.MagazineCapacity;
            so.ApplyModifiedPropertiesWithoutUndo();

            var grab = new SerializedObject(mag.GetComponent<UxrGrabbableObject>());
            grab.FindProperty("_tag").stringValue = r.MagazineTag;
            grab.ApplyModifiedPropertiesWithoutUndo();
            // Записи хвата аватаров магазина — только от базы; своих сборка не добавляет (перенос — WeaponGripCarryOver).

            ImpactSoundInstaller.Apply(mag, true);
            // Звук вставки задаёт AnchorSound гнезда; свой звук размещения боеприпаса (MagPlace донора) дал бы двойной.
            foreach (var audio in mag.GetComponentsInChildren<UltimateXR.Audio.UxrAudioManipulation>(true))
            {
                var place = new SerializedObject(audio);
                place.FindProperty("_audioOnPlace._clip").objectReferenceValue = null;
                place.ApplyModifiedPropertiesWithoutUndo();
            }
            // Подсветка магазина Hands — в префабе до сборки оружия: установщик подсветки оружия переиспользует её во
            // вложенном экземпляре, иначе там две GrabHighlight (одна от экземпляра, вторая от префаба).
            // KINEMATION ставит её позже сам (KinemationFixReview.ApplyMagazine, у R08 — на MechanismVisual).
            if (!(pack is KinemationWeapon)) WeaponInteractionInstaller.ApplyMagazine(mag);
            // Любая последующая сборка должна сохранять защиту магазина от поздней
            // привязки склада после уже выполненного захвата/броска.
            if (mag.GetComponent<VrBattlegrounds.Arsenal.MagazineManipulationHistory>() == null)
                mag.AddComponent<VrBattlegrounds.Arsenal.MagazineManipulationHistory>();
            if (r.FinalRootScale > 0f) mag.transform.localScale = Vector3.one * r.FinalRootScale;
            if (r.ManualLoading) ManualLoadingAuthoring.ConfigureCartridge(mag, r.Name, r.MagazineCapacity);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(mag, path);
            if (rootId != 0 && (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(saved, out string _, out long after) || after != rootId))
                throw new System.InvalidOperationException($"{path}: изменился localFileID корня магазина — внешние ссылки WeaponInfo/spawn разорвались бы");
            return saved;
        }

        /// <summary>Шаблон магазина: текущий префаб магазина ствола, у первой сборки — <see cref="HandsPackWeaponRecipe.MagazineBase" />.</summary>
        internal static string MagazineTemplate(HandsPackWeaponRecipe r)
        {
            if (r.PreserveDonorGameplay) return r.MagazineBase;
            string own = $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.MagazineName ?? r.Name + "_mag"}.prefab";
            return AssetDatabase.LoadAssetAtPath<GameObject>(own) != null ? own : r.MagazineBase;
        }

        /// <summary>Шаблон — магазин другого ствола: его записи хвата аватаров и snap-точки _orig не переносятся.</summary>
        private static void ClearTemplateGrips(GameObject mag)
        {
            var grabbable = mag.GetComponent<UxrGrabbableObject>();
            if (grabbable != null)
            {
                var so = new SerializedObject(grabbable);
                so.FindProperty("_grabPoint._avatarGripPoseEntries").arraySize = 0;
                SerializedProperty additional = so.FindProperty("_additionalGrabPoints");
                for (int i = 0; i < additional.arraySize; i++)
                    additional.GetArrayElementAtIndex(i).FindPropertyRelative("_avatarGripPoseEntries").arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Transform grabs = mag.transform.Find("Grabs");
            if (grabs != null)
                foreach (Transform child in grabs.Cast<Transform>().ToList())
                    Object.DestroyImmediate(child.gameObject);
        }

        /// <summary>
        /// Имена объектов геометрии базы магазина (<see cref="HandsPackWeaponRecipe.MagazineBase" />), которые
        /// <see cref="BuildMagazine" /> удаляет. Перенос хватов отбрасывает записи с трансформами внутри них.
        /// </summary>
        public static string[] MagazineBaseGeometry(HandsPackWeaponRecipe r)
        {
            string template = MagazineTemplate(r);
            var basePrefab = string.IsNullOrEmpty(template) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(template);
            if (basePrefab == null) return System.Array.Empty<string>();
            // Узлы, которые сборка создаёт заново (меш пака и детали магазина), — не удалённая геометрия базы.
            var rebuilt = new HashSet<string> { "Mesh" };
            rebuilt.UnionWith((r.MagazineExtraParts ?? new string[0]).Select(Clean));
            return BaseGeometry(basePrefab.transform).Select(t => t.name)
                .Concat(basePrefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.gameObject.name))
                .Where(n => !rebuilt.Contains(n)).Distinct().ToArray();
        }

        /// <summary>
        /// Геометрия базы магазина — прямые дети корня, кроме собственного <c>Grabs</c>, с мешем, рендерером, коллайдером
        /// или точками хвата внутри: <c>MagMachinegunGeo</c>/<c>MagGunGeo</c>/<c>MagShotGunGeo</c> SDK и их уже обрезанные
        /// копии у неизменяемых доноров набора Hands (<c>Geo</c> с коллайдером, пустой <c>MagShotGunGeo</c> с точками хвата).
        /// </summary>
        private static IEnumerable<Transform> BaseGeometry(Transform magazine) =>
            magazine.Cast<Transform>().Where(child => child.name != "Grabs" &&
                (child.GetComponentInChildren<Renderer>(true) != null || child.GetComponentInChildren<MeshFilter>(true) != null ||
                 child.GetComponentInChildren<Collider>(true) != null || child.GetComponentInChildren<UxrGrabbableObjectSnapTransform>(true) != null));

        /// <summary>Сэмпловые аватары UltimateXR (CyborgAvatar, BigHandsAvatar, SmallHandsAvatar): их записей хвата у магазинов паков нет.</summary>
        public static readonly string[] SdkSampleAvatars =
        {
            "e7468ec6b7af89c4d94d451ec3c1807b", "dc011efb41f2d4a419389aefb4b4270d", "7195d091dac462e4db9ba0793d9a6727"
        };

        /// <summary>
        /// Записи хвата сэмпловых аватаров SDK (у доноров набора Hands их точки лежат в собственном <c>Grabs</c>) и записи,
        /// у которых обе трансформы выравнивания удалены вместе с геометрией базы; ставшие ненужными точки — тоже.
        /// </summary>
        private static void RemoveOrphanGripEntries(UxrGrabbableObject grabbable)
        {
            if (grabbable == null) return;
            var orphans = new List<Transform>();
            var so = new SerializedObject(grabbable);
            var lists = new List<SerializedProperty> { so.FindProperty("_grabPoint._avatarGripPoseEntries") };
            SerializedProperty additional = so.FindProperty("_additionalGrabPoints");
            for (int i = 0; i < additional.arraySize; i++)
                lists.Add(additional.GetArrayElementAtIndex(i).FindPropertyRelative("_avatarGripPoseEntries"));
            foreach (SerializedProperty entries in lists)
                for (int i = entries.arraySize - 1; i >= 0; i--)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    var left = entry.FindPropertyRelative("_gripAlignTransformHandLeft").objectReferenceValue as Transform;
                    var right = entry.FindPropertyRelative("_gripAlignTransformHandRight").objectReferenceValue as Transform;
                    bool sample = SdkSampleAvatars.Contains(entry.FindPropertyRelative("_avatarPrefabGuid").stringValue);
                    if (!sample && (left != null || right != null)) continue;
                    orphans.Add(left);
                    orphans.Add(right);
                    entries.DeleteArrayElementAtIndex(i);
                }
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (Transform orphan in orphans.Where(t => t != null).Distinct().ToList())
                if (orphan != null) HandsPackPoseImporter.RemoveUnused(grabbable.gameObject, orphan, null);
        }

        // ── Вспомогательное ─────────────────────────────────────────────────────

        private static UxrGrabbableObject FindPart(GameObject donor)
        {
            UxrGrabbableObject root = donor.GetComponent<UxrGrabbableObject>();
            return donor.GetComponentsInChildren<UxrGrabbableObject>(true)
                        .First(g => g != root && g.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Length == 0);
        }

        /// <summary>
        /// Копирует сериализованные поля донора, кроме id UltimateXR и ссылок на объекты донора
        /// внутри его префаба (они обнуляются — их назначает сборщик). Ассеты (звуки, позы,
        /// префабы эффектов) копируются.
        /// </summary>
        private static void CopyFields(Object donor, Object target, SerializedObject dst = null)
        {
            if (donor == null) return;
            bool apply = dst == null;
            dst ??= new SerializedObject(target);
            var src = new SerializedObject(donor);
            string donorPath = AssetDatabase.GetAssetPath(donor);

            SerializedProperty p = src.GetIterator();
            bool enter = true;
            while (p.Next(enter))
            {
                enter = p.propertyType == SerializedPropertyType.Generic;
                if (PrefabBookkeeping.Contains(p.propertyPath) || p.propertyPath.Contains("uxrUniqueId") ||
                    p.propertyPath.StartsWith("__") || p.propertyPath.Contains("_avatarGripPoseEntries") || p.propertyPath.Contains("_grabProximityBox") ||
                    p.propertyPath.Contains("_enableOnHandNear") || p.propertyPath.Contains("_alignToControllerAxes") || p.propertyPath.Contains("_grabProximityTransform"))
                {
                    enter = false;
                    continue;
                }

                if (p.propertyType == SerializedPropertyType.Generic && !p.isArray) continue;

                if (p.isArray && p.propertyType == SerializedPropertyType.Generic)
                {
                    dst.FindProperty(p.propertyPath)?.SetArraySize(p.arraySize);
                    continue;
                }

                SerializedProperty d = dst.FindProperty(p.propertyPath);
                if (d == null || d.propertyType != p.propertyType) continue;

                if (p.propertyType == SerializedPropertyType.ObjectReference)
                {
                    Object o = p.objectReferenceValue;
                    bool insideDonor = o != null && AssetDatabase.GetAssetPath(o) == donorPath && !(o is ScriptableObject);
                    d.objectReferenceValue = insideDonor ? null : o;
                }
                else if (p.propertyType != SerializedPropertyType.ArraySize)
                {
                    d.boxedValue = p.boxedValue;
                }
            }

            if (apply) dst.ApplyModifiedPropertiesWithoutUndo();
        }

        // Служебные поля объекта и префаба: у донора-варианта m_CorrespondingSourceObject указывает
        // на его базу, скопированный он привязал бы новый компонент к чужому префабу.
        private static readonly HashSet<string> PrefabBookkeeping = new HashSet<string>
        {
            "m_Script", "m_GameObject", "m_Name", "m_ObjectHideFlags", "m_CorrespondingSourceObject",
            "m_PrefabInstance", "m_PrefabAsset", "m_EditorHideFlags", "m_EditorClassIdentifier", "m_Enabled"
        };

        private static void SetArraySize(this SerializedProperty p, int size) => p.arraySize = size;

        private static Transform Empty(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        internal static string Clean(string part) => part.Replace("Shogun_", "").Replace("_mesh", "").Replace("_Mesh", "").Replace("-", "").Replace(" ", "");

        /// <summary>Как <c>TwoHandGrabCases.SupportGripMaxGap</c>: ближе — вторая рука на рукояти (пистолет), дальше — цевьё.</summary>
        private const float SupportGripMaxGap = 0.10f;

        /// <summary>Обновляет готовый префаб тем же расчётом среза, что и сборка, сохраняя ссылки и настройки выстрелов.</summary>
        public static void AlignMuzzle(GameObject root, HandsPackWeaponRecipe recipe)
        {
            Transform container = root.transform.Find("MeshContainer");
            Transform part = container?.Find(recipe.MuzzlePart == null ? recipe.BodyObjectName : Clean(recipe.MuzzlePart));
            Mesh mesh = part == null ? null : part.GetComponent<MeshFilter>()?.sharedMesh;
            UxrProjectileSource source = root.GetComponent<UxrProjectileSource>();
            Transform breech = container?.Find("BarrelCheck");
            float scale = root.transform.lossyScale.z;
            if (mesh == null || mesh.vertexCount == 0 || source == null || source.ShotTypes.Count == 0 ||
                source.ShotTypes.Any(shot => shot.Tip == null || shot.ShotSource == null) || breech == null || !Finite(scale) || scale <= 0f)
                throw new System.InvalidOperationException($"{recipe.Name}: нет полной геометрии/ссылок для расчёта дула ({recipe.MuzzlePart ?? recipe.BodyPart})");

            Bounds muzzle = MuzzleRing(mesh, part, recipe.Action == HandsPackWeaponRecipe.ActionKind.Pump || recipe.Pellets);
            Vector3 tip = new Vector3(muzzle.center.x, muzzle.center.y, muzzle.max.z);
            Vector3 shotSource = tip - Vector3.forward * (0.01f / scale);
            Vector3 breechPosition = container.InverseTransformPoint(breech.position);
            foreach (var shot in source.ShotTypes)
            {
                shot.Tip.position = container.TransformPoint(tip);
                shot.ShotSource.position = container.TransformPoint(shotSource);
            }
            breech.position = container.TransformPoint(new Vector3(tip.x, tip.y, breechPosition.z));
        }

        /// <summary>
        /// Кольцо вершин у дула, в осях контейнера. Срез — доля длины (единицы меша у моделей пака разные,
        /// от 0.07 у MP5K до 1.7 у Uzi): тонкий, 0.5 % длины — только сам срез ствола, без мушки и
        /// переднего упора за ним. <paramref name="topCluster" /> — у дробовика в срез попадает и
        /// подствольный магазин: берётся верхний кластер, срез 1.65 % (прежний 1 см у Shotgun_real).
        /// </summary>
        private static Bounds MuzzleRing(Mesh mesh, Transform body, bool topCluster)
        {
            if (mesh == null || mesh.vertexCount == 0)
                throw new System.InvalidOperationException("Расчёт дула требует непустой меш выбранной детали");
            Matrix4x4 m = body.parent.worldToLocalMatrix * body.localToWorldMatrix;
            List<Vector3> v = mesh.vertices.Select(x => m.MultiplyPoint3x4(x)).ToList();
            if (v.Any(x => !Finite(x)))
                throw new System.InvalidOperationException($"{mesh.name}: нечисловая геометрия выбранной детали дула");
            float front = v.Max(x => x.z);
            float depth = (topCluster ? 0.0165f : 0.005f) * (front - v.Min(x => x.z));
            List<Vector3> ring = v.Where(x => x.z > front - depth).ToList();
            if (ring.Count == 0)
                throw new System.InvalidOperationException($"{mesh.name}: выбранная деталь не имеет среза вдоль оси ствола");
            float mid = (ring.Max(x => x.y) + ring.Min(x => x.y)) * 0.5f;
            List<Vector3> top = topCluster ? ring.Where(x => x.y >= mid).ToList() : ring;
            var b = new Bounds(top[0], Vector3.zero);
            foreach (Vector3 x in top) b.Encapsulate(x);
            return b;
        }

        private static int DominantAxis(Vector3 a) => Mathf.Abs(a.x) >= Mathf.Abs(a.y) && Mathf.Abs(a.x) >= Mathf.Abs(a.z) ? 0 : Mathf.Abs(a.y) >= Mathf.Abs(a.z) ? 1 : 2;

        private static float AxisComponent(Vector3 a) => a[DominantAxis(a)];
    }
}
