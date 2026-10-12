using System;
using UnityEngine;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Комплект прицела всегда в реальном размере: корни оружия масштабированы (AR15 0,7, MP5K 4,533, Uzi 0,209…),
    /// поэтому localScale экземпляра комплекта = 1 / масштаб родителя (MeshContainer), и lossyScale комплекта = 1.
    /// Тогда локальные единицы комплекта — метры мира, и ход SightAdjustablePart задан в метрах.
    /// Неравномерный масштаб или скос родителя компенсировать одним localScale нельзя — отказ.
    /// </summary>
    public static class SightKitScale
    {
        public const float UniformTolerance = 1e-4f;
        public const float UnitTolerance = 1e-5f;

        /// <summary>Равномерный мировой масштаб родителя; отказ при неравномерном масштабе или скосе.</summary>
        public static float ParentScale(Transform parent)
        {
            if (parent == null) return 1f;
            Matrix4x4 m = parent.localToWorldMatrix;
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
            float sx = x.magnitude, sy = y.magnitude, sz = z.magnitude;
            if (sx < 1e-8f || sy < 1e-8f || sz < 1e-8f)
                throw new InvalidOperationException($"{parent.name}: нулевой масштаб родителя комплекта");
            float s = (sx + sy + sz) / 3f;
            if (Mathf.Abs(sx - s) > UniformTolerance * s || Mathf.Abs(sy - s) > UniformTolerance * s || Mathf.Abs(sz - s) > UniformTolerance * s)
                throw new InvalidOperationException($"{parent.name}: неравномерный масштаб родителя ({sx:0.######}, {sy:0.######}, {sz:0.######}) — комплект в реальном размере поставить нельзя");
            if (Mathf.Abs(Vector3.Dot(x, y)) > UniformTolerance * s * s || Mathf.Abs(Vector3.Dot(y, z)) > UniformTolerance * s * s
                || Mathf.Abs(Vector3.Dot(x, z)) > UniformTolerance * s * s)
                throw new InvalidOperationException($"{parent.name}: скос в цепочке родителя (неравномерный масштаб под поворотом) — комплект не ставится");
            return s;
        }

        /// <summary>localScale комплекта под родителем, при котором его мировой масштаб равен 1.</summary>
        public static Vector3 CompensatingLocalScale(Transform parent) => Vector3.one / ParentScale(parent);

        public static void Apply(Transform kit)
        {
            kit.localScale = CompensatingLocalScale(kit.parent);
            string problem = Problem(kit);
            if (problem != null) throw new InvalidOperationException(problem);
        }

        /// <summary>null — комплект в реальном размере (мировой масштаб 1 ± 1e-5 по каждой оси).</summary>
        public static string Problem(Transform kit)
        {
            Matrix4x4 m = kit.localToWorldMatrix;
            for (int i = 0; i < 3; i++)
            {
                float length = ((Vector3)m.GetColumn(i)).magnitude;
                if (Mathf.Abs(length - 1f) > UnitTolerance)
                    return $"{kit.name}: мировой масштаб комплекта {length:0.######} по оси {i} — должен быть 1 (реальный размер)";
            }
            return null;
        }
    }
}
