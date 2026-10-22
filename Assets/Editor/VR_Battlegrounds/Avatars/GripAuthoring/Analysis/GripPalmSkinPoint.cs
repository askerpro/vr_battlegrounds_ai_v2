using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    /// <summary>
    /// Точка ладони P1 на видимой поверхности: луч из центра ладони SDK вдоль GetPalmOutDirection пересекается
    /// с ладонными треугольниками всех SMR аватара, скинованных на кости этой руки (кожа, перчатка поверх неё),
    /// после BakeMesh; берётся внешнее пересечение (максимальное t). Смещение «центр SDK → точка» и нормаль
    /// считаются один раз и хранятся в рамке кисти <see cref="GripHandFrame"/> (как ручная разметка);
    /// ладонь жёсткая относительно запястья.
    /// </summary>
    internal sealed class GripPalmSkinPoint
    {
        // Дальше этой длины луча поверхность ладони не ищем (м)
        const float MaxRayLength = 0.06f;

        /// <summary>Попадания луча в один рендерер (слой).</summary>
        internal sealed class Layer
        {
            public string Name;
            public int PalmTriangles;
            public readonly List<float> HitsMm = new List<float>();
        }

        bool _resolved;
        int _avatarId;
        UxrHandSide _side;
        ulong _renderersKey;

        public bool HasSkin { get; private set; }
        public bool HasNormal { get; private set; }
        public string RendererName { get; private set; } = "";
        public string FailReason { get; private set; } = "";
        /// <summary>Ладонная фильтрация не дала треугольников — луч проверялся по всем треугольникам рендереров руки.</summary>
        public bool UsedAllTriangles { get; private set; }
        /// <summary>t выбранного (внешнего) попадания, мм.</summary>
        public float SelectedTMm { get; private set; }
        public readonly List<Layer> Layers = new List<Layer>();
        /// <summary>Смещение «центр ладони SDK → точка на поверхности» в рамке кисти <see cref="GripHandFrame"/>.</summary>
        public Vector3 OffsetHand { get; private set; }
        /// <summary>Нормаль поверхности в точке попадания в рамке кисти <see cref="GripHandFrame"/>.</summary>
        public Vector3 NormalHand { get; private set; }

        public void Invalidate()
        {
            _resolved = false;
        }

        /// <summary>Пересчитывает точку при смене аватара, руки или набора SMR; иначе оставляет сохранённую.</summary>
        public void Ensure(UxrAvatar avatar, UxrHandSide side, UxrAvatarHand hand)
        {
            SkinnedMeshRenderer[] skins = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var key = new GripHash();
            foreach (SkinnedMeshRenderer skin in skins)
            {
                key.Add(skin.GetHashCode());
                key.Add(skin.sharedMesh != null ? skin.sharedMesh.GetHashCode() : 0);
                key.Add(IsVisible(skin) ? 1 : 0);
            }

            int avatarId = avatar.GetHashCode();
            if (_resolved && _avatarId == avatarId && _side == side && _renderersKey == key.Value)
            {
                return;
            }

            _resolved = true;
            _avatarId = avatarId;
            _side = side;
            _renderersKey = key.Value;
            Resolve(skins, avatar, side, hand);
        }

        static bool IsVisible(SkinnedMeshRenderer skin) => skin != null && skin.enabled && skin.gameObject.activeInHierarchy;

        /// <summary>
        /// BakeMesh всех видимых SMR аватара, скинованных на кости этой руки (кожа и перчатка),
        /// с признаком ладонного треугольника (доминируют запястье/пястные).
        /// </summary>
        public static List<BakedSkin> BakeHandSkins(SkinnedMeshRenderer[] skins, UxrAvatarHand hand)
        {
            // Кости руки (для отбора рендереров) и ладонные кости: запястье и пястные
            var handBones = new HashSet<Transform>();
            foreach (Transform bone in hand.Transforms)
            {
                if (bone != null) handBones.Add(bone);
            }

            var palmBones = new HashSet<Transform> { hand.Wrist };
            foreach (UxrAvatarFinger finger in new[] { hand.Thumb, hand.Index, hand.Middle, hand.Little, hand.Ring })
            {
                if (finger != null && finger.Metacarpal != null) palmBones.Add(finger.Metacarpal);
            }

            var meshes = new List<BakedSkin>();
            foreach (SkinnedMeshRenderer skin in skins)
            {
                if (!IsVisible(skin) || skin.sharedMesh == null) continue;
                Transform[] bones = skin.bones;
                bool skinnedToHand = false;
                foreach (Transform bone in bones)
                {
                    if (bone != null && handBones.Contains(bone))
                    {
                        skinnedToHand = true;
                        break;
                    }
                }

                if (skinnedToHand) meshes.Add(BakedSkin.Capture(skin, bones, palmBones));
            }

            return meshes;
        }

        /// <summary>
        /// Дальнее (внешнее) пересечение луча с треугольниками SMR руки, у которых не меньше двух вершин скинованы
        /// преимущественно на кости из <paramref name="bones"/>: изнутри фаланги наружу последнее попадание — внешний слой
        /// (перчатка поверх кожи), соседние пальцы отброшены по костям.
        /// </summary>
        public static bool RaycastFarthest(List<BakedSkin> meshes, HashSet<Transform> bones, Vector3 origin, Vector3 direction, float maxDistance, out float distance) =>
            RaycastFarthest(meshes, bones, origin, direction, maxDistance, out distance, out _);

        /// <summary>То же, плюс нормаль поверхности в точке попадания (интерполяция вершинных нормалей, ориентирована по лучу наружу).</summary>
        public static bool RaycastFarthest(List<BakedSkin> meshes, HashSet<Transform> bones, Vector3 origin, Vector3 direction, float maxDistance,
            out float distance, out Vector3 normal)
        {
            distance = -1f;
            normal = Vector3.zero;
            direction.Normalize();
            BakedSkin bestMesh = null;
            int bestA = -1, bestB = -1, bestC = -1;
            float bestU = 0f, bestV = 0f;
            foreach (BakedSkin m in meshes)
            {
                if (m.VertexBone == null)
                {
                    continue;
                }

                for (int i = 0; i + 2 < m.Indices.Length; i += 3)
                {
                    int a = m.Indices[i], b = m.Indices[i + 1], c = m.Indices[i + 2];
                    int owned = (bones.Contains(m.VertexBone[a]) ? 1 : 0) + (bones.Contains(m.VertexBone[b]) ? 1 : 0) + (bones.Contains(m.VertexBone[c]) ? 1 : 0);
                    if (owned < 2)
                    {
                        continue;
                    }

                    if (RayTriangle(origin, direction, m.Vertices[a], m.Vertices[b], m.Vertices[c], out float t, out float u, out float v) && t <= maxDistance && t > distance)
                    {
                        distance = t;
                        bestMesh = m;
                        bestA = a;
                        bestB = b;
                        bestC = c;
                        bestU = u;
                        bestV = v;
                    }
                }
            }

            if (bestMesh == null)
            {
                return false;
            }

            normal = bestMesh.Normals != null
                ? bestMesh.Normals[bestA] * (1f - bestU - bestV) + bestMesh.Normals[bestB] * bestU + bestMesh.Normals[bestC] * bestV
                : Vector3.zero;
            if (normal.sqrMagnitude < 1e-12f)
            {
                normal = Vector3.Cross(bestMesh.Vertices[bestB] - bestMesh.Vertices[bestA], bestMesh.Vertices[bestC] - bestMesh.Vertices[bestA]);
            }

            // Изнутри фаланги наружу: нормаль внешней поверхности смотрит по лучу
            if (Vector3.Dot(normal, direction) < 0f)
            {
                normal = -normal;
            }

            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : direction;
            return true;
        }

        /// <summary>Ближайшее пересечение луча (мировые координаты) со всеми треугольниками запечённых SMR руки.</summary>
        public static bool RaycastNearest(List<BakedSkin> meshes, Vector3 origin, Vector3 direction, float maxDistance,
            out float distance, out Vector3 point, out Vector3 normal)
        {
            distance = maxDistance;
            point = normal = Vector3.zero;
            BakedSkin bestMesh = null;
            int bestA = -1, bestB = -1, bestC = -1;
            float bestU = 0f, bestV = 0f;
            direction.Normalize();
            foreach (BakedSkin m in meshes)
            {
                for (int i = 0; i + 2 < m.Indices.Length; i += 3)
                {
                    int a = m.Indices[i], b = m.Indices[i + 1], c = m.Indices[i + 2];
                    if (RayTriangle(origin, direction, m.Vertices[a], m.Vertices[b], m.Vertices[c], out float t, out float u, out float v) && t < distance)
                    {
                        distance = t;
                        bestMesh = m;
                        bestA = a;
                        bestB = b;
                        bestC = c;
                        bestU = u;
                        bestV = v;
                    }
                }
            }

            if (bestMesh == null)
            {
                return false;
            }

            point = origin + direction * distance;
            normal = bestMesh.Normals != null
                ? bestMesh.Normals[bestA] * (1f - bestU - bestV) + bestMesh.Normals[bestB] * bestU + bestMesh.Normals[bestC] * bestV
                : Vector3.zero;
            if (normal.sqrMagnitude < 1e-12f)
            {
                normal = Vector3.Cross(bestMesh.Vertices[bestB] - bestMesh.Vertices[bestA], bestMesh.Vertices[bestC] - bestMesh.Vertices[bestA]);
            }

            // Нормаль поверхности — навстречу лучу (наружу видимой стороны)
            if (Vector3.Dot(normal, direction) > 0f)
            {
                normal = -normal;
            }

            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : -direction;
            return true;
        }

        void Resolve(SkinnedMeshRenderer[] skins, UxrAvatar avatar, UxrHandSide side, UxrAvatarHand hand)
        {
            HasSkin = false;
            HasNormal = false;
            UsedAllTriangles = false;
            SelectedTMm = 0f;
            OffsetHand = Vector3.zero;
            NormalHand = Vector3.zero;
            RendererName = "";
            FailReason = "";
            Layers.Clear();

            if (hand.Wrist == null || !hand.GetPalmCenter(out Vector3 center) || !hand.GetPalmOutDirection(side, out Vector3 palmOut))
            {
                FailReason = "SDK не вычислил центр ладони";
                return;
            }

            palmOut.Normalize();
            if (!GripHandFrame.TryGet(avatar, side, out GripHandFrame frame, out string frameError))
            {
                FailReason = "нет рамки кисти: " + frameError;
                return;
            }

            List<BakedSkin> meshes;
            try
            {
                meshes = BakeHandSkins(skins, hand);
                if (meshes.Count == 0)
                {
                    FailReason = "нет видимых SkinnedMeshRenderer, скинованных на кости этой руки";
                    return;
                }

                int palmTriangles = 0;
                foreach (BakedSkin m in meshes) palmTriangles += m.PalmTriangleCount;
                UsedAllTriangles = palmTriangles == 0;

                float bestT = -1f;
                BakedSkin bestMesh = null;
                int bestA = -1, bestB = -1, bestC = -1;
                float bestU = 0f, bestV = 0f;
                foreach (BakedSkin m in meshes)
                {
                    var layer = new Layer { Name = m.Name, PalmTriangles = m.PalmTriangleCount };
                    for (int i = 0; i + 2 < m.Indices.Length; i += 3)
                    {
                        if (!UsedAllTriangles && !m.PalmTriangle[i / 3]) continue;
                        int a = m.Indices[i], b = m.Indices[i + 1], c = m.Indices[i + 2];
                        if (!RayTriangle(center, palmOut, m.Vertices[a], m.Vertices[b], m.Vertices[c], out float t, out float u, out float v) || t > MaxRayLength)
                        {
                            continue;
                        }

                        layer.HitsMm.Add(t * 1000f);
                        // Внешняя (видимая) поверхность — последнее пересечение вдоль луча наружу
                        if (t > bestT)
                        {
                            bestT = t;
                            bestMesh = m;
                            bestA = a;
                            bestB = b;
                            bestC = c;
                            bestU = u;
                            bestV = v;
                        }
                    }

                    layer.HitsMm.Sort();
                    Layers.Add(layer);
                }

                if (bestMesh == null)
                {
                    FailReason = $"луч наружу из ладони не попал в {(UsedAllTriangles ? "треугольники" : "ладонные треугольники")} рендереров руки в пределах {MaxRayLength * 1000f:0} мм";
                    return;
                }

                Vector3 hit = center + palmOut * bestT;
                OffsetHand = frame.VectorFromWorld(hit - center);
                HasSkin = true;
                RendererName = bestMesh.Name;
                SelectedTMm = bestT * 1000f;

                Vector3 normal = bestMesh.Normals != null
                    ? bestMesh.Normals[bestA] * (1f - bestU - bestV) + bestMesh.Normals[bestB] * bestU + bestMesh.Normals[bestC] * bestV
                    : Vector3.zero;
                if (normal.sqrMagnitude < 1e-12f)
                {
                    normal = Vector3.Cross(bestMesh.Vertices[bestB] - bestMesh.Vertices[bestA], bestMesh.Vertices[bestC] - bestMesh.Vertices[bestA]);
                }

                if (normal.sqrMagnitude > 1e-12f)
                {
                    normal.Normalize();
                    // Нормаль поверхности ладони смотрит наружу из ладони
                    if (Vector3.Dot(normal, palmOut) < 0f)
                    {
                        normal = -normal;
                    }

                    NormalHand = frame.DirFromWorld(normal);
                    HasNormal = true;
                }
            }
            catch (Exception ex)
            {
                HasSkin = false;
                HasNormal = false;
                FailReason = "ошибка BakeMesh: " + ex.Message;
            }
        }

        /// <summary>Мировые вершины/нормали BakeMesh и признак ладонного треугольника.</summary>
        internal sealed class BakedSkin
        {
            public string Name;
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public int[] Indices;
            public bool[] PalmTriangle;
            public int PalmTriangleCount;
            /// <summary>Доминирующая кость вершины (наибольший вес); null — нет весов.</summary>
            public Transform[] VertexBone;

            public static BakedSkin Capture(SkinnedMeshRenderer skin, Transform[] bones, HashSet<Transform> palmBones)
            {
                Mesh source = skin.sharedMesh;
                var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    // useScale=true: вершины в локальной системе SMR с его scale, полный localToWorld применяется один раз
                    skin.BakeMesh(baked, true);
                    Matrix4x4 toWorld = skin.localToWorldMatrix;
                    Matrix4x4 normalToWorld = toWorld.inverse.transpose;
                    Vector3[] vertices = baked.vertices;
                    Vector3[] normals = baked.normals;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] = toWorld.MultiplyPoint3x4(vertices[i]);
                    }

                    bool hasNormals = normals != null && normals.Length == vertices.Length;
                    if (hasNormals)
                    {
                        for (int i = 0; i < normals.Length; i++)
                        {
                            normals[i] = normalToWorld.MultiplyVector(normals[i]);
                        }
                    }

                    // Ладонная вершина: доминирующий вес у запястья или пястной кости этой руки
                    var palmVertex = new bool[vertices.Length];
                    var vertexBone = new Transform[vertices.Length];
                    using (NativeArray<byte> counts = source.GetBonesPerVertex())
                    using (NativeArray<BoneWeight1> weights = source.GetAllBoneWeights())
                    {
                        if (counts.Length == vertices.Length)
                        {
                            int offset = 0;
                            for (int v = 0; v < counts.Length; v++)
                            {
                                float best = -1f;
                                int bestBone = -1;
                                for (int j = 0; j < counts[v]; j++, offset++)
                                {
                                    BoneWeight1 w = weights[offset];
                                    if (w.weight > best)
                                    {
                                        best = w.weight;
                                        bestBone = w.boneIndex;
                                    }
                                }

                                Transform dominant = bestBone >= 0 && bestBone < bones.Length ? bones[bestBone] : null;
                                vertexBone[v] = dominant;
                                palmVertex[v] = dominant != null && palmBones.Contains(dominant);
                            }
                        }
                    }

                    var indices = new List<int>(baked.triangles.Length);
                    for (int sub = 0; sub < baked.subMeshCount; sub++)
                    {
                        if (baked.GetTopology(sub) == MeshTopology.Triangles)
                        {
                            indices.AddRange(baked.GetTriangles(sub));
                        }
                    }

                    var result = new BakedSkin
                    {
                        Name = skin.name,
                        Vertices = vertices,
                        Normals = hasNormals ? normals : null,
                        Indices = indices.ToArray(),
                        VertexBone = vertexBone,
                        PalmTriangle = new bool[indices.Count / 3]
                    };
                    for (int i = 0; i + 2 < result.Indices.Length; i += 3)
                    {
                        bool palm = palmVertex[result.Indices[i]] && palmVertex[result.Indices[i + 1]] && palmVertex[result.Indices[i + 2]];
                        result.PalmTriangle[i / 3] = palm;
                        if (palm) result.PalmTriangleCount++;
                    }

                    return result;
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
            }
        }

        /// <summary>Möller–Trumbore, обе стороны треугольника; t &gt; 0 вдоль луча.</summary>
        static bool RayTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
        {
            t = u = v = 0f;
            Vector3 e1 = b - a;
            Vector3 e2 = c - a;
            Vector3 p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f)
            {
                return false;
            }

            float inv = 1f / det;
            Vector3 s = origin - a;
            u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(s, e1);
            v = Vector3.Dot(dir, q) * inv;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            t = Vector3.Dot(e2, q) * inv;
            return t > 1e-6f;
        }
    }
}
