using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UnityEngine;
using VrBattlegrounds.Editor.HandGeometry;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Исходная геометрия в физических метрах корпуса, без ссылок на временный клон.</summary>
    [Serializable]
    public sealed class GripSourceContactFrame
    {
        public const string CoordinateSpace = "body-rigid-meters";
        public string SourceKind;
        public HandAssetIdentity Model, Weapon, Clip, WeaponRestClip;
        public float TimeSeconds;
        public UxrHandSide SemanticSourceSide;
        public Matrix4x4 BodyFrameToWorld;
        public float BodySourceUniformScale;
        public GripSourceHandFrame[] Hands;
        public GripSourceSkinFrame[] Skins;
        public GripSourceObjectMesh[] ObjectMeshes;
        public string ObjectGeometryCoverage;
        public string ObjectGeometryCoverageDescription;
        public string[] UnavailableHands;
        // Снятый меш ещё не является проверенной областью контакта или целью IK.
        public bool ContactTargetsValidated;
    }

    [Serializable]
    public sealed class GripSourceBoneFrame
    {
        public string Path;
        public Vector3 PositionBody;
        public Quaternion RotationBody;
    }

    [Serializable]
    public sealed class GripSourceFingerFrame
    {
        public string Finger;
        public GripSourceBoneFrame Metacarpal, Proximal, Intermediate, Distal;
    }

    [Serializable]
    public sealed class GripSourceHandFrame
    {
        public UxrHandSide Side;
        // Другая рука исходного клипа сохраняет собственную роль, а не заменяет зеркальный хват.
        public string SemanticRole;
        public bool Inferred;
        public GripSourceBoneFrame Wrist;
        public Vector3 PalmCenterBody;
        public Quaternion UniversalRotationBody;
        public GripSourceFingerFrame[] Fingers;
        public GripSourceHandMeshBinding[] MeshBindings;
    }

    [Serializable]
    public sealed class GripSourceHandMeshBinding
    {
        public string MeshId;
        public int[] HandBoneIndices;
        public GripSourceFingerMeshBinding[] Fingers;
    }

    [Serializable]
    public sealed class GripSourceFingerMeshBinding
    {
        public string Finger;
        public int[] BoneIndices;
    }

    [Serializable]
    public sealed class GripSourceSkinFrame
    {
        public string Id;
        public bool RendererEnabled, ActiveInHierarchy;
        public string LodGroupPath;
        public int LodIndex = -1;
        // Bones очищается до возврата; пути и все веса сохраняются для материальной разметки.
        public HandMeshSample Mesh;
    }

    [Serializable]
    public sealed class GripSourceObjectMesh
    {
        public string Id, RendererPath;
        // Роль инвентаризации источника; не подтверждает участие в целевой сборке или пригодность контакта.
        public string SourceRendererRole;
        public bool RendererEnabled, ActiveInHierarchy;
        public string LodGroupPath;
        public int LodIndex = -1;
        public HandAssetIdentity Asset;
        public Vector3[] VerticesBody, NormalsBody;
        public int[][] SubmeshTriangles;
    }

    /// <summary>Живой кадр доступен только в синхронном callback экстрактора; сохранять следует Capture().</summary>
    public sealed class GripSourceCaptureContext
    {
        internal GameObject CharacterRoot, ObjectRoot, ModelAsset, WeaponAsset;
        internal AnimationClip Clip, RestClip;
        internal GripSourceLiveHand[] LiveHands;
        internal Renderer[] ObjectRenderers;
        internal string[] UnavailableHands;
        internal Matrix4x4 BodyToWorld;
        internal string SourceKind;
        internal float Time;
        internal UxrHandSide SourceSide;

        public GripSourceContactFrame Capture() => GripSourceContactCapture.Capture(this);

        internal void Invalidate()
        {
            CharacterRoot = ObjectRoot = ModelAsset = WeaponAsset = null;
            Clip = RestClip = null;
            LiveHands = null;
            ObjectRenderers = null;
        }
    }

    internal sealed class GripSourceLiveHand
    {
        public UxrHandSide Side;
        public UxrAvatarHand Hand;
        public UxrUniversalLocalAxes Axes;
    }

    /// <summary>Снимает штатный skinning и прямые кадры обеих рук, не назначая область контакта по расстоянию.</summary>
    public static class GripSourceContactCapture
    {
        private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "little" };

        internal static void Deliver(Action<GripSourceCaptureContext> callback, GripSourceCaptureContext context)
        {
            try { callback(context); }
            finally { context.Invalidate(); }
        }

        internal static GripSourceLiveHand[] PrepareHands(Func<UxrHandSide, UxrAvatarArm> buildArm, out string[] unavailable)
        {
            var result = new List<GripSourceLiveHand>();
            var issues = new List<string>();
            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                try
                {
                    UxrAvatarHand hand = buildArm(side).Hand;
                    // Descriptor пака опускает пястные; геометрическая разметка должна знать и их веса.
                    foreach (UxrAvatarFinger finger in Fingers(hand))
                    {
                        Transform parent = finger.Proximal.parent;
                        if (!finger.Metacarpal && parent && parent != hand.Wrist && parent.parent == hand.Wrist)
                            finger.Metacarpal = parent;
                    }
                    HandsPackPoseExtractor.SolveAxes(hand, side, out UxrUniversalLocalAxes axes, out _);
                    result.Add(new GripSourceLiveHand { Side = side, Hand = hand, Axes = axes });
                }
                catch (KeyNotFoundException error)
                {
                    issues.Add(side + ": " + error.Message);
                }
            }
            unavailable = issues.ToArray();
            return result.ToArray();
        }

        public static GripSourceContactFrame Capture(GripSourceCaptureContext context)
        {
            if (context == null || !context.CharacterRoot || !context.ObjectRoot)
                throw new ArgumentException("Исходный клон уже уничтожен или кадр отсутствует.");
            SkinnedMeshRenderer[] handRenderers = context.CharacterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.sharedMesh && context.LiveHands.Any(h => HasHandBones(renderer, h.Hand))).ToArray();
            Renderer[] objectRenderers = context.ObjectRenderers.Where(r => r).Distinct().ToArray();
            RequireUniformPositiveConsumedTransforms(context, handRenderers, objectRenderers);
            RequireUniformPositive(context.BodyToWorld, "корпус источника");

            Matrix4x4 bodyFrame = Matrix4x4.TRS(context.BodyToWorld.GetColumn(3), context.BodyToWorld.rotation, Vector3.one);
            Matrix4x4 worldToBody = bodyFrame.inverse;
            Quaternion rotationToBody = Quaternion.Inverse(bodyFrame.rotation);
            var hands = context.LiveHands.Select(live => CaptureHand(live, context, worldToBody, rotationToBody)).ToArray();
            var skins = new List<GripSourceSkinFrame>();
            var bindings = hands.Select(_ => new List<GripSourceHandMeshBinding>()).ToArray();
            foreach (SkinnedMeshRenderer renderer in handRenderers)
            {
                // Общий capture владеет BakeMesh и компенсацией scale; здесь skinning не дублируется.
                HandMeshSample mesh = HandMeshCapture.Capture(renderer, worldToBody, context.CharacterRoot.transform);
                string id = MeshId("hand", mesh.RendererPath, mesh.Asset);
                for (int handIndex = 0; handIndex < hands.Length; handIndex++)
                {
                    UxrAvatarHand hand = context.LiveHands[handIndex].Hand;
                    HashSet<Transform> handBones = HandBones(hand);
                    int[] indices = Enumerable.Range(0, mesh.Bones.Length).Where(i => handBones.Contains(mesh.Bones[i])).ToArray();
                    if (indices.Length == 0) continue;
                    UxrAvatarFinger[] fingers = Fingers(hand);
                    HashSet<Transform>[] fingerBones = fingers.Select(FingerBones).ToArray();
                    bindings[handIndex].Add(new GripSourceHandMeshBinding
                    {
                        MeshId = id,
                        HandBoneIndices = indices,
                        Fingers = fingers.Select((finger, i) => new GripSourceFingerMeshBinding
                        {
                            Finger = FingerNames[i],
                            BoneIndices = Enumerable.Range(0, mesh.Bones.Length).Where(b => fingerBones[i].Contains(mesh.Bones[b])).ToArray()
                        }).ToArray()
                    });
                }
                mesh.Bones = Array.Empty<Transform>();
                var captured = new GripSourceSkinFrame
                {
                    Id = id, Mesh = mesh, RendererEnabled = renderer.enabled, ActiveInHierarchy = renderer.gameObject.activeInHierarchy
                };
                LODGroup lod = renderer.GetComponentInParent<LODGroup>();
                if (lod)
                {
                    captured.LodGroupPath = HandMeshSample.PathOf(lod.transform, context.CharacterRoot.transform);
                    captured.LodIndex = Array.FindIndex(lod.GetLODs(), level => level.renderers.Contains(renderer));
                }
                skins.Add(captured);
            }
            for (int i = 0; i < hands.Length; i++) hands[i].MeshBindings = bindings[i].ToArray();
            if (!hands.Any(h => h.Side == context.SourceSide && h.MeshBindings.Length > 0))
                throw new InvalidOperationException("Source contact capture: у руки рецепта нет исходной геометрии кисти.");

            return new GripSourceContactFrame
            {
                SourceKind = context.SourceKind,
                Model = HandAssetIdentity.Capture(context.ModelAsset),
                Weapon = context.WeaponAsset ? HandAssetIdentity.Capture(context.WeaponAsset) : null,
                Clip = HandAssetIdentity.Capture(context.Clip),
                WeaponRestClip = context.RestClip ? HandAssetIdentity.Capture(context.RestClip) : null,
                TimeSeconds = context.Time,
                SemanticSourceSide = context.SourceSide,
                BodyFrameToWorld = bodyFrame,
                BodySourceUniformScale = ((Vector3)context.BodyToWorld.GetColumn(0)).magnitude,
                Hands = hands,
                Skins = skins.ToArray(),
                ObjectMeshes = objectRenderers.Select(r => CaptureObject(r, context.ObjectRoot.transform, worldToBody,
                    context.SourceKind == PackClipGripSource.Hands ? "recipe_body_renderer" : "pack_weapon_inventory_renderer")).ToArray(),
                ObjectGeometryCoverage = context.SourceKind == PackClipGripSource.Hands ? "recipe_body_renderer" : "pack_weapon_hierarchy",
                ObjectGeometryCoverageDescription = context.SourceKind == PackClipGripSource.Hands
                    ? "Только renderer корпуса рецепта; остальные детали исходного предмета не сняты. Это подмножество, а не вся исходная или целевая сборка."
                    : "Полная инвентаризация mesh-renderer иерархии оружия пака, включая disabled/inactive и все LOD; выбор участвующих поверхностей остаётся вызывающему коду. Это не состав целевой сборки.",
                UnavailableHands = context.UnavailableHands,
                ContactTargetsValidated = false
            };
        }

        private static GripSourceHandFrame CaptureHand(GripSourceLiveHand live, GripSourceCaptureContext context,
                                                       Matrix4x4 worldToBody, Quaternion rotationToBody)
        {
            return new GripSourceHandFrame
            {
                Side = live.Side,
                SemanticRole = live.Side == context.SourceSide ? "recipe_hand" : "other_clip_hand",
                Inferred = false,
                Wrist = Bone(live.Hand.Wrist, context.CharacterRoot.transform, worldToBody, rotationToBody),
                PalmCenterBody = worldToBody.MultiplyPoint3x4(HandsPackPoseExtractor.PalmCenterWorld(live.Hand)),
                UniversalRotationBody = rotationToBody * live.Axes.UniversalRotation,
                Fingers = Fingers(live.Hand).Select((finger, i) => new GripSourceFingerFrame
                {
                    Finger = FingerNames[i],
                    Metacarpal = Bone(finger.Metacarpal, context.CharacterRoot.transform, worldToBody, rotationToBody),
                    Proximal = Bone(finger.Proximal, context.CharacterRoot.transform, worldToBody, rotationToBody),
                    Intermediate = Bone(finger.Intermediate, context.CharacterRoot.transform, worldToBody, rotationToBody),
                    Distal = Bone(finger.Distal, context.CharacterRoot.transform, worldToBody, rotationToBody)
                }).ToArray()
            };
        }

        private static GripSourceBoneFrame Bone(Transform bone, Transform root, Matrix4x4 worldToBody, Quaternion rotationToBody) =>
            !bone ? null : new GripSourceBoneFrame
            {
                Path = HandMeshSample.PathOf(bone, root),
                PositionBody = worldToBody.MultiplyPoint3x4(bone.position),
                RotationBody = rotationToBody * bone.rotation
            };

        private static GripSourceObjectMesh CaptureObject(Renderer renderer, Transform root, Matrix4x4 worldToBody, string sourceRendererRole)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                HandMeshSample mesh = HandMeshCapture.Capture(skin, worldToBody, root);
                return WithSourceRendererMetadata(new GripSourceObjectMesh
                {
                    Id = MeshId("object", mesh.RendererPath, mesh.Asset), RendererPath = mesh.RendererPath, Asset = mesh.Asset,
                    VerticesBody = mesh.Vertices, NormalsBody = mesh.Normals, SubmeshTriangles = mesh.SubmeshTriangles
                }, renderer, root, sourceRendererRole);
            }
            if (!(renderer is MeshRenderer) || !renderer.TryGetComponent(out MeshFilter filter) || !filter || !filter.sharedMesh)
                throw new InvalidOperationException("У renderer предмета нет доступного меша: " + renderer.name);
            Mesh source = filter.sharedMesh;
            Matrix4x4 matrix = worldToBody * renderer.localToWorldMatrix;
            Matrix4x4 normals = matrix.inverse.transpose;
            string path = HandMeshSample.PathOf(renderer.transform, root);
            HandAssetIdentity identity = HandAssetIdentity.Capture(source);
            return WithSourceRendererMetadata(new GripSourceObjectMesh
            {
                Id = MeshId("object", path, identity), RendererPath = path, Asset = identity,
                VerticesBody = source.vertices.Select(matrix.MultiplyPoint3x4).ToArray(),
                NormalsBody = source.normals.Select(n => normals.MultiplyVector(n).normalized).ToArray(),
                SubmeshTriangles = Enumerable.Range(0, source.subMeshCount)
                    .Select(i => source.GetTopology(i) == MeshTopology.Triangles ? source.GetTriangles(i) : Array.Empty<int>()).ToArray()
            }, renderer, root, sourceRendererRole);
        }

        private static GripSourceObjectMesh WithSourceRendererMetadata(GripSourceObjectMesh mesh, Renderer renderer, Transform root, string role)
        {
            // Читаем исходное состояние, не переключая renderer, GameObject или LODGroup.
            mesh.SourceRendererRole = role;
            mesh.RendererEnabled = renderer.enabled;
            mesh.ActiveInHierarchy = renderer.gameObject.activeInHierarchy;
            LODGroup lod = renderer.GetComponentInParent<LODGroup>();
            if (lod)
            {
                mesh.LodGroupPath = HandMeshSample.PathOf(lod.transform, root);
                mesh.LodIndex = Array.FindIndex(lod.GetLODs(), level => level.renderers.Contains(renderer));
            }
            return mesh;
        }

        private static string MeshId(string kind, string path, HandAssetIdentity identity) =>
            kind + "/" + path + "|" + identity.Guid + ":" + identity.LocalFileId;

        private static UxrAvatarFinger[] Fingers(UxrAvatarHand hand) =>
            new[] { hand.Thumb, hand.Index, hand.Middle, hand.Ring, hand.Little };

        private static HashSet<Transform> FingerBones(UxrAvatarFinger finger)
        {
            var result = new HashSet<Transform>(finger.Transforms);
            if (finger.Distal)
                foreach (Transform child in finger.Distal.GetComponentsInChildren<Transform>(true)) result.Add(child);
            return result;
        }

        private static HashSet<Transform> HandBones(UxrAvatarHand hand)
        {
            var result = new HashSet<Transform> { hand.Wrist };
            foreach (UxrAvatarFinger finger in Fingers(hand)) result.UnionWith(FingerBones(finger));
            return result;
        }

        private static bool HasHandBones(SkinnedMeshRenderer skin, UxrAvatarHand hand)
        {
            HashSet<Transform> bones = HandBones(hand);
            return skin.bones.Any(bones.Contains);
        }

        // ParticleSystemRenderer и прочие procedural renderers не являются исходной mesh-геометрией.
        // Проверка Unity bool обязательна: ?. не распознаёт fake-null отсутствующего компонента в Editor.
        internal static bool HasCaptureMesh(Renderer renderer)
        {
            if (!renderer) return false;
            if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
            return renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter && filter.sharedMesh;
        }

        private static void RequireUniformPositiveConsumedTransforms(GripSourceCaptureContext context,
            IEnumerable<SkinnedMeshRenderer> handRenderers, IEnumerable<Renderer> objectRenderers)
        {
            var consumed = new HashSet<Transform>();
            void AddChain(Transform node)
            {
                // Проверяем и внешних предков клонов: их scale также участвует в world-матрицах.
                for (; node; node = node.parent) consumed.Add(node);
            }
            void AddSkin(SkinnedMeshRenderer skin)
            {
                AddChain(skin.transform);
                AddChain(skin.rootBone);
                // Сохраняется весь skinning, поэтому нужны все кости, а не только кости выбранной кисти.
                foreach (Transform bone in skin.bones) AddChain(bone);
            }
            AddChain(context.CharacterRoot.transform);
            AddChain(context.ObjectRoot.transform);
            foreach (GripSourceLiveHand live in context.LiveHands)
                foreach (Transform bone in HandBones(live.Hand)) AddChain(bone);
            foreach (SkinnedMeshRenderer skin in handRenderers) AddSkin(skin);
            foreach (Renderer renderer in objectRenderers)
            {
                AddChain(renderer.transform);
                if (renderer is SkinnedMeshRenderer skin) AddSkin(skin);
            }
            // Посторонние sibling-узлы не читаются Capture и не влияют на участвующие матрицы.
            foreach (Transform node in consumed.OrderBy(t => HandMeshSample.PathOf(t, context.CharacterRoot.transform), StringComparer.Ordinal))
            {
                string path = HandMeshSample.PathOf(node, context.CharacterRoot.transform);
                if (string.IsNullOrEmpty(path)) path = context.CharacterRoot.name;
                Vector3 scale = node.localScale;
                if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0)
                    throw new NotSupportedException("Source contact capture: отрицательный/нулевой scale: " + path);
                RequireUniformPositive(node.localToWorldMatrix, path);
            }
        }

        private static void RequireUniformPositive(Matrix4x4 matrix, string label)
        {
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            Vector3 position = matrix.GetColumn(3);
            float size = Mathf.Max(x.magnitude, Mathf.Max(y.magnitude, z.magnitude));
            float tolerance = Mathf.Max(1e-8f, size * 1e-4f);
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(position) || !Finite(size) || size <= 0 || Mathf.Abs(x.magnitude - y.magnitude) > tolerance ||
                Mathf.Abs(x.magnitude - z.magnitude) > tolerance || Vector3.Dot(Vector3.Cross(x, y), z) <= 0 ||
                Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > 1e-4f ||
                Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > 1e-4f ||
                Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > 1e-4f)
                throw new NotSupportedException("Source contact capture: нужен положительный равномерный scale без shear: " + label);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
