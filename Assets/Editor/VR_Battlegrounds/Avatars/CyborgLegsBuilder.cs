using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FIMSpace.FProceduralAnimation;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;
using VRBattlegrounds.Integration;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Ноги киборга (<c>PlayerControllersCyborgAvatar</c>) — ноги робота-донора из <c>ThirdParty/UnityStarter_Robot</c>
    /// (<see cref="LegsDonor"/>: Kyle или Armature; в префабе — <see cref="Selected"/>), пришитые к скелету киборга.
    /// Генерация, а не ручная настройка: всё, чем киборг с ногами отличается от сэмпла UltimateXR, задано здесь,
    /// и повторная сборка приводит префаб к этому виду (объекты ищутся по имени — id и ссылки сохраняются).
    /// Донор описан рецептом (<c>DonorRecipe</c>: модель, кости ног, срез таза, меш, материалы подсеток) —
    /// код сборки общий.
    ///
    /// <list type="number">
    /// <item><b>Кости.</b> Под <c>Pelvis</c> — цепочки <c>UpperLeg → LowerLeg → Foot → Toes</c> (<c>_Left</c>/<c>_Right</c>)
    ///       в позиции костей донора, пересчитанных на киборга: масштаб — отношение высоты головы киборга к
    ///       высоте головы донора (пропорции робота сохраняются), по горизонтали таз донора совмещён с тазом
    ///       киборга, по вертикали ступни остаются на полу. Повороты костей — осями корня аватара:
    ///       плагину ног и хитбоксам важны только позиции, а ровные оси стопы — это
    ///       <c>AnkleForward/Up</c> без поправок.</item>
    /// <item><b>Меш</b> (путь — в рецепте) — треугольники донора, все вершины которых принадлежат ногам
    ///       (главная кость — кость ноги или её потомок) или низу таза донора (не выше среза рецепта над
    ///       тазобедренными суставами: выше, например, красная пластина живота Kyle пробивает корпус киборга).
    ///       Подсетки донора сохраняются. Вершины запечены в пространство нового скина, веса таза и
    ///       позвоночника донора — на <c>Pelvis</c> киборга. Низ таза донора уходит под пояс киборга и закрывает пах.</item>
    /// <item><b>Скин</b> <c>CyborgGeo/LegsGeo</c> на копиях материалов донора в <see cref="OutFolder"/>,
    ///       настройки рендерера — как у <c>BodyGeo</c>; добавлен в <c>UxrAvatar._avatarRenderers</c>.</item>
    /// <item><b>Скелет UltimateXR</b> — ноги в <c>UxrAvatarRig</c> (по ним <see cref="HitboxBuilder"/> строит
    ///       хитбоксы ног).</item>
    /// <item><b>Humanoid</b> — <c>Animator</c> на объекте <c>Cyborg</c> с аватаром <see cref="HumanAvatarPath"/>,
    ///       собранным <c>AvatarBuilder</c> из скелета киборга. Humanoid-Hips — <c>CyborgRig</c>: таз и
    ///       позвоночник киборга — соседи под ним.</item>
    /// <item><b>Legs Animator</b> + <see cref="LegsAnimatorUxrBridge"/> на <c>Cyborg</c> — копия настроек
    ///       <c>MEF_Base_Avatar</c> с переназначенными костями, таз плагина — <c>Pelvis</c> (таз
    ///       <c>UxrAvatarRig</c>; <c>CyborgRig</c> UltimateXR при старте переносит под <c>Dummy Forward</c>),
    ///       <c>baseTransform</c> — корень аватара: <c>Cyborg</c> висит на 1.55 м, и плагин, инициализируясь
    ///       до привязки моста, мерил бы таз от него.</item>
    /// </list>
    /// Меню <c>Build Cyborg Legs</c> после сборки запускает <see cref="HitboxBuilder.Build"/> — хитбоксы ног,
    /// призрак (вариант киборга) и трупы. Проверка — <c>AvatarLoadoutTests.Legs_Animator_настроен_на_своих_костях</c>,
    /// <c>PrefabCompositionTests.У_каждого_аватара_один_Legs_Animator_на_humanoid_риге</c>, <c>HitboxTests</c>.
    /// </summary>
    public static class CyborgLegsBuilder
    {
        public const string AvatarPath = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        public const string OutFolder = "Assets/Art/Avatars/PlayerControllersCyborgAvatar/Legs";
        public const string HumanAvatarPath = OutFolder + "/CyborgHumanoid.asset";

        private const string TemplatePath = "Assets/Prefabs/Player/MEF_Base_Avatar.prefab";
        private const string RobotPack = "Assets/ThirdParty/UnityStarter_Robot/";

        private const string ModelName = "Cyborg";
        private const string RigName = "CyborgRig";
        private const string PelvisName = "Pelvis";
        private const string GeoName = "CyborgGeo";
        private const string BodyGeoName = "BodyGeo";
        private const string LegsGeoName = "LegsGeo";

        /// <summary>Модель — донор ног.</summary>
        public enum LegsDonor
        {
            /// <summary>Робот Kyle: тонкие ноги, 1,9 тыс. треугольников, один материал.</summary>
            Kyle,

            /// <summary>Робот Armature: плотные ноги, ~11,9 тыс. треугольников, два материала (ноги и таз).</summary>
            Armature,
        }

        /// <summary>
        /// Донор ног в префабе киборга. Меню <c>Build Cyborg Legs</c> собирает его. Armature — с 2026-09-30:
        /// ноги Kyle при корпусе киборга выглядели чужими и слишком худыми (сравнение — CHANGELOG).
        /// </summary>
        public const LegsDonor Selected = LegsDonor.Armature;

        /// <summary>
        /// Рецепт донора: модель, его кости (в порядке <see cref="Bones"/>), срез таза, куда писать меш и
        /// какими материалами красить подсетки. Новый донор — новый рецепт, код сборки общий.
        /// </summary>
        private sealed class DonorRecipe
        {
            public string ModelPath;
            public string Hips;
            public string Head;
            public string[] LegBones;
            /// <summary>Срез таза донора над его тазобедренными суставами, метры донора: выше таз пробивает корпус.</summary>
            public float PelvisCut;
            public string MeshPath;
            /// <summary>По подсетке донора: исходный материал и имя копии в <see cref="OutFolder"/>.</summary>
            public string[] SourceMaterials;
            public string[] MaterialNames;
        }

        private static readonly DonorRecipe KyleRecipe = new DonorRecipe
        {
            ModelPath = RobotPack + "KyleRobot/Models/KyleRobot.fbx",
            Hips = "Hips",
            Head = "Head",
            LegBones = new[] { "LeftLeg", "LeftCalf", "LeftFoot", "LeftToes", "RightLeg", "RightCalf", "RightFoot", "RightToes" },
            PelvisCut = 0.04f,
            MeshPath = OutFolder + "/CyborgLegs_Mesh.asset",
            SourceMaterials = new[] { RobotPack + "KyleRobot/Materials/KyleRobot.mat" },
            MaterialNames = new[] { "CyborgLegs" },
        };

        /// <summary>
        /// Armature: подсетка 0 — корпус (из неё берётся низ таза), 1 — руки и голова (в ноги не попадает),
        /// 2 — ноги. Материалы в FBX не переназначены (встроенные «Lit») — берутся из папки пака.
        /// </summary>
        private static readonly DonorRecipe ArmatureRecipe = new DonorRecipe
        {
            ModelPath = RobotPack + "Armature/Models/Armature.fbx",
            Hips = "Hips",
            Head = "Head",
            LegBones = new[] { "Left_UpperLeg", "Left_LowerLeg", "Left_Foot", "Left_Toes", "Right_UpperLeg", "Right_LowerLeg", "Right_Foot", "Right_Toes" },
            PelvisCut = 0f,
            MeshPath = OutFolder + "/CyborgLegs_Armature_Mesh.asset",
            SourceMaterials = new[] { RobotPack + "Armature/Materials/M_Armature_Body.mat", RobotPack + "Armature/Materials/M_Armature_Arms.mat", RobotPack + "Armature/Materials/M_Armature_Legs.mat" },
            MaterialNames = new[] { "CyborgLegs_ArmatureBody", "CyborgLegs_ArmatureArms", "CyborgLegs_ArmatureLegs" },
        };

        private static DonorRecipe RecipeOf(LegsDonor donor) => donor == LegsDonor.Armature ? ArmatureRecipe : KyleRecipe;

        /// <summary>Кость ноги киборга и её родитель.</summary>
        private sealed class LegBone
        {
            public string Name;
            public string Parent;
        }

        private static readonly LegBone[] Bones =
        {
            new LegBone { Name = "UpperLeg_Left", Parent = PelvisName },
            new LegBone { Name = "LowerLeg_Left", Parent = "UpperLeg_Left" },
            new LegBone { Name = "Foot_Left", Parent = "LowerLeg_Left" },
            new LegBone { Name = "Toes_Left", Parent = "Foot_Left" },
            new LegBone { Name = "UpperLeg_Right", Parent = PelvisName },
            new LegBone { Name = "LowerLeg_Right", Parent = "UpperLeg_Right" },
            new LegBone { Name = "Foot_Right", Parent = "LowerLeg_Right" },
            new LegBone { Name = "Toes_Right", Parent = "Foot_Right" },
        };

        /// <summary>Humanoid-разметка киборга (Mecanim → кость).</summary>
        private static readonly string[,] HumanMap =
        {
            { "Hips", RigName }, { "Spine", "Spine01" }, { "Chest", "Spine02" }, { "Neck", "Neck" }, { "Head", "Head" },
            { "LeftShoulder", "Clavicle_Left" }, { "LeftUpperArm", "Arm_Left" }, { "LeftLowerArm", "Forearm_Left" }, { "LeftHand", "Hand_Left" },
            { "RightShoulder", "Clavicle_Right" }, { "RightUpperArm", "Arm_Right" }, { "RightLowerArm", "Forearm_Right" }, { "RightHand", "Hand_Right" },
            { "LeftUpperLeg", "UpperLeg_Left" }, { "LeftLowerLeg", "LowerLeg_Left" }, { "LeftFoot", "Foot_Left" }, { "LeftToes", "Toes_Left" },
            { "RightUpperLeg", "UpperLeg_Right" }, { "RightLowerLeg", "LowerLeg_Right" }, { "RightFoot", "Foot_Right" }, { "RightToes", "Toes_Right" },
        };

        private static void BuildMenu()
        {
            string report = Build(Selected);
            if (report.StartsWith("ОТКАЗ")) return;

            // Хитбоксы ног по новому скелету; сборщик хитбоксов пересобирает и призрака (вариант киборга), и трупы.
            HitboxBuilder.Build();
        }

        /// <summary>Собирает ноги киборга. Возвращает отчёт; при отказе префаб не меняется.</summary>
        public static string Build(LegsDonor donor)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
            if (template == null) return Fail($"нет {TemplatePath}");

            EnsureFolder(OutFolder);

            GameObject root = PrefabUtility.LoadPrefabContents(AvatarPath);
            try
            {
                var report = new StringBuilder();
                string error = Assemble(root, RecipeOf(donor), template, report);
                if (error != null) return Fail(error);

                VrBattlegrounds.Editor.Avatars.Workbench.AvatarMaintenanceTools.SavePrefab(root, AvatarPath);
                AssetDatabase.SaveAssets();
                GameLog.Player.Info($"[CyborgLegsBuilder] {AvatarPath}: {report}");
                return "OK. " + report;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string Fail(string message)
        {
            GameLog.Error($"[CyborgLegsBuilder] Отказ, префаб не изменён: {message}");
            return "ОТКАЗ. " + message;
        }

        /// <summary>
        /// Пробная сборка ног на экземпляре киборга (например, в превью-сцене) для сравнения доноров на рендерах:
        /// кости и скин с мешем в памяти, меш ассетом не пишется, humanoid и Legs Animator не трогаются.
        /// Копии материалов донора создаются (они нужны и превью, и будущей сборке).
        /// </summary>
        public static string Preview(GameObject instance, LegsDonor donor)
        {
            EnsureFolder(OutFolder);
            var report = new StringBuilder();
            string error = BuildLegs(instance, RecipeOf(donor), false, report, out _, out _);
            return error != null ? "ОТКАЗ. " + error : "OK. " + report;
        }

        /// <summary>Кости ног и скин <c>LegsGeo</c>. <paramref name="persist"/> — записать меш ассетом рецепта.</summary>
        private static string BuildLegs(GameObject root, DonorRecipe recipe, bool persist, StringBuilder report,
                                        out Dictionary<string, Transform> created, out SkinnedMeshRenderer skin)
        {
            created = null;
            skin = null;
            Transform pelvis = Find(root.transform, PelvisName);
            Transform geo = Find(root.transform, GeoName);
            Transform bodyGeo = geo != null ? geo.Find(BodyGeoName) : null;
            var avatar = root.GetComponent<UxrAvatar>();
            if (pelvis == null || geo == null || bodyGeo == null || avatar == null)
                return $"в '{root.name}' нет {PelvisName}/{GeoName}/{BodyGeoName} или UxrAvatar";

            var donor = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.ModelPath);
            SkinnedMeshRenderer donorSkin = donor != null ? donor.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (donorSkin == null) return $"нет донора или его скина: {recipe.ModelPath}";
            if (donorSkin.sharedMesh.subMeshCount != recipe.SourceMaterials.Length)
                return $"у донора {donorSkin.sharedMesh.subMeshCount} подсеток, в рецепте материалов {recipe.SourceMaterials.Length}";

            // Мир донора в позе привязки: матрица кости = скин · bindpose⁻¹.
            Matrix4x4[] donorBind = donorSkin.sharedMesh.bindposes;
            var donorBoneWorld = new Dictionary<string, Matrix4x4>();
            for (int b = 0; b < donorSkin.bones.Length; b++)
                donorBoneWorld[donorSkin.bones[b].name] = donorSkin.transform.localToWorldMatrix * donorBind[b].inverse;

            if (!donorBoneWorld.ContainsKey(recipe.Hips) || !donorBoneWorld.ContainsKey(recipe.Head) || recipe.LegBones.Any(b => !donorBoneWorld.ContainsKey(b)))
                return $"у донора {recipe.ModelPath} нет нужных костей (таз, голова, ноги)";

            Transform head = avatar.AvatarRig.Head.Head;
            if (head == null) return "у киборга не размечена голова в UxrAvatarRig";

            // Перенос донор → киборг в пространстве корня аватара: масштаб по высоте головы, таз совмещён по горизонтали.
            Vector3 donorHead = donorBoneWorld[recipe.Head].GetColumn(3);
            Vector3 donorHips = donorBoneWorld[recipe.Hips].GetColumn(3);
            Vector3 cyborgHead = root.transform.InverseTransformPoint(head.position);
            Vector3 cyborgPelvis = root.transform.InverseTransformPoint(pelvis.position);
            float scale = cyborgHead.y / donorHead.y;
            var offset = new Vector3(cyborgPelvis.x - donorHips.x * scale, 0f, cyborgPelvis.z - donorHips.z * scale);
            Matrix4x4 toAvatar = root.transform.localToWorldMatrix * Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one * scale);
            report.Append($"донор {System.IO.Path.GetFileNameWithoutExtension(recipe.ModelPath)}, масштаб {scale:F3}, сдвиг {offset.ToString("F3")}. ");

            // 1. Кости.
            created = new Dictionary<string, Transform> { { PelvisName, pelvis } };
            for (int i = 0; i < Bones.Length; i++)
            {
                LegBone bone = Bones[i];
                Transform parent = created[bone.Parent];
                Transform t = parent.Find(bone.Name);
                if (t == null)
                {
                    t = new GameObject(bone.Name).transform;
                    t.SetParent(parent, false);
                }

                t.gameObject.layer = pelvis.gameObject.layer;
                t.SetPositionAndRotation(toAvatar.MultiplyPoint3x4(donorBoneWorld[recipe.LegBones[i]].GetColumn(3)), root.transform.rotation);
                t.localScale = Vector3.one;
                created[bone.Name] = t;
            }

            // 2. Меш и скин.
            Transform legsGeo = geo.Find(LegsGeoName);
            if (legsGeo == null)
            {
                legsGeo = new GameObject(LegsGeoName).transform;
                legsGeo.SetParent(geo, false);
            }
            legsGeo.gameObject.layer = bodyGeo.gameObject.layer;
            legsGeo.localPosition = Vector3.zero;
            legsGeo.localRotation = Quaternion.identity;
            legsGeo.localScale = Vector3.one;

            var skinBones = new Transform[Bones.Length + 1];
            skinBones[0] = pelvis;
            for (int i = 0; i < Bones.Length; i++) skinBones[i + 1] = created[Bones[i].Name];

            float donorHipJoint = donorBoneWorld[recipe.LegBones[0]].GetColumn(3).y;
            Mesh mesh = BuildMesh(donorSkin, recipe, donorHipJoint, toAvatar, legsGeo, skinBones, persist, report, out int[] usedSubmeshes);
            if (mesh == null) return $"у донора {recipe.ModelPath} не нашлось треугольников ног";

            skin = legsGeo.GetComponent<SkinnedMeshRenderer>();
            if (skin == null) skin = legsGeo.gameObject.AddComponent<SkinnedMeshRenderer>();
            var body = bodyGeo.GetComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = skinBones;
            skin.rootBone = pelvis;
            skin.sharedMaterials = usedSubmeshes.Select(s => EnsureMaterial(recipe.SourceMaterials[s], recipe.MaterialNames[s])).ToArray();
            skin.localBounds = LocalBounds(mesh, legsGeo, pelvis);
            skin.shadowCastingMode = body.shadowCastingMode;
            skin.receiveShadows = body.receiveShadows;
            skin.quality = body.quality;
            skin.updateWhenOffscreen = body.updateWhenOffscreen;
            skin.skinnedMotionVectors = body.skinnedMotionVectors;
            skin.lightProbeUsage = body.lightProbeUsage;
            skin.reflectionProbeUsage = body.reflectionProbeUsage;
            skin.probeAnchor = body.probeAnchor;
            return null;
        }

        private static string Assemble(GameObject root, DonorRecipe recipe, GameObject template, StringBuilder report)
        {
            Transform model = Find(root.transform, ModelName);
            Transform rig = Find(root.transform, RigName);
            Transform pelvis = Find(root.transform, PelvisName);
            var avatar = root.GetComponent<UxrAvatar>();
            if (model == null || rig == null || pelvis == null || avatar == null)
                return $"в {AvatarPath} нет {ModelName}/{RigName}/{PelvisName} или UxrAvatar";

            string legsBuildError = BuildLegs(root, recipe, true, report, out Dictionary<string, Transform> created, out SkinnedMeshRenderer skin);
            if (legsBuildError != null) return legsBuildError;

            // 3. UxrAvatar: ноги скелета и список рендереров.
            var so = new SerializedObject(avatar);
            SetLeg(so, "_rig._leftLeg", created["UpperLeg_Left"], created["LowerLeg_Left"], created["Foot_Left"], created["Toes_Left"]);
            SetLeg(so, "_rig._rightLeg", created["UpperLeg_Right"], created["LowerLeg_Right"], created["Foot_Right"], created["Toes_Right"]);
            SerializedProperty renderers = so.FindProperty("_avatarRenderers");
            bool listed = false;
            for (int i = 0; i < renderers.arraySize; i++)
                listed |= renderers.GetArrayElementAtIndex(i).objectReferenceValue == skin;
            if (!listed)
            {
                renderers.arraySize++;
                renderers.GetArrayElementAtIndex(renderers.arraySize - 1).objectReferenceValue = skin;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // 4. Humanoid.
            Avatar human = BuildHumanAvatar(model, out string humanError);
            if (human == null) return humanError;

            Animator templateAnimator = template.GetComponentsInChildren<Animator>(true).First(a => a.avatar != null && a.avatar.isHuman);
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.gameObject.AddComponent<Animator>();
            animator.avatar = human;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = templateAnimator.applyRootMotion;
            animator.cullingMode = templateAnimator.cullingMode;
            animator.updateMode = templateAnimator.updateMode;

            // 5. Legs Animator и мост.
            string legsError = SetupLegsAnimator(root, model, animator, pelvis, created, template, report);
            if (legsError != null) return legsError;

            report.Append($"костей ног {Bones.Length}, humanoid '{human.name}'.");
            return null;
        }

        private static Mesh BuildMesh(SkinnedMeshRenderer donor, DonorRecipe recipe, float donorHipJointHeight, Matrix4x4 toAvatar,
                                     Transform legsGeo, Transform[] skinBones, bool persist, StringBuilder report, out int[] usedSubmeshes)
        {
            usedSubmeshes = null;
            Mesh source = donor.sharedMesh;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            Vector2[] uv = source.uv;
            BoneWeight[] weights = source.boneWeights;
            string[] donorNames = donor.bones.Select(b => b.name).ToArray();

            // Индекс кости донора → индекс кости скина: кость ноги рецепта или её потомок (ToesEnd) —
            // своя кость, всё прочее — таз (индекс 0).
            int[] remap = donor.bones.Select(LegIndexOf(recipe)).ToArray();
            // Ноги — целиком; таз донора — только ниже среза: выше он пробивает корпус киборга спереди
            // (у Kyle — красная пластина на животе). Оставшийся низ таза закрывает пах под поясом киборга.
            Matrix4x4 donorToWorld = donor.transform.localToWorldMatrix;
            float cut = donorHipJointHeight + recipe.PelvisCut;
            bool[] keep = new bool[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                int bone = weights[i].boneIndex0;
                keep[i] = remap[bone] > 0 || (donorNames[bone] == recipe.Hips && donorToWorld.MultiplyPoint3x4(vertices[i]).y <= cut);
            }

            var map = new Dictionary<int, int>();
            var submeshes = new List<List<int>>();
            var used = new List<int>();
            int triangleCount = 0;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                int[] triangles = source.GetTriangles(s);
                triangleCount += triangles.Length / 3;
                var kept = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (!keep[triangles[i]] || !keep[triangles[i + 1]] || !keep[triangles[i + 2]]) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = triangles[i + k];
                        if (!map.TryGetValue(v, out int n)) map[v] = n = map.Count;
                        kept.Add(n);
                    }
                }

                if (kept.Count == 0) continue;
                submeshes.Add(kept);
                used.Add(s);
            }
            if (submeshes.Count == 0) return null;
            usedSubmeshes = used.ToArray();

            // Вершины донора → мир аватара → пространство LegsGeo. Масштаб равномерный — нормали не искажаются.
            Matrix4x4 toGeo = legsGeo.worldToLocalMatrix * toAvatar * donor.transform.localToWorldMatrix;
            int count = map.Count;
            var newVertices = new Vector3[count];
            var newNormals = new Vector3[count];
            var newTangents = new Vector4[count];
            var newUv = new Vector2[count];
            var newWeights = new BoneWeight[count];
            foreach (KeyValuePair<int, int> pair in map)
            {
                int o = pair.Key, n = pair.Value;
                newVertices[n] = toGeo.MultiplyPoint3x4(vertices[o]);
                if (normals.Length > 0) newNormals[n] = toGeo.MultiplyVector(normals[o]).normalized;
                if (tangents.Length > 0)
                {
                    Vector3 t = toGeo.MultiplyVector(tangents[o]).normalized;
                    newTangents[n] = new Vector4(t.x, t.y, t.z, tangents[o].w);
                }
                if (uv.Length > 0) newUv[n] = uv[o];
                newWeights[n] = Remap(weights[o], remap);
            }

            Mesh mesh = persist ? AssetDatabase.LoadAssetAtPath<Mesh>(recipe.MeshPath) : null;
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            mesh.Clear();
            mesh.name = System.IO.Path.GetFileNameWithoutExtension(recipe.MeshPath).Replace("_Mesh", "");
            mesh.indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = newVertices;
            if (normals.Length > 0) mesh.normals = newNormals;
            if (tangents.Length > 0) mesh.tangents = newTangents;
            if (uv.Length > 0) mesh.uv = newUv;
            mesh.boneWeights = newWeights;
            mesh.bindposes = skinBones.Select(b => b.worldToLocalMatrix * legsGeo.localToWorldMatrix).ToArray();
            mesh.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s);
            mesh.RecalculateBounds();

            if (persist)
            {
                if (isNew) AssetDatabase.CreateAsset(mesh, recipe.MeshPath);
                else EditorUtility.SetDirty(mesh);
            }

            int legTriangles = submeshes.Sum(s => s.Count) / 3;
            report.Append($"меш ног: вершин {count}, треугольников {legTriangles} (из {triangleCount} у донора), подсеток {submeshes.Count}. ");
            return mesh;
        }

        /// <summary>Кость донора → индекс кости скина (1…8) по ближайшей кости ноги рецепта вверх по иерархии; 0 — таз.</summary>
        private static Func<Transform, int> LegIndexOf(DonorRecipe recipe) => bone =>
        {
            for (Transform t = bone; t != null; t = t.parent)
            {
                int index = Array.IndexOf(recipe.LegBones, t.name);
                if (index >= 0) return index + 1;
            }
            return 0;
        };

        /// <summary>Переносит веса на кости скина: одноимённые кости сливаются, остаются четыре сильнейшие.</summary>
        private static BoneWeight Remap(BoneWeight w, int[] remap)
        {
            var sum = new Dictionary<int, float>();
            void Add(int index, float weight)
            {
                if (weight <= 0f) return;
                int target = remap[index];
                sum[target] = (sum.TryGetValue(target, out float s) ? s : 0f) + weight;
            }

            Add(w.boneIndex0, w.weight0);
            Add(w.boneIndex1, w.weight1);
            Add(w.boneIndex2, w.weight2);
            Add(w.boneIndex3, w.weight3);

            KeyValuePair<int, float>[] top = sum.OrderByDescending(p => p.Value).Take(4).ToArray();
            float total = top.Sum(p => p.Value);
            var result = new BoneWeight();
            if (top.Length > 0) { result.boneIndex0 = top[0].Key; result.weight0 = top[0].Value / total; }
            if (top.Length > 1) { result.boneIndex1 = top[1].Key; result.weight1 = top[1].Value / total; }
            if (top.Length > 2) { result.boneIndex2 = top[2].Key; result.weight2 = top[2].Value / total; }
            if (top.Length > 3) { result.boneIndex3 = top[3].Key; result.weight3 = top[3].Value / total; }
            return result;
        }

        private static Bounds LocalBounds(Mesh mesh, Transform geo, Transform rootBone)
        {
            Matrix4x4 toRoot = rootBone.worldToLocalMatrix * geo.localToWorldMatrix;
            Vector3[] v = mesh.vertices;
            var bounds = new Bounds(toRoot.MultiplyPoint3x4(v[0]), Vector3.zero);
            foreach (Vector3 p in v) bounds.Encapsulate(toRoot.MultiplyPoint3x4(p));
            bounds.Expand(0.1f); // запас на шаг и сгиб колена
            return bounds;
        }

        /// <summary>Копия материала донора в <see cref="OutFolder"/> (создаётся один раз, дальше правится как свой).</summary>
        private static Material EnsureMaterial(string sourcePath, string name)
        {
            string path = $"{OutFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetLeg(SerializedObject so, string path, Transform upper, Transform lower, Transform foot, Transform toes)
        {
            so.FindProperty(path + "._upperLeg").objectReferenceValue = upper;
            so.FindProperty(path + "._lowerLeg").objectReferenceValue = lower;
            so.FindProperty(path + "._foot").objectReferenceValue = foot;
            so.FindProperty(path + "._toes").objectReferenceValue = toes;
        }

        /// <summary>
        /// Humanoid-аватар из скелета киборга. В скелет идут только кости разметки и их предки до
        /// <c>Cyborg</c>: в остальной иерархии есть одноимённые объекты (<c>GrabProxy</c> карманов), а
        /// <c>AvatarBuilder</c> ищет кости по имени.
        /// </summary>
        private static Avatar BuildHumanAvatar(Transform model, out string error)
        {
            error = null;
            var human = new List<HumanBone>();
            var skeleton = new List<Transform> { model };
            for (int i = 0; i < HumanMap.GetLength(0); i++)
            {
                Transform bone = Find(model, HumanMap[i, 1]);
                if (bone == null)
                {
                    error = $"нет кости '{HumanMap[i, 1]}' для humanoid-разметки {HumanMap[i, 0]}";
                    return null;
                }

                human.Add(new HumanBone { humanName = HumanMap[i, 0], boneName = bone.name, limit = new HumanLimit { useDefaultValues = true } });
                for (Transform t = bone; t != null && t != model; t = t.parent)
                    if (!skeleton.Contains(t)) skeleton.Add(t);
            }

            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            Avatar built = AvatarBuilder.BuildHumanAvatar(model.gameObject, description);
            if (built == null || !built.isValid || !built.isHuman)
            {
                error = "AvatarBuilder не собрал humanoid-аватар киборга (см. консоль)";
                return null;
            }

            built.name = "CyborgHumanoid";
            var existing = AssetDatabase.LoadAssetAtPath<Avatar>(HumanAvatarPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(built, HumanAvatarPath);
                return built;
            }

            // Тот же ассет (GUID и ссылки на него сохраняются), новое содержимое.
            EditorUtility.CopySerialized(built, existing);
            existing.name = "CyborgHumanoid";
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(built);
            return existing;
        }

        /// <summary>
        /// Legs Animator и мост — копия настроек MEF, ссылки переназначены на свой риг
        /// (скилл <c>/setup-avatar</c>, <c>game-variant.md</c>, раздел 5).
        /// </summary>
        private static string SetupLegsAnimator(GameObject root, Transform model, Animator animator, Transform pelvis,
                                                Dictionary<string, Transform> bones, GameObject template, StringBuilder report)
        {
            LegsAnimator templateLegs = template.GetComponentInChildren<LegsAnimator>(true);
            LegsAnimatorUxrBridge templateBridge = template.GetComponentInChildren<LegsAnimatorUxrBridge>(true);
            if (templateLegs == null || templateBridge == null) return $"у {TemplatePath} нет Legs Animator и моста — не с чего копировать";

            var legs = model.GetComponent<LegsAnimator>();
            if (legs == null) legs = model.gameObject.AddComponent<LegsAnimator>();
            var bridge = model.GetComponent<LegsAnimatorUxrBridge>();
            if (bridge == null) bridge = model.gameObject.AddComponent<LegsAnimatorUxrBridge>();

            EditorUtility.CopySerialized(templateLegs, legs);
            EditorUtility.CopySerialized(templateBridge, bridge);

            var so = new SerializedObject(legs);
            so.FindProperty("Mecanim").objectReferenceValue = animator;
            so.FindProperty("Hips").objectReferenceValue = pelvis;
            so.FindProperty("baseTransform").objectReferenceValue = root.transform;
            so.FindProperty("Calibrate").intValue = 2; // FixedCalibrate: анимации нет
            SerializedProperty modules = so.FindProperty("CustomModules");
            for (int i = 0; i < modules.arraySize; i++)
                modules.GetArrayElementAtIndex(i).FindPropertyRelative("Parent").objectReferenceValue = legs;

            SerializedProperty legList = so.FindProperty("Legs");
            if (legList.arraySize != 2) return $"у Legs Animator MEF ног {legList.arraySize}, ожидается 2";
            string[] sides = { "Left", "Right" };
            for (int i = 0; i < 2; i++)
            {
                SerializedProperty leg = legList.GetArrayElementAtIndex(i);
                leg.FindPropertyRelative("Owner").objectReferenceValue = legs;
                leg.FindPropertyRelative("BoneStart").objectReferenceValue = bones["UpperLeg_" + sides[i]];
                leg.FindPropertyRelative("BoneMid").objectReferenceValue = bones["LowerLeg_" + sides[i]];
                leg.FindPropertyRelative("BoneEnd").objectReferenceValue = bones["Foot_" + sides[i]];
                leg.FindPropertyRelative("BoneFeet").objectReferenceValue = null;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // Высота лодыжки и оси стопы — в позе префаба от пола аватара (корень), не с MEF.
            legs.User_RefreshHelperVariablesOnParametersChange();
            foreach (LegsAnimator.Leg leg in legs.Legs) leg.RefreshLegAnkleToHeelAndFeetAndAxes(root.transform);

            var bridgeSo = new SerializedObject(bridge);
            bridgeSo.FindProperty("footHeightOffset").floatValue = 0f;
            bridgeSo.ApplyModifiedPropertiesWithoutUndo();

            legs.enabled = true;
            bridge.enabled = true;
            EditorUtility.SetDirty(legs);

            report.Append($"Legs Animator: AnkleToHeel {string.Join(" / ", legs.Legs.Select(l => l.AnkleToHeel.magnitude.ToString("F3")))}. ");
            return null;
        }

        private static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }
    }
}
