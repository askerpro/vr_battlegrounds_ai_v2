using System.Collections.Generic;
using System.Linq;
using UltimateXR.Core;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Avatars;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Ствол из пака KINEMATION Tactical Shooter (T-39): что взять из пака. Остальное — общий рецепт
    /// <see cref="HandsPackWeaponRecipe" /> (доноры, теги, магазин), сборка — тем же <see cref="HandsPackWeaponBuilder" />.
    /// </summary>
    public sealed class KinemationWeaponRecipe
    {
        public HandsPackWeaponRecipe Weapon;

        public string PackPrefab;     // W_Oryx_SRM-12
        public string AnimFolder;     // SRM-12 — Animations/<папка>/{Character,Weapon}
        public string PoseClip;       // клип персонажа с руками на оружии: A_FP_SRM-12_Idle_Pose
        public string RestClip;       // клип покоя оружия A_W_*_Idle: ставит магазин в гнездо (в позе префаба пака он не всегда там)
        public string[] Excluded;     // детали, которых в префабе нет: патроны магазина кроме верхнего, пружина; «X*» — по началу имени
        public string[] Attachments;  // обвесы пака (статичные MeshRenderer: глушитель, корпус коллиматора) — в префаб статичными деталями

        public string ReloadClip;     // клип оружия перезарядки (A_W_*_Reload_Empty)
        public string ShotSound;      // SFX/…/*.wav — выстрел
        public float ShotLength = 1.5f;
        public string ReloadSound;    // SFX/…/*_Reload_Empty.wav — из него режутся магазин (и затвор, если нет BoltSound)
        public string BoltSound;      // отдельный звук затвора (SRM-12: ShotBoltOnly) — делится по паузе на «назад» и «вперёд»
        public string MagOutSound;    // готовый звук снятия магазина (R08: открыть барабан) — вместо нарезки перезарядки
        public string MagInSound;     // готовый звук вставки (R08: закрыть барабан; 11-87: патрон в трубку)
        public string BoltClip;       // клип оружия, где ходит затвор для нарезки звука (по умолчанию ReloadClip)
        public string BoltSoundSource; // файл, из которого режется затвор (по умолчанию ReloadSound)
    }

    /// <summary>
    /// Собирает стволы KINEMATION: геометрия и механика — <see cref="KinemationWeapon" />, звуки под Quest —
    /// <see cref="KinemationAudio" />, позы рук MEF — <see cref="KinemationPoseExtractor" /> через
    /// <see cref="HandsPackPoseImporter" />. Регистрация (WeaponInfo, реестр, сеть, стена, баланс, теги) — шаги
    /// скилла <c>/add-weapon</c>, не сборщика.
    /// </summary>
    public static class KinemationWeaponBuilder
    {
        public const string PoseFolder = "Assets/Art/HandPoses/Kinemation";
        private const string Sfx = KinemationWeapon.Pack + "SFX/";

        /// <summary>Доноры — как у стволов пака Hands: M16 (физика и хват, снаряд, рукоятка затвора), магазин от пулемёта.</summary>
        private static HandsPackWeaponRecipe Rifle(HandsPackWeaponRecipe r) => Kinemation(HandsPackWeaponBuilder.RifleDonor(r));

        /// <summary>Доноры пистолета — Gun_real.</summary>
        private static HandsPackWeaponRecipe Pistol(HandsPackWeaponRecipe r) => Kinemation(HandsPackWeaponBuilder.PistolDonor(r));

        private static HandsPackWeaponRecipe Kinemation(HandsPackWeaponRecipe r)
        {
            r.TargetLength = 0f; // пак в реальном масштабе
            r.BodyPart = "Body";
            r.RestClip = null;   // покой — поза префаба пака
            return r;
        }

        /// <summary>
        /// Скаут (роль SSG 08): Desert Tech SRS — болтовая булл-пап .338, магазин на 5. Затвор пока как у
        /// <c>SniperRifle</c>: по прямой на полный ход из клипа выстрела (поворот 60° — отдельная задача, T-39 шаг 6),
        /// огонь ручной — затвор после каждого выстрела. Сошки сложены, прицел пака не ставится.
        /// </summary>
        public static KinemationWeaponRecipe SRM12
        {
            get
            {
                HandsPackWeaponRecipe r = Rifle(new HandsPackWeaponRecipe
                {
                    Name = "SRM12", PrefabFolder = "SRM12",
                    PoseClip = "A_FP_SRM-12_Idle_Pose", ActionClip = "A_W_SRM-12_Fire",
                    TriggerPart = "Trigger", ActionPart = "Bolt", SupportGrip = true,
                    UxrTag = "BackWeapon", MagazinePart = "SRS_Magazine", MagazineExtraParts = new[] { "Bullet" },
                    MagazineTag = "MagSRM12", MagazineCapacity = 5
                });
                r.ShotCycle = UxrShotCycle.ManualReload;
                return new KinemationWeaponRecipe
                {
                    Weapon = r, PackPrefab = "W_Oryx_SRM-12", AnimFolder = "SRM-12", RestClip = "A_W_SRM-12_Idle", PoseClip = "A_FP_SRM-12_Idle_Pose",
                    Excluded = new[] { "Bullet_001", "Bullet_002", "Bullet_003", "Bullet_004", "Spring", "Follower" }, ReloadClip = "A_W_SRM-12_Reload_Empty",
                    ShotSound = Sfx + "SRM-12/Actions/S_SRM-12_ShotOnly.WAV", ShotLength = 2f,
                    ReloadSound = Sfx + "SRM-12/Actions/S_SRM-12_EmptyReload.WAV",
                    BoltSound = Sfx + "SRM-12/Actions/S_SRM-12_ShotBoltOnly.WAV"
                };
            }
        }

        /// <summary>
        /// Тяжёлая марксманская (роль SCAR-20): Mk 14 EBR, магазин на 20. Затвор — рукоятка затворной рамы
        /// (<c>BoltCharger</c>), сам затвор (<c>Bolt</c>) ходит с ней. Спуск в клипах пака не двигается (угол 0, как у Uzi).
        /// </summary>
        public static KinemationWeaponRecipe Mk14 => new KinemationWeaponRecipe
        {
            Weapon = Rifle(new HandsPackWeaponRecipe
            {
                Name = "Mk14", PrefabFolder = "Mk14", PoseClip = "A_FP_Mk14EBR_Idle", ActionClip = "A_W_Mk14EBR_Fire",
                TriggerPart = "Trigger", ActionPart = "BoltCharger", ActionExtraParts = new[] { "Bolt" }, SupportGrip = true,
                UxrTag = "BackWeapon", MagazinePart = "Mk14_Magazine", MagazineExtraParts = new[] { "Mk14_Ammo_001", "Mk14_Ammo_002" },
                MagazineTag = "MagMk14", MagazineCapacity = 20
            }),
            PackPrefab = "W_Mk14EBR", AnimFolder = "Mk14EBR", PoseClip = "A_FP_Mk14EBR_Idle",
            Excluded = new[] { "Mk14_Ammo_*", "Mk14_Follower", "Mk14_MagString" }, ReloadClip = "A_W_Mk14EBR_Reload_Empty",
            ShotSound = Sfx + "Mk14EBR/S_Mk14EBR_Fire_01.WAV", ReloadSound = Sfx + "Mk14EBR/S_Mk14EBR_Reload_Empty.WAV"
        };

        /// <summary>Винтовка (роль AK-47): АК-105, магазин на 30. Затвор — рукоятка затворной рамы (<c>Charger</c>).</summary>
        public static KinemationWeaponRecipe AK105 => new KinemationWeaponRecipe
        {
            Weapon = Rifle(new HandsPackWeaponRecipe
            {
                Name = "AK105", PrefabFolder = "AK105", PoseClip = "A_FP_AK105_Idle", ActionClip = "A_W_AK105_Fire",
                MuzzlePart = "Muzzle",
                TriggerPart = "Trigger", ActionPart = "Charger", ActionGripPart = "Charger", ActionGripContact = new Vector3(0.042f, 0.105f, 0.005f), SupportGrip = true,
                UxrTag = "BackWeapon", MagazinePart = "Magazine", MagazineExtraParts = new[] { "Ammo_001", "Ammo_002" },
                MagazineTag = "MagAK105", MagazineCapacity = 30
            }),
            PackPrefab = "W_AK105", AnimFolder = "AK105", RestClip = "A_W_AK105_Idle", PoseClip = "A_FP_AK105_Idle",
            Excluded = new[] { "Ammo_*", "Follower", "Spring" }, ReloadClip = "A_W_AK105_Reload_Empty",
            ShotSound = Sfx + "AK105/Fire/S_AK105_Fire_Unsuppressed_0.wav", ReloadSound = Sfx + "AK105/Actions/S_AK105_Reload_Empty.WAV"
        };

        /// <summary>Пистолет-пулемёт (роль MP9): MKR9, магазин на 30. Второй магазин префаба пака (запасной в руке) не берётся.</summary>
        public static KinemationWeaponRecipe MKR9 => new KinemationWeaponRecipe
        {
            Weapon = Rifle(new HandsPackWeaponRecipe
            {
                Name = "MKR9", PrefabFolder = "MKR9", PoseClip = "A_FP_MKR9_Idle", ActionClip = "A_W_MKR9_Fire",
                TriggerPart = "Trigger", ActionPart = "Bolt", ActionGripPart = "ChargingHandle", ActionGripContact = new Vector3(0f, 0.012f, 0.022f), ActionExtraParts = new[] { "ChargingHandle" }, SupportGrip = true,
                UxrTag = "BackWeapon", MagazinePart = "Mag", MagazineExtraParts = new[] { "Ammo_01", "Ammo_02" },
                MagazineTag = "MagMKR9", MagazineCapacity = 30
            }),
            PackPrefab = "W_MKR9", AnimFolder = "MKR9", RestClip = "A_W_MKR9_Idle", PoseClip = "A_FP_MKR9_Idle",
            Excluded = new[] { "Ammo_*", "Follower", "Spring", "SKM_MKR9_Mag.*" }, ReloadClip = "A_W_MKR9_Reload_Empty",
            ShotSound = Sfx + "MKR9/S_MKR9_Shot_01.WAV", ReloadSound = Sfx + "MKR9/S_MKR9_Empty_Reload.WAV"
        };

        /// <summary>
        /// Стартовый пистолет (роль Glock-18, <c>WeaponRegistry.DefaultSidearm</c>): WK-11 Viper (2011), затвор-кожух
        /// <c>Bolt</c>.
        /// </summary>
        public static KinemationWeaponRecipe Viper => new KinemationWeaponRecipe
        {
            Weapon = Pistol(new HandsPackWeaponRecipe
            {
                Name = "Viper", PrefabFolder = "Viper", PoseClip = "A_FP_WK-11_Viper_Idle_Pose", ActionClip = "A_W_WK-11_Viper_Fire",
                TriggerPart = "Trigger", ActionPart = "Bolt", ActionGripContact = new Vector3(0f, -0.025f, 0f), SupportGrip = true,
                UxrTag = "Gun", MagazinePart = "Magazine", MagazineExtraParts = new[] { "Cartridge_026", "Cartridge_025" },
                MagazineTag = "MagViper", MagazineCapacity = 20
            }),
            PackPrefab = "W_WK-11_Viper", AnimFolder = "WK-11_Viper", RestClip = "A_W_WK-11_Viper_Idle", PoseClip = "A_FP_WK-11_Viper_Idle_Pose",
            Excluded = new[] { "Cartridge*", "String", "Follower" }, ReloadClip = "A_W_WK-11_Viper_Reload_Empty",
            ShotSound = Sfx + "WK-11_Viper/Fire/S_WK-11_Viper_Shot_1_With_Tail.wav", ShotLength = 1.2f,
            ReloadSound = Sfx + "WK-11_Viper/Actions/S_WK-11_Viper_Reload_Empty.WAV"
        };

        /// <summary>
        /// Тяжёлый пистолет (роль R8 Revolver): револьвер на 8. Затвора нет, «магазин» — барабан с патронами, как у
        /// <c>Revolver</c> пака Hands; звуки барабана у пака раздельные (открыть/закрыть). Ускоритель заряжания не берётся.
        /// </summary>
        public static KinemationWeaponRecipe R08
        {
            get
            {
                HandsPackWeaponRecipe r = Pistol(new HandsPackWeaponRecipe
                {
                    Name = "R08", PrefabFolder = "R08", PoseClip = "A_FP_R08_Idle", ActionClip = "A_W_R08_Fire",
                    TriggerPart = "Trigger", SupportGrip = true,
                    UxrTag = "Gun", MagazinePart = "Cylinder",
                    MagazineExtraParts = new[] { "Ammo_01", "Ammo_02", "Ammo_03", "Ammo_04", "Ammo_05", "Ammo_06", "Ammo_07", "Ammo_08", "Ejector" },
                    MagazineTag = "MagR08", MagazineCapacity = 8
                });
                r.Action = HandsPackWeaponRecipe.ActionKind.None;
                return new KinemationWeaponRecipe
                {
                    Weapon = r, PackPrefab = "W_R08", AnimFolder = "R08", RestClip = "A_W_R08_Idle", PoseClip = "A_FP_R08_Idle",
                    Excluded = new[] { "Speedloader" },
                    ShotSound = Sfx + "R08/S_R08_Fire_01.WAV", ShotLength = 1.5f,
                    MagOutSound = Sfx + "R08/S_R08_OpenCylinder.WAV", MagInSound = Sfx + "R08/S_R08_CloseCylinder.WAV"
                };
            }
        }

        /// <summary>
        /// Полуавтоматический дробовик (роль XM1014): Remington 11-87, трубчатый магазин на 7 — заряжается патроном в
        /// окно (<c>Feed</c>), как <c>Shotgun_real</c>; затвор — рукоятка <c>Bolt</c>, дробь без помпы.
        /// </summary>
        public static KinemationWeaponRecipe Herrington
        {
            get
            {
                HandsPackWeaponRecipe r = Rifle(new HandsPackWeaponRecipe
                {
                    Name = "Herrington", PrefabFolder = "Herrington", PoseClip = "A_FP_Herrington_11-87_Idle",
                    ActionClip = "A_W_Herrington_11-87_Fire", TriggerPart = "Trigger", ActionPart = "Bolt", SupportGrip = true,
                    UxrTag = "Shotgun", MagazinePart = "Cartridge", LoadingPart = "Feed", MagazineTag = "MagHerrington", MagazineCapacity = 7,
                    MagazineIsInternal = true
                });
                r.MagazineBase = "Assets/Prefabs/Weapons/Shotgun/MagShotgun.prefab";
                r.Pellets = true;
                return new KinemationWeaponRecipe
                {
                    Weapon = r, PackPrefab = "W_Herrington_11-87_Police", AnimFolder = "Herrington_11-87", RestClip = "A_W_Herrington_11-87_Idle", PoseClip = "A_FP_Herrington_11-87_Idle",
                    ShotSound = Sfx + "Herrington_11-87/Fire/S_Herrington_11-87_Unsuppressed_1_With_Tail.WAV",
                    MagOutSound = Sfx + "Herrington_11-87/Actions/S_Herrington_11-87_Reload_Start_Normal.WAV",
                    MagInSound = Sfx + "Herrington_11-87/Actions/S_Herrington_11-87_Reload_Loop.WAV",
                    BoltClip = "A_W_Herrington_11-87_Reload_Empty_Start",
                    BoltSoundSource = Sfx + "Herrington_11-87/Actions/S_Herrington_11-87_Reload_Start_Empty.WAV"
                };
            }
        }

        /// <summary>
        /// Винтовка с глушителем (роль M4A1-S): TR15 (AR-15/M4), магазин на 20 как у M4A1-S (модель магазина — на 30).
        /// Глушитель AR и корпус коллиматора XPS2 — обвесы пака, статичными деталями; дуло — срез глушителя
        /// (<see cref="HandsPackWeaponRecipe.MuzzlePart" />). Голограмма прицела (свой шейдер пака, не URP Lit) и вертикальная
        /// рукоять не берутся — хват рук снят с клипа без рукояти (<c>Idle_Pose_Non_Grip</c>). Затвор — <c>Bolt</c>, рукоятка
        /// взведения (<c>Charger</c>) ходит с ним (в клипах пака она неподвижна, как у MKR9). Выстрел — вариант пака
        /// «с глушителем» (решение пользователя: громкая M16 не нравится, нужно «как в CS с глушителем»).
        /// </summary>
        public static KinemationWeaponRecipe TR15
        {
            get
            {
                HandsPackWeaponRecipe r = Rifle(new HandsPackWeaponRecipe
                {
                    Name = "TR15", PrefabFolder = "TR15", PoseClip = "A_FP_TR15_Idle_Pose_Non_Grip", ActionClip = "A_W_TR15_Fire",
                    TriggerPart = "Trigger", ActionPart = "Bolt", ActionGripPart = "Charger", ActionGripContact = new Vector3(-0.004f, 0f, -0.006f), ActionExtraParts = new[] { "Charger" }, SupportGrip = true,
                    UxrTag = "BackWeapon", MagazinePart = "Magazine", MagazineExtraParts = new[] { "Cartridge_1", "Cartridge_2" },
                    MagazineTag = "MagTR15", MagazineCapacity = 20
                });
                r.MuzzlePart = Silencer;
                return new KinemationWeaponRecipe
                {
                    Weapon = r, PackPrefab = "W_TR15", AnimFolder = "TR15", RestClip = "A_W_TR15_Idle", PoseClip = "A_FP_TR15_Idle_Pose_Non_Grip",
                    Attachments = new[] { Silencer, "SM_Attach_AR15_XPS2" },
                    Excluded = new[] { "Cartridge_*", "Follower", "Spring" }, ReloadClip = "A_W_TR15_Reload_Empty",
                    ShotSound = Sfx + "TR15/Fire/S_TR15_V1_Suppressed_With_Tail_0.WAV",
                    ReloadSound = Sfx + "TR15/Actions/S_TR15_Reload_Empty.WAV"
                };
            }
        }

        public const string Silencer = "SM_Attach_AR15_Silencer";

        /// <summary>Выстрел тяжёлого пистолета <c>Revolver</c> (пак Hands) — из выстрела R08: громкий, плотный, моно.</summary>
        public const string RevolverShotPath = "Assets/Audio/SFX/Weapons/Revolver/Revolver_Shot.wav";
        private const string RevolverShotSource = Sfx + "R08/S_R08_Fire_02.WAV";

        /// <summary>
        /// Делает <see cref="RevolverShotPath" /> из выстрела R08 и ставит его на спуск префаба <c>Revolver</c> (без
        /// пересборки: правка одного поля). R08 здесь служит источником звука независимо от регистрации каталога.
        /// </summary>
        private static void OpenRevolverAudio() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenMaintenance(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Build);

        public static void MakeRevolverShot()
        {
            string folder = RevolverShotPath.Substring(0, RevolverShotPath.LastIndexOf('/'));
            KinemationWeapon.EnsureFolder(folder);
            AudioClip clip = KinemationAudio.Shot(RevolverShotSource, 1.5f, folder, System.IO.Path.GetFileNameWithoutExtension(RevolverShotPath));

            const string prefabPath = "Assets/Prefabs/Weapons/Revolver/Revolver.prefab";
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
            GameLog.Debug.Info($"[KinemationWeaponBuilder] {prefabPath}: выстрел {RevolverShotPath} ({clip.length:F2} с)");
        }

        public static IEnumerable<KinemationWeaponRecipe> All => new[] { SRM12, R08, AK105, Viper, MKR9, Herrington, Mk14, TR15 };

        private static void BuildTr15() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenWeapon(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Build, PrefabPath(TR15));

        private static void BuildSrm12() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenWeapon(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Build, PrefabPath(SRM12));

        private static void BuildAll() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenBuildSet(All.Select(PrefabPath));

        private static void ReportAll() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenTab(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Checks);

        public static string Report(KinemationWeaponRecipe k)
        {
            using var model = Model(k);
            return model.Report();
        }

        private static KinemationWeapon Model(KinemationWeaponRecipe k) =>
            new KinemationWeapon(k.PackPrefab, k.AnimFolder, k.PoseClip, k.Weapon.Name, k.Weapon.BodyPart, k.RestClip, k.Attachments);

        public static string PrefabPath(KinemationWeaponRecipe k) => $"Assets/Prefabs/Weapons/{k.Weapon.PrefabFolder}/{k.Weapon.Name}.prefab";

        public static GameObject Build(KinemationWeaponRecipe k)
        {
            HandsPackWeaponRecipe r = k.Weapon;

            // Звуки — до геометрии и на своём экземпляре модели: импорт новых WAV обновляет базу ассетов, и у
            // экземпляра, созданного до него, меш пака однажды пришёл без треугольников детали (AK105, первая сборка).
            using (KinemationWeapon timing = Model(k))
                MakeSounds(k, timing);

            using KinemationWeapon model = Model(k);

            // Статичные детали — все, что не названы иначе и не выброшены.
            var used = new HashSet<string> { r.BodyPart, r.TriggerPart, r.ActionPart, r.MagazinePart };
            used.UnionWith(r.ActionExtraParts ?? new string[0]);
            used.UnionWith(r.MagazineExtraParts ?? new string[0]);
            string[] excluded = k.Excluded ?? new string[0];
            r.StaticParts = model.Parts.Where(p => !used.Contains(p) && !excluded.Any(e => e.EndsWith("*") ? p.StartsWith(e.TrimEnd('*')) : p == e)).ToArray();

            GameObject saved = HandsPackWeaponBuilder.Build(r, model);
            string path = PrefabPath(k);
            ImportPoses(k, model);
            HandsPackWeaponBuilder.AlignRecoilToMainGrip(path);
            // Тот же узкий установщик восьми KINEMATION: штатная сборка сохраняет исправленные bindings.
            KinemationFixReview.Apply(r.Name);
            if (r.Name == "TR15") Tr15OpticBuilder.Apply();
            AssetDatabase.SaveAssets();
            GameLog.Debug.Info($"[KinemationWeaponBuilder] {path}: статичные детали {string.Join(", ", r.StaticParts)}");
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>Звуки ствола: выстрел, затвор назад/вперёд, магазин снять/вставить — куски звуков пака.</summary>
        private static void MakeSounds(KinemationWeaponRecipe k, KinemationWeapon model)
        {
            HandsPackWeaponRecipe r = k.Weapon;
            string folder = $"{KinemationAudio.Root}/{r.Name}";

            r.ShotAudio = AssetDatabase.GetAssetPath(KinemationAudio.Shot(k.ShotSound, k.ShotLength, folder, $"{r.Name}_Shot"));

            if (k.MagOutSound != null && k.MagInSound != null)
            {
                r.TakeOutAudio = AssetDatabase.GetAssetPath(KinemationAudio.Shot(k.MagOutSound, 1f, folder, $"{r.Name}_MagOut"));
                r.LoadAudio = AssetDatabase.GetAssetPath(KinemationAudio.Shot(k.MagInSound, 1f, folder, $"{r.Name}_MagIn"));
            }
            else
            {
                (float magOut, float magIn) = model.Excursion(r.MagazinePart, k.ReloadClip);
                r.TakeOutAudio = AssetDatabase.GetAssetPath(KinemationAudio.CutAtEvent(k.ReloadSound, magOut, 0.6f, folder, $"{r.Name}_MagOut"));
                r.LoadAudio = AssetDatabase.GetAssetPath(KinemationAudio.CutAtEvent(k.ReloadSound, magIn, 0.6f, folder, $"{r.Name}_MagIn"));
            }

            if (r.Action == HandsPackWeaponRecipe.ActionKind.None) return;
            if (k.BoltSound != null)
            {
                (AudioClip back, AudioClip forward) = KinemationAudio.SplitAtGap(k.BoltSound, folder, $"{r.Name}_BoltBack", $"{r.Name}_BoltForward");
                r.SlideBackAudio = AssetDatabase.GetAssetPath(back);
                r.SlideForwardAudio = AssetDatabase.GetAssetPath(forward);
            }
            else
            {
                string source = k.BoltSoundSource ?? k.ReloadSound;
                (float back, float forward) = model.Excursion(r.ActionPart, k.BoltClip ?? k.ReloadClip);
                // Клип начинается с открытым затвором (пустой магазин: затвор на задержке) — оттягивания в нём нет,
                // звук «назад» берётся с того же места, что «вперёд» (у пистолета это один и тот же щелчок кожуха).
                if (back < 0.05f) back = forward;
                r.SlideBackAudio = AssetDatabase.GetAssetPath(KinemationAudio.CutAtEvent(source, back, 0.45f, folder, $"{r.Name}_BoltBack"));
                r.SlideForwardAudio = AssetDatabase.GetAssetPath(KinemationAudio.CutAtEvent(source, forward, 0.45f, folder, $"{r.Name}_BoltForward"));
            }
        }

        /// <summary>Позы кисти MEF из клипа рук пака: правая — рукоять (точка 0), левая — вторая рука (точка 1).</summary>
        public static IEnumerable<HandsPackPoseRecipe> PoseRecipes(KinemationWeaponRecipe k)
        {
            string weapon = PrefabPath(k);
            yield return new HandsPackPoseRecipe
            {
                Pose = $"Kinemation_{k.Weapon.Name}_Grip", PackSide = UxrHandSide.Right, Weapon = weapon,
                BodyPath = "MeshContainer/Base", GrabPoint = 0, PoseFolder = PoseFolder
            };
            if (!k.Weapon.SupportGrip) yield break;
            yield return new HandsPackPoseRecipe
            {
                Pose = $"Kinemation_{k.Weapon.Name}_Support", PackSide = UxrHandSide.Left, Weapon = weapon,
                BodyPath = "MeshContainer/Base", GrabPoint = 1, PoseFolder = PoseFolder
            };
        }

        /// <summary>Узкий импорт существующих поз: без пересборки геометрии, звуков и motion.</summary>
        public static void ImportPoses(KinemationWeaponRecipe k)
        {
            using var model = Model(k);
            ImportPoses(k, model);
        }

        private static void ImportPoses(KinemationWeaponRecipe k, KinemationWeapon model) =>
            HandsPackPoseImporter.Import(PoseRecipes(k).Select(p => (p, model.GripSample(p.PackSide))).ToList());
    }
}
