using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.Avatars.ContactIk
{
    /// <summary>Стабильный материальный якорь. Source совпадает с HumanPath renderer, не с порядком baked-списка.</summary>
    public sealed class ContactIkSurfaceAnchor
    {
        public string Source;
        public int SourceSubMesh, SourceTriangle;
        public Vector3 Barycentric;

        public HandPosePotentialContactTriangleKey Key => new HandPosePotentialContactTriangleKey(Source, SourceSubMesh, SourceTriangle);

        public ContactIkAnchorSample Evaluate(IReadOnlyList<FitTriangle> triangles)
        {
            Validate();
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            FitTriangle? found = null;
            foreach (FitTriangle t in triangles)
                if (Key.Equals(new HandPosePotentialContactTriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle)))
                {
                    if (found.HasValue) throw new ArgumentException("Неоднозначный материальный якорь: " + Key);
                    found = t;
                }
            if (!found.HasValue) throw new ArgumentException("Нет материального треугольника якоря: " + Key);
            return EvaluateTriangle(found.Value);
        }

        public ContactIkAnchorSample EvaluateTriangle(FitTriangle triangle)
        {
            Validate();
            if (!Finite(triangle.A) || !Finite(triangle.B) || !Finite(triangle.C) || triangle.Area <= 1e-12f)
                throw new ArgumentException("Якорь ссылается на вырожденную или нечисловую грань.");
            return new ContactIkAnchorSample
            {
                PositionMeters = triangle.A * Barycentric.x + triangle.B * Barycentric.y + triangle.C * Barycentric.z,
                Normal = triangle.Normal
            };
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Source) || SourceSubMesh < 0 || SourceTriangle < 0 || !Finite(Barycentric) ||
                Barycentric.x < 0 || Barycentric.y < 0 || Barycentric.z < 0 ||
                Math.Abs(Barycentric.x + Barycentric.y + Barycentric.z - 1f) > 1e-5f)
                throw new ArgumentException("Якорю нужны явные Source/submesh/triangle и barycentric внутри треугольника.");
        }

        public ContactIkSurfaceAnchor Copy() => new ContactIkSurfaceAnchor
        { Source = Source, SourceSubMesh = SourceSubMesh, SourceTriangle = SourceTriangle, Barycentric = Barycentric };

        /// <summary>Положительная uniform матрица: источник задан в физических метрах, масштаб переносится явно.</summary>
        public static void ValidateUniformFrame(Matrix4x4 matrix, bool requireRigid)
        {
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
                if (!Finite(matrix[r, c])) throw new ArgumentException("Нечисловая матрица кадра.");
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            float scale = x.magnitude;
            double tolerance = (double)scale * scale * 1e-5;
            float handedness = Vector3.Dot(Vector3.Cross(x, y), z);
            if (!Finite(scale) || !Finite(handedness) || scale <= 1e-8f || Math.Abs(y.magnitude - scale) > scale * 1e-5 || Math.Abs(z.magnitude - scale) > scale * 1e-5 ||
                Math.Abs(Vector3.Dot(x, y)) > tolerance || Math.Abs(Vector3.Dot(x, z)) > tolerance || Math.Abs(Vector3.Dot(y, z)) > tolerance ||
                handedness <= 0 || Math.Abs(matrix[3, 0]) > 1e-6 || Math.Abs(matrix[3, 1]) > 1e-6 ||
                Math.Abs(matrix[3, 2]) > 1e-6 || Math.Abs(matrix[3, 3] - 1) > 1e-6 || (requireRigid && Math.Abs(scale - 1) > 1e-5))
                throw new ArgumentException("Нужна положительная uniform рамка без отражения/shear/perspective; worldToObject должна быть rigid.");
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }

    public sealed class ContactIkAnchorSample
    {
        public Vector3 PositionMeters, Normal;
    }

    /// <summary>Явное соответствие target-участка цели. Не является автоматическим решением семантики пальца.</summary>
    public sealed class ContactIkPatchBinding
    {
        public ContactIkSurfaceAnchor Anchor;
        public ContactIkTarget Target;
        public bool CorrespondenceReviewed;
        public string Provenance;
    }

    /// <summary>Только явный перенос выбранного source-материала. Ничего не зеркалит и не ищет nearest вместо correspondence.</summary>
    public static class ContactIkSourceTargetMapping
    {
        public static ContactIkTarget FromSource(GripSample sample, UxrHandSide side, string semanticRole, string sourceMeshId,
                                                 ContactIkSurfaceAnchor sourceAnchor, Matrix4x4 sourceBodyToTargetObject,
                                                 ContactIkTarget explicitTargetSettings)
        {
            if (sample?.SourceContactFrame == null) throw new ArgumentException("Нет явно захваченного source contact frame.");
            GripSourceContactFrame frame = sample.SourceContactFrame;
            if (string.IsNullOrWhiteSpace(semanticRole) || string.IsNullOrWhiteSpace(sourceMeshId) || explicitTargetSettings == null || sourceAnchor == null)
                throw new ArgumentException("Нужны явные side/semantic role/mesh/material anchor/settings цели.");
            ContactIkSurfaceAnchor.ValidateUniformFrame(sourceBodyToTargetObject, false);
            GripSourceHandFrame[] hands = (frame.Hands ?? Array.Empty<GripSourceHandFrame>())
                .Where(h => h != null && h.Side == side && h.SemanticRole == semanticRole && !h.Inferred).ToArray();
            if (hands.Length != 1) throw new ArgumentException("Нет единственной прямой исходной руки заданной стороны и роли.");
            GripSourceSkinFrame[] skins = (frame.Skins ?? Array.Empty<GripSourceSkinFrame>()).Where(s => s != null && s.Id == sourceMeshId).ToArray();
            GripSourceHandMeshBinding[] bindings = (hands[0].MeshBindings ?? Array.Empty<GripSourceHandMeshBinding>()).Where(b => b.MeshId == sourceMeshId).ToArray();
            if (skins.Length != 1 || bindings.Length != 1 || skins[0].Mesh == null) throw new ArgumentException("Нет единственной явной source skin/hand binding.");
            var mesh = skins[0].Mesh;
            sourceAnchor.Validate();
            if (sourceAnchor.Source != mesh.RendererPath || sourceAnchor.SourceSubMesh >= mesh.SubmeshTriangles.Length)
                throw new ArgumentException("Source anchor не принадлежит выбранному renderer/submesh.");
            int[] indices = mesh.SubmeshTriangles[sourceAnchor.SourceSubMesh];
            long offset = (long)sourceAnchor.SourceTriangle * 3;
            if (offset + 2 >= indices.Length) throw new ArgumentException("Source anchor triangle вне topology.");
            int a = indices[(int)offset], b = indices[(int)offset + 1], c = indices[(int)offset + 2];
            var handBones = new HashSet<int>(bindings[0].HandBoneIndices);
            if ((mesh.WeightOf(a, handBones) + mesh.WeightOf(b, handBones) + mesh.WeightOf(c, handBones)) / 3 < .5f)
                throw new ArgumentException("Source anchor не относится к выбранной руке по исходным skin weights.");
            ContactIkAnchorSample p = sourceAnchor.EvaluateTriangle(new FitTriangle(mesh.Vertices[a], mesh.Vertices[b], mesh.Vertices[c]));
            return new ContactIkTarget
            {
                PatchId = explicitTargetSettings.PatchId, Mode = explicitTargetSettings.Mode,
                PositionMeters = sourceBodyToTargetObject.MultiplyPoint3x4(p.PositionMeters),
                Normal = sourceBodyToTargetObject.MultiplyVector(p.Normal).normalized, MatchNormal = explicitTargetSettings.MatchNormal,
                PositionToleranceMeters = explicitTargetSettings.PositionToleranceMeters, NormalToleranceDegrees = explicitTargetSettings.NormalToleranceDegrees,
                MinimumClearanceMeters = explicitTargetSettings.MinimumClearanceMeters, Weight = explicitTargetSettings.Weight
            };
        }
    }
}
