using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;
using UltimateXR.Animation.IK;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Клипы ходьбы Mixamo С root motion и контроллер ног на них (<see cref="ControllerPath"/>) — набор клипов, которым
    /// шагают ноги аватаров (UltimateXR, патч 37, <c>UxrAnimatedLegs</c>); ставит его аватарам <see cref="AvatarLegsSetup"/>.
    ///
    /// <list type="bullet">
    /// <item><see cref="RifleFolder"/> — «Pro Rifle Pack» (полусогнутая боевая стойка): шаг/бег/спринт и шаг в приседе × 8
    /// направлений, покой, покой в приседе, повороты на 90°.</item>
    /// <item><see cref="PistolFolder"/> — «Pistol/Handgun Locomotion Pack»: шаг, бег, назад, бег вбок; дуги и колено не берутся.
    /// Присед у пистолета и без оружия — колено и шаг в приседе набора винтовки (своего приседа в наборах нет).</item>
    /// <item><see cref="UnarmedFolder"/> + <see cref="PlainFolder"/> — без оружия: «Locomotion Pack» (вперёд, вбок, повороты) и
    /// назад/диагонали из набора без In Place.</item>
    /// </list>
    ///
    /// <para>
    /// Импорт: Humanoid, общий Avatar в T-позе (<see cref="EnsureSharedAvatar"/>), без материалов, цикл; корень по XZ НЕ заперт
    /// (root motion двигает корень копии рига), поворот корня и высота запечены в позу — поворачивают только клипы поворота на
    /// месте; высота — по стопам («Based Upon Feet»). Фаза шага выровнена (<see cref="AlignPhase"/>); Foot IK — только у хода (покой — без него, см. <see cref="RecreateController"/>).
    /// Почему так — T-42, «Доводка: стопы и подошва на клипах Mixamo».
    /// </para>
    ///
    /// <para>
    /// Контроллер собирается с нуля (повторный запуск пересобирает его на месте, GUID тот же): параметры и состояния — контракт
    /// <c>UxrLegLocomotion</c> (<c>Legs_*</c>), покой ⇄ ход по <c>Legs_IsMoving</c>, переход назад — <c>Legs_Stop</c>, маска —
    /// корень, корпус и ноги (<see cref="MaskPath"/>). Стойка — поддерево по <c>Legs_Stance</c>: 0 — без оружия, 1 — пистолет,
    /// 2 — винтовка (по умолчанию; выбор по оружию пока выключен). Приседа и сидения нет (упрощение 2026-10-06). Клипы хода — в точках их средней
    /// скорости (м/с, x — вправо, y — вперёд), смешивание Freeform Directional с покоем в центре.
    /// </para>
    /// </summary>
    public static class AvatarMixamoLocomotionSetup
    {
        public const string LocomotionFolder = "Assets/Art/Animations/Locomotion";
        public const string RifleFolder = LocomotionFolder + "/MixamoRifle";
        public const string PistolFolder = LocomotionFolder + "/MixamoPistol";
        public const string UnarmedFolder = LocomotionFolder + "/MixamoUnarmed";
        public const string PlainFolder = LocomotionFolder + "/MixamoRM";
        public const string ControllerPath = LocomotionFolder + "/AvatarLegs_Locomotion.controller";
        public const string MaskPath = LocomotionFolder + "/AvatarLegs.mask";

        /// <summary>Стойка: 0 — без оружия, 1 — пистолет, 2 — винтовка.</summary>
        public const float SetUnarmed = 0f, SetPistol = 1f, SetRifle = 2f;

        /// <summary>Аватар, на копии рига которого меряется фаза шага и разворот стойки клипов.</summary>
        public const string MainAvatarPrefab = "Assets/Prefabs/Player/MEF_Base_Avatar.prefab";

        private static readonly string[] Directions =
            { "Forward", "ForwardRight", "Right", "BackwardRight", "Backward", "BackwardLeft", "Left", "ForwardLeft" };

        /// <summary>Импорт наборов и контроллер со стойками. Возвращает контроллер или null.</summary>
        public static AnimatorController EnsureController()
        {
            Dictionary<string, AnimationClip> rifle = EnsureClips(RifleFolder);
            Dictionary<string, AnimationClip> pistol = EnsureClips(PistolFolder);
            Dictionary<string, AnimationClip> unarmed = EnsureClips(UnarmedFolder);
            foreach (var pair in EnsureClips(PlainFolder)) unarmed[pair.Key] = pair.Value;
            if (rifle.Count == 0)
            {
                GameLog.Error($"[AvatarMixamoLocomotionSetup] Нет клипов в {RifleFolder}.");
                return null;
            }

            AnimatorController controller = RecreateController(out AnimatorState idle, out AnimatorState moving);

            // Винтовка: 8 направлений × шаг/бег/спринт, повороты.
            var rifleMove = new List<AnimationClip>();
            foreach (string gait in new[] { "Walk", "Run", "Sprint" })
            foreach (string dir in Directions)
                rifleMove.Add(Get(rifle, gait + dir));
            (BlendTree rifleIdleSet, BlendTree rifleMoveSet) = BuildSet(controller, "Rifle",
                Get(rifle, "Idle"), Get(rifle, "Turn90Left"), Get(rifle, "Turn90Right"), rifleMove);

            // Пистолет: шаг, бег, назад, бег вбок (сторона — по замеру скорости клипа). Своих поворотов нет — без оружия.
            List<AnimationClip> pistolMove = new[] { "PistolWalk", "PistolRun", "PistolWalkBackward", "PistolRunBackward", "PistolStrafe", "PistolStrafe_2" }
                .Select(n => Get(pistol, n)).ToList();
            (BlendTree pistolIdleSet, BlendTree pistolMoveSet) = BuildSet(controller, "Pistol",
                Get(pistol, "PistolIdle"), Get(unarmed, "LeftTurn90"), Get(unarmed, "RightTurn90"), pistolMove);

            // Без оружия: вперёд и вбок — «Locomotion Pack», назад и диагонали — набор без In Place.
            List<AnimationClip> unarmedMove = new[]
            {
                "Walking", "Running", "LeftStrafeWalking", "RightStrafeWalking", "LeftStrafe", "RightStrafe",
                "WalkingBackwards", "RunningBackward", "JogForwardDiagonal", "JogForwardDiagonal_Mirror",
                "JogBackwardDiagonal", "JogBackwardDiagonal_Mirror",
            }.Select(n => Get(unarmed, n)).ToList();
            (BlendTree unarmedIdleSet, BlendTree unarmedMoveSet) = BuildSet(controller, "Unarmed",
                Get(unarmed, "Idle"), Get(unarmed, "LeftTurn90"), Get(unarmed, "RightTurn90"), unarmedMove);

            BlendTree idleBySet = Tree(controller, "IdleBySet", UxrLegLocomotion.StanceParam);
            idleBySet.AddChild(unarmedIdleSet, SetUnarmed);
            idleBySet.AddChild(pistolIdleSet, SetPistol);
            idleBySet.AddChild(rifleIdleSet, SetRifle);
            idle.motion = idleBySet;

            BlendTree moveBySet = Tree(controller, "MoveBySet", UxrLegLocomotion.StanceParam);
            moveBySet.AddChild(unarmedMoveSet, SetUnarmed);
            moveBySet.AddChild(pistolMoveSet, SetPistol);
            moveBySet.AddChild(rifleMoveSet, SetRifle);
            moving.motion = moveBySet;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            GameLog.Player.Info("[AvatarMixamoLocomotionSetup] Контроллер ног собран: " + ControllerPath);
            return controller;
        }

        /// <summary>
        /// Поддеревья одной стойки: покой (поворот на месте по <c>Legs_Turn</c>: −0,5 — 90° влево, +0,5 — вправо) и ход
        /// (Freeform Directional по <c>Legs_MoveX/MoveZ</c>, покой в центре).
        /// </summary>
        private static (BlendTree idle, BlendTree move) BuildSet(AnimatorController controller, string set,
            AnimationClip idle, AnimationClip turnLeft, AnimationClip turnRight, List<AnimationClip> move)
        {
            BlendTree idleTree = TurnTree(controller, set + "_Idle", StaticClip(idle), turnLeft, turnRight);

            BlendTree moveTree = Tree(controller, set + "_Move", UxrLegLocomotion.MoveXParam, UxrLegLocomotion.MoveZParam);
            // Покой в центре: медленный ход — клип своего направления вместе с покоем, а не смесь противоположных
            // направлений (Freeform Cartesian без точки (0,0) гасил шаги друг другом).
            if (idle != null) moveTree.AddChild(StaticClip(idle), Vector2.zero);
            foreach (AnimationClip clip in move) AddAtSpeed(moveTree, clip);
            return (idleTree, moveTree);
        }

        private static BlendTree TurnTree(AnimatorController controller, string name, AnimationClip idle, AnimationClip left, AnimationClip right)
        {
            BlendTree tree = Tree(controller, name, UxrLegLocomotion.TurnParam);
            if (left != null) tree.AddChild(left, -0.5f);
            if (idle != null) tree.AddChild(idle, 0f);
            if (right != null) tree.AddChild(right, 0.5f);
            return tree;
        }

        /// <summary>
        /// Импорт клипов папки: Humanoid с общим Avatar (<see cref="EnsureSharedAvatar"/>), без материалов; цикл; поворот и
        /// высота корня — в позу, XZ — root motion (у поворотов на месте поворот корня — тоже root motion). Имя клипа = имя
        /// файла.
        /// </summary>
        public static Dictionary<string, AnimationClip> EnsureClips(string folder)
        {
            var result = new Dictionary<string, AnimationClip>();
            if (!AssetDatabase.IsValidFolder(folder)) return result;
            Avatar shared = EnsureSharedAvatar();
            string stamp = SharedAvatarStamp();

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                bool turn = name.Contains("Turn");

                bool isSource = path == SharedAvatarSource;
                ModelImporterAvatarSetup setup = isSource || shared == null ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther;
                bool changed = importer.animationType != ModelImporterAnimationType.Human
                               || importer.avatarSetup != setup
                               || setup == ModelImporterAvatarSetup.CopyFromOther && (importer.sourceAvatar != shared || importer.userData != stamp)
                               || importer.materialImportMode != ModelImporterMaterialImportMode.None;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = setup;
                if (setup == ModelImporterAvatarSetup.CopyFromOther)
                {
                    importer.sourceAvatar = shared;
                    // Клип пересчитывается в мышцы при импорте: правка позы общего Avatar требует переимпорта всех клипов.
                    importer.userData = stamp;
                }
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importCameras = false;
                importer.importLights = false;

                ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                foreach (ModelImporterClipAnimation clip in clips)
                {
                    changed |= clip.name != name || !clip.loopTime || !clip.loopPose || clip.lockRootRotation == turn || !clip.lockRootHeightY
                               || clip.lockRootPositionXZ || !clip.keepOriginalOrientation || clip.keepOriginalPositionY || !clip.heightFromFeet
                               || !clip.keepOriginalPositionXZ;
                    clip.name = name;
                    clip.loopTime = true;
                    clip.loopPose = true;
                    clip.lockRootRotation = !turn;
                    clip.lockRootHeightY = true;
                    clip.lockRootPositionXZ = false;
                    clip.keepOriginalOrientation = true;
                    // Высота корня — по стопам («Based Upon Feet»), а не исходная: скелет Mixamo с другой длиной ног, и
                    // с исходной высотой стопы клипа на нашем скелете уходили в пол (−4…−10 см на копии MEF и на манекене).
                    clip.keepOriginalPositionY = false;
                    clip.heightFromFeet = true;
                    clip.keepOriginalPositionXZ = true;
                }

                if (changed || importer.clipAnimations.Length == 0)
                {
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                }

                AnimationClip loaded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (loaded != null && AlignPhase(importer, loaded)) loaded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (loaded != null) result[name.Replace("Rifle_", "").Replace("MixamoRM_", "").Replace("Pistol_", "").Replace("Unarmed_", "")] = loaded;
            }

            if (AlignPackFacing(folder)) return EnsureClips(folder);
            return result;
        }

        /// <summary>FBX, с которого строится общий Avatar всех клипов Mixamo (покой без оружия — ближе всех к T-позе).</summary>
        public const string SharedAvatarSource = UnarmedFolder + "/Unarmed_Idle.fbx";

        /// <summary>
        /// Общий Avatar всех клипов Mixamo — со скелета <see cref="SharedAvatarSource"/>, поза приведена к T-позе («Enforce
        /// T-Pose» конфигуратора: <c>AvatarSetupTool.MakePoseValid</c>, внутренний API редактора — через отражение).
        /// <para>
        /// Клипы Mixamo скачаны без скина: в FBX нет позы привязки, и «Create From This Model» строил Avatar каждого клипа из
        /// его первого кадра (стойка с винтовкой, стопа в шаге). Пересадка с таким Avatar ломается: клип, проигранный на
        /// своём же скелете, не повторяет исходный кадр — стопа скручена на 23° (бег с винтовкой) и до 72° (шаг назад),
        /// носок уходит на 17–67 см (замер 2026-10-01). С общим Avatar в T-позе исходный кадр повторяется точно (0°, 0 см).
        /// Скелет у всех наборов Mixamo один (mixamorig), поэтому «Copy From Other Avatar».
        /// </para>
        /// </summary>
        public static Avatar EnsureSharedAvatar()
        {
            var importer = AssetImporter.GetAtPath(SharedAvatarSource) as ModelImporter;
            if (importer == null)
            {
                GameLog.Error($"[AvatarMixamoLocomotionSetup] Нет {SharedAvatarSource} — общий Avatar клипов Mixamo не собрать.");
                return null;
            }

            if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }

            if (!SharedAvatarInTPose(importer)) EnforceTPose(importer);
            return AssetDatabase.LoadAllAssetsAtPath(SharedAvatarSource).OfType<Avatar>().FirstOrDefault(a => a.isHuman);
        }

        /// <summary>Отпечаток позы общего Avatar (повороты костей описания) — метка в <c>userData</c> импортёров клипов.</summary>
        private static string SharedAvatarStamp()
        {
            var importer = AssetImporter.GetAtPath(SharedAvatarSource) as ModelImporter;
            if (importer == null) return "";
            int hash = 17;
            foreach (SkeletonBone bone in importer.humanDescription.skeleton)
                hash = hash * 31 + Mathf.RoundToInt(bone.rotation.x * 1000f) * 7 + Mathf.RoundToInt(bone.rotation.y * 1000f) * 13 + Mathf.RoundToInt(bone.rotation.z * 1000f);
            return "MixamoSharedAvatar:" + hash.ToString("X8");
        }

        private const BindingFlags ToolFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static System.Type SetupTool => typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool");

        /// <summary>Кости гуманоида экземпляра модели (<c>AvatarSetupTool.BoneWrapper[]</c>) по разметке импортёра.</summary>
        private static object HumanBones(ModelImporter importer, GameObject instance)
        {
            System.Type tool = SetupTool;
            MethodInfo getModelBones = tool?.GetMethod("GetModelBones", ToolFlags);
            MethodInfo getHumanBones = tool?.GetMethods(ToolFlags).FirstOrDefault(m => m.Name == "GetHumanBones" && m.GetParameters()[0].ParameterType == typeof(Dictionary<string, string>));
            if (getModelBones == null || getHumanBones == null) return null;

            object actual = getModelBones.Invoke(null, new object[] { instance.transform, false, null });
            var map = importer.humanDescription.human.ToDictionary(h => h.humanName, h => h.boneName);
            return getHumanBones.Invoke(null, new object[] { map, actual });
        }

        private static bool SharedAvatarInTPose(ModelImporter importer)
        {
            GameObject instance = InstantiateFromDescription(importer);
            try
            {
                object bones = HumanBones(importer, instance);
                MethodInfo isValid = SetupTool?.GetMethod("IsPoseValid", ToolFlags);
                if (bones == null || isValid == null) return true;
                return (bool)isValid.Invoke(null, new[] { bones }) && FeetForwardError(importer, instance) < 1f;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>«Enforce T-Pose»: поза скелета в описании гуманоида — T-поза; переимпорт.</summary>
        private static void EnforceTPose(ModelImporter importer)
        {
            GameObject instance = InstantiateFromDescription(importer);
            try
            {
                object bones = HumanBones(importer, instance);
                MethodInfo makeValid = SetupTool?.GetMethod("MakePoseValid", ToolFlags);
                MethodInfo getSkeleton = SetupTool?.GetMethod("GetSkeletonBones", ToolFlags);
                if (bones == null || makeValid == null || getSkeleton == null)
                {
                    GameLog.Error("[AvatarMixamoLocomotionSetup] Нет AvatarSetupTool.MakePoseValid/GetSkeletonBones (другая версия Unity) — Avatar клипов Mixamo не в T-позе.");
                    return;
                }

                makeValid.Invoke(null, new[] { bones });
                AlignFeetForward(importer, instance);
                HumanDescription description = importer.humanDescription;
                description.skeleton = (SkeletonBone[])getSkeleton.Invoke(null, new object[] { instance.transform });
                importer.humanDescription = description;
                importer.SaveAndReimport();
                GameLog.Player.Info($"[AvatarMixamoLocomotionSetup] Общий Avatar клипов Mixamo приведён к T-позе: {SharedAvatarSource}.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Стопы T-позы — носком строго вперёд. «Enforce T-Pose» выпрямляет кости, но разворот стопы вокруг вертикали
        /// оставляет как в исходной позе (покой: носки наружу на 14–27°). Разворот стопы в T-позе источника — ноль отсчёта
        /// стопы при пересадке: с носками наружу в T-позе все клипы на нашем скелете ставили стопы носком внутрь на 15–20°
        /// (замер на манекене и копии MEF против исходного скелета Mixamo).
        /// </summary>
        private static void AlignFeetForward(ModelImporter importer, GameObject instance)
        {
            foreach ((Transform foot, Transform toes) in Feet(importer, instance))
            {
                Vector3 dir = Vector3.ProjectOnPlane(toes.position - foot.position, Vector3.up);
                if (dir.sqrMagnitude < 1e-8f) continue;
                Quaternion q = Quaternion.AngleAxis(-Vector3.SignedAngle(Vector3.forward, dir, Vector3.up), Vector3.up);
                foot.rotation = q * foot.rotation;
            }
        }

        /// <summary>Наибольший разворот носка стопы от «вперёд» в позе экземпляра, °.</summary>
        private static float FeetForwardError(ModelImporter importer, GameObject instance)
        {
            float worst = 0f;
            foreach ((Transform foot, Transform toes) in Feet(importer, instance))
            {
                Vector3 dir = Vector3.ProjectOnPlane(toes.position - foot.position, Vector3.up);
                if (dir.sqrMagnitude > 1e-8f) worst = Mathf.Max(worst, Vector3.Angle(Vector3.forward, dir));
            }

            return worst;
        }

        private static IEnumerable<(Transform foot, Transform toes)> Feet(ModelImporter importer, GameObject instance)
        {
            var map = importer.humanDescription.human.ToDictionary(h => h.humanName, h => h.boneName);
            var byName = instance.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach ((string foot, string toes) in new[] { ("LeftFoot", "LeftToes"), ("RightFoot", "RightToes") })
                if (map.TryGetValue(foot, out string f) && map.TryGetValue(toes, out string t) && byName.TryGetValue(f, out Transform ft) && byName.TryGetValue(t, out Transform tt))
                    yield return (ft, tt);
        }

        /// <summary>Экземпляр модели в позе из описания гуманоида; имя корня — как в описании (по нему импортёр ищет кости).</summary>
        private static GameObject InstantiateFromDescription(ModelImporter importer)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath);
            GameObject instance = Object.Instantiate(model);
            HumanDescription description = importer.humanDescription;
            if (description.skeleton != null && description.skeleton.Length > 0)
            {
                instance.name = description.skeleton[0].name;
                var byName = instance.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                foreach (SkeletonBone bone in description.skeleton.Skip(1))
                    if (byName.TryGetValue(bone.name, out Transform t))
                        t.SetLocalPositionAndRotation(bone.position, bone.rotation);
            }

            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return instance;
        }

        /// <summary>Нормированное время, на котором у всех клипов хода левая стопа ниже всего (опора).</summary>
        private const float LeftPlantPhase = 0.5f;

        /// <summary>
        /// Фаза шага — одна у всех клипов хода (шаг/бег/спринт, присед): <c>cycleOffset</c> ставит опору левой стопы на
        /// <see cref="LeftPlantPhase"/>. Смешивание синхронизирует нормированное время клипов; у Mixamo шаг (опора левой на
        /// 0,74) и бег (0,45) разошлись на треть цикла — на скоростях между ними стопы смешанных клипов ехали по полу.
        /// Меряется один раз, пока смещение 0 (повторный запуск не трогает). Возвращает true, если клип переимпортирован.
        /// </summary>
        private static bool AlignPhase(ModelImporter importer, AnimationClip clip)
        {
            if (!(clip.name.Contains("Walk") || clip.name.Contains("Run") || clip.name.Contains("Sprint") ||
                  clip.name.Contains("Strafe") || clip.name.Contains("Jog")))
                return false;

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips.Length == 0 || clips[0].cycleOffset != 0f) return false;

            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarLegsRigBaker.RigPath(MainAvatarPrefab));
            if (rigPrefab == null) return false;

            GameObject rig = Object.Instantiate(rigPrefab);
            PlayableGraph graph = PlayableGraph.Create("AlignPhase");
            float phase;
            try
            {
                Animator animator = rig.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(playable);

                const int Samples = 100;
                float best = float.MaxValue;
                phase = 0f;
                for (int i = 0; i < Samples; i++)
                {
                    playable.SetTime(clip.length * i / Samples);
                    graph.Evaluate();
                    float d = left.position.y - right.position.y;
                    if (d < best)
                    {
                        best = d;
                        phase = (float)i / Samples;
                    }
                }
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(rig);
            }

            float offset = Mathf.Repeat(phase - LeftPlantPhase, 1f);
            if (offset < 0.005f) offset = 0.005f; // 0 — «ещё не мерили»
            clips[0].cycleOffset = offset;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return true;
        }

        /// <summary>Разворот стойки набора, который ещё не снят, °: меньше — не правится.</summary>
        private const float FacingTolerance = 1f;

        /// <summary>
        /// Таз клипов хода набора смотрит по корню: <c>Root Transform Rotation Offset</c> снимает «боевой разворот» стойки
        /// набора — одним углом на все клипы хода папки. Возвращает true, если клипы переимпортированы.
        /// <para>
        /// Mixamo «Pro Rifle Pack» и пистолет записаны в развороте: таз за цикл в среднем повёрнут на 25–40° вправо от
        /// корня (направления root motion), верх тела довёрнут обратно к прицелу. Это в самих данных (поза исходного FBX:
        /// таз 40° у шага с винтовкой), не в пересадке. У нас верх тела ведёт UltimateXR лицом вперёд, ноги — клип от
        /// корня, и стопы в развороте видны как «косолапость»: левый носок внутрь на 25–50° (стенд, 2026-10-01). Тот же
        /// заворот был и в основном конвейере T-42 на тех же клипах. Относительно своего таза стопы клипа стоят нормально.
        /// </para>
        /// <para>
        /// Угол один на набор (средний разворот таза по всем клипам хода), а не свой у каждого клипа: у клипов вбок таз
        /// повёрнут к ходу законно, и поклиповое снятие свело бы направления клипов вместе (шаг вбок совпал с диагональю,
        /// назад — с назад-вправо), а смешивание с совпадающими точками ломается — бег брал клип хода назад. Смещение
        /// (поворот корня запечён в позу, «Based Upon Original») поворачивает позу вместе с направлением root motion, и
        /// смешивание ставит клип в точку его настоящей скорости (<see cref="AddAtSpeed"/>) — поставленная стопа не едет.
        /// </para>
        /// </summary>
        /// <summary>
        /// Снимать ли разворот стойки набора. Выключено (2026-10-01, стенд-плейграунд): смещение поворачивает не только таз,
        /// но и root motion — «вперёд» у винтовки шёл на −28°, и прямой ход играл смесью «вперёд» и «вперёд-вправо», а клип
        /// своего направления не выигрывал (вес 0,6 против 0,9 у VRIK). Разворот стойки таза снимает драйвер (таз — отклонение
        /// от idle).
        /// </summary>
        private static readonly bool AlignFacing = false;

        private static bool AlignPackFacing(string folder)
        {
            if (!AlignFacing) return ResetPackFacing(folder);

            var clips = new List<(ModelImporter importer, AnimationClip clip, float authored)>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                ModelImporterClipAnimation[] settings = importer.clipAnimations;
                if (clip == null || settings.Length == 0 || !settings[0].lockRootRotation || !IsMoveClip(clip.name)) continue;

                float facing = MeasureHipsFacing(clip);
                if (float.IsNaN(facing)) return false;
                // Положительное смещение поворачивает таз влево (замер на Rifle_WalkForward: +32,7° → таз 0°).
                clips.Add((importer, clip, facing + settings[0].rotationOffset));
            }

            if (clips.Count == 0) return false;
            Vector2 sum = Vector2.zero;
            foreach (var c in clips) sum += new Vector2(Mathf.Sin(c.authored * Mathf.Deg2Rad), Mathf.Cos(c.authored * Mathf.Deg2Rad));
            float pack = Mathf.Atan2(sum.x, sum.y) * Mathf.Rad2Deg;
            if (Mathf.Abs(pack) < FacingTolerance) pack = 0f;

            bool changed = false;
            foreach (var c in clips)
            {
                ModelImporterClipAnimation[] settings = c.importer.clipAnimations;
                if (Mathf.Abs(Mathf.DeltaAngle(settings[0].rotationOffset, pack)) < FacingTolerance) continue;
                settings[0].rotationOffset = pack;
                c.importer.clipAnimations = settings;
                c.importer.SaveAndReimport();
                changed = true;
            }

            if (changed) GameLog.Player.Info($"[AvatarMixamoLocomotionSetup] {folder}: разворот стойки набора {pack:0.0}° снят смещением поворота корня ({clips.Count} клипов хода).");
            return changed;
        }

        /// <summary>Смещение поворота корня клипов хода — в 0 (честные направления root motion). true — что-то переимпортировано.</summary>
        private static bool ResetPackFacing(string folder)
        {
            bool changed = false;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                ModelImporterClipAnimation[] settings = importer.clipAnimations;
                if (settings.Length == 0 || Mathf.Abs(settings[0].rotationOffset) < 0.01f) continue;
                settings[0].rotationOffset = 0f;
                importer.clipAnimations = settings;
                importer.SaveAndReimport();
                changed = true;
            }
            if (changed) GameLog.Player.Info($"[AvatarMixamoLocomotionSetup] {folder}: смещение поворота корня клипов сброшено в 0.");
            return changed;
        }

        private static bool IsMoveClip(string name) =>
            name.Contains("Walk") || name.Contains("Run") || name.Contains("Sprint") || name.Contains("Strafe") || name.Contains("Jog");

        /// <summary>Средний за цикл разворот линии бёдер клипа на копии рига от её корня, ° (&gt; 0 — вправо). NaN — нет рига.</summary>
        private static float MeasureHipsFacing(AnimationClip clip)
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarLegsRigBaker.RigPath(MainAvatarPrefab));
            if (rigPrefab == null) return float.NaN;

            GameObject rig = Object.Instantiate(rigPrefab);
            PlayableGraph graph = PlayableGraph.Create("MeasureHipsFacing");
            try
            {
                Animator animator = rig.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(playable);

                const int Samples = 40;
                Vector2 sum = Vector2.zero;
                for (int i = 0; i < Samples; i++)
                {
                    playable.SetTime(clip.length * i / Samples);
                    graph.Evaluate();
                    rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Vector3 across = Vector3.ProjectOnPlane(right.position - left.position, Vector3.up);
                    Vector3 forward = Vector3.Cross(across, Vector3.up);
                    float a = Mathf.Atan2(forward.x, forward.z);
                    sum += new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                }

                return Mathf.Atan2(sum.x, sum.y) * Mathf.Rad2Deg;
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(rig);
            }
        }

        private static AnimationClip Get(Dictionary<string, AnimationClip> clips, string name)
        {
            if (clips.TryGetValue(name, out AnimationClip clip)) return clip;
            GameLog.Error($"[AvatarMixamoLocomotionSetup] Нет клипа {name}.");
            return null;
        }

        private static void AddAtSpeed(BlendTree tree, AnimationClip clip)
        {
            if (clip == null) return;
            Vector3 v = clip.averageSpeed;
            tree.AddChild(clip, new Vector2(v.x, v.z));
        }

        /// <summary>Новое смешивание — вложенный ассет контроллера (одно- или двумерное — Freeform Directional).</summary>
        private static BlendTree Tree(AnimatorController controller, string name, string param, string paramY = null)
        {
            var tree = new BlendTree
            {
                name = name,
                blendParameter = param,
                // 2D — Freeform Directional: клипы по направлению (в одном направлении — шаг/бег/спринт по скорости), в
                // центре — покой. Cartesian смешивал противоположные направления на малой скорости.
                blendType = paramY == null ? BlendTreeType.Simple1D : BlendTreeType.FreeformDirectional2D,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            if (paramY != null) tree.blendParameterY = paramY;
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        /// <summary>
        /// Foot IK — в мышцы ног неподвижного клипа: первый кадр исходника на копии рига основного аватара с Foot IK, мышцы ног
        /// снимаются и пишутся константами. Состояние покоя играет без Foot IK, а без него стопы клипов Mixamo на скелете MEF
        /// уходят в пол и заваливаются — поэтому поза покоя запекается с Foot IK заранее.
        /// </summary>
        private static void BakeFootIK(AnimationClip source, AnimationClip target)
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarLegsRigBaker.RigPath(MainAvatarPrefab));
            if (rigPrefab == null) return;
            GameObject rig = Object.Instantiate(rigPrefab);
            rig.hideFlags = HideFlags.HideAndDontSave;
            PlayableGraph graph = default;
            try
            {
                foreach (MonoBehaviour mb in rig.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
                Animator animator = rig.GetComponent<Animator>();
                if (animator == null || !animator.isHuman) return;
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                graph = PlayableGraph.Create("BakeFootIK");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, source);
                playable.SetApplyFootIK(true);
                playable.SetTime(0);
                AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(playable);
                graph.Evaluate();

                var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                handler.Dispose();
                string[] names = HumanTrait.MuscleName;
                for (int i = 0; i < names.Length; i++)
                {
                    if (!(names[i].Contains("Upper Leg") || names[i].Contains("Lower Leg") || names[i].Contains("Foot") || names[i].Contains("Toes"))) continue;
                    AnimationUtility.SetEditorCurve(target, EditorCurveBinding.FloatCurve("", typeof(Animator), names[i]), AnimationCurve.Constant(0f, 1f, pose.muscles[i]));
                }
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(rig);
            }
        }

        /// <summary>Неподвижный клип — первый кадр исходного (покой без «дыхания»: игрок в шлеме качается сам).</summary>
        private static AnimationClip StaticClip(AnimationClip source)
        {
            if (source == null) return null;
            string path = Path.GetDirectoryName(AssetDatabase.GetAssetPath(source)).Replace(Path.DirectorySeparatorChar, '/') + "/" + source.name + "_Static.anim";
            // Пересобирается каждый раз (на месте, GUID тот же): кривые исходника — мышцы, они меняются с Avatar и импортом.
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            AnimationClip clip = existing != null ? existing : new AnimationClip { name = Path.GetFileNameWithoutExtension(path) };
            clip.ClearCurves();
            clip.frameRate = source.frameRate;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                float value = AnimationUtility.GetEditorCurve(source, binding).Evaluate(0f);
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f, value));
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            // Смещение высоты корня из импорта (сидение на полу поднято на 0,11 м — иначе ягодицы MEF в полу): в кривые оно не
            // запекается, это настройка клипа.
            settings.level = AnimationUtility.GetAnimationClipSettings(source).level;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            // Клип со смещением высоты (сидение) уже подогнан к MEF без Foot IK: Foot IK держит стопы на месте исходника
            // относительно тела, и поднятое тело поднимало бы стопы над полом (+11 см).
            if (Mathf.Approximately(settings.level, 0f)) BakeFootIK(source, clip);
            if (existing == null) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>
        /// Контроллер с нуля на месте ассета (GUID тот же): старые слои, состояния, переходы и смешивания удаляются; параметры,
        /// слой с маской, покой и ход, переходы между ними — по контракту <see cref="UxrLegLocomotion"/>.
        /// </summary>
        private static AnimatorController RecreateController(out AnimatorState idle, out AnimatorState moving)
        {
            AvatarLegsRigBaker.EnsureFolder(LocomotionFolder);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.layers = new AnimatorControllerLayer[0];
            controller.parameters = new AnimatorControllerParameter[0];
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (sub != null && sub != controller && !(sub is AnimatorController)) AssetDatabase.RemoveObjectFromAsset(sub);

            AddParameter(controller, UxrLegLocomotion.MoveXParam, AnimatorControllerParameterType.Float, 0f);
            AddParameter(controller, UxrLegLocomotion.MoveZParam, AnimatorControllerParameterType.Float, 0f);
            AddParameter(controller, UxrLegLocomotion.IsMovingParam, AnimatorControllerParameterType.Bool, 0f);
            AddParameter(controller, UxrLegLocomotion.SpeedParam, AnimatorControllerParameterType.Float, 1f);
            AddParameter(controller, UxrLegLocomotion.TurnParam, AnimatorControllerParameterType.Float, 0f);
            // Винтовка — стойка по умолчанию (выбор по оружию пока выключен, AvatarStanceFromGrabs.SelectByGrabs).
            AddParameter(controller, UxrLegLocomotion.StanceParam, AnimatorControllerParameterType.Float, SetRifle);

            controller.AddLayer("Legs");
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].avatarMask = EnsureMask();
            layers[0].defaultWeight = 1f;
            controller.layers = layers;

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            idle = machine.AddState(UxrLegLocomotion.IdleState, new Vector3(300f, 0f));
            moving = machine.AddState(UxrLegLocomotion.MoveState, new Vector3(300f, 120f));
            machine.defaultState = idle;

            // Foot IK гуманоида: стопа копии — в позе стопы исходника (с поправкой на высоту подошвы аватаров), а не
            // там, куда её приводят мышцы ноги с чужими пропорциями. Без него стопы клипов Mixamo на нашем скелете уходили
            // в пол на 5–13 см и заваливались на ребро до 25° (замер на копии рига, 2026-10-01).
            // Покой — без Foot IK: в нём смешиваются колено и сидение, а цель Foot IK правой стопы смешивается между стопой
            // сзади на носке и стопой впереди, повёрнутой подошвой, — на 25–75 % бедро копии переворачивалось (колено над
            // тазом, мышцы за ±2). Без Foot IK смесь мышц линейна. Подошву покоя ставит на пол UxrAnimatedLegs (сдвиг цели
            // стопы); завал стопы покоя без Foot IK до ~7°, на ходу (Foot IK нужен: подошва −8 см, завал 23°) — не трогается.
            idle.iKOnFeet = false;
            moving.iKOnFeet = true;
            idle.writeDefaultValues = true;
            moving.writeDefaultValues = true;
            // Частота шага: скорость проигрывания хода подгоняет UxrLegLocomotion, чтобы root motion догонял тело.
            moving.speedParameter = UxrLegLocomotion.SpeedParam;
            moving.speedParameterActive = true;

            // Как в контроллере VRIK Animated (Final IK): старт с фазы 0,75 цикла, переходы 0,2 с.
            AnimatorStateTransition start = idle.AddTransition(moving);
            start.AddCondition(AnimatorConditionMode.If, 0f, UxrLegLocomotion.IsMovingParam);
            start.hasExitTime = false;
            start.hasFixedDuration = true;
            start.duration = 0.2f;
            start.offset = 0.75f;

            AnimatorStateTransition stop = moving.AddTransition(idle);
            stop.name = UxrLegLocomotion.StopTransition;
            stop.AddCondition(AnimatorConditionMode.IfNot, 0f, UxrLegLocomotion.IsMovingParam);
            stop.hasExitTime = false;
            stop.hasFixedDuration = true;
            stop.duration = 0.2f;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddParameter(AnimatorController controller, string name, AnimatorControllerParameterType type, float value)
        {
            controller.AddParameter(new AnimatorControllerParameter
                { name = name, type = type, defaultFloat = value, defaultBool = value > 0f });
        }

        /// <summary>
        /// Маска слоя ног: корень (root motion), корпус (таз и грудь — от них отклонения на аватар) и ноги с Foot IK. Голова,
        /// руки и пальцы копии не нужны — их не считать.
        /// </summary>
        private static AvatarMask EnsureMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            bool create = mask == null;
            if (create) mask = new AvatarMask();
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Root || part == AvatarMaskBodyPart.Body ||
                                                     part == AvatarMaskBodyPart.LeftLeg || part == AvatarMaskBodyPart.RightLeg ||
                                                     part == AvatarMaskBodyPart.LeftFootIK || part == AvatarMaskBodyPart.RightFootIK);
            if (create) AssetDatabase.CreateAsset(mask, MaskPath);
            else EditorUtility.SetDirty(mask);
            return mask;
        }

        private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine)
        {
            foreach (ChildAnimatorState child in machine.states) yield return child.state;
            foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
            foreach (AnimatorState state in AllStates(sub.stateMachine))
                yield return state;
        }
    }
}
