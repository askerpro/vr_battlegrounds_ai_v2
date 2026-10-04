using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UltimateXR.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Editor.Avatars;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Геометрия и механика оружия из пака KINEMATION Tactical Shooter (T-39) — вычислением, как
    /// <see cref="HandsPackWeapon" /> для пака Hands.
    ///
    /// <para>
    /// Оружие пака — префаб <c>Prefabs/Weapons/W_*.prefab</c>: корпус — <b>один</b>
    /// <see cref="SkinnedMeshRenderer" />, детали (затвор, спуск, предохранитель) — кости внутри него с жёсткими
    /// весами; магазин — отдельный рендерер с костями патронов, подавателя и пружины. Деталь здесь — кость:
    /// вершины, у которых она главная, вырезаются в отдельный статичный меш в осях кости (без масштаба),
    /// и место детали — место кости (<see cref="PartInBody" />). Лишнее (патроны магазина, кроме верхнего,
    /// пружина) просто не собирается — рецепт называет только нужные детали.
    /// </para>
    ///
    /// <para>
    /// Масштаб пака реальный (1 ед. = 1 м), ствол по +Z корня. Меши деталей и материалы URP Lit
    /// (<see cref="KinemationMaterials" />) сохраняются в <c>Assets/Art/Weapons/Kinemation/&lt;имя&gt;/</c>; повторный
    /// запуск перезаписывает их на месте (GUID сохраняются).
    /// </para>
    ///
    /// <para>
    /// Клипы: оружие — <c>Animations/&lt;папка&gt;/Weapon/A_W_*</c> (ход затвора и спуска), руки —
    /// <c>Animations/&lt;папка&gt;/Character/A_FP_*_Idle</c> (<see cref="KinemationPoseExtractor" />).
    /// </para>
    /// </summary>
    public sealed class KinemationWeapon : IWeaponModel, IHandGripSource, IDisposable
    {
        public const string Pack = "Assets/ThirdParty/KINEMATION/TacticalShooterPack/";
        public const string ArtRoot = "Assets/Art/Weapons/Kinemation";

        private readonly GameObject _prefab;
        private readonly GameObject _instance;
        private readonly Animator _animator;
        private readonly string _animFolder;
        private readonly string _poseClip;
        private readonly string _body;
        private readonly string _assetFolder;
        private readonly string _restClip;
        private readonly Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> _rest = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
        private readonly Dictionary<string, Part> _parts = new Dictionary<string, Part>();
        private readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();

        private sealed class Part
        {
            public string Name;
            public Transform Bone;
            public Renderer Renderer;
            public int BoneIndex;
            public int Vertices;
            public bool Static;   // обвес пака: отдельный MeshRenderer (глушитель, коллиматор), а не кость скина
        }

        /// <param name="prefab">Префаб пака: <c>W_Oryx_SRM-12</c></param>
        /// <param name="animFolder">Папка клипов: <c>SRM-12</c></param>
        /// <param name="poseClip">Клип персонажа с руками на оружии: <c>A_FP_SRM-12_Idle_Pose</c></param>
        /// <param name="assetName">Имя папки ассетов проекта: <c>SRM12</c></param>
        /// <param name="bodyBone">Кость корпуса</param>
        /// <param name="restClip">
        /// Клип покоя оружия (<c>A_W_*_Idle</c>): в позе префаба пака у части стволов магазин не вставлен — гнездо
        /// ставит на место клип покоя (у AK105, MKR9, Viper магазин висел под стволом). <c>null</c> — поза префаба.
        /// </param>
        /// <param name="attachments">
        /// Обвесы пака — статичные <see cref="MeshRenderer" /> внутри префаба (<c>SM_Attach_AR15_Silencer</c>): деталь с
        /// таким именем — весь меш обвеса в осях его трансформа. Только названные: у префабов пака есть и обвесы, которые
        /// не берутся (рукоять, голограмма прицела).
        /// </param>
        public KinemationWeapon(string prefab, string animFolder, string poseClip, string assetName, string bodyBone = "Body", string restClip = null,
                                IEnumerable<string> attachments = null)
        {
            _restClip = restClip;
            _prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}Prefabs/Weapons/{prefab}.prefab")
                      ?? throw new ArgumentException($"Нет префаба пака {prefab}");
            _animFolder = animFolder;
            _poseClip = poseClip;
            _assetFolder = $"{ArtRoot}/{assetName}";

            EnsureReadable(_prefab, attachments);
            _instance = Object.Instantiate(_prefab);
            _instance.hideFlags = HideFlags.HideAndDontSave;
            _animator = _instance.GetComponentInChildren<Animator>(true);
            foreach (Transform t in _instance.GetComponentsInChildren<Transform>(true))
                _rest[t] = (t.localPosition, t.localRotation, t.localScale);

            foreach (SkinnedMeshRenderer r in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null) continue;
                BoneWeight[] weights = r.sharedMesh.boneWeights;
                var count = new int[r.bones.Length];
                foreach (BoneWeight w in weights) count[w.boneIndex0]++;
                for (int b = 0; b < count.Length; b++)
                {
                    if (count[b] == 0 || r.bones[b] == null) continue;
                    string name = r.bones[b].name;
                    if (_parts.TryGetValue(name, out Part other) && other.Bone != r.bones[b]) name = $"{r.name}.{name}";
                    _parts[name] = new Part { Name = name, Bone = r.bones[b], Renderer = r, BoneIndex = b, Vertices = count[b] };
                }
            }

            foreach (string attachment in attachments ?? Enumerable.Empty<string>())
            {
                MeshFilter filter = _instance.GetComponentsInChildren<MeshFilter>(true)
                                             .FirstOrDefault(f => f.name == attachment && f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null)
                                    ?? throw new ArgumentException($"{prefab}: нет обвеса '{attachment}' (MeshFilter + MeshRenderer)");
                _parts[attachment] = new Part
                {
                    Name = attachment, Bone = filter.transform, Renderer = filter.GetComponent<MeshRenderer>(), BoneIndex = -1,
                    Vertices = filter.sharedMesh.vertexCount, Static = true
                };
            }

            if (!_parts.ContainsKey(bodyBone)) throw new ArgumentException($"{prefab}: нет детали-корпуса '{bodyBone}'. Есть: {string.Join(", ", _parts.Keys)}");
            _body = bodyBone;
        }

        public GameObject PackPrefab => _prefab;
        public string Body => _body;

        /// <summary>Детали: кости, у которых есть свои вершины.</summary>
        public IEnumerable<string> Parts => _parts.Keys;

        public void Dispose()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
        }

        // ── Геометрия ───────────────────────────────────────────────────────────

        /// <summary>
        /// Меш детали: вершины, у которых кость детали главная, в её осях без масштаба (метры). Треугольник
        /// уходит детали большинства своих вершин. Подмеши — по материалам рендерера, пустые выбрасываются.
        /// </summary>
        public Mesh MeshOf(string part)
        {
            if (_meshes.TryGetValue(part, out Mesh cached)) return cached;
            Part p = Get(part);
            Sample(null);

            // Обвес — весь свой меш в осях своего трансформа (с его масштабом); деталь скина — вершины кости × bindpose.
            Mesh src = WithCpuData(p.Static ? p.Bone.GetComponent<MeshFilter>().sharedMesh : ((SkinnedMeshRenderer)p.Renderer).sharedMesh);
            BoneWeight[] weights = p.Static ? null : src.boneWeights;
            Matrix4x4 toPart = Rigid(p.Bone.localToWorldMatrix).inverse * p.Bone.localToWorldMatrix * (p.Static ? Matrix4x4.identity : src.bindposes[p.BoneIndex]);
            Matrix4x4 toPartNormal = toPart.inverse.transpose;

            Vector3[] vertices = src.vertices;
            Vector3[] normals = src.normals;
            Vector4[] tangents = src.tangents;
            Vector2[] uv = src.uv;

            var map = new Dictionary<int, int>();
            var outV = new List<Vector3>();
            var outN = new List<Vector3>();
            var outT = new List<Vector4>();
            var outUv = new List<Vector2>();
            var subs = new List<(int material, List<int> indices)>();

            for (int s = 0; s < src.subMeshCount; s++)
            {
                int[] tris = src.GetTriangles(s);
                var indices = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    if (!p.Static && Majority(weights[a].boneIndex0, weights[b].boneIndex0, weights[c].boneIndex0) != p.BoneIndex) continue;

                    foreach (int v in new[] { a, b, c })
                    {
                        if (!map.TryGetValue(v, out int nv))
                        {
                            nv = outV.Count;
                            map[v] = nv;
                            outV.Add(toPart.MultiplyPoint3x4(vertices[v]));
                            if (normals.Length > 0) outN.Add(toPartNormal.MultiplyVector(normals[v]).normalized);
                            if (tangents.Length > 0)
                            {
                                Vector3 t = toPart.MultiplyVector(tangents[v]).normalized;
                                outT.Add(new Vector4(t.x, t.y, t.z, tangents[v].w));
                            }
                            if (uv.Length > 0) outUv.Add(uv[v]);
                        }
                        indices.Add(nv);
                    }
                }
                if (indices.Count > 0) subs.Add((s, indices));
            }

            if (outV.Count == 0) throw new InvalidOperationException($"{_prefab.name}/{part}: у детали нет треугольников (меш {src.name}: вершин {vertices.Length}, весов {weights?.Length ?? 0}, подмешей {src.subMeshCount}, треугольников 0-го {src.GetIndexCount(0) / 3}, кость {p.BoneIndex}, isReadable {src.isReadable})");

            var mesh = new Mesh { name = $"{Path.GetFileName(_assetFolder)}_{Clean(part)}" };
            mesh.indexFormat = outV.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(outV);
            if (outN.Count == outV.Count) mesh.SetNormals(outN);
            if (outT.Count == outV.Count) mesh.SetTangents(outT);
            if (outUv.Count == outV.Count) mesh.SetUVs(0, outUv);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i].indices, i);
            mesh.RecalculateBounds();

            Mesh saved = SaveMesh(mesh, part);
            _subMaterials[part] = subs.Select(x => x.material).ToArray();
            _meshes[part] = saved;
            return saved;
        }

        private readonly Dictionary<string, int[]> _subMaterials = new Dictionary<string, int[]>();

        /// <summary>Материалы URP Lit, сделанные из материалов пака (<see cref="KinemationMaterials" />), — по подмешам детали.</summary>
        public Material[] MaterialsOf(string part)
        {
            MeshOf(part);
            Part p = Get(part);
            Material[] src = p.Renderer.sharedMaterials;
            Material fallback = src.FirstOrDefault(m => m != null && !HandsPackWeapon.IsModelMaterial(m));
            return _subMaterials[part].Select(i =>
            {
                Material m = i < src.Length ? src[i] : null;
                // Заглушка из FBX (у магазина Mk14 — на 1,4k треугольников) — основной материал рендерера.
                if (m == null || HandsPackWeapon.IsModelMaterial(m)) m = fallback;
                return KinemationMaterials.Convert(m, $"{ArtRoot}/Materials");
            }).ToArray();
        }

        public Matrix4x4 PartInBody(string part) => Rigid(Get(_body).Bone.localToWorldMatrix).inverse * Rigid(Get(part).Bone.localToWorldMatrix);

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
                go.transform.localScale = Vector3.one;
                go.AddComponent<MeshFilter>().sharedMesh = MeshOf(pair.Key);
                go.AddComponent<MeshRenderer>().sharedMaterials = MaterialsOf(pair.Key);
                result[pair.Key] = go.transform;
            }
            return result;
        }

        // ── Механика ────────────────────────────────────────────────────────────

        /// <summary>Клип оружия (<c>Animations/&lt;папка&gt;/Weapon</c>) на аниматоре оружия; <c>null</c> — поза префаба пака.</summary>
        public void Sample(string clipName, float time = 0f)
        {
            if (clipName == null)
            {
                foreach (var pair in _rest)
                {
                    pair.Key.SetLocalPositionAndRotation(pair.Value.p, pair.Value.r);
                    pair.Key.localScale = pair.Value.s;
                }
                if (_restClip != null) Clip(_restClip).SampleAnimation(_animator.gameObject, 0f);
                SampleFullMagazines(_instance, _animator);
                return;
            }

            Clip(clipName).SampleAnimation(_animator.gameObject, time);
        }

        /// <summary>
        /// Магазин — полный: у магазинов пака свой аниматор (<c>MagAnimator</c>), его первый клип по «расходу патронов»
        /// в кадре 0 ставит корпус магазина, патроны и подаватель на места. В позе префаба корпус магазина AK105
        /// сидит внутри ствольной коробки, у MKR9 и Viper — висит под углом.
        /// </summary>
        public static void SampleFullMagazines(GameObject weapon, Animator main)
        {
            foreach (Animator a in weapon.GetComponentsInChildren<Animator>(true))
            {
                if (a == main || a.runtimeAnimatorController == null) continue;
                AnimationClip[] clips = a.runtimeAnimatorController.animationClips;
                if (clips.Length > 0) clips[0].SampleAnimation(a.gameObject, 0f);
            }
        }

        public bool HasClip(string clipName) => clipName != null && WeaponClips().Any(c => c.name == clipName);

        public AnimationClip Clip(string clipName)
        {
            AnimationClip[] candidates = WeaponClips().Where(c => c.name == clipName).Distinct().ToArray();
            if (candidates.Length == 1) return candidates[0];
            // OverrideController пакета использует короткий .anim, а FBX содержит одноимённый take.
            if (clipName == "A_W_Mk14EBR_Fire")
                return candidates.Single(c => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c)) == "d375975b289f01247a7b0f78bc018eb9");
            AnimationClip[] referenced = _animator.runtimeAnimatorController == null ? Array.Empty<AnimationClip>() :
                _animator.runtimeAnimatorController.animationClips.Where(c => c.name == clipName && candidates.Contains(c)).Distinct().ToArray();
            if (referenced.Length == 1) return referenced[0];
            throw new ArgumentException($"{_animFolder}: клип '{clipName}' не найден или неоднозначен ({candidates.Length} источников)");
        }

        public (string path, int boneIndex) PartIdentity(string part)
        {
            Part value = Get(part);
            return (AnimationUtility.CalculateTransformPath(value.Bone, _instance.transform), value.BoneIndex);
        }

        private IEnumerable<AnimationClip> WeaponClips() =>
            AssetDatabase.FindAssets("t:AnimationClip", new[] { $"{Pack}Animations/{_animFolder}/Weapon" })
                         .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                         .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview"));

        /// <summary>Ход и поворот детали за клип от позы покоя (клип покоя модели или <paramref name="restClip" />, если он есть).</summary>
        public PartMotion Measure(string part, string clipName, string restClip = null)
        {
            Sample(null);
            if (HasClip(restClip)) Sample(restClip);
            Matrix4x4 rest = PartInBody(part);
            AnimationClip clip = Clip(clipName);
            var motion = new PartMotion { Part = part, Clip = clipName };

            for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 120f)
            {
                clip.SampleAnimation(_animator.gameObject, t);
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

            Sample(null);
            return motion;
        }

        /// <summary>
        /// Время первого и последнего кадра клипа, где деталь отошла от покоя дальше <paramref name="threshold" />
        /// (метры): по ним режутся звуки перезарядки (<see cref="KinemationAudio" />).
        /// </summary>
        public (float leave, float back) Excursion(string part, string clipName, float threshold = 0.01f)
        {
            Sample(null);
            Matrix4x4 rest = PartInBody(part);
            AnimationClip clip = Clip(clipName);
            float leave = -1f, back = -1f;
            for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 120f)
            {
                clip.SampleAnimation(_animator.gameObject, t);
                float d = Vector3.Distance(PartInBody(part).GetColumn(3), rest.GetColumn(3));
                if (d > threshold)
                {
                    if (leave < 0f) leave = t;
                    back = t;
                }
            }
            Sample(null);
            return (leave, back);
        }

        /// <summary>Ствол пака смотрит по +Z корня префаба, верх — +Y: оси прицеливания — они, в осях корпуса.</summary>
        public (Vector3 forward, Vector3 up) AimAxes(string poseClip)
        {
            Sample(null);
            Quaternion body = Quaternion.Inverse(Get(_body).Bone.rotation) * _instance.transform.rotation;
            return (ClosestAxis(body * Vector3.forward), ClosestAxis(body * Vector3.up));
        }

        // ── Руки ────────────────────────────────────────────────────────────────

        public HandsPackPoseExtractor.Sample GripSample(UxrHandSide side) =>
            KinemationPoseExtractor.Extract(PoseClip(), 0f, side, _prefab, _body, _restClip != null ? Clip(_restClip) : null);

        public AnimationClip PoseClip() =>
            AssetDatabase.FindAssets("t:AnimationClip", new[] { $"{Pack}Animations/{_animFolder}/Character" })
                         .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                         .OfType<AnimationClip>().FirstOrDefault(c => c.name == _poseClip)
            ?? throw new ArgumentException($"В {Pack}Animations/{_animFolder}/Character нет клипа '{_poseClip}'");

        // ── Отчёт ───────────────────────────────────────────────────────────────

        /// <summary>Детали (вершины, треугольники, место) и что двигается в клипах оружия.</summary>
        public string Report()
        {
            var sb = new StringBuilder($"{_prefab.name}, корпус {_body}\n");
            Sample(null);
            foreach (Part p in _parts.Values)
            {
                Vector3 at = PartInBody(p.Name).GetColumn(3);
                sb.AppendLine($"  {p.Name} [{p.Renderer.name}]: вершин {p.Vertices}, в корпусе {at * 100f:F1} см");
            }
            foreach (AnimationClip clip in WeaponClips())
            foreach (string part in _parts.Keys)
            {
                if (part == _body) continue;
                PartMotion m = Measure(part, clip.name);
                if (m.Travel > 0.003f || m.MaxAngle - m.MinAngle > 2f) sb.AppendLine("  " + m);
            }
            return sb.ToString();
        }

        // ── Вспомогательное ─────────────────────────────────────────────────────

        private Part Get(string part) =>
            _parts.TryGetValue(part, out Part p) ? p : throw new ArgumentException($"{_prefab.name}: нет детали '{part}'. Есть: {string.Join(", ", _parts.Keys)}");

        private Mesh SaveMesh(Mesh mesh, string part)
        {
            string folder = $"{_assetFolder}/Meshes";
            EnsureFolder(folder);
            string path = $"{folder}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            // Перезапись на месте: GUID и ссылки префабов сохраняются.
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = mesh.name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        public static string Clean(string part) => part.Replace("SKM_", "").Replace(".", "_").Replace("-", "").Replace(" ", "");

        /// <summary>
        /// Меш пака с данными на CPU: без Read/Write Unity после импорта других ассетов (WAV, текстуры) отдаёт у
        /// загруженного меша пустой <c>vertices</c> при целых весах и индексах, и деталь выходила без треугольников
        /// (AK105, MKR9, Mk14 — через раз). Модели оружия пака получают Read/Write (<see cref="EnsureReadable" />): в сборку
        /// они не попадают — префабы ссылаются на вырезанные меши проекта.
        /// </summary>
        private static Mesh WithCpuData(Mesh mesh)
        {
            if (mesh.vertexCount == 0 || mesh.vertices.Length == mesh.vertexCount) return mesh;
            throw new InvalidOperationException($"{AssetDatabase.GetAssetPath(mesh)}/{mesh.name}: вершины меша недоступны на CPU — модель без Read/Write");
        }

        /// <summary>Read/Write у моделей, на которые ссылаются рендереры префаба пака (правка только <c>.meta</c> пака).</summary>
        public static void EnsureReadable(GameObject prefab, IEnumerable<string> attachments = null)
        {
            var selected = new HashSet<string>(attachments ?? Enumerable.Empty<string>());
            var meshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh)
                               .Concat(prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => selected.Contains(f.name)).Select(f => f.sharedMesh));
            foreach (Mesh mesh in meshes)
            {
                if (mesh == null) continue;
                if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) is ModelImporter importer && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }
        }

        private static int Majority(int a, int b, int c) => a == b || a == c ? a : b == c ? b : a;

        private static Matrix4x4 Rigid(Matrix4x4 m) => Matrix4x4.TRS(m.GetColumn(3), m.rotation, Vector3.one);

        private static Vector3 ClosestAxis(Vector3 v)
        {
            var a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }
    }
}
