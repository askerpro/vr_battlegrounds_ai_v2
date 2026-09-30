using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Геометрия и механика оружия из пака Hands Weapons Animations — вычислением, а не на глаз.
    ///
    /// <para>
    /// Пак — FPS-руки с оружием: каждая деталь оружия (корпус, цевьё, затвор, спуск) — отдельный
    /// <see cref="SkinnedMeshRenderer" />, жёстко привязанный к одной кости. Трансформ самого
    /// рендерера с положением меша не совпадает (расхождение до 1.5 в матрице), поэтому деталь,
    /// переставленная в <see cref="MeshFilter" /> по трансформу рендерера, съезжает. Точное место
    /// детали — <c>кость × bindpose</c> её основной кости.
    /// </para>
    ///
    /// <para>
    /// Клипы пака двигают те же кости, поэтому из них вычисляются ход помпы/затвора и угол спуска,
    /// а ладони FPS-рук показывают, где руки лежат на оружии. Ладонь переводится в точку хвата
    /// UltimateXR калибровкой, снятой с оружия, у которого хват уже настроен вручную
    /// (<see cref="GripCalibration" />).
    /// </para>
    ///
    /// <para>
    /// Все размеры — в единицах меша: детали ставятся с масштабом 1, реальный размер задаёт
    /// масштаб корня префаба (правило CLAUDE.md, эталон в <c>WeaponScaleTests</c>).
    /// </para>
    /// </summary>
    public sealed class HandsPackWeapon : IDisposable, IWeaponModel
    {
        public const string PackModels = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/";

        private readonly GameObject _instance;
        private readonly string _folder;
        private readonly Dictionary<string, SkinnedMeshRenderer> _parts = new Dictionary<string, SkinnedMeshRenderer>();
        private readonly Dictionary<string, int> _mainBone = new Dictionary<string, int>();
        private readonly string _body;

        /// <param name="folder">Папка оружия в паке, например <c>Hands_Shotgun</c>.</param>
        /// <param name="bodyPart">Имя рендерера корпуса, например <c>Shogun_Base_mesh</c>.</param>
        public HandsPackWeapon(string folder, string bodyPart)
        {
            _folder = PackModels + folder;
            string modelPath = AssetDatabase.FindAssets("t:Model", new[] { _folder })
                                            .Select(AssetDatabase.GUIDToAssetPath)
                                            .FirstOrDefault(p => !p.Contains("@"));
            if (modelPath == null) throw new ArgumentException($"В {_folder} нет модели без '@'");

            _instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            _instance.hideFlags = HideFlags.HideAndDontSave;
            ModelPath = modelPath;

            foreach (var r in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null || r.name.StartsWith("Arm") || r.name.StartsWith("Glove")) continue;
                _parts[r.name] = r;
                _mainBone[r.name] = MainBone(r);
            }

            if (!_parts.ContainsKey(bodyPart)) throw new ArgumentException($"Нет детали '{bodyPart}'. Есть: {string.Join(", ", _parts.Keys)}");
            _body = bodyPart;
        }

        public string ModelPath { get; }
        public IEnumerable<string> Parts => _parts.Keys;
        public Mesh MeshOf(string part) => _parts[part].sharedMesh;
        public Material[] MaterialsOf(string part) => PackMaterialsFor(_parts[part].sharedMesh, _parts[part].sharedMaterials);

        public const string PackPrefabs = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Prefabs/";

        /// <summary>
        /// Материалы детали из префабов пака (<c>Prefabs/Hands_*.prefab</c>) — рендерер с тем же мешем. У модели
        /// (<c>.fbx</c>) материалы — заглушки <c>NN - Default</c> без текстур: настоящие пак назначает только в своих
        /// префабах. Не нашлось — <paramref name="fallback" />. Проверка — <c>WeaponMaterialTests</c>.
        /// </summary>
        public static Material[] PackMaterialsFor(Mesh mesh, Material[] fallback)
        {
            if (mesh == null) return fallback;
            string model = AssetDatabase.GetAssetPath(mesh);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PackPrefabs }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var r in renderers)
                {
                    if (r.sharedMesh != mesh) continue;
                    Material[] materials = r.sharedMaterials;
                    if (materials.Length > 0 && materials.All(m => m != null && !IsModelMaterial(m))) return materials;
                }

                // Пак и сам оставил на части деталей заглушку (спуск дробовика, детали Uzi и револьвера) или
                // держит деталь другим мешем (патрон дробовика). Текстура у оружия пака одна на весь ствол —
                // берётся основной материал оружия из префаба той же модели: самый частый не-заглушка, кроме рук.
                if (!renderers.Any(r => r.sharedMesh != null && AssetDatabase.GetAssetPath(r.sharedMesh) == model)) continue;
                Material main = renderers.Where(o => !o.name.StartsWith("Arm") && !o.name.StartsWith("Glove"))
                                         .SelectMany(o => o.sharedMaterials)
                                         .Where(m => m != null && !IsModelMaterial(m))
                                         .GroupBy(m => m).OrderByDescending(g => g.Count()).Select(g => g.Key)
                                         .FirstOrDefault();
                if (main != null) return Enumerable.Repeat(main, Math.Max(1, fallback.Length)).ToArray();
            }
            return fallback;
        }

        /// <summary>Материал встроен в модель (<c>.fbx</c>/<c>.obj</c>) — заглушка, а не материал пака.</summary>
        public static bool IsModelMaterial(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material).ToLowerInvariant();
            return path.EndsWith(".fbx") || path.EndsWith(".obj");
        }

        public void Dispose()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
        }

        /// <summary>Поза клипа в момент <paramref name="time" />; <c>null</c> — bind-pose модели.</summary>
        public void Sample(string clipName, float time = 0f)
        {
            if (clipName == null)
            {
                // Модель заново — bind-pose без следов прошлых клипов.
                var fresh = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                foreach (var src in fresh.GetComponentsInChildren<Transform>(true))
                {
                    Transform dst = Find(_instance.transform, src.name);
                    if (dst == null) continue;
                    dst.SetLocalPositionAndRotation(src.localPosition, src.localRotation);
                    // Масштаб тоже: клипы пака анимируют его (у магазина AX-50 в bind-pose 1.048, в
                    // клипах 1) — без сброса деталь после клипа ставилась со сдвигом в 1.8 мм.
                    dst.localScale = src.localScale;
                }
                return;
            }

            Clip(clipName).SampleAnimation(_instance, time);
        }

        public bool HasClip(string clipName) =>
            clipName != null && AssetDatabase.FindAssets("t:AnimationClip", new[] { _folder })
                                             .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                                             .Any(o => o is AnimationClip c && c.name == clipName);

        public AnimationClip Clip(string clipName)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { _folder }))
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                if (o is AnimationClip c && c.name == clipName) return c;
            throw new ArgumentException($"В {_folder} нет клипа '{clipName}'");
        }

        /// <summary>Место детали в осях корпуса, в единицах меша (масштаб детали 1).</summary>
        public Matrix4x4 PartInBody(string part) => Placement(_body).inverse * Placement(part);

        /// <summary>Ладонь FPS-руки в осях корпуса, единицы меша; ориентация — как у кости пака.</summary>
        public Matrix4x4 PalmInBody(bool right)
        {
            Transform palm = _instance.GetComponentsInChildren<Transform>(true).First(t => t.name.EndsWith(right ? "RArmPalm" : "LArmPalm"));
            Matrix4x4 body = Placement(_body);
            Matrix4x4 m = Unscaled(body).inverse * palm.localToWorldMatrix;
            float s = body.lossyScale.x;
            // Позиция — в единицы меша, поворот без изменений.
            return Matrix4x4.TRS((Vector3)m.GetColumn(3) / s, m.rotation, Vector3.one);
        }

        /// <summary>
        /// Ход и поворот детали относительно корпуса за клип, от кадра <paramref name="restClip" />.
        /// Ось хода — направление первого заметного смещения, в осях корпуса (единицы меша).
        /// </summary>
        public PartMotion Measure(string part, string clipName, string restClip = "Idle")
        {
            // Сначала bind-pose: клип покоя двигает не все кости, и без сброса деталь осталась бы
            // там, где её оставил предыдущий замер (ход получался с обратным знаком).
            Sample(null);
            if (HasClip(restClip)) Sample(restClip); // у части моделей клип покоя зовётся иначе (idle, Idel)
            Matrix4x4 rest = PartInBody(part);
            AnimationClip clip = Clip(clipName);
            var motion = new PartMotion { Part = part, Clip = clipName };

            for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 120f)
            {
                clip.SampleAnimation(_instance, t);
                Matrix4x4 m = PartInBody(part);

                Vector3 d = (Vector3)m.GetColumn(3) - (Vector3)rest.GetColumn(3);
                if (d.magnitude > motion.Far.magnitude) motion.Far = d;
                if (motion.Axis == Vector3.zero && d.magnitude > 0.003f) motion.Axis = d.normalized;
                float travel = motion.Axis == Vector3.zero ? 0f : Vector3.Dot(d, motion.Axis);
                motion.MinTravel = Mathf.Min(motion.MinTravel, travel);
                motion.MaxTravel = Mathf.Max(motion.MaxTravel, travel);

                (m.rotation * Quaternion.Inverse(rest.rotation)).ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                if (Mathf.Abs(angle) > 1f && motion.RotationAxis == Vector3.zero) motion.RotationAxis = axis * Mathf.Sign(angle);
                float signed = motion.RotationAxis == Vector3.zero ? 0f : angle * Mathf.Sign(Vector3.Dot(axis, motion.RotationAxis));
                motion.MinAngle = Mathf.Min(motion.MinAngle, signed);
                motion.MaxAngle = Mathf.Max(motion.MaxAngle, signed);
            }

            return motion;
        }

        /// <summary>
        /// Оси прицеливания в осях корпуса (единицы меша): куда смотрит ствол и где верх, когда
        /// руки пака держат оружие в клипе <paramref name="poseClip" /> (камера FPS смотрит по мировой +Z).
        /// Округлены до осей меша — у разных моделей пака ствол идёт по разным осям (-Y, +Y, +Z).
        /// </summary>
        public (Vector3 forward, Vector3 up) AimAxes(string poseClip)
        {
            Sample(null);
            Sample(poseClip);
            Matrix4x4 toBody = Placement(_body).inverse;
            return (ClosestAxis(toBody.MultiplyVector(Vector3.forward)), ClosestAxis(toBody.MultiplyVector(Vector3.up)));
        }

        private static Vector3 ClosestAxis(Vector3 v)
        {
            var a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        /// <summary>
        /// Строит детали статичными мешами под <paramref name="container" /> в bind-pose.
        /// Корпус встаёт в <paramref name="bodyLocal" />, остальные — точно относительно него.
        /// </summary>
        public Dictionary<string, Transform> BuildParts(Transform container, Matrix4x4 bodyLocal, IDictionary<string, string> partNames)
        {
            Sample(null);
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

        /// <summary>Отчёт по всем деталям и клипам — что движется, насколько.</summary>
        public string Report(string restClip = "Idle")
        {
            var sb = new StringBuilder($"{ModelPath}, корпус {_body}\n");
            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { _folder })
                                     .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                                     .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).Select(c => c.name).Distinct();

            foreach (string clip in clips)
            foreach (string part in _parts.Keys)
            {
                if (part == _body) continue;
                PartMotion m = Measure(part, clip, restClip);
                if (m.Travel > 0.003f || m.MaxAngle - m.MinAngle > 2f) sb.AppendLine("  " + m);
            }

            return sb.ToString();
        }

        private Matrix4x4 Placement(string part)
        {
            SkinnedMeshRenderer r = _parts[part];
            int bone = _mainBone[part];
            return r.bones[bone].localToWorldMatrix * r.sharedMesh.bindposes[bone];
        }

        private static Matrix4x4 Unscaled(Matrix4x4 m) => Matrix4x4.TRS(m.GetColumn(3), m.rotation, Vector3.one);

        private static int MainBone(SkinnedMeshRenderer r)
        {
            var count = new int[r.bones.Length];
            foreach (var w in r.sharedMesh.boneWeights) count[w.boneIndex0]++;
            int best = 0;
            for (int i = 1; i < count.Length; i++)
                if (count[i] > count[best]) best = i;
            return best;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform r = Find(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }

    /// <summary>Движение детали за клип в осях корпуса, единицы меша и градусы.</summary>
    public struct PartMotion
    {
        public string Part;
        public string Clip;
        public Vector3 Axis;
        public Vector3 Far;         // наибольшее смещение от покоя — полный ход, если деталь идёт не по прямой
        public float MinTravel;
        public float MaxTravel;
        public Vector3 RotationAxis;
        public float MinAngle;
        public float MaxAngle;

        public float Travel => MaxTravel - MinTravel;

        public override string ToString() =>
            $"{Clip} / {Part}: ход {MinTravel * 100f:F2}..{MaxTravel * 100f:F2} см по {Axis:F2}, поворот {MinAngle:F0}..{MaxAngle:F0}° вокруг {RotationAxis:F2}";
    }

    /// <summary>
    /// Перевод ладони FPS-руки пака в точку хвата UltimateXR для конкретного аватара.
    ///
    /// <para>
    /// Калибровка — смещение точки хвата от ладони пака на оружии, у которого хват настроен
    /// вручную. Проверено: калибровка с <c>Gun_real</c> предсказывает правую руку M16 с ошибкой
    /// 1.1 см и ~20°. Для длинноствольного оружия брать калибровку с M16.
    /// </para>
    /// </summary>
    public static class GripCalibration
    {
        /// <summary>Смещение точки хвата от ладони, в мировых метрах (жёсткое).</summary>
        /// <param name="bodyMesh">Трансформ корпуса в эталонном префабе (тот же меш, что корпус пака).</param>
        public static Matrix4x4 Measure(HandsPackWeapon pack, string poseClip, bool right, Transform bodyMesh, Transform snap)
        {
            pack.Sample(poseClip);
            Matrix4x4 palm = Rigid(bodyMesh.localToWorldMatrix * pack.PalmInBody(right));
            return palm.inverse * Rigid(snap.localToWorldMatrix);
        }

        /// <summary>Мировое место точки хвата на новом оружии.</summary>
        public static Matrix4x4 Apply(HandsPackWeapon pack, string poseClip, bool right, Transform bodyMesh, Matrix4x4 calibration)
        {
            pack.Sample(poseClip);
            return Rigid(bodyMesh.localToWorldMatrix * pack.PalmInBody(right)) * calibration;
        }

        public static Matrix4x4 Rigid(Matrix4x4 m) => Matrix4x4.TRS(m.GetColumn(3), m.rotation, Vector3.one);
    }

    public static class HandsPackWeaponMenu
    {
        [MenuItem("Tools/VR Battlegrounds/Gameplay/Hands Pack Weapon Report")]
        private static void ReportAll()
        {
            var bodies = new Dictionary<string, string>
            {
                { "Hands_Shotgun", "Shogun_Base_mesh" },
                { "Hands_Gun", "Base_mesh" },
                { "Hands_Automatic_Rifle03", "Rifle_Body_Mesh" },
                { "Hands_Automatic_Rifle01", "Scar_Base_mesh" },
                { "Hands_Tommy_gun", "body_Mesh" },
                { "Hands_Automatic_Rifle04", "Rifle04_Body_Mesh" },
                { "Hands_Gun02", "Gun02_Body_Mesh" },
                { "Hands_Gun_03", "Gun_03_Body_Mesh" },
                { "Hands_Sniper_Rifle", "Sniper_Rifle_Base_Mesh" }
            };

            foreach (var pair in bodies)
            {
                using var pack = new HandsPackWeapon(pair.Key, pair.Value);
                GameLog.Debug.Info(pack.Report());
            }
        }
    }
}
