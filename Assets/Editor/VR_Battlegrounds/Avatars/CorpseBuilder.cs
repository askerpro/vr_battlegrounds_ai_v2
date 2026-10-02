using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Генерирует трупы (<see cref="Corpse"/>, T-35) для каждого аватара, которого можно выбрать в
    /// команде, и привязывает их к аватарам (<see cref="CorpseSource"/>). Префабы трупов не правятся
    /// руками: всё задано здесь, повторная сборка перезаписывает их.
    ///
    /// <list type="bullet">
    /// <item><b>Модель</b> — поддерево гуманоидного <c>Animator</c> аватара (не интеграции рук): те же
    ///       кости и меши, остальные компоненты сняты. Иерархия совпадает с аватаром — поза копируется
    ///       по путям.</item>
    /// <item><b>Рэгдолл</b> — 11 тел на гуманоидных костях (таз, грудь, голова, плечи, предплечья,
    ///       бёдра, голени): капсулы вдоль кости, сфера головы, <c>CharacterJoint</c> к родителю, масса
    ///       ~70 кг. В префабе кинематический — отпускает его <see cref="Corpse.Launch"/>.</item>
    /// <item><b>Слой</b> <see cref="CorpsePhysics.LayerName"/> — добавляется в TagManager, если его нет.</item>
    /// </list>
    /// Проверка — <c>CorpseTests</c>.
    /// </summary>
    public static class CorpseBuilder
    {
        public const string OutFolder = "Assets/Prefabs/Player/Corpses";

        private sealed class Part
        {
            public HumanBodyBones Bone;
            public HumanBodyBones Toward;
            public HumanBodyBones Parent;
            public float Radius;
            public float Mass;
            public float Extend;
            public Vector3 TwistLimits; // low, high, swing
        }

        private static readonly Part[] Parts =
        {
            new Part { Bone = HumanBodyBones.Hips, Toward = HumanBodyBones.Spine, Parent = HumanBodyBones.LastBone, Radius = 0.13f, Mass = 12f },
            new Part { Bone = HumanBodyBones.Chest, Toward = HumanBodyBones.Head, Parent = HumanBodyBones.Hips, Radius = 0.14f, Mass = 12f, TwistLimits = new Vector3(-15, 15, 15) },
            new Part { Bone = HumanBodyBones.Head, Toward = HumanBodyBones.LastBone, Parent = HumanBodyBones.Chest, Radius = 0.11f, Mass = 5f, TwistLimits = new Vector3(-40, 25, 30) },
            new Part { Bone = HumanBodyBones.LeftUpperArm, Toward = HumanBodyBones.LeftLowerArm, Parent = HumanBodyBones.Chest, Radius = 0.05f, Mass = 2.5f, TwistLimits = new Vector3(-70, 10, 60) },
            new Part { Bone = HumanBodyBones.RightUpperArm, Toward = HumanBodyBones.RightLowerArm, Parent = HumanBodyBones.Chest, Radius = 0.05f, Mass = 2.5f, TwistLimits = new Vector3(-70, 10, 60) },
            new Part { Bone = HumanBodyBones.LeftLowerArm, Toward = HumanBodyBones.LeftHand, Parent = HumanBodyBones.LeftUpperArm, Radius = 0.045f, Mass = 2f, Extend = 0.1f, TwistLimits = new Vector3(-90, 0, 10) },
            new Part { Bone = HumanBodyBones.RightLowerArm, Toward = HumanBodyBones.RightHand, Parent = HumanBodyBones.RightUpperArm, Radius = 0.045f, Mass = 2f, Extend = 0.1f, TwistLimits = new Vector3(-90, 0, 10) },
            new Part { Bone = HumanBodyBones.LeftUpperLeg, Toward = HumanBodyBones.LeftLowerLeg, Parent = HumanBodyBones.Hips, Radius = 0.08f, Mass = 7.5f, TwistLimits = new Vector3(-20, 70, 30) },
            new Part { Bone = HumanBodyBones.RightUpperLeg, Toward = HumanBodyBones.RightLowerLeg, Parent = HumanBodyBones.Hips, Radius = 0.08f, Mass = 7.5f, TwistLimits = new Vector3(-20, 70, 30) },
            new Part { Bone = HumanBodyBones.LeftLowerLeg, Toward = HumanBodyBones.LeftFoot, Parent = HumanBodyBones.LeftUpperLeg, Radius = 0.06f, Mass = 5f, Extend = 0.08f, TwistLimits = new Vector3(-80, 0, 10) },
            new Part { Bone = HumanBodyBones.RightLowerLeg, Toward = HumanBodyBones.RightFoot, Parent = HumanBodyBones.RightUpperLeg, Radius = 0.06f, Mass = 5f, Extend = 0.08f, TwistLimits = new Vector3(-80, 0, 10) },
        };

        /// <summary>Компоненты модели, которые остаются в трупе.</summary>
        private static readonly System.Type[] Kept =
        {
            typeof(Transform), typeof(SkinnedMeshRenderer), typeof(MeshRenderer), typeof(MeshFilter), typeof(LODGroup),
        };

        /// <summary>Аватары, которых можно выбрать в команде, — им нужен труп.</summary>
        public static List<GameObject> TeamAvatars() =>
            AssetDatabase.FindAssets("t:TeamData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<TeamData>)
                .Where(t => t != null && t.avatars != null)
                .SelectMany(t => t.avatars)
                .Where(a => a != null && a.prefab != null)
                .Select(a => a.prefab)
                .Distinct()
                .ToList();

        /// <summary>Модель аватара — гуманоидный Animator вне интеграции рук.</summary>
        public static Animator ModelOf(GameObject avatar) =>
            avatar.GetComponentsInChildren<Animator>(true)
                  .FirstOrDefault(a => a.avatar != null && a.avatar.isHuman && a.GetComponentInParent<UxrHandIntegration>(true) == null);

        [MenuItem("Tools/VR Battlegrounds/Avatars/Build Corpses")]
        public static void Build()
        {
            HitboxBuilder.EnsureLayer(CorpsePhysics.LayerName);
            CorpsePhysics.ConfigureLayerCollisions();
            if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/Prefabs/Player", "Corpses");

            // Базы раньше вариантов: вариант наследует CorpseSource и переопределяет только ссылки.
            foreach (GameObject avatar in TeamAvatars().OrderBy(VariantDepth))
            {
                Corpse corpse = BuildCorpse(avatar);
                if (corpse != null) Attach(avatar, corpse);
            }

            AssetDatabase.SaveAssets();
        }

        private static int VariantDepth(GameObject prefab)
        {
            int depth = 0;
            for (GameObject p = PrefabUtility.GetCorrespondingObjectFromSource(prefab); p != null; p = PrefabUtility.GetCorrespondingObjectFromSource(p))
                depth++;
            return depth;
        }

        private static Corpse BuildCorpse(GameObject avatar)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(avatar);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            try
            {
                Animator animator = ModelOf(instance);
                if (animator == null)
                {
                    GameLog.Error($"[CorpseBuilder] {avatar.name}: нет гуманоидной модели — труп не собрать.");
                    return null;
                }

                var bones = new Dictionary<HumanBodyBones, Transform>();
                foreach (HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (b == HumanBodyBones.LastBone) continue;
                    Transform t = animator.GetBoneTransform(b);
                    if (t != null) bones[b] = t;
                }
                // Грудь у некоторых ригов не размечена — берём позвоночник.
                if (!bones.ContainsKey(HumanBodyBones.Chest) && bones.ContainsKey(HumanBodyBones.Spine))
                    bones[HumanBodyBones.Chest] = bones[HumanBodyBones.Spine];

                Transform model = animator.transform;
                model.SetParent(null, true);
                model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.localScale = Vector3.one;
                model.name = $"{avatar.name}_Corpse";

                Strip(model.gameObject);

                int layer = CorpsePhysics.Layer;
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;

                var bodies = new Dictionary<HumanBodyBones, Rigidbody>();
                foreach (Part part in Parts)
                {
                    if (!bones.TryGetValue(part.Bone, out Transform bone)) continue;
                    bodies[part.Bone] = AddBody(bone, part, bones, bodies);
                }

                Corpse corpse = model.gameObject.AddComponent<Corpse>();
                var so = new SerializedObject(corpse);
                SetArray(so.FindProperty("_bodies"), Parts.Where(p => bodies.ContainsKey(p.Bone)).Select(p => (Object)bodies[p.Bone]).ToList());
                // Только скелет: аксессуары (часы T-46 с узлами табло) едут за своей костью, а в списке позы
                // ломали труп при каждой их правке — путь пропадал из модели аватара.
                HashSet<Transform> skeleton = SkeletonOf(model);
                List<Transform> nodes = model.GetComponentsInChildren<Transform>(true).Where(skeleton.Contains).ToList();
                SetArray(so.FindProperty("_nodes"), nodes.Cast<Object>().ToList());
                SerializedProperty paths = so.FindProperty("_nodePaths");
                paths.arraySize = nodes.Count;
                for (int i = 0; i < nodes.Count; i++)
                    paths.GetArrayElementAtIndex(i).stringValue = AnimationUtility.CalculateTransformPath(nodes[i], model);
                so.ApplyModifiedPropertiesWithoutUndo();

                string path = $"{OutFolder}/{model.name}.prefab";
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(model.gameObject, path);
                Object.DestroyImmediate(model.gameObject);
                GameLog.Player.Info($"[CorpseBuilder] Труп собран: {path} ({bodies.Count} тел).");
                return saved.GetComponent<Corpse>();
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Кости скин-мешей модели и их предки до корня (корень не входит) — то, что двигает тело.</summary>
        private static HashSet<Transform> SkeletonOf(Transform model)
        {
            var set = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Transform bone in skin.bones.Append(skin.rootBone))
                    for (Transform t = bone; t != null && t != model && t.IsChildOf(model); t = t.parent)
                        set.Add(t);
            }
            return set;
        }

        /// <summary>Снимает всё, кроме костей и мешей. Несколько проходов — из-за RequireComponent.</summary>
        private static void Strip(GameObject root)
        {
            for (int pass = 0; pass < 5; pass++)
            {
                bool removed = false;
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    // RectTransform (табло часов) — тоже Transform: снять его нельзя, Unity ругается ошибкой.
                    if (c == null || c is Transform || Kept.Contains(c.GetType())) continue;
                    if (!CanRemove(c)) continue;
                    Object.DestroyImmediate(c);
                    removed = true;
                }
                if (!removed) break;
            }
        }

        private static bool CanRemove(Component component)
        {
            foreach (Component other in component.GetComponents<Component>())
            {
                if (other == null || other == component) continue;
                foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    if (req.m_Type0 == component.GetType() || req.m_Type1 == component.GetType() || req.m_Type2 == component.GetType())
                        return false;
                }
            }
            return true;
        }

        private static Rigidbody AddBody(Transform bone, Part part, Dictionary<HumanBodyBones, Transform> bones,
                                         Dictionary<HumanBodyBones, Rigidbody> bodies)
        {
            float scale = Mathf.Max(1e-4f, bone.lossyScale.x);
            float radius = part.Radius / scale;

            Vector3 twist = Vector3.up;
            if (part.Toward != HumanBodyBones.LastBone && bones.TryGetValue(part.Toward, out Transform toward))
            {
                Vector3 local = bone.InverseTransformPoint(toward.position);
                local *= 1f + part.Extend / Mathf.Max(1e-4f, local.magnitude * scale);
                int axis = MaxAxis(local);
                var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                capsule.direction = axis;
                capsule.radius = radius;
                capsule.height = Mathf.Abs(local[axis]) + 2f * radius;
                capsule.center = local * 0.5f;
                twist = Vector3.zero;
                twist[axis] = Mathf.Sign(local[axis]);
            }
            else
            {
                // Голова: сфера над основанием черепа.
                var sphere = bone.gameObject.AddComponent<SphereCollider>();
                sphere.radius = radius;
                sphere.center = bone.InverseTransformDirection(Vector3.up) * (0.08f / scale);
                int axis = MaxAxis(bone.InverseTransformDirection(Vector3.up));
                twist[axis] = 1f;
            }

            var body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = part.Mass;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.3f;

            if (part.Parent != HumanBodyBones.LastBone && bodies.TryGetValue(part.Parent, out Rigidbody parent))
            {
                var joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                joint.axis = twist;
                joint.swingAxis = Perpendicular(twist);
                joint.lowTwistLimit = new SoftJointLimit { limit = part.TwistLimits.x };
                joint.highTwistLimit = new SoftJointLimit { limit = part.TwistLimits.y };
                joint.swing1Limit = new SoftJointLimit { limit = part.TwistLimits.z };
                joint.swing2Limit = new SoftJointLimit { limit = part.TwistLimits.z };
                joint.enableProjection = true;
            }

            return body;
        }

        private static int MaxAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
        }

        private static Vector3 Perpendicular(Vector3 axis) =>
            Mathf.Abs(axis.x) > 0.5f ? Vector3.up : Vector3.right;

        private static void SetArray(SerializedProperty property, List<Object> values)
        {
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>
        /// Ставит <see cref="CorpseSource"/> на корень аватара и заполняет ссылки. Правится сам
        /// ассет (<c>SavePrefabAsset</c>), а не его пересохранённая копия: <c>SaveAsPrefabAsset</c>
        /// обнулял бы переопределения <c>UxrAvatar._rigInfo</c> (setup-avatar, game-variant.md).
        /// </summary>
        private static void Attach(GameObject avatar, Corpse corpse)
        {
            Animator model = ModelOf(avatar);
            CorpseSource source = avatar.GetComponent<CorpseSource>();
            if (source == null) source = avatar.AddComponent<CorpseSource>();

            var so = new SerializedObject(source);
            so.FindProperty("_corpse").objectReferenceValue = corpse;
            so.FindProperty("_modelRoot").objectReferenceValue = model != null ? model.transform : null;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(avatar);
        }
    }
}
