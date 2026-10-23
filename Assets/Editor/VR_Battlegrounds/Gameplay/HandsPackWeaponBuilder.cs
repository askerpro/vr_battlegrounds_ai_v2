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
    /// Рецепт оружия из пака Hands Weapons Animations (и KINEMATION через <see cref="KinemationWeaponRecipe" />): что взять
    /// из пака. Всё, что можно вычислить, вычисляет <see cref="HandsPackWeaponBuilder" />; то, что не выводится из пака и
    /// не входит в баланс, — общие значения <see cref="HandsPackBuildDefaults" /> (доноров нет, решение 2026-10-23), отличия
    /// конкретного ствола — явными полями здесь.
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
        // Масштаб корня при сборке напрямую (единицы меша → метры) вместо TargetLength: размер принятой легаси-сборки,
        // где длина была габаритом × масштабом корпуса донора, то есть масштаб корня = масштаб корпуса донора. 0 — по TargetLength.
        public float RootScale;
        public float FinalRootScale;   // 0 — без override; итоговый размер после расчёта authored child frames

        // Детали корпуса без движения; null — все детали источника пака Hands, не названные иначе и не исключённые
        // (HandsPackMechanism.ResolveStaticParts, как у легаси-сборки).
        public string[] StaticParts;
        public string[] ExcludedSourceParts; // FPS reload props вне игрового оружия; preflight обязан учесть явно
        public bool ClaimAllSourceParts; // каждая деталь источника названа или исключена: иначе отказ до первой записи
        public string MuzzlePart;      // деталь среза ствола: пламегаситель/глушитель; null — корпус
        public string TriggerPart;
        public string ActionPart;      // помпа или затвор; по нему меряется ход
        public string ActionGripPart;  // видимая ручка: независима от детали, по которой меряется ход
        public Vector3? ActionGripContact; // явный контакт в локальных осях меша ручки
        public Vector3? ActionTravelAxis; // явная ось линейной VR-тяги в осях готового корня; длина проецируется из источника
        // «Нет в паке»: ход затвора в осях готового корня (единицы меша), если в клипе деталь неподвижна (AR15).
        public Vector3? StaticActionTravel;
        // «Нет в паке»: угол спуска вокруг X, если в клипе спуск неподвижен; null — угол 0, как в паке (Uzi, MP5K).
        public float? StaticTriggerDegrees;
        public string[] ActionExtraParts; // детали, которые ходят вместе с затвором (рукоять затвора болтовки)
        public ActionKind Action;
        // Ручной цикл механизма (помпа, болт): затвор/помпа после каждого выстрела (UxrShotCycle.ManualReload). Иначе режим
        // огня — баланс (WeaponInfo.FullAuto, WeaponBalanceApplier). Помпа — всегда ручной цикл.
        public bool ManualCycle;
        public bool Pellets;           // дробь без помпы (полуавтоматический дробовик); у помпового — всегда
        // Анимация механизма из клипа выстрела пака (HandsPackMechanism): затвор, спуск и подвижные детали корпуса ходят
        // при стрельбе и перезарядке; хост WeaponSystem ставится после неё.
        public bool MechanismMotion;
        public string EmptyActionClip; // клип пака «последний выстрел» (затвор на задержке) — поза Empty механизма; null — нет
        public string LoadingPart;     // деталь внешней точки приёма; null — приём на месте магазина пака
        public bool SupportGrip;       // вторая рука на корне (цевьё или поддержка пистолета) — точка 1

        // Снаряд: отличия ствола от общего набора (HandsPackBuildDefaults); null — общее значение.
        public string MuzzleEffect;    // вспышка у дула; null — Effects/Muzzle_Default
        public float? ProjectileLength;
        public float? ProjectileImpactForce; // множитель толчка попадания (CorpseTests: дробина FABARM 300×50)
        public int? DecalLayers;       // слои декалей попадания

        public string UxrTag;          // тег хвата корня: по нему карманы и слоты принимают оружие
        public string MagazineBase;    // шаблон магазина первой сборки (сеть, физика, звук); дальше шаблон — текущий магазин ствола
        public string MagazineName;    // прежнее имя ассета магазина; null — Name + "_mag"
        public string InteractionFolder; // ассеты областей подсветки; null — Art/Weapons/Interaction/<имя>
        public string MagazinePart;    // меш магазина из пака
        public string[] MagazineExtraParts; // вынимаются вместе с магазином: патроны, гильзы барабана
        public string MagazineTag;     // тег хвата магазина: его принимают якорь и карманы
        public int MagazineCapacity;
        public bool MagazineIsInternal; // боезапас внутри корпуса: внешняя точка приёма отдельно от конечной позы
        public bool ManualLoading; // явный recipe opt-in, не вывод из имени/дроби/внутреннего магазина
        public string ManualLoadingProfile;
        // Явный профиль готовности съёмного магазина (патронник, ручной цикл, Empty-поза); пусто — legacy без ledger.
        public string ReadinessProfile;

        // Звуки пака Hands: что резать (HandsPackAudio). Куски сборка записывает в поля ниже; null — без нарезки.
        public HandsPackSoundRecipe PackSounds;
        // Итоговые клипы (пути ассетов): готовый клип или кусок пака (HandsPackAudio, KinemationWeaponBuilder.MakeSounds).
        public string ShotAudio;       // выстрел; у ствола реестра свой (WeaponShotSoundTests)
        public string LoadAudio;       // звук вставки патрона в окно приёма (ручное заряжание)
        public string InsertAudio;     // звук вставки магазина в гнездо (кусок перезарядки пака); пусто — дефолт категории/роли гнезда в рантайме
        public string TakeOutAudio;    // звук снятия магазина (кусок пака); пусто — дефолт категории/роли гнезда в рантайме (AnchorSound)
        public string SlideBackAudio;  // оттягивание затвора/помпы; пусто — дефолт категории хоста
        public string SlideForwardAudio; // обратный ход затвора/помпы; пусто — дефолт категории хоста

        /// <summary>Механизм с ручным циклом: режим огня — <see cref="UxrShotCycle.ManualReload" />, баланс его не меняет.</summary>
        public bool IsManualCycle => ManualCycle || Action == ActionKind.Pump;
    }

    /// <summary>
    /// Собирает префаб оружия и его магазина по <see cref="HandsPackWeaponRecipe" />.
    ///
    /// <para>
    /// Геометрия и механика — из пака (<see cref="HandsPackWeapon" />): детали на точных местах,
    /// ход помпы/затвора и угол спуска из клипа, звуки — нарезкой звуков пака (<see cref="HandsPackAudio" />). Доноров нет
    /// (решение пользователя 2026-10-23): физика, хват, якорь, снаряд и вход механики — общие значения
    /// <see cref="HandsPackBuildDefaults" />; баланс (урон, темп, отдача, разлёт, скорость и дальность снаряда, режим огня)
    /// — <see cref="WeaponBalanceApplier" /> в общем конце пересборки (<see cref="WeaponRebuildFinish" />).
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
        // Вспышки принятых легаси-сборок (AR15, BrowningHiPower): прежде приходили с эффектами донора, теперь — поле рецепта.
        internal const string MuzzleRifle = "Assets/Prefabs/Weapons/Effects/Muzzle_Rifle.prefab";
        internal const string MuzzlePistol = "Assets/Prefabs/Weapons/Effects/Muzzle_Pistol.prefab";
        private const string Snd = HandsPackAudio.Pack;

        /// <summary>
        /// Помповый дробовик (роль Nova): FABARM SDASS, трубчатый магазин на 8, патрон — в окно у скобы. Полный рецепт без
        /// доноров (решение 2026-10-23; раньше — легаси-набор из снимков): ручной цикл помпы явно, спуск и помпа ходят по
        /// анимации механизма пака, снаряд — поля сэмпла SDK, принятые у FABARM (дробина 0,34, толчок ×50, декали только на
        /// Default; CorpseTests). Второй патрон пака — prop руки в Recharge, не часть оружия.
        /// </summary>
        public static HandsPackWeaponRecipe ShotgunReal => new HandsPackWeaponRecipe
        {
            Name              = "FabarmSDASS",
            PrefabFolder      = "FabarmSDASS",
            MagazineName      = "FabarmSDASS_Ammo",
            Folder            = "Hands_Shotgun",
            BodyPart          = "Shogun_Base_mesh",
            PoseClip          = "Aming_Idle",
            ActionClip        = "Shot",
            // Принятый игровой размер: масштаб корня легаси-сборки (= корпус снимка Shotgun_real). Прежняя длина 0.72 м
            // полного рецепта с ним не сверена — размер по ней мог отличаться от принятого.
            RootScale         = 1.1892964f,
            StaticParts       = null, // Staple (окно приёма), Gate и прочие детали корпуса — как у легаси, по источнику
            // Live probe: в bind/Shot второй патрон выше корпуса; Recharge переносит его из руки к окну заряжания.
            ExcludedSourceParts = new[] { "Shogun_Patron_mesh001" },
            ClaimAllSourceParts = true,
            TriggerPart       = "Shogun_Triger_mesh",
            ActionPart        = "Shogun_Fore-End_mesh",
            Action            = HandsPackWeaponRecipe.ActionKind.Pump,
            ManualCycle       = true,
            MechanismMotion   = true,
            LoadingPart       = "Shogun_Staple_mesh",
            ProjectileLength  = 0.34f,
            ProjectileImpactForce = 50f,
            DecalLayers       = 1, // Default
            UxrTag            = "Shotgun",
            MagazineBase      = MagShotgunBase,
            MagazinePart      = "Shogun_Patron_mesh",
            MagazineTag       = "MagShotgun",
            MagazineCapacity  = 8,
            MagazineIsInternal = true,
            ManualLoading = true,
            ManualLoadingProfile = "Assets/Data/Weapons/Profiles/ManualLoadFabarmReadiness.asset",
            PackSounds = new HandsPackSoundRecipe
            {
                Shot = Snd + "10Shotgun_Set/edit/shot.mp3", Bolt = Snd + "10Shotgun_Set/Zatvor.mp3", Load = Snd + "10Shotgun_Set/Reload_1.mp3"
            }
        };

        /// <summary>
        /// Автомат (роль M4A4): AR-15, магазин на 30. Полный рецепт без доноров (решение 2026-10-23; раньше — легаси-набор).
        /// Затвор и спуск в клипах пака неподвижны — ход 0,06 и спуск 40° явными полями «нет в паке» (значения принятой
        /// легаси-сборки, приходили от снимка M16); анимация четырёх деталей механизма — <see cref="HandsPackMechanism" />.
        /// Размер — как у легаси: масштаб сборки 0,83 (корпус снимка M16), итоговый корень 0,70 (контакт рукоятки с TR15).
        /// </summary>
        public static HandsPackWeaponRecipe AR15 => Rifle(new HandsPackWeaponRecipe
        {
            Name = "AR15", PrefabFolder = "AR15", MagazineName = "AR15_Magazine", Folder = "Hands_Automatic_Rifle03",
            BodyPart = "Rifle_Body_Mesh", BodyObjectName = "Rifle_Body_Mesh", PoseClip = "Aim_Idle", ActionClip = "Shot",
            RootScale = 0.83f, FinalRootScale = 0.70f,
            StaticParts = null, ClaimAllSourceParts = true, // остальные детали корпуса (Rifle_Detail_03/04) — по источнику
            TriggerPart = "Rifle_Detail_02_Mesh", ActionPart = "Rifle_Detail_01_Mesh", SupportGrip = true,
            StaticActionTravel = new Vector3(0f, 0f, -0.06f), StaticTriggerDegrees = 40f,
            MechanismMotion = true, MuzzleEffect = MuzzleRifle,
            UxrTag = "M16_Rifle", MagazinePart = "Rifle_Magazine_Mesh", MagazineTag = "M16_Mag", MagazineCapacity = 30,
            PackSounds = new HandsPackSoundRecipe
            {
                Shot = Snd + "1Assault_Rifle_Set/Rifle_Shot_1.mp3",
                Reload = Snd + "1Assault_Rifle_Set/Reload.mp3", ReloadClip = "Reload", SlideFromReload = true
            }
        });

        // ── Оружие T-38 ─────────────────────────────────────────────────────────
        // Длина — по руке пака: длина в паке × 0,707 (как у Gun_real), чтобы хват из клипа пака
        // ложился в ладонь. Позы кисти снимаются отдельно — HandsPackPoseImporter.Recipes. Звуки — из набора пака
        // ствола (Sounds/<N>_Set, решение 2026-10-23): у каждого ствола реестра свой выстрел (WeaponShotSoundTests).

        // Шаблон магазина первой сборки нового ствола — готовый магазин паков того же типа (базы SDK удалены 2026-10-22).
        private const string MagRifleBase = "Assets/Prefabs/Weapons/Scar/Scar_mag.prefab";
        private const string MagPistolBase = "Assets/Prefabs/Weapons/PPK/PPK_mag.prefab";
        internal const string MagShotgunBase = "Assets/Prefabs/Weapons/Herrington/Herrington_mag.prefab";

        /// <summary>Длинноствольное с затвором: шаблон магазина первой сборки — магазин винтовки паков.</summary>
        internal static HandsPackWeaponRecipe Rifle(HandsPackWeaponRecipe r)
        {
            r.MagazineBase = MagRifleBase;
            r.Action = HandsPackWeaponRecipe.ActionKind.Slide;
            return r;
        }

        /// <summary>Пистолет с затвором-кожухом: шаблон магазина первой сборки — магазин пистолета паков.</summary>
        internal static HandsPackWeaponRecipe Pistol(HandsPackWeaponRecipe r)
        {
            r.MagazineBase = MagPistolBase;
            r.Action = HandsPackWeaponRecipe.ActionKind.Slide;
            return r;
        }

        /// <summary>Автомат-карабин (роль AK-47): SCAR-L CQC.</summary>
        public static HandsPackWeaponRecipe Scar => Rifle(new HandsPackWeaponRecipe
        {
            Name = "Scar", PrefabFolder = "Scar", Folder = "Hands_Automatic_Rifle01", BodyPart = "Scar_Base_mesh",
            PoseClip = "Aiming_Idle", ActionClip = "Singl_Shot", RestClip = "idle",
            TargetLength = 0.635f, // SCAR-L CQC: 63.5 см; по руке пака 64.5
            StaticParts = new[] { "Scar_Detail_01_mesh", "Scar_Detail_02_mesh", "Scar_Detail_03_mesh", "Scar_Planck_mesh" },
            TriggerPart = "Scar_Triger_mesh", ActionPart = "Scar_Grip_mesh", SupportGrip = true,
            UxrTag = "BackWeapon", MagazinePart = "Scar_Magazine_mesh", MagazineTag = "MagScar", MagazineCapacity = 30,
            PackSounds = new HandsPackSoundRecipe
            {
                Shot = Snd + "1Assault_Rifle_Set/Rifle_Shot_2.mp3",
                Reload = Snd + "1Assault_Rifle_Set/Reload.mp3", ReloadClip = "Recharge", SlideFromReload = true
            }
        });

        /// <summary>Пистолет-пулемёт одной рукой (роль MAC-10): Uzi. Вторая рука пака не на оружии — точки поддержки нет.</summary>
        public static HandsPackWeaponRecipe Uzi
        {
            get
            {
                HandsPackWeaponRecipe r = Rifle(new HandsPackWeaponRecipe
                {
                    Name = "Uzi", PrefabFolder = "Uzi", Folder = "Hands_Tommy_gun", BodyPart = "body_Mesh",
                    PoseClip = "Aiming_Idle", ActionClip = "Shot",
                    TargetLength = 0.35f, // Mini-Uzi со сложенным прикладом: 36 см; по руке пака 35
                    StaticParts = new string[0],
                    TriggerPart = "Trigger_mesh", ActionPart = "Reload_frame_mesh", SupportGrip = false,
                    UxrTag = "Gun", MagazinePart = "Magazine_Mesh", MagazineTag = "MagUzi", MagazineCapacity = 30,
                    PackSounds = new HandsPackSoundRecipe
                    {
                        Shot = Snd + "12Tommy_Gun/Tommy_GunI_1.mp3",
                        Reload = Snd + "12Tommy_Gun/Tommy_Gun_Reload.mp3", ReloadClip = "Reloading", SlideFromReload = true
                    }
                });
                r.MagazineBase = MagPistolBase;
                return r;
            }
        }

        /// <summary>Пистолет-пулемёт (роль MP9): HK MP5K. Затвор в выстреле пака неподвижен — ход рукоятки взведения из перезарядки.</summary>
        public static HandsPackWeaponRecipe MP5K => Rifle(new HandsPackWeaponRecipe
        {
            Name = "MP5K", PrefabFolder = "MP5K", Folder = "Hands_Automatic_Rifle04", BodyPart = "Rifle04_Body_Mesh",
            PoseClip = "Idle_Aim", ActionClip = "Reload",
            TargetLength = 0.33f, // HK MP5K: 32.5 см
            StaticParts = new string[0],
            TriggerPart = "Rifle04_Trigger_Mesh", ActionPart = "Rifle04_Detail_Mesh", ActionTravelAxis = Vector3.back, SupportGrip = true,
            UxrTag = "BackWeapon", MagazinePart = "Rifle04_Magazine_Mesh", MagazineTag = "MagMP5K", MagazineCapacity = 30,
            PackSounds = new HandsPackSoundRecipe
            {
                Shot = Snd + "1Assault_Rifle_Set/Rifle_Shot_3.mp3",
                Reload = Snd + "1Assault_Rifle_Set/Reload.mp3", ReloadClip = "Reload", SlideFromReload = true
            }
        });

        /// <summary>Стартовый пистолет (роль Glock): Walther PPK. Патроны — часть магазина.</summary>
        public static HandsPackWeaponRecipe PPK => Pistol(new HandsPackWeaponRecipe
        {
            Name = "PPK", PrefabFolder = "PPK", Folder = "Hands_Gun02", BodyPart = "Gun02_Body_Mesh",
            PoseClip = "Aim_Idle", ActionClip = "Shot",
            TargetLength = 0.20f, // PPK 15.5 см, но модель пака крупнее: по руке пака 20 см
            StaticParts = new[] { "Gun02_Detail_02_Mesh" },
            TriggerPart = "Gun02_Trigger_Mesh", ActionPart = "Gun02_Detail_01_Mesh", SupportGrip = true,
            UxrTag = "Gun", MagazinePart = "Gun02_Magazine_Mesh", MagazineExtraParts = new[] { "Gun02_Bulets_Mesh" },
            MagazineTag = "MagPPK", MagazineCapacity = 20,
            PackSounds = new HandsPackSoundRecipe
            {
                Shot = Snd + "8Pistol/Shot_1.mp3", Reload = Snd + "8Pistol/Reload_Full.mp3", ReloadClip = "Reload",
                Bolt = Snd + "8Pistol/Zatvor.mp3"
            }
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
                HandsPackWeaponRecipe r = Pistol(new HandsPackWeaponRecipe
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
                // Выстрел пака Hands (8Pistol/9Run_Set) — тот же, что у лёгких пистолетов, и звучал «как с глушителем»
                // (жалоба пользователя 2026-10-01); свой громкий — выстрел револьвера R08 из пака KINEMATION
                // (KinemationWeaponBuilder.MakeRevolverShot). Звука барабана в наборе пака нет — снятие общим звуком.
                r.ShotAudio = KinemationWeaponBuilder.RevolverShotPath;
                return r;
            }
        }

        /// <summary>
        /// Тяжёлая винтовка (роль SSG 08), заглушка: болтовой механики у сборщика нет — затвор идёт по прямой,
        /// как у автомата (рукоять не поднимается), но огонь ручной: затвор после каждого выстрела.
        /// </summary>
        public static HandsPackWeaponRecipe SniperRifle => Rifle(new HandsPackWeaponRecipe
        {
            Name = "SniperRifle", PrefabFolder = "SniperRifle", Folder = "Hands_Sniper_Rifle", BodyPart = "Sniper_Rifle_Base_Mesh",
            PoseClip = "Aiming_Idle", ActionClip = "Shot", RestClip = "Idel",
            TargetLength = 1.15f, // AX-50: ~123 см; по руке пака 115
            StaticParts = new string[0],
            TriggerPart = "Sniper_Rifle_Triger_Mesh", ActionPart = "Sniper _Rifle_Gate_End_Mesh",
            ActionExtraParts = new[] { "Sniper _Rifle_Gate_Mesh" }, SupportGrip = true, ManualCycle = true,
            UxrTag = "BackWeapon", MagazinePart = "Sniper_Rifle_Magazine_Mesh", MagazineTag = "MagSniper", MagazineCapacity = 10,
            PackSounds = new HandsPackSoundRecipe
            {
                Shot = Snd + "11Sniper_Rifle_Set/Sniper_Rifle_Shot_1.mp3",
                Reload = Snd + "11Sniper_Rifle_Set/Reload.mp3", ReloadClip = "Recharge", SlideFromReload = true
            }
        });

        /// <summary>
        /// Автоматическая винтовка дальнего боя (arsenal-combat-lineup, решение пользователя 2026-10-23): вид SniperRifle
        /// (AX-50 пака Hands), поведение Mk14 — автоматический огонь, отъёмный магазин на 10; оптика ×2 — комплект прицела.
        /// Та же модель и детали, что у <see cref="SniperRifle" />, свой префаб и магазин; тег магазина общий (тот же магазин).
        /// </summary>
        public static HandsPackWeaponRecipe SniperRifleAuto
        {
            get
            {
                HandsPackWeaponRecipe r = SniperRifle;
                r.Name = "SniperRifleAuto";
                r.PrefabFolder = "SniperRifleAuto";
                // Автоматический огонь — из баланса (WeaponInfo.FullAuto, решение пользователя 2026-10-23): механизм не ручной,
                // в отличие от болтовой SniperRifle, иначе сверка «паспорт ↔ механизм» откажет.
                r.ManualCycle = false;
                // Свой выстрел (WeaponShotSoundTests): обрезанный выстрел снайперского набора пака Hands — см. MakeSniperRifleAutoShot;
                // нарезка выстрела пака у этого ствола не нужна (перезарядка и затвор — общие с SniperRifle).
                r.ShotAudio = SniperRifleAutoShotPath;
                r.PackSounds.Shot = null;
                return r;
            }
        }

        /// <summary>Выстрел SniperRifleAuto: начало выстрела снайперского набора пака Hands, без цикла болта.</summary>
        public const string SniperRifleAutoShotPath = "Assets/Audio/SFX/Weapons/SniperRifleAuto/SniperRifleAuto_Shot.wav";
        private const string SniperRifleAutoShotSource =
            "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/11Sniper_Rifle_Set/Sniper_Rifle_Shot_2.mp3";

        /// <summary>
        /// Делает <see cref="SniperRifleAutoShotPath" /> и ставит его на спуск префаба SniperRifleAuto (без пересборки: одно
        /// поле). Файлы выстрелов снайперского набора пака (1,88 с) несут и звук болтовки (подъём, назад, вперёд, вниз
        /// после 0,55 с): у автомата он звучал на каждый выстрел как перезарядка (шлем 2026-10-23). Оставляем 0,5 с.
        /// </summary>
        public static string MakeSniperRifleAutoShot()
        {
            string folder = SniperRifleAutoShotPath.Substring(0, SniperRifleAutoShotPath.LastIndexOf('/'));
            KinemationWeapon.EnsureFolder(folder);
            AudioClip clip = KinemationAudio.Shot(SniperRifleAutoShotSource, 0.5f, folder,
                                                  System.IO.Path.GetFileNameWithoutExtension(SniperRifleAutoShotPath));
            string prefabPath = $"Assets/Prefabs/Weapons/{SniperRifleAuto.PrefabFolder}/{SniperRifleAuto.Name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) return $"{SniperRifleAutoShotPath} ({clip.length:F2} с); префаба ещё нет";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var so = new SerializedObject(root.GetComponent<UxrFirearmWeapon>());
                so.FindProperty("_triggers").GetArrayElementAtIndex(0).FindPropertyRelative("_shotAudio._clip").objectReferenceValue = clip;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return $"{prefabPath}: выстрел {SniperRifleAutoShotPath} ({clip.length:F2} с)";
        }

        public static IEnumerable<HandsPackWeaponRecipe> T38 => new[] { Scar, Uzi, MP5K, PPK, Revolver, SniperRifle, SniperRifleAuto };

        /// <summary>
        /// Все стволы пака Hands на полном рецепте: T-38, AR15 и FabarmSDASS (решение 2026-10-23). BrowningHiPower остаётся
        /// на легаси-сборке (<see cref="HandsPackLegacyRebuild.Browning" />): Shot_end, HoldOpen и расширение хода.
        /// </summary>
        public static IEnumerable<HandsPackWeaponRecipe> Full => T38.Concat(new[] { AR15, ShotgunReal });

        private static void BuildT38() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenBuildSet(T38.Select(r => $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.Name}.prefab"));

        public static string WeaponPath(HandsPackWeaponRecipe r) => $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.Name}.prefab";

        public static string MagazinePath(HandsPackWeaponRecipe r) => $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.MagazineName ?? r.Name + "_mag"}.prefab";

        /// <summary>
        /// Что пишет <see cref="BuildFull" /> помимо текущих префабов ствола и их зависимостей — для отката: области подсветки,
        /// анимация механизма (<see cref="HandsPackMechanism.Folder" />), нарезанные звуки (<see cref="HandsPackAudio.Folder" />).
        /// </summary>
        public static IEnumerable<string> RebuildOutputs(HandsPackWeaponRecipe r)
        {
            // Области: Interaction/<имя> (у стволов KINEMATION — папка ствола KINEMATION).
            var outputs = new List<string> { "Assets/Art/Weapons/Interaction/" + r.Name, KinemationFixReview.InteractionFolder(r.Name) };
            if (r.MechanismMotion) outputs.Add(HandsPackMechanism.Folder(r));
            if (r.PackSounds != null) outputs.Add(HandsPackAudio.Folder(r.Name));
            return outputs;
        }

        /// <summary>
        /// Полный маршрут одного ствола пака Hands: звуки из пака (<see cref="HandsPackAudio" />) → проверка деталей источника
        /// → сборка (точка отдачи и области хвата — по центрам ладоней пака, <see cref="PalmPoints" />) → при
        /// <see cref="HandsPackWeaponRecipe.MechanismMotion" /> анимация механизма и затем хост (<see cref="HandsPackMechanism" />)
        /// → проверка GUID/root localFileID/assetId → перенос хватов из текущего префаба → подсветка магазина → прицел, хост
        /// WeaponSystem, баланс (<see cref="WeaponRebuildFinish" />) → исходные позы пака и snap-точки <c>_orig</c>
        /// (<see cref="PackOrigPoses" />). Позы и хваты аватаров не регистрируются — отдельная команда
        /// (<c>AvatarGripRegistration</c> из <c>_orig</c>, Grip Extraction). <paramref name="poses" /> — только
        /// <see cref="WeaponGripPoseMode.KeepCurrent" />.
        /// </summary>
        public static void BuildFull(HandsPackWeaponRecipe r, WeaponGripPoseMode poses = WeaponGripPoseMode.KeepCurrent)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Пересборка запрещена в Play Mode.");
            WeaponGripCarryOver.RequireBuilderMode(poses, "HandsPackWeaponBuilder");
            string path = WeaponPath(r);
            string magazine = MagazinePath(r);
            // Всё или ничего: отказ любого шага, включая прицел и _orig, возвращает файлы ствола к состоянию до сборки.
            WeaponRebuildFinish.Run("HandsPackWeaponBuilder", path, magazine, RebuildOutputs(r), () =>
            {
                // Внешние ссылки (WeaponInfo, spawnPrefabs, карманы) держатся на GUID и корне — сверка в конце сборки.
                PrefabIdentity identity = PrefabIdentity.Capture(path, magazine);
                // Хваты снимаются до сборки: она перезаписывает оружие и магазин.
                WeaponGripCarryOver.Snapshot grips = WeaponGripCarryOver.Capture(path, magazine);
                // Звуки — до геометрии и на своём экземпляре модели, как у KINEMATION: импорт новых WAV обновляет базу ассетов.
                if (r.PackSounds != null)
                    using (var timing = new HandsPackWeapon(r.Folder, r.BodyPart))
                        HandsPackAudio.MakeSounds(r, timing);

                using var pack = new HandsPackWeapon(r.Folder, r.BodyPart);
                HandsPackMechanism.ResolveStaticParts(r, pack);
                // Ошибка структуры источника — до перезаписи игровых префабов.
                HandsPackMechanism.ValidateSourceParts(r, pack);
                if (r.MechanismMotion)
                {
                    HandsPackMechanism.Export(r, pack);
                    OwnAssetSave.Save(new[] { HandsPackMechanism.Folder(r) });
                }
                // Хост WeaponSystem — после клипов механизма: профиль HoldOpen без них не настраивается.
                Build(r, pack, configureHost: !r.MechanismMotion);
                if (r.MechanismMotion)
                {
                    HandsPackMechanism.Install(path, r, pack);
                    ConfigureHost(path, r);
                }
                identity.Verify();
                WeaponGripCarryOver.Restore(grips, poses, MagazineBaseGeometry(r), DroppedGripAvatars);
                // Подсветка выдаваемого отдельно магазина. Области хвата оружия сборка уже испекла по центрам ладоней
                // пака — перенесённые хваты аватаров их не меняют.
                WeaponInteractionReview.Apply(System.Array.Empty<string>(), new[] { magazine });
            });
        }

        /// <summary>
        /// GUID, persistent localFileID корня и <c>NetworkIdentity._assetId</c> префабов ствола до пересборки: на них держатся
        /// WeaponInfo, <c>spawnPrefabs</c>, карманы и сеть. Новый префаб (ещё не на диске) не сверяется.
        /// </summary>
        internal sealed class PrefabIdentity
        {
            private readonly List<(string Path, string Guid, long Root, uint AssetId)> entries = new List<(string, string, long, uint)>();

            public static PrefabIdentity Capture(params string[] paths)
            {
                var identity = new PrefabIdentity();
                foreach (string path in paths)
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                        identity.entries.Add((path, AssetDatabase.AssetPathToGUID(path), ReadRootLocalId(path), ReadAssetId(path)));
                return identity;
            }

            /// <summary>Отказ, если пересборка сменила GUID, корень или сетевой id; <c>_assetId</c> на диске — сначала канон.</summary>
            public void Verify()
            {
                // SaveAsPrefabAsset пишет _assetId = 0 — канон (от GUID) возвращается до сверки.
                VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(entries.Select(e => e.Path), false);
                foreach (var (path, guid, root, assetId) in entries)
                    if (AssetDatabase.AssetPathToGUID(path) != guid || ReadRootLocalId(path) != root || ReadAssetId(path) != assetId)
                        throw new System.InvalidOperationException($"Изменён GUID/root localFileID/NetworkIdentity assetId: {path}");
            }

            // Без NetworkIdentity — 0: пропажа компонента у сетевого префаба тоже расходится с прежним id.
            internal static uint ReadAssetId(string path)
            {
                var identity = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkIdentity>();
                return identity == null ? 0u : (uint)new SerializedObject(identity).FindProperty("_assetId").longValue;
            }

            internal static long ReadRootLocalId(string path)
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(root, out string _, out long localId) || localId == 0)
                    throw new System.InvalidOperationException($"Нет persistent root localFileID: {path}");
                return localId;
            }
        }

        /// <summary>
        /// Нейтральные точки хвата в осях корня <paramref name="root" />: центр ладони руки пака в кадре позы
        /// (определение SDK <c>UxrAvatarHand.GetPalmCenter</c>). [0] — правая рука на рукояти (точка 0), [1] — левая,
        /// вторая рука (точка 1). Та же величина, что начало snap-точек <c>Orig/&lt;поза&gt;_orig/Right|Left</c>
        /// (<see cref="PackOrigPoses" />, <see cref="GripSample.PalmInBody" />). От аватаров не зависит: по ним сборка
        /// ставит точку отдачи, решает о <see cref="MainGripAimLock" /> и печёт области подсветки
        /// (<see cref="WeaponInteractionRecipes" />). <paramref name="body" /> — корпус в префабе, тот же меш, что в паке;
        /// <paramref name="poseClip" /> — клип пака Hands (у KINEMATION клип позы задаёт модель, у Low Poly клипов нет).
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

                // Центр ладони в осях корпуса: пак Hands — по рукам FPS в клипе позы, Low Poly — из рецепта.
                if (pack is IPalmSource palm)
                    return root.InverseTransformPoint(body.TransformPoint(palm.PalmCenterInBody(side)));
                throw new System.InvalidOperationException($"{pack.GetType().Name}: источник без центров ладоней (IHandGripSource или IPalmSource)");
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
        /// (KINEMATION — <see cref="KinemationFixReview" />, пак Hands — <see cref="HandsPackMechanism.Install" />),
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
            if (r.RootScale < 0f || !Finite(r.RootScale) || (r.RootScale > 0f && r.TargetLength > 0f))
                throw new System.InvalidOperationException($"{r.Name}: масштаб корня задаётся либо RootScale, либо TargetLength");
            RequireCycleMatchesBalance(r);
            if (pack is HandsPackWeapon hands) HandsPackMechanism.ResolveStaticParts(r, hands);
            string folder = $"Assets/Prefabs/Weapons/{r.PrefabFolder}";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Prefabs/Weapons", r.PrefabFolder);

            Scene scene = EditorSceneManager.NewPreviewScene();

            try
            {
                // Масштаб корня: габарит корпуса в единицах меша → реальная длина (или явный масштаб легаси-сборки).
                Mesh body = pack.MeshOf(r.BodyPart);
                float scale = r.RootScale > 0f ? r.RootScale :
                    r.TargetLength > 0f ? r.TargetLength / Mathf.Max(body.bounds.size.x, body.bounds.size.y, body.bounds.size.z) : 1f;

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

        /// <summary>
        /// Ход затвора/помпы в осях готового корня: наибольшее смещение детали из клипа (с проекцией на явную ось тяги). Деталь
        /// в клипе неподвижна — <see cref="HandsPackWeaponRecipe.StaticActionTravel" /> («нет в паке», AR15);
        /// <paramref name="fromRecipe" /> — ход взят из рецепта.
        /// </summary>
        internal static Vector3 ResolveActionTravel(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal, out bool fromRecipe)
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
            fromRecipe = travel.magnitude < 1e-5f && r.StaticActionTravel.HasValue;
            if (!fromRecipe)
            {
                if (travel.magnitude < 1e-5f)
                    GameLog.WeaponSystem.Warning($"[HandsPackWeaponBuilder] {r.Name}: в клипе {r.ActionClip} деталь {r.ActionPart} неподвижна, " +
                                                 "StaticActionTravel не задан — у затвора нет хода");
                return travel;
            }

            // RestrictLocalOffset задаёт box: ход «нет в паке» — по одной оси, от нуля.
            travel = r.StaticActionTravel.Value;
            if (!Finite(travel) || MovingAxes(travel) != 1)
                throw new System.InvalidOperationException($"{r.Name}: StaticActionTravel должен иметь одну ненулевую координату: {travel}");
            return travel;
        }

        /// <summary>
        /// Ось и угол спуска: из клипа; спуск в клипе неподвижен — <see cref="HandsPackWeaponRecipe.StaticTriggerDegrees" />
        /// вокруг X («нет в паке», AR15), без него угол 0, как в паке (Uzi, MP5K).
        /// </summary>
        internal static (int Axis, float Degrees, bool FromRecipe) ResolveTriggerRotation(HandsPackWeaponRecipe r, PartMotion motion)
        {
            if (Mathf.Abs(motion.MaxAngle) >= 1e-5f || !r.StaticTriggerDegrees.HasValue)
                return (DominantAxis(motion.RotationAxis), Mathf.Round(motion.MaxAngle) * Mathf.Sign(AxisComponent(motion.RotationAxis)), false);
            float degrees = r.StaticTriggerDegrees.Value;
            if (!Finite(degrees) || Mathf.Abs(degrees) < 1e-5f)
                throw new System.InvalidOperationException($"{r.Name}: StaticTriggerDegrees должен быть ненулевым числом: {degrees}");
            return (0, degrees, true);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        // RestrictLocalOffset задаёт box. Диагональный ход расширил бы одномерный ход до box.
        private static int MovingAxes(Vector3 value) => (Mathf.Abs(value.x) >= 1e-5f ? 1 : 0) +
            (Mathf.Abs(value.y) >= 1e-5f ? 1 : 0) + (Mathf.Abs(value.z) >= 1e-5f ? 1 : 0);

        /// <summary>
        /// Режим огня — баланс (<see cref="VrBattlegrounds.Arsenal.WeaponInfo.FullAuto" />, <see cref="WeaponBalanceApplier" />),
        /// ручной цикл — свойство механизма (<see cref="HandsPackWeaponRecipe.IsManualCycle" />). Паспорт ствола с автоматическим
        /// огнём при ручном механизме — противоречие: отказ до первой записи.
        /// </summary>
        private static void RequireCycleMatchesBalance(HandsPackWeaponRecipe r)
        {
            if (!r.IsManualCycle) return;
            string path = WeaponPath(r);
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null || AssetDatabase.GetAssetPath(info.WeaponPrefab) != path) continue;
                if (info.FullAuto)
                    throw new System.InvalidOperationException($"{r.Name}: механизм с ручным циклом ({(r.Action == HandsPackWeaponRecipe.ActionKind.Pump ? "помпа" : "ManualCycle")}), " +
                                                               $"а в балансе {info.name} включён автоматический огонь (FullAuto) — исправьте паспорт или рецепт");
            }
        }

        private static GameObject BuildWeapon(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal, float scale, Scene scene, GameObject magPrefab,
                                              bool configureHost)
        {
            var root = new GameObject(r.Name);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.localScale = Vector3.one * scale;

            var rb = root.AddComponent<Rigidbody>();
            HandsPackBuildDefaults.ApplyRigidbody(rb);
            var grabbable = root.AddComponent<UxrGrabbableObject>();
            var projectile = root.AddComponent<UxrProjectileSource>();
            var firearm = root.AddComponent<UxrFirearmWeapon>();
            // Сетевой id — канон от GUID префаба (NetworkAssetIdNormalizer): сборка его не задаёт, GUID сохраняется.
            root.AddComponent<NetworkIdentity>();

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
                BuildAction(r, pack, bodyLocal, root);

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
            HandsPackBuildDefaults.ApplyAnchor(anchorSo);
            anchorSo.FindProperty("_compatibleTags").arraySize = 1;
            anchorSo.FindProperty("_compatibleTags").GetArrayElementAtIndex(0).stringValue = r.MagazineTag;
            anchorSo.FindProperty("_alignTransformUseSelf").boolValue = true;
            anchorSo.FindProperty("_dropProximityTransformUseSelf").boolValue = true;
            anchorSo.ApplyModifiedPropertiesWithoutUndo();
            // Источник звуков гнезда (AnchorSound играет вставку по Placed и снятие по Removed): настройки Unity по умолчанию.
            // Не на Activate On Placed и без Play On Awake: AnchorActivationAudioTests.
            var source = anchorGo.AddComponent<AudioSource>();
            source.playOnAwake = false;
            // Звук вставки принадлежит гнезду (не магазину — иначе звучит дважды): свой кусок перезарядки пака ствола
            // (решение пользователя 2026-10-23) — оверрайд в объекте. Нет своей нарезки — поле пустое, AnchorSound в момент
            // вставки/снятия берёт запасную пару категории ствола (WeaponFeedbackDefaults), затем дефолт роли гнезда
            // (MagazineAnchorSoundDefaults). Дефолты в префаб не копируются. LoadAudio — окну приёма патронов.
            source.clip = string.IsNullOrEmpty(r.InsertAudio) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(r.InsertAudio) ??
                throw new System.InvalidOperationException($"{r.Name}: нет звука вставки {r.InsertAudio}");
            var sound = new SerializedObject(anchorGo.AddComponent<AnchorSound>());
            HandsPackBuildDefaults.ApplyAnchorSound(sound, source, r.TakeOutAudio);
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

            // Хват корня; вторая рука — дополнительная точка (цевьё или поддержка пистолета).
            var so = new SerializedObject(grabbable);
            HandsPackBuildDefaults.ApplyRootGrab(so, r.SupportGrip ? 1 : 0);
            so.FindProperty("_rigidBodySource").objectReferenceValue = rb;
            so.FindProperty("_tag").stringValue = r.UxrTag;
            UseGripAsProximity(so);
            so.FindProperty("_startAnchor").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Снаряд и спуск. Помповое — дробь: второй тип выстрела для дробинок, без вспышки у дула на каждую.
            var ps = new SerializedObject(projectile);
            SerializedProperty shotTypes = ps.FindProperty("_shotTypes");
            bool pellets = r.Action == HandsPackWeaponRecipe.ActionKind.Pump || r.Pellets;
            HandsPackBuildDefaults.NewShots(shotTypes, pellets ? 2 : 1);
            string muzzleEffect = r.MuzzleEffect ?? MuzzleEffect;
            for (int i = 0; i < shotTypes.arraySize; i++)
            {
                SerializedProperty shot = shotTypes.GetArrayElementAtIndex(i);
                shot.FindPropertyRelative("_shotSource").objectReferenceValue = shotSource;
                shot.FindPropertyRelative("_tip").objectReferenceValue = tip;

                // Вид выстрела оружия проекта: белый трассер и попадание без сэмплов SDK
                // (TracerVisibilityTests, ImpactEffectTests).
                shot.FindPropertyRelative("_projectilePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Tracer);
                shot.FindPropertyRelative("_prefabInstantiateOnImpact").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactEffect);
                shot.FindPropertyRelative("_prefabInstantiateOnImpactLife").floatValue = 2f;
                shot.FindPropertyRelative("_prefabScenarioImpactDecal").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UxrImpactDecal>(ImpactDecal);

                // Вспышка у дула — только частицы, привязана к оружию и масштабируется с ним
                // (MuzzleEffectTests). Дробинкам своя вспышка не нужна — она обнуляется ниже.
                shot.FindPropertyRelative("_prefabInstantiateOnTipWhenShot").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(muzzleEffect) ??
                    throw new System.InvalidOperationException($"{r.Name}: нет вспышки у дула {muzzleEffect}");
                shot.FindPropertyRelative("_prefabInstantiateOnTipParent").boolValue = true;
                shot.FindPropertyRelative("_prefabInstantiateOnTipLife").floatValue = 1f;

                // Отличия снаряда ствола от общего набора (FABARM: дробина сэмпла SDK, толчок трупа, декали только на Default).
                if (r.ProjectileLength.HasValue) shot.FindPropertyRelative("_projectileLength").floatValue = r.ProjectileLength.Value;
                if (r.ProjectileImpactForce.HasValue) shot.FindPropertyRelative("_projectileImpactForceMultiplier").floatValue = r.ProjectileImpactForce.Value;
                if (r.DecalLayers.HasValue) shot.FindPropertyRelative("_createDecalLayerMask").intValue = r.DecalLayers.Value;

                if (pellets)
                {
                    // 8 дробинок × 14 — 112 в упор при здоровье 100 (ShotgunPelletsTests); урон и дальность затем — баланс.
                    shot.FindPropertyRelative("_projectileDamageNear").floatValue = 14f;
                    shot.FindPropertyRelative("_projectileDamageFar").floatValue = 4f;
                    shot.FindPropertyRelative("_projectileMaxDistance").floatValue = 60f;
                    if (i > 0) shot.FindPropertyRelative("_prefabInstantiateOnTipWhenShot").objectReferenceValue = null;
                }
            }
            ps.ApplyModifiedPropertiesWithoutUndo();
            if (pellets) root.AddComponent<ShotgunPellets>();

            var fw = new SerializedObject(firearm);
            var recoil = Empty("RecoilSourceAxes", container, Vector3.zero);
            fw.FindProperty("_recoilAxes").objectReferenceValue = recoil;
            // Без затвора патрон досылать нечем: выстрел — прямо из барабана.
            SerializedProperty trig = HandsPackBuildDefaults.NewTrigger(fw.FindProperty("_triggers"), r.Action != HandsPackWeaponRecipe.ActionKind.None,
                HandsPackBuildDefaults.CategoryDefaults(r.Name));
            trig.FindPropertyRelative("_triggerGrabbable").objectReferenceValue = grabbable;
            trig.FindPropertyRelative("_grabbableGrabPointIndex").intValue = 0;
            trig.FindPropertyRelative("_triggerTransform").objectReferenceValue = parts[r.TriggerPart];
            var triggerRotation = ResolveTriggerRotation(r, triggerMotion);
            trig.FindPropertyRelative("_triggerRotationAxis._axis").intValue = triggerRotation.Axis;
            trig.FindPropertyRelative("_triggerRotationDegrees").floatValue = triggerRotation.Degrees;
            trig.FindPropertyRelative("_ammunitionMagAnchor").objectReferenceValue = anchor;
            // Ручной цикл — от механизма; иначе режим огня пишет баланс по WeaponInfo.FullAuto (общий конец пересборки),
            // до него — полуавтомат.
            trig.FindPropertyRelative("_cycleType").intValue = (int)(r.IsManualCycle ? UxrShotCycle.ManualReload : UxrShotCycle.SemiAutomatic);
            if (!string.IsNullOrEmpty(r.ShotAudio))
                trig.FindPropertyRelative("_shotAudio._clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(r.ShotAudio) ??
                    throw new System.InvalidOperationException($"{r.Name}: нет звука выстрела {r.ShotAudio}");
            else GameLog.WeaponSystem.Warning($"[HandsPackWeaponBuilder] {r.Name}: у рецепта нет звука выстрела (ShotAudio/PackSounds.Shot)");
            fw.ApplyModifiedPropertiesWithoutUndo();

            // Нейтральные точки хвата — центры ладоней пака: правая — рукоять, левая — цевьё/поддержка. Записей
            // хвата аватаров сборка не пишет; точка отдачи — у правой ладони на рукояти. RecoilAccumulator и WeaponSpread
            // ставит баланс (WeaponBalanceApplier.EnsureComponent).
            Vector3[] palms = PalmPoints(pack, r.PoseClip, root.transform, parts[r.BodyPart]);
            recoil.position = root.transform.TransformPoint(palms[0]);

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

        /// <summary>
        /// Помпа или затвор: ход из клипа (или «нет в паке» из рецепта), общий граббабл механизма
        /// (<see cref="HandsPackBuildDefaults.ApplyActionGrab" />) и вход хоста — <see cref="UxrShotgunPump" /> или
        /// <see cref="AutomaticWeaponSlideFeedback" /> с порогом, возвратом и звуками рецепта.
        /// </summary>
        private static void BuildAction(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal, GameObject root)
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
            HandsPackBuildDefaults.ApplyActionGrab(ag);
            ag.FindProperty("_translationConstraintMode").enumValueIndex = (int)UxrTranslationConstraintMode.RestrictLocalOffset;
            ag.FindProperty("_translationLimitsMin").vector3Value = Vector3.Min(travel, Vector3.zero);
            ag.FindProperty("_translationLimitsMax").vector3Value = Vector3.Max(travel, Vector3.zero);
            ag.FindProperty("_rotationConstraintMode").enumValueIndex = (int)UxrRotationConstraintMode.RestrictLocalRotation;
            UseGripAsProximity(ag);

            // Рукоятка затвора не наводит ствол и от винтовки не зависит (как Recoil у M16). Помпу держит вторая рука,
            // она наводит ствол, как цевьё (так у сэмплового дробовика SDK, PumpAimTests).
            bool isPump = r.Action == HandsPackWeaponRecipe.ActionKind.Pump;
            ag.FindProperty("_controlParentDirection").boolValue = isPump;
            ag.FindProperty("_ignoreGrabbableParentDependency").boolValue = !isPump;
            ag.ApplyModifiedPropertiesWithoutUndo();

            if (isPump)
            {
                var pump = new SerializedObject(root.AddComponent<UxrShotgunPump>());
                HandsPackBuildDefaults.ApplyPump(pump);
                pump.FindProperty("_pump").objectReferenceValue = actionGrab;
                pump.FindProperty("_localPumpDirection").vector3Value = travel.normalized;
                pump.FindProperty("_localPumpOffset").vector3Value = travel;
                // Хост читает звуки помпы по смыслу: _audioSlide — оттяжка, _audioSlideBack — возврат (WeaponSystemAuthoring).
                HandsPackBuildDefaults.SetAudio(pump, "_audioSlide", r.SlideBackAudio, HandsPackBuildDefaults.PumpVolume);
                HandsPackBuildDefaults.SetAudio(pump, "_audioSlideBack", r.SlideForwardAudio, HandsPackBuildDefaults.PumpVolume);
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
                HandsPackBuildDefaults.ApplySlide(slide);
                slide.FindProperty("_slide").objectReferenceValue = actionGrab;
                // Свои звуки затвора (куски пака) — и для пустого, и для заряженного хода; без них — дефолт категории хоста.
                foreach (var (field, audio) in new[] { ("_audioSlideBack", r.SlideBackAudio), ("_audioSlideBackWhenLoaded", r.SlideBackAudio),
                                                       ("_audioSlideForward", r.SlideForwardAudio), ("_audioSlideForwardWhenLoaded", r.SlideForwardAudio) })
                    HandsPackBuildDefaults.SetAudio(slide, field, audio);
                slide.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Близость руки к точке хвата — от её же точки выравнивания (Use Self), как у принятых стволов. Без флага при
        /// пустом трансформе близость мерилась от корня, и вторая рука у револьвера выбирала основную точку
        /// (GunTwoHandGrabTests).
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
            if (template != path) ClearTemplateGrips(mag);
            mag.name = r.MagazineName ?? r.Name + "_mag";
            mag.transform.localScale = Vector3.one * scale;
            // Старые Animation/Animator шаблона не должны повторно двигать детали нового механизма. Сетевой id — канон от
            // GUID магазина (NetworkAssetIdNormalizer), GUID и корень сохраняются.
            foreach (var animation in mag.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(animation);
            foreach (var animator in mag.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);

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
            // Звук вставки задаёт AnchorSound гнезда; свой звук размещения боеприпаса (MagPlace шаблона) дал бы двойной.
            // Звук хвата шаблона (SDK Magazine_attach) звучал при каждом взятии, в том числе вместе со звуком кармана:
            // доставание озвучивает AnchorSound приёмника (решение пользователя 2026-10-23).
            foreach (var audio in mag.GetComponentsInChildren<UltimateXR.Audio.UxrAudioManipulation>(true))
            {
                var place = new SerializedObject(audio);
                place.FindProperty("_audioOnPlace._clip").objectReferenceValue = null;
                place.FindProperty("_audioOnGrab._clip").objectReferenceValue = null;
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
            string own = MagazinePath(r);
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
        /// Аватары, выведенные из схемы хватов (решение пользователя 2026-10-23): MEF_Base_Avatar в игре не используется,
        /// хваты семейства Cyborg регистрирует <c>AvatarGripRegistration</c> из <c>_orig</c>. Их записи сборка не пишет и не переносит.
        /// </summary>
        public static readonly string[] RetiredGripAvatars = { HandsPackPoseImporter.MefBaseAvatarGuid };

        /// <summary>Аватары, чьи записи хвата сборка убирает и не переносит: сэмплы SDK и выведенные из схемы.</summary>
        public static readonly string[] DroppedGripAvatars = SdkSampleAvatars.Concat(RetiredGripAvatars).ToArray();

        /// <summary>Папка выравнивания выведенного аватара в <c>Grabs</c> (MEF у доноров набора Hands).</summary>
        private const string RetiredGripFolder = "MEF_Base_Avatar";

        /// <summary>
        /// Записи хвата убранных аватаров (<see cref="DroppedGripAvatars" />: у доноров набора Hands их точки лежат в собственном
        /// <c>Grabs</c>) и записи, у которых обе трансформы выравнивания удалены вместе с геометрией базы; ставшие ненужными
        /// точки — тоже. Дополнительные точки хвата магазина без записей удаляются (вторая точка BrowningHiPower_Magazine
        /// из сэмпла SDK — решение пользователя 2026-10-23), как и пустые папки выведенных аватаров.
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
                    bool dropped = DroppedGripAvatars.Contains(entry.FindPropertyRelative("_avatarPrefabGuid").stringValue);
                    if (!dropped && (left != null || right != null)) continue;
                    orphans.Add(left);
                    orphans.Add(right);
                    entries.DeleteArrayElementAtIndex(i);
                }
            for (int i = additional.arraySize - 1; i >= 0; i--)
                if (additional.GetArrayElementAtIndex(i).FindPropertyRelative("_avatarGripPoseEntries").arraySize == 0)
                    additional.DeleteArrayElementAtIndex(i);
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (Transform orphan in orphans.Where(t => t != null).Distinct().ToList())
                if (orphan != null) HandsPackPoseImporter.RemoveUnused(grabbable.gameObject, orphan, null);
            foreach (Transform folder in grabbable.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.name == RetiredGripFolder).ToList())
                if (folder != null && folder.GetComponentsInChildren<Transform>(true).All(t => t.GetComponents<Component>().Length == 1))
                    Object.DestroyImmediate(folder.gameObject);
        }

        // ── Вспомогательное ─────────────────────────────────────────────────────

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
