using System.Collections.Generic;
using System.IO;
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
        public string PoseClip;        // клип, где руки держат оружие: Aming_Idle
        public string ActionClip;      // клип, где ходят затвор/помпа и спуск: Shot
        public string RestClip = "Idle";
        public float TargetLength;     // габарит в метрах, по реальному прототипу

        public string[] StaticParts;   // детали корпуса без движения
        public string TriggerPart;
        public string ActionPart;      // помпа или затвор; по нему меряется ход
        public string[] ActionExtraParts; // детали, которые ходят вместе с затвором (рукоять затвора болтовки)
        public ActionKind Action;
        public string LoadingPart;     // деталь, у которой встаёт магазин (якорь); null — якорь на месте магазина пака
        public bool SupportGrip;       // вторая рука на корне (цевьё или поддержка пистолета) — точка 1 донора
        public UxrShotCycle? ShotCycle; // режим огня вместо донорского (болтовка — ManualReload: затвор после каждого выстрела)
        public bool Pellets;           // дробь без помпы (полуавтоматический дробовик); у помпового — всегда

        public string UxrTag;          // тег хвата корня: по нему карманы и слоты принимают оружие
        public string MagazineBase;    // префаб магазина, от которого делается вариант (сеть, физика, звук)
        public string MagazinePart;    // меш магазина из пака
        public string[] MagazineExtraParts; // вынимаются вместе с магазином: патроны, гильзы барабана
        public string MagazineTag;     // тег хвата магазина: его принимают якорь и карманы
        public int MagazineCapacity;

        public string GripDonor;       // префаб с вручную настроенным хватом на том же паке
        public string GripDonorFolder; // его папка в паке
        public string GripDonorBody;   // рендерер его корпуса в паке
        public string GripDonorBodyPath; // путь его корпуса в префабе
        public string GripDonorPoseClip;
        public string FirearmDonor;    // префаб, у которого берутся снаряд, звук выстрела, отдача
        public string ShotAudio;       // свой звук выстрела вместо донорского (необязательно)
        public string LoadAudio;       // звук вставки магазина/патрона в якорь (необязательно)
        public string TakeOutAudio;    // звук снятия магазина (необязательно; иначе донорский)
        public string SlideBackAudio;  // оттягивание затвора (необязательно; иначе донорский)
        public string SlideForwardAudio; // обратный ход затвора (необязательно; иначе донорский)
    }

    /// <summary>
    /// Собирает префаб оружия и его магазина по <see cref="HandsPackWeaponRecipe" />.
    ///
    /// <para>
    /// Геометрия и механика — из пака (<see cref="HandsPackWeapon" />): детали на точных местах,
    /// ход помпы/затвора и угол спуска из клипа. Точки хвата — из ладоней FPS-рук пака через
    /// калибровку по донору (<see cref="GripCalibration" />). Настройки, которые не выводятся
    /// из пака (снаряд, звук, отдача, физика хвата), копируются у доноров по полям — без
    /// <c>_uxrUniqueId</c>, чтобы не делить id с донором.
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
        private const string ImpactEffect = "Assets/Prefabs/Weapons/Effects/Impact_Default.prefab";
        private const string ImpactDecal = "Assets/Prefabs/Weapons/Effects/ImpactDecal_Default.prefab";
        private const string MuzzleEffect = "Assets/Prefabs/Weapons/Effects/Muzzle_Default.prefab";

        // Позы хвата настроены под MEF — как у M16 и Gun_real, с которых снята калибровка.
        private static string MefAvatar => AssetDatabase.GUIDToAssetPath("b6fe59db941fa944696ece5e1aabc032");

        public static HandsPackWeaponRecipe ShotgunReal => new HandsPackWeaponRecipe
        {
            Name              = "Shotgun_real",
            PrefabFolder      = "ShotgunReal",
            Folder            = "Hands_Shotgun",
            BodyPart          = "Shogun_Base_mesh",
            PoseClip          = "Aming_Idle",
            ActionClip        = "Shot",
            TargetLength      = 0.72f, // Mossberg 500 Cruiser (пистолетная рукоять, ствол 18.5"): 71 см
            StaticParts       = new[] { "Shogun_Staple_mesh", "Shogun_Gate_mesh" },
            TriggerPart       = "Shogun_Triger_mesh",
            ActionPart        = "Shogun_Fore-End_mesh",
            Action            = HandsPackWeaponRecipe.ActionKind.Pump,
            LoadingPart       = "Shogun_Staple_mesh",
            UxrTag            = "Shotgun",
            MagazineBase      = "Assets/Prefabs/Weapons/Shotgun/MagShotgun.prefab",
            MagazinePart      = "Shogun_Patron_mesh",
            MagazineTag       = "MagShotgun",
            MagazineCapacity  = 6,
            GripDonor         = "Assets/Prefabs/Weapons/M16/M16_Rifle_prefab.prefab",
            GripDonorFolder   = "Hands_Automatic_Rifle03",
            GripDonorBody     = "Rifle_Body_Mesh",
            GripDonorBodyPath = "MeshContainer/Rifle_Body_Mesh",
            GripDonorPoseClip = "Aim_Idle",
            FirearmDonor      = "Assets/Prefabs/Weapons/Shotgun/Shotgun.prefab",
            ShotAudio         = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/10Shotgun_Set/edit/shot.mp3",
            LoadAudio         = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/10Shotgun_Set/Reload_1.mp3"
        };

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Build Shotgun_real From Hands Pack")]
        private static void BuildShotgunReal() => Build(ShotgunReal);

        // ── Оружие T-38 ─────────────────────────────────────────────────────────
        // Длина — по руке пака: длина в паке × 0,707 (как у Gun_real), чтобы хват из клипа пака
        // ложился в ладонь. Доноры: длинноствольное — M16 (хват, снаряд, автоматический огонь),
        // пистолеты — Gun_real. Позы кисти снимаются отдельно — HandsPackPoseImporter.Recipes.

        private const string M16 = "Assets/Prefabs/Weapons/M16/M16_Rifle_prefab.prefab";
        private const string GunReal = "Assets/Prefabs/Weapons/GunReal/Gun_real.prefab";
        private const string MagRifleBase = "Assets/Prefabs/Weapons/Machinegun/MagMachinegun.prefab";
        private const string MagPistolBase = "Assets/Prefabs/Weapons/Gun/MagGun.prefab";

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
            TriggerPart = "Rifle04_Trigger_Mesh", ActionPart = "Rifle04_Detail_Mesh", SupportGrip = true,
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

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Build T-38 Weapons From Hands Pack")]
        private static void BuildT38()
        {
            foreach (HandsPackWeaponRecipe r in T38)
            {
                Build(r);
                // Хват MEF из кадра пака (поза кисти + место ладони) поверх калибровочного хвата сборщика.
                string path = $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.Name}.prefab";
                VrBattlegrounds.Editor.Avatars.HandsPackPoseImporter.ImportFor(path);
                AlignRecoilToMainGrip(path);
            }
        }

        /// <summary>
        /// Точка отдачи — у правой ладони на рукояти. После позы пака хват MEF стоит по кадру пака, а не по
        /// калибровке сборщика (у револьвера — другой риг рук, калибровка с Gun_real ошибается на сантиметры).
        /// </summary>
        public static void AlignRecoilToMainGrip(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform recoil = root.transform.Find("MeshContainer/RecoilSourceAxes");
                Transform grip = root.GetComponent<UxrGrabbableObject>().GetGrabPoint(0).GetGripPoseInfo(MefAvatarComponent()).GripAlignTransformHandRight;
                if (recoil == null || grip == null) return;
                recoil.position = grip.position;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
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
        public static GameObject Build(HandsPackWeaponRecipe r, IWeaponModel pack)
        {
            string folder = $"Assets/Prefabs/Weapons/{r.PrefabFolder}";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Prefabs/Weapons", r.PrefabFolder);

            Scene scene = EditorSceneManager.NewPreviewScene();

            try
            {
                // Масштаб корня: габарит корпуса в единицах меша → реальная длина.
                Mesh body = pack.MeshOf(r.BodyPart);
                float scale = r.TargetLength > 0f ? r.TargetLength / Mathf.Max(body.bounds.size.x, body.bounds.size.y, body.bounds.size.z) : 1f;

                // Корпус повёрнут стволом по +Z, верхом по +Y (оси меша у моделей пака разные —
                // берутся из клипа прицеливания), начало координат — у спуска.
                (Vector3 forward, Vector3 up) = pack.AimAxes(r.PoseClip);
                pack.Sample(null);
                Matrix4x4 bodyRot = Matrix4x4.Rotate(Quaternion.Inverse(Quaternion.LookRotation(forward, up)));
                Vector3 trigger = (bodyRot * pack.PartInBody(r.TriggerPart)).GetColumn(3);
                Matrix4x4 bodyLocal = Matrix4x4.Translate(new Vector3(0f, -trigger.y, -trigger.z)) * bodyRot;

                GameObject mag = BuildMagazine(r, pack, bodyLocal, scale, folder, scene);
                GameObject weapon = BuildWeapon(r, pack, bodyLocal, scale, scene, mag);

                ImpactSoundInstaller.Apply(weapon, false);
                string path = $"{folder}/{r.Name}.prefab";
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(weapon, path);
                GameLog.Debug.Info($"[HandsPackWeaponBuilder] {path}: масштаб ×{scale:F3}, магазин {AssetDatabase.GetAssetPath(mag)}");
                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // ── Оружие ──────────────────────────────────────────────────────────────

        private static GameObject BuildWeapon(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal, float scale, Scene scene, GameObject magPrefab)
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
            root.AddComponent<NetworkIdentity>();

            // Детали
            var container = new GameObject("MeshContainer").transform;
            container.SetParent(root.transform, false);
            var names = new Dictionary<string, string> { { r.BodyPart, "Base" } };
            foreach (string p in r.StaticParts ?? new string[0]) names[p] = Clean(p);
            names[r.TriggerPart] = "Trigger";
            Dictionary<string, Transform> parts = pack.BuildParts(container, bodyLocal, names);
            parts[r.BodyPart].gameObject.AddComponent<MeshCollider>().convex = true;

            // Помпа/затвор — отдельный граббабл у корня, как Recoil у M16. У револьвера его нет.
            PartMotion triggerMotion = pack.Measure(r.TriggerPart, r.ActionClip, r.RestClip);
            UxrGrabbableObject actionGrab = null;
            Transform actionPart = null;
            if (r.Action != HandsPackWeaponRecipe.ActionKind.None)
                (actionGrab, actionPart) = BuildAction(r, pack, bodyLocal, root, gripDonor, fireDonor);

            root.AddComponent<AnchoredItemCollisionIgnore>();
            root.AddComponent<OutOfWorldGuard>();

            // Ствол: дуло — передний край корпуса, ось — центр кольца вершин у дула (у помпового —
            // верхнего: ниже ствола подствольный магазин).
            Bounds muzzle = MuzzleRing(pack.MeshOf(r.BodyPart), parts[r.BodyPart], r.Action == HandsPackWeaponRecipe.ActionKind.Pump || r.Pellets);
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
                // Патрон торчит из окна заряжания: центр детали, ниже на половину высоты магазина.
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
            if (!string.IsNullOrEmpty(r.LoadAudio)) source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(r.LoadAudio);
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
            shotTypes.arraySize = pellets ? 2 : 1;

            for (int i = 0; i < shotTypes.arraySize; i++)
            {
                SerializedProperty shot = shotTypes.GetArrayElementAtIndex(i);
                if (i > 0) shot.boxedValue = shotTypes.GetArrayElementAtIndex(0).boxedValue;

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
            if (pellets) root.AddComponent<ShotgunPellets>();

            var fw = new SerializedObject(firearm);
            CopyFields(fireDonor.GetComponent<UxrFirearmWeapon>(), firearm, fw);
            var recoil = Empty("RecoilSourceAxes", container, Vector3.zero);
            fw.FindProperty("_recoilAxes").objectReferenceValue = recoil;
            SerializedProperty trig = fw.FindProperty("_triggers").GetArrayElementAtIndex(0);
            trig.FindPropertyRelative("_triggerGrabbable").objectReferenceValue = grabbable;
            trig.FindPropertyRelative("_grabbableGrabPointIndex").intValue = 0;
            trig.FindPropertyRelative("_triggerTransform").objectReferenceValue = parts[r.TriggerPart];
            trig.FindPropertyRelative("_triggerRotationAxis._axis").intValue = DominantAxis(triggerMotion.RotationAxis);
            trig.FindPropertyRelative("_triggerRotationDegrees").floatValue = Mathf.Round(triggerMotion.MaxAngle) * Mathf.Sign(AxisComponent(triggerMotion.RotationAxis));
            trig.FindPropertyRelative("_ammunitionMagAnchor").objectReferenceValue = anchor;
            // Без затвора патрон досылать нечем: выстрел — прямо из барабана.
            if (r.Action == HandsPackWeaponRecipe.ActionKind.None)
                trig.FindPropertyRelative("_useHasReloadedForSemiAndFullAuto").boolValue = false;
            if (r.ShotCycle.HasValue)
                trig.FindPropertyRelative("_cycleType").enumValueIndex = (int)r.ShotCycle.Value;
            if (!string.IsNullOrEmpty(r.ShotAudio))
                trig.FindPropertyRelative("_shotAudio._clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(r.ShotAudio);
            fw.ApplyModifiedPropertiesWithoutUndo();

            // Точки хвата из ладоней пака: правая — рукоять, левая — помпа/цевьё/поддержка.
            PlaceGrips(r, pack, root, parts[r.BodyPart], grabbable, actionGrab, actionPart, gripDonor);
            UltimateXR.Avatar.UxrAvatar mef = MefAvatarComponent();
            recoil.position = grabbable.GetGrabPoint(0).GetGripPoseInfo(mef).GripAlignTransformHandRight.position;

            // Вторая рука пистолета обнимает первую: она не должна поворачивать оружие и браться
            // одна (GunTwoHandAimTests, SupportGripTests; порог — TwoHandGrabCases.SupportGripMaxGap).
            if (r.SupportGrip)
            {
                float gap = Vector3.Distance(grabbable.GetGrabPoint(0).GetGripPoseInfo(mef).GripAlignTransformHandRight.position,
                                             grabbable.GetGrabPoint(1).GetGripPoseInfo(mef).GripAlignTransformHandLeft.position);
                if (gap < SupportGripMaxGap)
                {
                    root.AddComponent<MainGripAimLock>();
                    root.AddComponent<SupportGripRequiresMain>();
                }
            }

            // Подсветка при поднесённой руке — копия корпуса и помпы/затвора (WeaponFeedbackTests).
            WeaponGrabHighlight.Assign(grabbable, 0, parts[r.BodyPart]);
            if (r.SupportGrip) WeaponGrabHighlight.Assign(grabbable, 1, parts[r.BodyPart]);
            if (actionGrab != null) WeaponGrabHighlight.Assign(actionGrab, 0, actionPart);

            return root;
        }

        /// <summary>Помпа или затвор: отдельный граббабл у корня с ходом из клипа.</summary>
        private static (UxrGrabbableObject, Transform) BuildAction(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal,
                                                                  GameObject root, GameObject gripDonor, GameObject fireDonor)
        {
            var action = new GameObject(r.Action == HandsPackWeaponRecipe.ActionKind.Pump ? "Pump" : "Slide");
            action.transform.SetParent(root.transform, false);
            var actionNames = new Dictionary<string, string> { { r.ActionPart, Clean(r.ActionPart) } };
            foreach (string p in r.ActionExtraParts ?? new string[0]) actionNames[p] = Clean(p);
            Transform actionPart = pack.BuildParts(action.transform, bodyLocal, actionNames)[r.ActionPart];

            // Механика из клипа: полный ход — наибольшее смещение от покоя (у рычага взведения
            // MP5K путь не прямой, первое направление движения с ним не совпадает).
            PartMotion actionMotion = pack.Measure(r.ActionPart, r.ActionClip, r.RestClip);
            Vector3 travel = bodyLocal.MultiplyVector(actionMotion.Far);

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

                // Помпа идёт за смещением руки с момента хвата, иначе запаздывает на промах хвата.
                var follow = new SerializedObject(root.AddComponent<PumpGrabFollow>());
                follow.FindProperty("_pump").objectReferenceValue = actionGrab;
                follow.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                var slide = new SerializedObject(root.AddComponent<AutomaticWeaponSlideFeedback>());
                CopyFields(gripDonor.GetComponent<AutomaticWeaponSlideFeedback>(), slide.targetObject, slide);
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

            return (actionGrab, actionPart);
        }

        private static void PlaceGrips(HandsPackWeaponRecipe r, IWeaponModel pack, GameObject root, Transform bodyMesh,
                                       UxrGrabbableObject main, UxrGrabbableObject action, Transform actionPart, GameObject donor)
        {
            using var donorPack = new HandsPackWeapon(r.GripDonorFolder, r.GripDonorBody);
            Transform donorBody = donor.transform.Find(r.GripDonorBodyPath);
            var donorGrab = donor.GetComponent<UxrGrabbableObject>();
            var mef = MefAvatarComponent();

            UxrGripPoseInfo donorMain = donorGrab.GetGrabPoint(0).GetGripPoseInfo(mef);
            UxrGripPoseInfo donorSupport = donorGrab.GetGrabPoint(1).GetGripPoseInfo(mef);

            // Место хвата руки пака (правая — рукоять, левая — цевьё/помпа/поддержка) для руки аватара.
            System.Func<bool, UxrHandSide, Matrix4x4> grip;
            if (pack is IHandGripSource frames)
            {
                // Кадр рук в универсальных осях ладони (KINEMATION): хват сразу по нему, как делает позже
                // HandsPackGripAligner для позы пака, — калибровка по донору того же пака не нужна.
                var samples = new Dictionary<bool, VrBattlegrounds.Editor.Avatars.HandsPackPoseExtractor.Sample>
                {
                    { true, frames.GripSample(UxrHandSide.Right) },
                    { false, frames.GripSample(UxrHandSide.Left) }
                };
                GameObject mefPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MefAvatar);
                grip = (packRight, side) =>
                {
                    Transform t = Empty("AlignProbe", root.transform, Vector3.zero);
                    VrBattlegrounds.Editor.Avatars.HandsPackGripAligner.Place(root.transform, bodyMesh, samples[packRight],
                                                                              packRight ? UxrHandSide.Right : UxrHandSide.Left, mefPrefab, side, t);
                    Matrix4x4 m = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                    Object.DestroyImmediate(t.gameObject);
                    return m;
                };
            }
            else
            {
                var hands = (HandsPackWeapon)pack;
                var cal = new Dictionary<(bool, UxrHandSide), Matrix4x4>
                {
                    { (true, UxrHandSide.Right), GripCalibration.Measure(donorPack, r.GripDonorPoseClip, true, donorBody, donorMain.GripAlignTransformHandRight) },
                    { (true, UxrHandSide.Left), GripCalibration.Measure(donorPack, r.GripDonorPoseClip, true, donorBody, donorMain.GripAlignTransformHandLeft) },
                    { (false, UxrHandSide.Left), GripCalibration.Measure(donorPack, r.GripDonorPoseClip, false, donorBody, donorSupport.GripAlignTransformHandLeft) },
                    { (false, UxrHandSide.Right), GripCalibration.Measure(donorPack, r.GripDonorPoseClip, false, donorBody, donorSupport.GripAlignTransformHandRight) }
                };
                grip = (packRight, side) => GripCalibration.Apply(hands, r.PoseClip, packRight, bodyMesh, cal[(packRight, side)]);
            }

            AddGrip(main, 0, root.transform, "Main Grab Point", grip(true, UxrHandSide.Left), grip(true, UxrHandSide.Right), donorMain);
            if (r.SupportGrip)
                AddGrip(main, 1, root.transform, "Additional Grab Point 0", grip(false, UxrHandSide.Left), grip(false, UxrHandSide.Right), donorSupport);

            if (action == null) return;

            if (r.Action == HandsPackWeaponRecipe.ActionKind.Pump)
            {
                // Помпу держит вторая рука — её место в паке и есть место хвата.
                AddGrip(action, 0, action.transform, "Main Grab Point", grip(false, UxrHandSide.Left), grip(false, UxrHandSide.Right), donorSupport);
                return;
            }

            // Затвор: в клипе прицеливания рука пака на нём не лежит — хват переносится с затвора
            // донора как есть (в метрах, относительно центра детали, в осях прицеливания).
            UxrGrabbableObject donorSlide = FindPart(donor);
            Transform donorSlideMesh = donorSlide.GetComponentsInChildren<MeshFilter>(true).First(f => f.name != "GrabHighlight").transform;
            UxrGripPoseInfo donorSlidePose = donorSlide.GetGrabPoint(0).GetGripPoseInfo(mef);
            (Vector3 donorForward, Vector3 donorUp) = donorPack.AimAxes(r.GripDonorPoseClip);
            (Vector3 forward, Vector3 up) = pack.AimAxes(r.PoseClip);
            Matrix4x4 donorFrame = PartFrame(donorSlideMesh, donorBody, donorForward, donorUp);
            Matrix4x4 frame = PartFrame(actionPart, bodyMesh, forward, up);
            Matrix4x4 moveL = frame * donorFrame.inverse * GripCalibration.Rigid(donorSlidePose.GripAlignTransformHandLeft.localToWorldMatrix);
            Matrix4x4 moveR = frame * donorFrame.inverse * GripCalibration.Rigid(donorSlidePose.GripAlignTransformHandRight.localToWorldMatrix);
            AddGrip(action, 0, action.transform, "Main Grab Point", moveL, moveR, donorSlidePose);
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

        /// <summary>Центр меша детали с осями прицеливания оружия (+Z — ствол, +Y — верх), без масштаба.</summary>
        private static Matrix4x4 PartFrame(Transform part, Transform body, Vector3 bodyForward, Vector3 bodyUp)
        {
            Vector3 center = part.TransformPoint(part.GetComponent<MeshFilter>().sharedMesh.bounds.center);
            return Matrix4x4.TRS(center, body.rotation * Quaternion.LookRotation(bodyForward, bodyUp), Vector3.one);
        }

        /// <summary>Центр меша магазина пака в осях контейнера деталей — место якоря и начало координат магазина.</summary>
        private static Vector3 MagazineCenter(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal)
        {
            pack.Sample(null);
            return (bodyLocal * pack.PartInBody(r.MagazinePart)).MultiplyPoint3x4(pack.MeshOf(r.MagazinePart).bounds.center);
        }

        private static void AddGrip(UxrGrabbableObject grabbable, int point, Transform owner, string pointName,
                                    Matrix4x4 left, Matrix4x4 right, UxrGripPoseInfo donorPose)
        {
            Transform grabs = owner.Find("Grabs") ?? Empty("Grabs", owner, Vector3.zero);
            Transform avatar = Empty(Path.GetFileNameWithoutExtension(MefAvatar), grabs, Vector3.zero);
            Transform pointT = Empty(pointName, avatar, Vector3.zero);
            Transform l = Empty("Left Grab", pointT, Vector3.zero);
            Transform rt = Empty("Right Grab", pointT, Vector3.zero);
            l.SetPositionAndRotation(left.GetColumn(3), left.rotation);
            rt.SetPositionAndRotation(right.GetColumn(3), right.rotation);
            l.gameObject.AddComponent<UxrGrabbableObjectSnapTransform>();
            rt.gameObject.AddComponent<UxrGrabbableObjectSnapTransform>();

            var so = new SerializedObject(grabbable);
            SerializedProperty entries = so.FindProperty(point == 0 ? "_grabPoint._avatarGripPoseEntries" : $"_additionalGrabPoints.Array.data[{point - 1}]._avatarGripPoseEntries");
            entries.arraySize = 1;
            SerializedProperty e = entries.GetArrayElementAtIndex(0);
            e.FindPropertyRelative("_avatarPrefabGuid").stringValue = AssetDatabase.AssetPathToGUID(MefAvatar);
            e.FindPropertyRelative("_handPose").objectReferenceValue = donorPose.HandPose;
            e.FindPropertyRelative("_poseBlendValue").floatValue = donorPose.PoseBlendValue;
            e.FindPropertyRelative("_gripAlignTransformHandLeft").objectReferenceValue = l;
            e.FindPropertyRelative("_gripAlignTransformHandRight").objectReferenceValue = rt;
            so.FindProperty("_selectedAvatarForGrips").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(MefAvatar);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Магазин ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Вариант <see cref="HandsPackWeaponRecipe.MagazineBase" />: сеть, физика, звуки и хват
        /// сэмпловых аватаров от базы, геометрия — меш пака. Масштаб — как у оружия: вложенный
        /// магазин живёт под корнем оружия с ×1, выдаваемый должен быть того же размера.
        /// </summary>
        private static GameObject BuildMagazine(HandsPackWeaponRecipe r, IWeaponModel pack, Matrix4x4 bodyLocal, float scale, string folder, Scene scene)
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(r.MagazineBase);
            var mag = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, scene);
            mag.name = r.Name + "_mag";
            mag.transform.localScale = Vector3.one * scale;

            // Геометрия базы уходит целиком, вместе с индикатором патронов, который на неё ссылается.
            foreach (var indicator in mag.GetComponents<MonoBehaviour>().Where(c => c != null && c.GetType().Name == "MagAmmoIndicator").ToList())
                Object.DestroyImmediate(indicator);
            // Точки хвата сэмпловых аватаров живут внутри объекта геометрии (у MagShotgun — в
            // MagShotGunGeo), а Cyborg из реестра хватает по ним. Такой объект остаётся без меша.
            foreach (MeshFilter filter in mag.GetComponentsInChildren<MeshFilter>(true).ToList())
            {
                if (filter == null) continue;
                GameObject geo = filter.gameObject;
                if (geo.GetComponentInChildren<UxrGrabbableObjectSnapTransform>(true) == null)
                {
                    Object.DestroyImmediate(geo);
                    continue;
                }

                foreach (Component c in geo.GetComponents<Component>().Where(c => c is Renderer || c is Collider || c is MeshFilter || c is MonoBehaviour).Reverse().ToList())
                    Object.DestroyImmediate(c);
            }

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

            var so = new SerializedObject(mag.GetComponent<UxrFirearmMag>());
            so.FindProperty("_capacity").intValue = r.MagazineCapacity;
            so.FindProperty("_rounds").intValue = r.MagazineCapacity;
            so.ApplyModifiedPropertiesWithoutUndo();

            var grab = new SerializedObject(mag.GetComponent<UxrGrabbableObject>());
            grab.FindProperty("_tag").stringValue = r.MagazineTag;
            grab.ApplyModifiedPropertiesWithoutUndo();

            // Хват MEF — как у магазина Gun_real: ладонь сбоку, пальцы вокруг.
            var donor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/GunReal/Gun_real_mag.prefab");
            UxrGripPoseInfo donorPose = donor.GetComponent<UxrGrabbableObject>().GetGrabPoint(0).GetGripPoseInfo(MefAvatarComponent());
            Transform donorMesh = donor.GetComponentInChildren<MeshFilter>(true).transform;
            Matrix4x4 donorCenter = GripCalibration.Rigid(donorMesh.localToWorldMatrix * Matrix4x4.Translate(donorMesh.GetComponent<MeshFilter>().sharedMesh.bounds.center));
            Matrix4x4 meshCenter = GripCalibration.Rigid(meshGo.transform.localToWorldMatrix * Matrix4x4.Translate(mesh.bounds.center));
            Matrix4x4 left = meshCenter * (donorCenter.inverse * GripCalibration.Rigid(donorPose.GripAlignTransformHandLeft.localToWorldMatrix));
            Matrix4x4 right = meshCenter * (donorCenter.inverse * GripCalibration.Rigid(donorPose.GripAlignTransformHandRight.localToWorldMatrix));
            AddMagGrip(mag.GetComponent<UxrGrabbableObject>(), mag.transform, left, right, donorPose);

            ImpactSoundInstaller.Apply(mag, true);
            string path = $"{folder}/{mag.name}.prefab";
            return PrefabUtility.SaveAsPrefabAsset(mag, path);
        }

        private static void AddMagGrip(UxrGrabbableObject grabbable, Transform owner, Matrix4x4 left, Matrix4x4 right, UxrGripPoseInfo donorPose)
        {
            Transform grabs = owner.Find("Grabs") ?? Empty("Grabs", owner, Vector3.zero);
            Transform avatar = Empty(Path.GetFileNameWithoutExtension(MefAvatar), grabs, Vector3.zero);
            Transform l = Empty("Left Grab", avatar, Vector3.zero);
            Transform rt = Empty("Right Grab", avatar, Vector3.zero);
            l.SetPositionAndRotation(left.GetColumn(3), left.rotation);
            rt.SetPositionAndRotation(right.GetColumn(3), right.rotation);
            l.gameObject.AddComponent<UxrGrabbableObjectSnapTransform>();
            rt.gameObject.AddComponent<UxrGrabbableObjectSnapTransform>();

            var so = new SerializedObject(grabbable);
            SerializedProperty entries = so.FindProperty("_grabPoint._avatarGripPoseEntries");
            string guid = AssetDatabase.AssetPathToGUID(MefAvatar);
            int index = Enumerable.Range(0, entries.arraySize).FirstOrDefault(i => entries.GetArrayElementAtIndex(i).FindPropertyRelative("_avatarPrefabGuid").stringValue == guid);
            if (entries.arraySize == 0 || entries.GetArrayElementAtIndex(index).FindPropertyRelative("_avatarPrefabGuid").stringValue != guid)
            {
                index = entries.arraySize;
                entries.arraySize++;
            }

            SerializedProperty e = entries.GetArrayElementAtIndex(index);
            e.FindPropertyRelative("_avatarPrefabGuid").stringValue = guid;
            e.FindPropertyRelative("_handPose").objectReferenceValue = donorPose.HandPose;
            e.FindPropertyRelative("_poseBlendValue").floatValue = donorPose.PoseBlendValue;
            e.FindPropertyRelative("_gripAlignTransformHandLeft").objectReferenceValue = l;
            e.FindPropertyRelative("_gripAlignTransformHandRight").objectReferenceValue = rt;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Вспомогательное ─────────────────────────────────────────────────────

        private static UltimateXR.Avatar.UxrAvatar MefAvatarComponent() =>
            AssetDatabase.LoadAssetAtPath<GameObject>(MefAvatar).GetComponent<UltimateXR.Avatar.UxrAvatar>();

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

        private static string Clean(string part) => part.Replace("Shogun_", "").Replace("_mesh", "").Replace("_Mesh", "").Replace("-", "").Replace(" ", "");

        /// <summary>Как <c>TwoHandGrabCases.SupportGripMaxGap</c>: ближе — вторая рука на рукояти (пистолет), дальше — цевьё.</summary>
        private const float SupportGripMaxGap = 0.10f;

        /// <summary>
        /// Кольцо вершин у дула, в осях контейнера. Срез — доля длины (единицы меша у моделей пака разные,
        /// от 0.07 у MP5K до 1.7 у Uzi): тонкий, 0.5 % длины — только сам срез ствола, без мушки и
        /// переднего упора за ним. <paramref name="topCluster" /> — у дробовика в срез попадает и
        /// подствольный магазин: берётся верхний кластер, срез 1.65 % (прежний 1 см у Shotgun_real).
        /// </summary>
        private static Bounds MuzzleRing(Mesh mesh, Transform body, bool topCluster)
        {
            Matrix4x4 m = body.parent.worldToLocalMatrix * body.localToWorldMatrix;
            List<Vector3> v = mesh.vertices.Select(x => m.MultiplyPoint3x4(x)).ToList();
            float front = v.Max(x => x.z);
            float depth = (topCluster ? 0.0165f : 0.005f) * (front - v.Min(x => x.z));
            List<Vector3> ring = v.Where(x => x.z > front - depth).ToList();
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
