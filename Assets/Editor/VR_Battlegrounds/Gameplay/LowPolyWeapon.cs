using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltimateXR.Core;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Геометрия оружия из пака Low Poly Weapons VOL.2 для <see cref="HandsPackWeaponBuilder" />, третий источник рядом с
    /// <see cref="HandsPackWeapon" /> и <see cref="KinemationWeapon" />.
    ///
    /// <para>
    /// Оружие пака — префаб <c>Prefabs/&lt;класс&gt;/&lt;модель&gt;.prefab</c>: корпус — <see cref="MeshRenderer" /> на корне,
    /// детали (затвор, спуск, магазин, патроны) — дочерние <see cref="MeshRenderer" /> без скина. Деталь здесь — объект
    /// префаба по имени, её место — трансформ (<see cref="PartInBody" />), меш и материалы — как в префабе пака.
    /// </para>
    ///
    /// <para>
    /// Клипов у пака нет: оси прицеливания, ход затвора, угол спуска и центры ладоней — числа рецепта
    /// (<see cref="LowPolyWeaponRecipe" />), снятые с модели. <see cref="Sample" /> ничего не делает: у модели одна поза.
    /// </para>
    /// </summary>
    public sealed class LowPolyWeapon : IWeaponModel, IPalmSource, IDisposable
    {
        private readonly LowPolyWeaponRecipe _recipe;
        private readonly WeaponModelPreviewScope _preview;
        private readonly Dictionary<string, Part> _parts = new Dictionary<string, Part>();
        private readonly Transform _body;

        private sealed class Part
        {
            public Transform Transform;
            public Mesh Mesh;
            public Material[] Materials;
        }

        public LowPolyWeapon(LowPolyWeaponRecipe recipe)
        {
            WeaponModelPreviewScope.CheckEditor();
            _recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            string path = LowPolyWeaponBuilder.PackPrefabPath(recipe);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new ArgumentException($"Нет префаба пака {path}");
            CheckAxes(recipe);

            try
            {
                _preview = new WeaponModelPreviewScope(prefab);
                foreach (MeshFilter filter in _preview.Instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || renderer == null) continue;
                    // Имя корня у экземпляра — «(Clone)»: деталь-корпус называется именем префаба пака.
                    string name = filter.transform == _preview.Instance.transform ? prefab.name : filter.name;
                    if (_parts.ContainsKey(name)) throw new ArgumentException($"{path}: две детали с именем '{name}'");
                    _parts[name] = new Part { Transform = filter.transform, Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials };
                }

                if (!_parts.TryGetValue(recipe.Weapon.BodyPart, out Part body))
                    throw new ArgumentException($"{path}: нет детали-корпуса '{recipe.Weapon.BodyPart}'. Есть: {string.Join(", ", _parts.Keys)}");
                _body = body.Transform;
            }
            catch
            {
                _preview?.Dispose();
                throw;
            }
        }

        public IEnumerable<string> Parts => _parts.Keys;

        public Mesh MeshOf(string part) => Find(part).Mesh;

        /// <summary>Материалы рендерера префаба пака (<c>Gun_MAT</c>), не заглушки модели.</summary>
        public Material[] MaterialsOf(string part) => Find(part).Materials;

        /// <summary>Место детали в осях корпуса: <c>корпус.worldToLocal × деталь.localToWorld</c>.</summary>
        public Matrix4x4 PartInBody(string part) => _body.worldToLocalMatrix * Find(part).Transform.localToWorldMatrix;

        /// <summary>Клипов у пака нет — поза одна.</summary>
        public void Sample(string clipName, float time = 0f) { }

        /// <summary>
        /// Движение детали из чисел рецепта (клипы не читаются): затвор — <see cref="LowPolyWeaponRecipe.BoltTravel" /> по
        /// <see cref="LowPolyWeaponRecipe.BoltAxis" />, спуск — <see cref="LowPolyWeaponRecipe.TriggerDeg" /> вокруг оси
        /// <see cref="LowPolyWeaponRecipe.TriggerAxis" /> (локальная ось спуска; знак угла — направление поворота).
        /// </summary>
        public PartMotion Measure(string part, string clipName, string restClip = "Idle")
        {
            var motion = new PartMotion { Part = part, Clip = clipName };
            if (part == _recipe.Weapon.ActionPart && _recipe.BoltTravel > 0f)
            {
                // Ход задан в метрах префаба пака; в единицах меша — делённым на масштаб корпуса.
                Vector3 axis = _recipe.BoltAxis.normalized;
                motion.Axis = axis;
                motion.Far = axis * (_recipe.BoltTravel / _body.lossyScale.x);
                motion.MaxTravel = motion.Far.magnitude;
            }
            else if (part == _recipe.Weapon.TriggerPart && Mathf.Abs(_recipe.TriggerDeg) > 0f)
            {
                Vector3 axis = Vector3.zero;
                axis[_recipe.TriggerAxis] = Mathf.Sign(_recipe.TriggerDeg);
                motion.RotationAxis = axis;
                motion.MaxAngle = Mathf.Abs(_recipe.TriggerDeg);
            }
            return motion;
        }

        /// <summary>Оси прицеливания — из рецепта (клипа с руками у пака нет).</summary>
        public (Vector3 forward, Vector3 up) AimAxes(string poseClip) => (_recipe.Forward, _recipe.Up);

        /// <summary>
        /// Статичные меши деталей под <paramref name="container" />: корпус в <paramref name="bodyLocal" />, остальные —
        /// точно относительно него (как <see cref="HandsPackWeapon.BuildParts" />).
        /// </summary>
        public Dictionary<string, Transform> BuildParts(Transform container, Matrix4x4 bodyLocal, IDictionary<string, string> partNames)
        {
            var result = new Dictionary<string, Transform>();
            foreach (var pair in partNames)
            {
                var go = new GameObject(pair.Value);
                go.transform.SetParent(container, false);
                Matrix4x4 local = bodyLocal * PartInBody(pair.Key);
                go.transform.SetLocalPositionAndRotation(local.GetColumn(3), local.rotation);
                go.transform.localScale = local.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = MeshOf(pair.Key);
                go.AddComponent<MeshRenderer>().sharedMaterials = MaterialsOf(pair.Key);
                result[pair.Key] = go.transform;
            }
            return result;
        }

        /// <summary>
        /// Центр ладони в осях корпуса, единицы меша: правая — рукоять (<see cref="LowPolyWeaponRecipe.PalmGrip" />, точка 0),
        /// левая — вторая рука (<see cref="LowPolyWeaponRecipe.PalmSupport" />, точка 1).
        /// </summary>
        public Vector3 PalmCenterInBody(UxrHandSide side) => side == UxrHandSide.Right ? _recipe.PalmGrip : _recipe.PalmSupport;

        /// <summary>Детали, их места и габариты в осях корпуса, числа рецепта — для проверки источника.</summary>
        public string Report()
        {
            var sb = new StringBuilder($"{LowPolyWeaponBuilder.PackPrefabPath(_recipe)}, корпус {_recipe.Weapon.BodyPart}, масштаб корпуса {_body.lossyScale.x:F4}\n");
            foreach (var pair in _parts)
            {
                Matrix4x4 m = PartInBody(pair.Key);
                Bounds b = pair.Value.Mesh.bounds;
                sb.AppendLine($"  {pair.Key}: место {((Vector3)m.GetColumn(3)).ToString("F4")}, меш {b.min.ToString("F3")}..{b.max.ToString("F3")}, " +
                              $"вершин {pair.Value.Mesh.vertexCount}, материалы {string.Join(", ", pair.Value.Materials.Select(x => x != null ? x.name : "—"))}");
            }
            sb.AppendLine($"  ствол {_recipe.Forward}, верх {_recipe.Up}; затвор {_recipe.BoltTravel * 100f:F1} см по {_recipe.BoltAxis}; " +
                          $"спуск {_recipe.TriggerDeg:F0}° вокруг оси {_recipe.TriggerAxis}; ладони {_recipe.PalmGrip.ToString("F3")} / {_recipe.PalmSupport.ToString("F3")}");
            return sb.ToString();
        }

        public void Dispose() => _preview?.Dispose();

        private Part Find(string part) =>
            part != null && _parts.TryGetValue(part, out Part p) ? p : throw new ArgumentException($"{_recipe.Weapon.Name}: нет детали '{part}'. Есть: {string.Join(", ", _parts.Keys)}");

        /// <summary>Оси рецепта — оси меша (одна ненулевая координата ±1), ствол и верх перпендикулярны; ось спуска 0..2.</summary>
        private static void CheckAxes(LowPolyWeaponRecipe r)
        {
            bool Unit(Vector3 v) => Mathf.Approximately(v.sqrMagnitude, 1f) &&
                                    (Mathf.Abs(v.x) > 0.999f || Mathf.Abs(v.y) > 0.999f || Mathf.Abs(v.z) > 0.999f);
            if (!Unit(r.Forward) || !Unit(r.Up) || Mathf.Abs(Vector3.Dot(r.Forward, r.Up)) > 1e-3f)
                throw new ArgumentException($"{r.Weapon.Name}: оси ствола {r.Forward} и верха {r.Up} должны быть разными осями меша");
            if (r.BoltTravel > 0f && !Unit(r.BoltAxis))
                throw new ArgumentException($"{r.Weapon.Name}: ось хода затвора {r.BoltAxis} должна быть осью меша");
            if (r.TriggerAxis < 0 || r.TriggerAxis > 2)
                throw new ArgumentException($"{r.Weapon.Name}: ось спуска {r.TriggerAxis} вне 0..2");
        }
    }
}
