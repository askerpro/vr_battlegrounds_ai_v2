using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
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
        public enum ActionKind { Pump, Slide }

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
        public string ActionPart;      // помпа или затвор
        public ActionKind Action;
        public string LoadingPart;     // деталь, у которой встаёт магазин (якорь)

        public string UxrTag;          // тег хвата корня: по нему карманы и слоты принимают оружие
        public string MagazineBase;    // префаб магазина, от которого делается вариант (сеть, физика, звук)
        public string MagazinePart;    // меш магазина из пака
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

        public static GameObject Build(HandsPackWeaponRecipe r)
        {
            string folder = $"Assets/Prefabs/Weapons/{r.PrefabFolder}";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Prefabs/Weapons", r.PrefabFolder);

            Scene scene = EditorSceneManager.NewPreviewScene();

            try
            {
                using var pack = new HandsPackWeapon(r.Folder, r.BodyPart);

                // Масштаб корня: габарит корпуса в единицах меша → реальная длина.
                Mesh body = pack.MeshOf(r.BodyPart);
                float scale = r.TargetLength / Mathf.Max(body.bounds.size.x, body.bounds.size.y, body.bounds.size.z);

                // Корпус повёрнут как в паке (ствол по +Z), начало координат — у спуска.
                pack.Sample(null);
                Matrix4x4 bodyRot = Matrix4x4.Rotate(Quaternion.Euler(270f, 0f, 0f));
                Vector3 trigger = (bodyRot * pack.PartInBody(r.TriggerPart)).GetColumn(3);
                Matrix4x4 bodyLocal = Matrix4x4.Translate(new Vector3(0f, -trigger.y, -trigger.z)) * bodyRot;

                GameObject mag = BuildMagazine(r, pack, bodyLocal, scale, folder, scene);
                GameObject weapon = BuildWeapon(r, pack, bodyLocal, scale, scene, mag);

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

        private static GameObject BuildWeapon(HandsPackWeaponRecipe r, HandsPackWeapon pack, Matrix4x4 bodyLocal, float scale, Scene scene, GameObject magPrefab)
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
            foreach (string p in r.StaticParts) names[p] = Clean(p);
            names[r.TriggerPart] = "Trigger";
            Dictionary<string, Transform> parts = pack.BuildParts(container, bodyLocal, names);
            parts[r.BodyPart].gameObject.AddComponent<MeshCollider>().convex = true;

            // Помпа/затвор — отдельный граббабл у корня, как Recoil у M16.
            var action = new GameObject(r.Action == HandsPackWeaponRecipe.ActionKind.Pump ? "Pump" : "Slide");
            action.transform.SetParent(root.transform, false);
            pack.BuildParts(action.transform, bodyLocal, new Dictionary<string, string> { { r.ActionPart, Clean(r.ActionPart) } });

            // Механика из клипа
            PartMotion actionMotion = pack.Measure(r.ActionPart, r.ActionClip, r.RestClip);
            PartMotion triggerMotion = pack.Measure(r.TriggerPart, r.ActionClip, r.RestClip);
            Vector3 travel = bodyLocal.MultiplyVector(actionMotion.Axis * actionMotion.MaxTravel);

            var actionGrab = action.AddComponent<UxrGrabbableObject>();
            action.AddComponent<GrabOnlyWhenParentHeld>();
            var ag = new SerializedObject(actionGrab);
            CopyFields(FindPart(gripDonor), actionGrab, ag);
            ag.FindProperty("_translationConstraintMode").enumValueIndex = (int)UxrTranslationConstraintMode.RestrictLocalOffset;
            ag.FindProperty("_translationLimitsMin").vector3Value = Vector3.Min(travel, Vector3.zero);
            ag.FindProperty("_translationLimitsMax").vector3Value = Vector3.Max(travel, Vector3.zero);
            ag.FindProperty("_rotationConstraintMode").enumValueIndex = (int)UxrRotationConstraintMode.RestrictLocalRotation;

            // Донор — рукоятка затвора M16: она не наводит ствол и от винтовки не зависит. Помпу
            // держит вторая рука, она наводит ствол, как цевьё (так у сэмплового дробовика SDK).
            bool isPump = r.Action == HandsPackWeaponRecipe.ActionKind.Pump;
            ag.FindProperty("_controlParentDirection").boolValue = isPump;
            ag.FindProperty("_ignoreGrabbableParentDependency").boolValue = !isPump;
            ag.ApplyModifiedPropertiesWithoutUndo();

            if (r.Action == HandsPackWeaponRecipe.ActionKind.Pump)
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
                slide.ApplyModifiedPropertiesWithoutUndo();
            }

            root.AddComponent<AnchoredItemCollisionIgnore>();
            root.AddComponent<OutOfWorldGuard>();

            // Ствол: дуло — передний край корпуса, ось — центр верхнего кольца вершин у дула.
            Bounds muzzle = MuzzleRing(pack.MeshOf(r.BodyPart), parts[r.BodyPart]);
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

            // Якорь магазина у детали заряжания, магазин вложен и вставляется в Awake.
            // Магазин (патрон) торчит из окна заряжания: центр детали, ниже на половину высоты магазина.
            Transform loading = parts[r.LoadingPart];
            Vector3 loadingCenter = container.InverseTransformPoint(loading.TransformPoint(loading.GetComponent<MeshFilter>().sharedMesh.bounds.center));
            Bounds magBounds = magPrefab.GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds;
            float magHalfHeight = Mathf.Min(magBounds.size.x, magBounds.size.y, magBounds.size.z) * 0.5f;
            var anchorGo = Empty("MagAnchor", container, loadingCenter + Vector3.down * magHalfHeight).gameObject;
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
            so.FindProperty("_additionalGrabPoints").arraySize = 0;
            so.FindProperty("_startAnchor").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Снаряд и спуск
            var ps = new SerializedObject(projectile);
            CopyFields(fireDonor.GetComponent<UxrProjectileSource>(), projectile, ps);
            ps.FindProperty("_weaponAnimator").objectReferenceValue = null;

            // Помповое — дробь: второй тип выстрела для дробинок, без вспышки у дула на каждую.
            SerializedProperty shotTypes = ps.FindProperty("_shotTypes");
            bool pellets = r.Action == HandsPackWeaponRecipe.ActionKind.Pump;
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
            if (!string.IsNullOrEmpty(r.ShotAudio))
                trig.FindPropertyRelative("_shotAudio._clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(r.ShotAudio);
            fw.ApplyModifiedPropertiesWithoutUndo();

            // Точки хвата из ладоней пака: правая — рукоять, левая — помпа/цевьё.
            PlaceGrips(r, pack, root, parts[r.BodyPart], grabbable, actionGrab, gripDonor);
            recoil.position = grabbable.GetGrabPoint(0).GetGripPoseInfo(MefAvatarComponent()).GripAlignTransformHandRight.position;

            return root;
        }

        private static void PlaceGrips(HandsPackWeaponRecipe r, HandsPackWeapon pack, GameObject root, Transform bodyMesh,
                                       UxrGrabbableObject main, UxrGrabbableObject action, GameObject donor)
        {
            using var donorPack = new HandsPackWeapon(r.GripDonorFolder, r.GripDonorBody);
            Transform donorBody = donor.transform.Find(r.GripDonorBodyPath);
            var donorGrab = donor.GetComponent<UxrGrabbableObject>();
            var mef = MefAvatarComponent();

            UxrGripPoseInfo donorMain = donorGrab.GetGrabPoint(0).GetGripPoseInfo(mef);
            UxrGripPoseInfo donorSupport = donorGrab.GetGrabPoint(1).GetGripPoseInfo(mef);

            Matrix4x4 calMainR = GripCalibration.Measure(donorPack, r.GripDonorPoseClip, true, donorBody, donorMain.GripAlignTransformHandRight);
            Matrix4x4 calMainL = GripCalibration.Measure(donorPack, r.GripDonorPoseClip, true, donorBody, donorMain.GripAlignTransformHandLeft);
            Matrix4x4 calSupL = GripCalibration.Measure(donorPack, r.GripDonorPoseClip, false, donorBody, donorSupport.GripAlignTransformHandLeft);
            Matrix4x4 calSupR = GripCalibration.Measure(donorPack, r.GripDonorPoseClip, false, donorBody, donorSupport.GripAlignTransformHandRight);

            AddGrip(main, 0, root.transform, "Main Grab Point",
                    GripCalibration.Apply(pack, r.PoseClip, true, bodyMesh, calMainL),
                    GripCalibration.Apply(pack, r.PoseClip, true, bodyMesh, calMainR), donorMain);
            AddGrip(action, 0, action.transform, "Main Grab Point",
                    GripCalibration.Apply(pack, r.PoseClip, false, bodyMesh, calSupL),
                    GripCalibration.Apply(pack, r.PoseClip, false, bodyMesh, calSupR), donorSupport);
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
        private static GameObject BuildMagazine(HandsPackWeaponRecipe r, HandsPackWeapon pack, Matrix4x4 bodyLocal, float scale, string folder, Scene scene)
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

            // Меш пака: ориентирован как в оружии, центр — в начале координат магазина.
            pack.Sample(null);
            Matrix4x4 place = bodyLocal * pack.PartInBody(r.MagazinePart);
            var meshGo = new GameObject("Mesh");
            meshGo.transform.SetParent(mag.transform, false);
            meshGo.transform.localRotation = place.rotation;
            Mesh mesh = pack.MeshOf(r.MagazinePart);
            meshGo.transform.localPosition = -(place.rotation * mesh.bounds.center);
            meshGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshGo.AddComponent<MeshRenderer>().sharedMaterials = pack.MaterialsOf(r.MagazinePart);
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
            Matrix4x4 center = GripCalibration.Rigid(meshGo.transform.localToWorldMatrix * Matrix4x4.Translate(mesh.bounds.center));
            Matrix4x4 left = center * (donorCenter.inverse * GripCalibration.Rigid(donorPose.GripAlignTransformHandLeft.localToWorldMatrix));
            Matrix4x4 right = center * (donorCenter.inverse * GripCalibration.Rigid(donorPose.GripAlignTransformHandRight.localToWorldMatrix));
            AddMagGrip(mag.GetComponent<UxrGrabbableObject>(), mag.transform, left, right, donorPose);

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

        private static string Clean(string part) => part.Replace("Shogun_", "").Replace("_mesh", "").Replace("-", "");

        /// <summary>Кольцо вершин у дула, в осях контейнера: верхний кластер — ствол.</summary>
        private static Bounds MuzzleRing(Mesh mesh, Transform body)
        {
            Matrix4x4 m = body.parent.worldToLocalMatrix * body.localToWorldMatrix;
            List<Vector3> v = mesh.vertices.Select(x => m.MultiplyPoint3x4(x)).ToList();
            float front = v.Max(x => x.z);
            List<Vector3> ring = v.Where(x => x.z > front - 0.01f).ToList();
            float mid = (ring.Max(x => x.y) + ring.Min(x => x.y)) * 0.5f;
            List<Vector3> top = ring.Where(x => x.y >= mid).ToList();
            var b = new Bounds(top[0], Vector3.zero);
            foreach (Vector3 x in top) b.Encapsulate(x);
            return b;
        }

        private static int DominantAxis(Vector3 a) => Mathf.Abs(a.x) >= Mathf.Abs(a.y) && Mathf.Abs(a.x) >= Mathf.Abs(a.z) ? 0 : Mathf.Abs(a.y) >= Mathf.Abs(a.z) ? 1 : 2;

        private static float AxisComponent(Vector3 a) => a[DominantAxis(a)];
    }
}
