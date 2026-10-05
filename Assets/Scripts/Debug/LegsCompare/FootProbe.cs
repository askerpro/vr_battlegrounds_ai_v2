using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Замеры одной стопы для стенда <c>LegsComparisonRig</c> (удалён): разворот носка относительно корпуса, завал на ребро
    /// и высота подошвы ботинка над полом.
    ///
    /// <list type="bullet">
    /// <item><b>Разворот носка</b> (°): направление стопа → носок на полу против направления корпуса стенда. &gt; 0 — носок
    /// наружу (левый влево, правый вправо), &lt; 0 — внутрь («косолапость»).</item>
    /// <item><b>Завал</b> (°): поворот стопы вокруг её продольной оси от позы префаба (подошва на полу). &gt; 0 — на
    /// внутреннее ребро, &lt; 0 — на внешнее.</item>
    /// <item><b>Подошва</b> (м): нижняя точка меша ботинка над полом — вершины скина с главной костью стопы или пальцев,
    /// лежащие в позе префаба у самого низа, переносятся за костями каждый кадр. &lt; 0 — подошва в полу.</item>
    /// </list>
    ///
    /// <para>
    /// Опора — подошва у пола (<see cref="ContactHeight"/>) или стопа почти не движется (<see cref="PlantedSpeed"/>): по ней меряются разворот, завал и зазор
    /// «в опоре» — то, что видно, когда стоят или переступают. В воздухе стопа вправе быть развёрнута как угодно.
    /// </para>
    /// </summary>
    public sealed class FootProbe
    {
        /// <summary>Стопа в опоре, если кость стопы движется по горизонтали медленнее, м/с.</summary>
        public const float PlantedSpeed = 0.2f;

        /// <summary>Подошва ниже этого над полом — стопа в опоре (как порог скольжения стенда, 3 см), м.</summary>
        public const float ContactHeight = 0.03f;

        /// <summary>Подошва наклонена от пола меньше этого — стопа плашмя, °.</summary>
        public const float FlatTilt = 30f;

        /// <summary>Вершины ниже нижней точки подошвы в позе префаба не дальше этого — подошва, м.</summary>
        private const float SoleBand = 0.015f;

        private readonly Transform _foot, _toes;
        private readonly bool _left;
        private readonly Vector3 _soleUpLocal;
        private readonly List<(Transform bone, Vector3 local)> _sole = new List<(Transform, Vector3)>();
        private Vector3 _prev;
        private bool _hasPrev;

        // Кадр.
        public float Yaw, Roll, Sole;
        public bool Planted;
        /// <summary>Середина подошвы (среднее точек подошвы) этого кадра, мировая; без подошвы — кость стопы.</summary>
        public Vector3 SoleCenter;

        // Сегмент.
        public float YawMin, YawMax, RollMax, SoleMin, PlantSoleMin, PlantSoleMax;

        public bool HasSole => _sole.Count > 0;

        /// <summary>Снимается в позе префаба (сразу после появления стенда): подошва на полу, стопа без завала.</summary>
        public FootProbe(Transform root, Transform foot, Transform toes, bool left)
        {
            _foot = foot;
            _toes = toes;
            _left = left;
            _soleUpLocal = Quaternion.Inverse(foot.rotation) * root.up;
            CollectSole(root);
            ResetSegment();
        }

        /// <summary>
        /// Вершины подошвы — из запечённого в позе префаба меша (<c>BakeMesh</c>: меши импортированы без Read/Write, веса
        /// костей в Play не прочесть). Берутся вершины ниже лодыжки рядом с отрезком «лодыжка — пальцы» этой стопы, из них —
        /// нижняя полоса; каждая привязывается к кости пальцев, если лежит впереди неё, иначе к кости стопы.
        /// </summary>
        private void CollectSole(Transform root)
        {
            if (_foot == null) return;

            Vector3 up = root.up;
            Vector3 ankle = _foot.position;
            Vector3 toes = _toes != null ? _toes.position : ankle + root.forward * 0.12f;
            Vector3 dir = Vector3.ProjectOnPlane(toes - ankle, up);
            float length = dir.magnitude;
            if (length < 1e-4f) return;
            dir /= length;

            var candidates = new List<(Vector3 world, float y)>();
            var baked = new Mesh();
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (smr.sharedMesh == null) continue;
                smr.BakeMesh(baked, true);
                Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (Vector3 v in baked.vertices)
                {
                    Vector3 p = m.MultiplyPoint3x4(v);
                    Vector3 rel = Vector3.ProjectOnPlane(p - ankle, up);
                    float along = Vector3.Dot(rel, dir);
                    float across = (rel - dir * along).magnitude;
                    // Ботинок: от пятки (позади лодыжки) до носка (впереди пальцев), не дальше 9 см вбок, ниже лодыжки.
                    if (along < -0.15f || along > length + 0.12f || across > 0.09f) continue;
                    float y = Vector3.Dot(p - root.position, up);
                    if (y > Vector3.Dot(ankle - root.position, up)) continue;
                    candidates.Add((p, y));
                }
            }
            Object.Destroy(baked);

            if (candidates.Count == 0) return;
            float min = float.MaxValue;
            foreach (var c in candidates) min = Mathf.Min(min, c.y);
            foreach (var c in candidates)
            {
                if (c.y > min + SoleBand) continue;
                bool onToes = _toes != null && Vector3.Dot(Vector3.ProjectOnPlane(c.world - toes, up), dir) > 0f;
                Transform bone = onToes ? _toes : _foot;
                _sole.Add((bone, bone.InverseTransformPoint(c.world)));
            }
        }

        public void ResetSegment()
        {
            YawMin = RollMax = SoleMin = PlantSoleMin = float.MaxValue;
            YawMax = PlantSoleMax = float.MinValue;
            RollMax = 0f;
            _hasPrev = false;
        }

        /// <summary>Замер кадра: пол — плоскость через <paramref name="floor"/> с нормалью up, корпус — <paramref name="bodyForward"/>.</summary>
        public void Measure(Vector3 floor, Vector3 bodyForward, float dt)
        {
            Vector3 up = Vector3.up;
            Vector3 footPos = _foot.position;
            Vector3 toePos = _toes != null ? _toes.position : footPos + _foot.rotation * Vector3.forward * 0.1f;
            Vector3 dir = Vector3.ProjectOnPlane(toePos - footPos, up);
            Vector3 body = Vector3.ProjectOnPlane(bodyForward, up);

            float yaw = dir.sqrMagnitude > 1e-8f && body.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(body, dir, up) : 0f;
            Yaw = _left ? -yaw : yaw;

            // Завал: нормаль подошвы против вертикальной плоскости через продольную ось стопы.
            Vector3 axis = (toePos - footPos).normalized;
            Vector3 inward = Vector3.Cross(up, dir.normalized) * (_left ? 1f : -1f);
            Vector3 soleUp = Vector3.ProjectOnPlane(_foot.rotation * _soleUpLocal, axis);
            Roll = dir.sqrMagnitude > 1e-8f ? Mathf.Asin(Mathf.Clamp(Vector3.Dot(soleUp.normalized, inward), -1f, 1f)) * Mathf.Rad2Deg : 0f;

            Sole = float.NaN;
            Vector3 sum = Vector3.zero;
            foreach ((Transform bone, Vector3 local) in _sole)
            {
                Vector3 w = bone.TransformPoint(local);
                sum += w;
                float y = Vector3.Dot(w - floor, up);
                if (float.IsNaN(Sole) || y < Sole) Sole = y;
            }
            SoleCenter = _sole.Count > 0 ? sum / _sole.Count : footPos;

            // Опора: подошва у пола (ниже ContactHeight) и стопа почти плашмя (подошва не круче FlatTilt — не удар пяткой и
            // не отрыв носка, где завал и разворот не определены), или стопа почти стоит. Только по скорости нельзя: стопа,
            // которая в опоре скользит, опорой бы не считалась.
            Vector3 step = Vector3.ProjectOnPlane(footPos - _prev, up);
            bool flat = Vector3.Angle(_foot.rotation * _soleUpLocal, up) < FlatTilt;
            Planted = _hasPrev && (step.magnitude / Mathf.Max(dt, 1e-4f) < PlantedSpeed || !float.IsNaN(Sole) && Sole < ContactHeight && flat);
            _prev = footPos;
            _hasPrev = true;

            if (!float.IsNaN(Sole)) SoleMin = Mathf.Min(SoleMin, Sole);
            if (!Planted) return;

            YawMin = Mathf.Min(YawMin, Yaw);
            YawMax = Mathf.Max(YawMax, Yaw);
            if (Mathf.Abs(Roll) > Mathf.Abs(RollMax)) RollMax = Roll;
            if (float.IsNaN(Sole)) return;
            PlantSoleMin = Mathf.Min(PlantSoleMin, Sole);
            PlantSoleMax = Mathf.Max(PlantSoleMax, Sole);
        }

        /// <summary>Сводка сегмента: разворот в опоре мин..макс, завал в опоре (наибольший по модулю), подошва мин и в опоре мин..макс, см.</summary>
        public static string Summary(FootProbe l, FootProbe r)
        {
            return $"{Range(l.YawMin, l.YawMax, 1f, "0")}/{Range(r.YawMin, r.YawMax, 1f, "0")} | {F(l.RollMax, "0")}/{F(r.RollMax, "0")} | " +
                   $"{Cm(l.SoleMin)}/{Cm(r.SoleMin)} | {Range(l.PlantSoleMin, l.PlantSoleMax, 100f, "0.0")}/{Range(r.PlantSoleMin, r.PlantSoleMax, 100f, "0.0")}";
        }

        /// <summary>Кадр для frames.csv: разворот, завал, подошва (см), опора.</summary>
        public string Frame() => $"{F(Yaw, "0")};{F(Roll, "0")};{(float.IsNaN(Sole) ? "" : F(Sole * 100f, "0.0"))};{(Planted ? 1 : 0)}";

        private static string Cm(float v) => v == float.MaxValue || float.IsNaN(v) ? "—" : F(v * 100f, "0.0");

        private static string Range(float min, float max, float k, string format) =>
            min == float.MaxValue ? "—" : F(min * k, format) + ".." + F(max * k, format);

        private static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
    }
}
