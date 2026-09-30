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
    /// Ноги киборга (<c>PlayerControllersCyborgAvatar</c>) — ноги робота Kyle
    /// (<c>ThirdParty/UnityStarter_Robot/KyleRobot</c>), пришитые к скелету киборга. Генерация, а не ручная
    /// настройка: всё, чем киборг с ногами отличается от сэмпла UltimateXR, задано здесь, и повторная сборка
    /// приводит префаб к этому виду (объекты ищутся по имени — id и ссылки на них сохраняются).
    ///
    /// <list type="number">
    /// <item><b>Кости.</b> Под <c>Pelvis</c> — цепочки <c>UpperLeg → LowerLeg → Foot → Toes</c> (<c>_Left</c>/<c>_Right</c>)
    ///       в позиции костей Kyle, пересчитанных на киборга: масштаб — отношение высоты головы киборга к
    ///       высоте головы Kyle (пропорции робота сохраняются), по горизонтали таз Kyle совмещён с тазом
    ///       киборга, по вертикали ступни остаются на полу. Повороты костей — осями корня аватара:
    ///       плагину ног и хитбоксам важны только позиции, а ровные оси стопы — это
    ///       <c>AnkleForward/Up</c> без поправок.</item>
    /// <item><b>Меш</b> <see cref="MeshPath"/> — треугольники Kyle, все вершины которых принадлежат ногам
    ///       (главная кость — <c>LeftLeg</c>… <c>RightToes</c>) или низу таза Kyle (<c>Hips</c>, не выше
    ///       <see cref="PelvisCutAboveHipJoints"/> над тазобедренными суставами: выше красная пластина живота
    ///       Kyle пробивает корпус киборга). Вершины запечены в пространство нового скина, веса таза и
    ///       позвоночника Kyle — на <c>Pelvis</c> киборга. Низ таза Kyle уходит под пояс киборга и закрывает пах.</item>
    /// <item><b>Скин</b> <c>CyborgGeo/LegsGeo</c> на материале <see cref="MaterialPath"/> (копия материала Kyle),
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
        public const string MeshPath = OutFolder + "/CyborgLegs_Mesh.asset";
        public const string MaterialPath = OutFolder + "/CyborgLegs.mat";
        public const string HumanAvatarPath = OutFolder + "/CyborgHumanoid.asset";

        private const string DonorPath = "Assets/ThirdParty/UnityStarter_Robot/KyleRobot/Models/KyleRobot.fbx";
        private const string DonorMaterialPath = "Assets/ThirdParty/UnityStarter_Robot/KyleRobot/Materials/KyleRobot.mat";
        private const string TemplatePath = "Assets/Prefabs/Player/MEF_Base_Avatar.prefab";

        private const string ModelName = "Cyborg";
        private const string RigName = "CyborgRig";
        private const string PelvisName = "Pelvis";
        private const string GeoName = "CyborgGeo";
        private const string BodyGeoName = "BodyGeo";
        private const string LegsGeoName = "LegsGeo";
        private const string DonorHips = "Hips";
        private const string DonorHead = "Head";

        /// <summary>Срез таза Kyle над его тазобедренными суставами, метры донора.</summary>
        private const float PelvisCutAboveHipJoints = 0.04f;

        /// <summary>Кость ноги: имя у Kyle → имя у киборга, родитель у киборга.</summary>
        private sealed class LegBone
        {
            public string Donor;
            public string Name;
            public string Parent;
        }

        private static readonly LegBone[] Bones =
        {
            new LegBone { Donor = "LeftLeg", Name = "UpperLeg_Left", Parent = PelvisName },
            new LegBone { Donor = "LeftCalf", Name = "LowerLeg_Left", Parent = "UpperLeg_Left" },
            new LegBone { Donor = "LeftFoot", Name = "Foot_Left", Parent = "LowerLeg_Left" },
            new LegBone { Donor = "LeftToes", Name = "Toes_Left", Parent = "Foot_Left" },
            new LegBone { Donor = "RightLeg", Name = "UpperLeg_Right", Parent = PelvisName },
            new LegBone { Donor = "RightCalf", Name = "LowerLeg_Right", Parent = "UpperLeg_Right" },
            new LegBone { Donor = "RightFoot", Name = "Foot_Right", Parent = "LowerLeg_Right" },
            new LegBone { Donor = "RightToes", Name = "Toes_Right", Parent = "Foot_Right" },
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

        [MenuItem("Tools/VR Battlegrounds/Avatars/Build Cyborg Legs")]
        private static void BuildMenu()
        {
            string report = Build();
            if (report.StartsWith("ОТКАЗ")) return;

            // Хитбоксы ног по новому скелету; сборщик хитбоксов пересобирает и призрака (вариант киборга), и трупы.
            HitboxBuilder.Build();
        }

        /// <summary>Собирает ноги киборга. Возвращает отчёт; при отказе префаб не меняется.</summary>
        public static string Build()
        {
            var donor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPath);
            var donorMaterial = AssetDatabase.LoadAssetAtPath<Material>(DonorMaterialPath);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
            if (donor == null || donorMaterial == null || template == null)
                return Fail($"нет {DonorPath}, {DonorMaterialPath} или {TemplatePath}");

            EnsureFolder(OutFolder);

            GameObject root = PrefabUtility.LoadPrefabContents(AvatarPath);
            try
            {
                var report = new StringBuilder();
                string error = Assemble(root, donor, donorMaterial, template, report);
                if (error != null) return Fail(error);

                PrefabUtility.SaveAsPrefabAsset(root, AvatarPath);
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

        private static string Assemble(GameObject root, GameObject donor, Material donorMaterial, GameObject template, StringBuilder report)
        {
            Transform model = Find(root.transform, ModelName);
            Transform rig = Find(root.transform, RigName);
            Transform pelvis = Find(root.transform, PelvisName);
            Transform geo = Find(root.transform, GeoName);
            Transform bodyGeo = geo != null ? geo.Find(BodyGeoName) : null;
            var avatar = root.GetComponent<UxrAvatar>();
            if (model == null || rig == null || pelvis == null || geo == null || bodyGeo == null || avatar == null)
                return $"в {AvatarPath} нет {ModelName}/{RigName}/{PelvisName}/{GeoName}/{BodyGeoName} или UxrAvatar";

            SkinnedMeshRenderer donorSkin = donor.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (donorSkin == null) return $"в {DonorPath} нет скина";

            // Мир донора в позе привязки: матрица кости = скин · bindpose⁻¹.
            Matrix4x4[] donorBind = donorSkin.sharedMesh.bindposes;
            var donorBoneWorld = new Dictionary<string, Matrix4x4>();
            for (int b = 0; b < donorSkin.bones.Length; b++)
                donorBoneWorld[donorSkin.bones[b].name] = donorSkin.transform.localToWorldMatrix * donorBind[b].inverse;

            if (!donorBoneWorld.ContainsKey(DonorHips) || !donorBoneWorld.ContainsKey(DonorHead) || Bones.Any(b => !donorBoneWorld.ContainsKey(b.Donor)))
                return "у Kyle нет нужных костей (Hips, Head, ноги)";

            Transform head = avatar.AvatarRig.Head.Head;
            if (head == null) return "у киборга не размечена голова в UxrAvatarRig";

            // Перенос Kyle → киборг в пространстве корня аватара: масштаб по высоте головы, таз совмещён по горизонтали.
            Vector3 donorHead = donorBoneWorld[DonorHead].GetColumn(3);
            Vector3 donorHips = donorBoneWorld[DonorHips].GetColumn(3);
            Vector3 cyborgHead = root.transform.InverseTransformPoint(head.position);
            Vector3 cyborgPelvis = root.transform.InverseTransformPoint(pelvis.position);
            float scale = cyborgHead.y / donorHead.y;
            var offset = new Vector3(cyborgPelvis.x - donorHips.x * scale, 0f, cyborgPelvis.z - donorHips.z * scale);
            Matrix4x4 toAvatar = root.transform.localToWorldMatrix * Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one * scale);
            report.Append($"масштаб Kyle {scale:F3}, сдвиг {offset.ToString("F3")}. ");

            // 1. Кости.
            var created = new Dictionary<string, Transform> { { PelvisName, pelvis } };
            foreach (LegBone bone in Bones)
            {
                Transform parent = created[bone.Parent];
                Transform t = parent.Find(bone.Name);
                if (t == null)
                {
                    t = new GameObject(bone.Name).transform;
                    t.SetParent(parent, false);
                }

                t.gameObject.layer = pelvis.gameObject.layer;
                t.SetPositionAndRotation(toAvatar.MultiplyPoint3x4(donorBoneWorld[bone.Donor].GetColumn(3)), root.transform.rotation);
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

            Transform[] skinBones = new[] { pelvis }.Concat(Bones.Select(b => created[b.Name])).ToArray();
            float donorHipJoint = donorBoneWorld[Bones[0].Donor].GetColumn(3).y;
            Mesh mesh = BuildMesh(donorSkin, donorHipJoint, toAvatar, legsGeo, skinBones, report);
            if (mesh == null) return "у Kyle не нашлось треугольников ног";

            var skin = legsGeo.GetComponent<SkinnedMeshRenderer>();
            if (skin == null) skin = legsGeo.gameObject.AddComponent<SkinnedMeshRenderer>();
            var body = bodyGeo.GetComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = skinBones;
            skin.rootBone = pelvis;
            skin.sharedMaterials = new[] { EnsureMaterial(donorMaterial) };
            skin.localBounds = LocalBounds(mesh, legsGeo, pelvis);
            skin.shadowCastingMode = body.shadowCastingMode;
            skin.receiveShadows = body.receiveShadows;
            skin.quality = body.quality;
            skin.updateWhenOffscreen = body.updateWhenOffscreen;
            skin.skinnedMotionVectors = body.skinnedMotionVectors;
            skin.lightProbeUsage = body.lightProbeUsage;
            skin.reflectionProbeUsage = body.reflectionProbeUsage;
            skin.probeAnchor = body.probeAnchor;

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

        private static Mesh BuildMesh(SkinnedMeshRenderer donor, float donorHipJointHeight, Matrix4x4 toAvatar, Transform legsGeo,
                                      Transform[] skinBones, StringBuilder report)
        {
            Mesh source = donor.sharedMesh;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            Vector2[] uv = source.uv;
            BoneWeight[] weights = source.boneWeights;
            int[] triangles = source.triangles;
            string[] donorNames = donor.bones.Select(b => b.name).ToArray();

            // Индекс кости донора → индекс кости скина; всё, что не нога, — на таз (индекс 0).
            int[] remap = donorNames.Select(n => Array.FindIndex(Bones, b => b.Donor == n) + 1).ToArray();
            // Ноги — целиком; таз Kyle — только ниже среза: выше он пробивает корпус киборга спереди
            // (красная пластина на животе). Оставшийся низ таза закрывает пах под поясом киборга.
            var legBones = new HashSet<string>(Bones.Select(b => b.Donor));
            Matrix4x4 donorToWorld = donor.transform.localToWorldMatrix;
            float cut = donorHipJointHeight + PelvisCutAboveHipJoints;
            bool[] keep = new bool[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                string bone = donorNames[weights[i].boneIndex0];
                keep[i] = legBones.Contains(bone) || (bone == DonorHips && donorToWorld.MultiplyPoint3x4(vertices[i]).y <= cut);
            }

            var map = new Dictionary<int, int>();
            var newTriangles = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (!keep[triangles[i]] || !keep[triangles[i + 1]] || !keep[triangles[i + 2]]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = triangles[i + k];
                    if (!map.TryGetValue(v, out int n)) map[v] = n = map.Count;
                    newTriangles.Add(n);
                }
            }
            if (newTriangles.Count == 0) return null;

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

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "CyborgLegs";
            mesh.indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = newVertices;
            if (normals.Length > 0) mesh.normals = newNormals;
            if (tangents.Length > 0) mesh.tangents = newTangents;
            if (uv.Length > 0) mesh.uv = newUv;
            mesh.boneWeights = newWeights;
            mesh.bindposes = skinBones.Select(b => b.worldToLocalMatrix * legsGeo.localToWorldMatrix).ToArray();
            mesh.SetTriangles(newTriangles, 0);
            mesh.RecalculateBounds();

            if (isNew) AssetDatabase.CreateAsset(mesh, MeshPath);
            else EditorUtility.SetDirty(mesh);

            report.Append($"меш ног: вершин {count}, треугольников {newTriangles.Count / 3} (из {triangles.Length / 3} у Kyle). ");
            return mesh;
        }

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

        private static Material EnsureMaterial(Material donorMaterial)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            material = new Material(donorMaterial) { name = "CyborgLegs" };
            AssetDatabase.CreateAsset(material, MaterialPath);
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
