using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Общая визуальная политика X-ray. Метрики и подтверждение знака принадлежат вызывающему коду.</summary>
    public static class HandPoseXrayRenderer
    {
        public sealed class Style
        {
            public static Style Default { get; } = new Style();
            public Color HandColor { get; }
            public Color ObjectColor { get; }
            public Color OtherHandColor { get; }
            public Color IntersectionColor { get; }
            public Color ContactColor { get; }
            public Color Background { get; }
            public float ObjectAlpha { get; }
            public float HandFrontAlpha { get; }
            public float HandOccludedAlpha { get; }
            public float IntersectionAlpha { get; }

            public Style() : this(
                new Color(.15f, .72f, .95f),
                new Color(.62f, .65f, .70f),
                new Color(.65f, .35f, .9f),
                new Color(1f, .14f, .12f),
                new Color(1f, .88f, .10f),
                new Color(.08f, .10f, .13f),
                1f, .68f, .48f, .84f) { }

            public Style(Color handColor, Color objectColor, Color otherHandColor, Color background, float objectAlpha)
                : this(handColor, objectColor, otherHandColor, new Color(1f, .14f, .12f), new Color(1f, .88f, .10f),
                    background, objectAlpha, .68f, .48f, .84f) { }

            public Style(Color handColor, Color objectColor, Color otherHandColor, Color intersectionColor, Color contactColor,
                Color background, float objectAlpha, float handFrontAlpha, float handOccludedAlpha, float intersectionAlpha)
            {
                if (!Finite(handColor) || !Finite(objectColor) || !Finite(otherHandColor) ||
                    !Finite(intersectionColor) || !Finite(contactColor) || !Finite(background) ||
                    !ValidAlpha(objectAlpha) || !ValidAlpha(handFrontAlpha) ||
                    !ValidAlpha(handOccludedAlpha) || !ValidAlpha(intersectionAlpha))
                    throw new ArgumentException("Стиль X-ray требует конечные цвета и alpha в [0;1].");
                HandColor = handColor; ObjectColor = objectColor; OtherHandColor = otherHandColor;
                IntersectionColor = intersectionColor; ContactColor = contactColor; Background = background;
                ObjectAlpha = objectAlpha; HandFrontAlpha = handFrontAlpha;
                HandOccludedAlpha = handOccludedAlpha; IntersectionAlpha = intersectionAlpha;
            }
        }

        /// <summary>Один владелец native materials для SceneView или PNG; освобождается при завершении контекста.</summary>
        public sealed class MaterialSet : IDisposable
        {
            public Material Hand { get; }
            public Material HandDepth { get; }
            public Material HandOccluded { get; }
            public Material Object { get; }
            public Material OtherHand { get; }
            public Material Intersection { get; }
            public Material Wire { get; }
            public Material Overlay { get; }
            public Style VisualStyle { get; }
            bool _disposed;

            internal MaterialSet(Style style)
            {
                VisualStyle = style;
                var shader = Shader.Find("Hidden/VRBattlegrounds/HandPoseFit");
                if (!shader) throw new InvalidOperationException("Не импортирован общий shader HandPoseFit.");
                try
                {
                    // Внешняя оболочка оружия непрозрачна (ZWrite=1, ZTest=LessEqual, Cull=Back): внутренние детали скрыты.
                    Object = CreatePass(shader, style.ObjectColor, BlendMode.One, BlendMode.Zero,
                        zWrite: 1, CompareFunction.LessEqual, CullMode.Back, ColorWriteMask.All, shade: 1, alpha: 1f, queue: 2000);
                    OtherHand = CreatePass(shader, style.OtherHandColor, BlendMode.One, BlendMode.Zero,
                        zWrite: 1, CompareFunction.LessEqual, CullMode.Back, ColorWriteMask.All, shade: 1, alpha: 1f, queue: 2001);
                    // Кисть позади внешней оболочки оружия просвечивает по ZTest=Greater до записи глубины кисти.
                    HandOccluded = CreatePass(shader, style.HandColor, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha,
                        zWrite: 0, CompareFunction.Greater, CullMode.Back, ColorWriteMask.All, shade: 1, alpha: style.HandOccludedAlpha, queue: 3000);
                    // Depth pre-pass передней части кисти убирает внутреннее самоперекрытие пальцев перед полупрозрачным проходом.
                    HandDepth = CreatePass(shader, style.HandColor, BlendMode.Zero, BlendMode.One,
                        zWrite: 1, CompareFunction.LessEqual, CullMode.Back, 0, shade: 0, alpha: 0f, queue: 3001);
                    // Кисть перед оружием полупрозрачна, чтобы через неё читался и внешний контур оружия.
                    Hand = CreatePass(shader, style.HandColor, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha,
                        zWrite: 0, CompareFunction.LessEqual, CullMode.Back, ColorWriteMask.All, shade: 1, alpha: style.HandFrontAlpha, queue: 3002);
                    // Общая часть / пересечение меша кисти с оружием подсвечивается красным поверх обоих контуров.
                    Intersection = CreatePass(shader, style.IntersectionColor, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha,
                        zWrite: 0, CompareFunction.Always, CullMode.Back, ColorWriteMask.All, shade: 1, alpha: style.IntersectionAlpha, queue: 3003);
                    Wire = CreatePass(shader, new Color(.85f, .86f, .89f, .32f), BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha,
                        zWrite: 0, CompareFunction.Always, CullMode.Off, ColorWriteMask.All, shade: 0, alpha: 1f, queue: 3004);
                    Overlay = CreatePass(shader, Color.white, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha,
                        zWrite: 0, CompareFunction.Always, CullMode.Off, ColorWriteMask.All, shade: 0, alpha: 1f, queue: 3005);
                    Overlay.SetFloat("_UseVertexColor", 1);
                }
                catch { Dispose(); throw; }
            }

            internal void CheckAlive()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(MaterialSet));
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Release(Hand); Release(HandDepth); Release(HandOccluded);
                Release(Object); Release(OtherHand); Release(Intersection);
                Release(Wire); Release(Overlay);
            }
        }

        /// <summary>Проекция исходной камеры сохраняется; координаты камеры переводятся в rigid frame снимка.</summary>
        public sealed class CameraState
        {
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Matrix4x4 Projection { get; }
            public bool Orthographic { get; }
            public float OrthographicSize { get; }
            public float FieldOfView { get; }
            public float Aspect { get; }
            public float Near { get; }
            public float Far { get; }

            CameraState(Camera camera, Matrix4x4 worldToFrame)
            {
                Position = worldToFrame.MultiplyPoint3x4(camera.transform.position);
                Rotation = worldToFrame.rotation * camera.transform.rotation;
                Projection = camera.projectionMatrix; Orthographic = camera.orthographic;
                OrthographicSize = camera.orthographicSize; FieldOfView = camera.fieldOfView;
                Aspect = camera.aspect; Near = camera.nearClipPlane; Far = camera.farClipPlane;
                if (float.IsNaN(Aspect) || float.IsInfinity(Aspect) || Aspect <= 0)
                    throw new ArgumentException("Камера требует положительное конечное соотношение сторон.");
            }

            public static CameraState Capture(Camera camera, Matrix4x4 worldToFrame)
            {
                if (!camera) throw new ArgumentNullException(nameof(camera));
                ContactIkFrameGuard(worldToFrame);
                return new CameraState(camera, worldToFrame);
            }

            public void Apply(Camera camera)
            {
                if (!camera) throw new ArgumentNullException(nameof(camera));
                camera.transform.SetPositionAndRotation(Position, Rotation);
                camera.orthographic = Orthographic; camera.orthographicSize = OrthographicSize;
                camera.fieldOfView = FieldOfView; camera.aspect = Aspect;
                camera.nearClipPlane = Near; camera.farClipPlane = Far;
                camera.projectionMatrix = Projection;
            }
        }

        public static MaterialSet CreateMaterials(Style style = null) => new MaterialSet(style ?? Style.Default);

        /// <summary>Сначала внешняя оболочка оружия (без внутренностей), затем просвечивающаяся кисть и красное пересечение.</summary>
        public static void DrawNow(MaterialSet materials, Mesh hand, Mesh selectedObject, Matrix4x4 frameToWorld,
            Mesh wire = null, Mesh otherHand = null, Mesh intersection = null)
        {
            if (materials == null) throw new ArgumentNullException(nameof(materials));
            materials.CheckAlive();
            Draw(selectedObject, materials.Object);
            Draw(otherHand, materials.OtherHand);
            Draw(hand, materials.HandOccluded);
            Draw(hand, materials.HandDepth);
            Draw(hand, materials.Hand);
            Draw(intersection, materials.Intersection);
            Draw(wire, materials.Wire);
            void Draw(Mesh mesh, Material material)
            {
                if (mesh && material.SetPass(0)) Graphics.DrawMeshNow(mesh, frameToWorld);
            }
        }

        /// <summary>Те же материалы/цвета/depth, что в SceneView; Begin/End и камера принадлежат adapter.</summary>
        public static void DrawPreview(PreviewRenderUtility preview, MaterialSet materials, Mesh hand, Mesh selectedObject,
            Matrix4x4 frameToPreview, Mesh wire = null, Mesh otherHand = null, Mesh intersection = null)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            if (materials == null) throw new ArgumentNullException(nameof(materials));
            materials.CheckAlive();
            Draw(selectedObject, materials.Object);
            Draw(otherHand, materials.OtherHand);
            Draw(hand, materials.HandOccluded);
            Draw(hand, materials.HandDepth);
            Draw(hand, materials.Hand);
            Draw(intersection, materials.Intersection);
            Draw(wire, materials.Wire);
            void Draw(Mesh mesh, Material material)
            {
                if (mesh) preview.DrawMesh(mesh, frameToPreview, material, 0);
            }
        }

        static Material CreatePass(Shader shader, Color tint, BlendMode src, BlendMode dst, int zWrite,
            CompareFunction zTest, CullMode cull, ColorWriteMask colorMask, int shade, float alpha, int queue)
        {
            var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            m.SetFloat("_Src", (int)src);
            m.SetFloat("_Dst", (int)dst);
            m.SetFloat("_ZWrite", zWrite);
            m.SetFloat("_ZTest", (int)zTest);
            m.SetFloat("_Cull", (int)cull);
            m.SetFloat("_ColorMask", (int)colorMask);
            m.SetFloat("_Shade", shade);
            m.SetFloat("_Alpha", alpha);
            m.SetColor("_Tint", tint);
            m.SetFloat("_UseVertexColor", 0);
            m.SetFloat("_ObjectSpaceLighting", 1);
            return m;
        }

        static void ContactIkFrameGuard(Matrix4x4 matrix)
        {
            // Rigid кадр: масштаб уже находится в запечённых вершинах, не в проекции камеры.
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
                if (float.IsNaN(matrix[r, c]) || float.IsInfinity(matrix[r, c])) throw new ArgumentException("Нечисловой кадр камеры.");
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            if (Mathf.Abs(x.sqrMagnitude - 1) > 1e-4f || Mathf.Abs(y.sqrMagnitude - 1) > 1e-4f || Mathf.Abs(z.sqrMagnitude - 1) > 1e-4f ||
                Mathf.Abs(Vector3.Dot(x, y)) > 1e-4f || Mathf.Abs(Vector3.Dot(x, z)) > 1e-4f || Mathf.Abs(Vector3.Dot(y, z)) > 1e-4f ||
                Vector3.Dot(Vector3.Cross(x, y), z) < .9999f || Mathf.Abs(matrix[3, 0]) > 1e-5f || Mathf.Abs(matrix[3, 1]) > 1e-5f ||
                Mathf.Abs(matrix[3, 2]) > 1e-5f || Mathf.Abs(matrix[3, 3] - 1) > 1e-5f)
                throw new ArgumentException("Камере нужен rigid кадр снимка без scale/shear/reflection.");
        }

        static bool ValidAlpha(float a) => !(float.IsNaN(a) || float.IsInfinity(a) || a < 0 || a > 1);
        static bool Finite(Color c) => !(float.IsNaN(c.r) || float.IsInfinity(c.r) || float.IsNaN(c.g) || float.IsInfinity(c.g) ||
            float.IsNaN(c.b) || float.IsInfinity(c.b) || float.IsNaN(c.a) || float.IsInfinity(c.a));
        static void Release(Object value) { if (value) Object.DestroyImmediate(value); }
    }
}
