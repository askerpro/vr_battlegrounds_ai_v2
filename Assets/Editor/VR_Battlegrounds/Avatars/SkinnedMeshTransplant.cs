using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltimateXR.Avatar;
using UltimateXR.Networking.Integrations.Net.Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Пересадка скин-мешей из модели (FBX) на готовый игровой аватар с тем же скелетом —
    /// например, оптимизированной в Blender геометрии на вариант <c>MEF_Base_Avatar</c>.
    ///
    /// <para>
    /// <b>Что делает с префабом</b> (через <c>LoadPrefabContents</c>, одна запись на диск):
    /// <list type="number">
    ///   <item>удаляет прежние <c>SkinnedMeshRenderer</c> под <c>Geo</c> и объекты из списка
    ///   <see cref="Settings.ExtraObjectsToRemove" /> (у варианта это «removed GameObject»-override —
    ///   меши и материалы базы в сборку этого аватара не попадут);</item>
    ///   <item>кладёт под <c>Geo</c> по объекту на каждый скин модели: <c>sharedMesh</c> и
    ///   <c>localBounds</c> — из модели, <c>bones[]</c> — кости аватара с теми же именами,
    ///   <c>rootBone</c> — одноимённый модели, материал — <see cref="Settings.Material" />, прочие
    ///   настройки рендерера — как у удалённого скина;</item>
    ///   <item>меши с суффиксом <c>_LOD{n}</c> собирает в <c>LODGroup</c> на родителе <c>Geo</c>
    ///   (объект рига), пороги — <see cref="Settings.LodTransitions" />;</item>
    ///   <item><c>UxrMirrorAvatar._localDisabledGameObjects</c>: удалённые выпадают, добавляются новые
    ///   меши головы (≥ 80 % вершин главной костью — голова или её потомок, правило
    ///   <c>/setup-avatar</c>, раздел 3а);</item>
    ///   <item><c>UxrAvatar._avatarRenderers</c>: удалённые выпадают, новые добавляются.</item>
    /// </list>
    /// Кость, которой нет в аватаре, — отказ без записи: скин с пустой костью рвёт меш.
    /// </para>
    ///
    /// <para>
    /// Скелет модели обязан совпадать со скелетом аватара по именам и позе привязки: bindposes
    /// берутся из модели как есть. Проверка — <c>OptimizedMefAvatarTests</c>.
    /// </para>
    /// </summary>
    public sealed class SkinnedMeshTransplant : EditorWindow
    {
        /// <summary>Параметры пересадки.</summary>
        public sealed class Settings
        {
            /// <summary>Путь префаба аватара, в который пересаживаются меши.</summary>
            public string TargetPrefabPath;

            /// <summary>Модель (FBX) с новыми скинами.</summary>
            public GameObject SourceModel;

            /// <summary>Материал всех новых скинов (один слот).</summary>
            public Material Material;

            /// <summary>Имя объекта-контейнера мешей в риге аватара.</summary>
            public string GeoName = "Geo";

            /// <summary>Объекты аватара, которые тоже удалить (геометрия, вошедшая в новые меши).</summary>
            public string[] ExtraObjectsToRemove = Array.Empty<string>();

            /// <summary>Нижние границы высоты на экране для LOD0, LOD1, …; ниже последней — отсечение.</summary>
            public float[] LodTransitions = { 0.25f, 0.08f, 0.01f };

            /// <summary>Доля вершин на голове, с которой меш считается мешем головы.</summary>
            public float HeadVertexShare = 0.8f;
        }

        private const string LodSuffix = "_LOD";

        private GameObject _target;
        private GameObject _source;
        private Material _material;
        private string _extra = "handgunHolster_geo, strap_geo";

        [MenuItem("Tools/VR Battlegrounds/Avatars/Transplant Skinned Meshes...")]
        private static void Open()
        {
            GetWindow<SkinnedMeshTransplant>("Пересадка скинов");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Заменяет скины под Geo префаба аватара мешами модели с тем же скелетом. " +
                                    "Кости ищутся по имени; LOD по суффиксу _LOD{n}.", MessageType.Info);
            _target   = (GameObject)EditorGUILayout.ObjectField("Префаб аватара", _target, typeof(GameObject), false);
            _source   = (GameObject)EditorGUILayout.ObjectField("Модель (FBX)", _source, typeof(GameObject), false);
            _material = (Material)EditorGUILayout.ObjectField("Материал", _material, typeof(Material), false);
            _extra    = EditorGUILayout.TextField("Удалить также", _extra);

            using (new EditorGUI.DisabledScope(_target == null || _source == null || _material == null))
            {
                if (GUILayout.Button("Пересадить"))
                {
                    var settings = new Settings
                    {
                        TargetPrefabPath     = AssetDatabase.GetAssetPath(_target),
                        SourceModel          = _source,
                        Material             = _material,
                        ExtraObjectsToRemove = _extra.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray()
                    };
                    Run(settings);
                }
            }
        }

        /// <summary>Выполняет пересадку. Возвращает отчёт; при отказе префаб не меняется.</summary>
        public static string Run(Settings settings)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(settings.TargetPrefabPath);
            try
            {
                string report = Transplant(root, settings, out bool ok);
                if (ok)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, settings.TargetPrefabPath);
                    GameLog.Player.Info($"[SkinnedMeshTransplant] {settings.TargetPrefabPath}: {report}");
                }
                else
                {
                    GameLog.Error($"[SkinnedMeshTransplant] {settings.TargetPrefabPath}: отказ, префаб не изменён. {report}");
                }

                return (ok ? "OK. " : "ОТКАЗ. ") + report;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string Transplant(GameObject root, Settings settings, out bool ok)
        {
            var report = new StringBuilder();
            ok = false;

            Transform geo = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == settings.GeoName);
            if (geo == null)
            {
                report.Append($"нет объекта '{settings.GeoName}'.");
                return report.ToString();
            }

            Transform rig = geo.parent != null ? geo.parent : root.transform;
            SkinnedMeshRenderer[] sources = settings.SourceModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            // Кости аватара по имени. Дубли имён — неоднозначность, такой скелет не пересаживается.
            var bonesByName = new Dictionary<string, Transform>();
            var duplicates = new HashSet<string>();
            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (bonesByName.ContainsKey(t.name)) duplicates.Add(t.name);
                else bonesByName[t.name] = t;
            }

            var missing = new SortedSet<string>();
            var ambiguous = new SortedSet<string>();
            foreach (SkinnedMeshRenderer source in sources)
            {
                foreach (Transform bone in source.bones.Append(source.rootBone))
                {
                    if (bone == null) continue;
                    if (!bonesByName.ContainsKey(bone.name)) missing.Add(bone.name);
                    else if (duplicates.Contains(bone.name)) ambiguous.Add(bone.name);
                }
            }

            if (sources.Length == 0) report.Append("в модели нет скинов. ");
            if (missing.Count > 0) report.Append($"костей нет в аватаре ({missing.Count}): {string.Join(", ", missing)}. ");
            if (ambiguous.Count > 0) report.Append($"имя кости встречается дважды ({ambiguous.Count}): {string.Join(", ", ambiguous)}. ");
            if (report.Length > 0) return report.ToString();

            // Шаблон настроек рендерера — первый прежний скин.
            SkinnedMeshRenderer template = geo.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();

            // 1. Удаление прежней геометрии.
            var removed = new List<GameObject>();
            foreach (SkinnedMeshRenderer old in geo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sources.Any(s => s.name == old.name)) continue; // повторный прогон — пересоздадим ниже
                removed.Add(old.gameObject);
            }

            foreach (string name in settings.ExtraObjectsToRemove)
            {
                removed.AddRange(root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).Select(t => t.gameObject));
            }

            // Повторный прогон: свои прежние объекты тоже пересоздаются.
            foreach (SkinnedMeshRenderer source in sources)
            {
                Transform previous = geo.Find(source.name);
                if (previous != null) removed.Add(previous.gameObject);
            }

            RendererSettings snapshot = RendererSettings.From(template);
            int removedCount = removed.Distinct().Count();
            foreach (GameObject go in removed.Distinct()) UnityEngine.Object.DestroyImmediate(go);

            // 2. Новые скины.
            var created = new List<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer source in sources)
            {
                var go = new GameObject(source.name);
                go.layer = geo.gameObject.layer;
                go.transform.SetParent(geo, false);

                var skin = go.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh      = source.sharedMesh;
                skin.bones           = source.bones.Select(b => b == null ? null : bonesByName[b.name]).ToArray();
                skin.rootBone        = source.rootBone != null ? bonesByName[source.rootBone.name] : null;
                skin.localBounds     = source.localBounds;
                skin.sharedMaterials = Enumerable.Repeat(settings.Material, Math.Max(1, source.sharedMesh.subMeshCount)).ToArray();
                snapshot.ApplyTo(skin);
                created.Add(skin);
            }

            report.Append($"удалено объектов {removedCount}, создано скинов {created.Count}, " +
                          $"треугольников {string.Join(" / ", created.Select(s => $"{s.name} {s.sharedMesh.triangles.Length / 3}"))}. ");

            // 3. LODGroup.
            report.Append(SetupLods(rig, created, settings.LodTransitions));

            // 4. Своя голова.
            UxrAvatar avatar = root.GetComponent<UxrAvatar>();
            Transform head = avatar != null && avatar.AvatarRig != null ? avatar.AvatarRig.Head.Head : null;
            List<SkinnedMeshRenderer> heads = head != null ? created.Where(s => HeadShare(s, head) >= settings.HeadVertexShare).ToList() : new List<SkinnedMeshRenderer>();

            UxrMirrorAvatar net = root.GetComponent<UxrMirrorAvatar>();
            if (net != null)
            {
                var so = new SerializedObject(net);
                SerializedProperty list = so.FindProperty("_localDisabledGameObjects");
                List<GameObject> kept = Enumerate(list).OfType<GameObject>().Where(g => g != null).ToList();
                Assign(list, kept.Concat(heads.Select(h => h.gameObject)).Distinct().Cast<UnityEngine.Object>().ToList());
                so.ApplyModifiedPropertiesWithoutUndo();
                report.Append($"своя голова: {string.Join(", ", heads.Select(h => h.name))}. ");
            }

            // 5. Рендереры UxrAvatar.
            if (avatar != null)
            {
                var so = new SerializedObject(avatar);
                SerializedProperty list = so.FindProperty("_avatarRenderers");
                List<Renderer> kept = Enumerate(list).OfType<Renderer>().Where(r => r != null).ToList();
                Assign(list, kept.Concat(created).Distinct().Cast<UnityEngine.Object>().ToList());
                so.ApplyModifiedPropertiesWithoutUndo();
                report.Append($"рендереров UxrAvatar: {list.arraySize}.");
            }

            ok = true;
            return report.ToString();
        }

        private static string SetupLods(Transform rig, List<SkinnedMeshRenderer> created, float[] transitions)
        {
            var byLevel = new SortedDictionary<int, List<Renderer>>();
            foreach (SkinnedMeshRenderer skin in created)
            {
                int at = skin.name.LastIndexOf(LodSuffix, StringComparison.Ordinal);
                if (at < 0 || !int.TryParse(skin.name.Substring(at + LodSuffix.Length), out int level)) continue;
                if (!byLevel.TryGetValue(level, out List<Renderer> list)) byLevel[level] = list = new List<Renderer>();
                list.Add(skin);
            }

            LODGroup group = rig.GetComponent<LODGroup>();
            if (byLevel.Count == 0)
            {
                if (group != null) UnityEngine.Object.DestroyImmediate(group);
                return "LOD нет. ";
            }

            if (byLevel.Count > transitions.Length) return $"уровней LOD {byLevel.Count}, порогов {transitions.Length} — LODGroup не создан. ";

            if (group == null) group = rig.gameObject.AddComponent<LODGroup>();
            LOD[] lods = byLevel.Values.Select((renderers, i) => new LOD(transitions[i], renderers.ToArray())).ToArray();
            group.SetLODs(lods);
            group.RecalculateBounds();
            return $"LODGroup на '{rig.name}': {lods.Length} уровня ({string.Join(" / ", lods.Select(l => $"{l.screenRelativeTransitionHeight:P0}"))}). ";
        }

        /// <summary>Доля вершин, у которых главная кость — голова или её потомок.</summary>
        private static float HeadShare(SkinnedMeshRenderer skin, Transform head)
        {
            BoneWeight[] weights = skin.sharedMesh.boneWeights;
            if (weights.Length == 0) return 0f;

            bool[] isHead = skin.bones.Select(b => b != null && b.IsChildOf(head)).ToArray();
            int count = weights.Count(w => w.boneIndex0 < isHead.Length && isHead[w.boneIndex0]);
            return (float)count / weights.Length;
        }

        private static IEnumerable<UnityEngine.Object> Enumerate(SerializedProperty list)
        {
            for (int i = 0; i < list.arraySize; i++) yield return list.GetArrayElementAtIndex(i).objectReferenceValue;
        }

        private static void Assign(SerializedProperty list, List<UnityEngine.Object> values)
        {
            list.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>Настройки рендерера, переносимые с прежнего скина на новые.</summary>
        private readonly struct RendererSettings
        {
            private readonly bool _valid;
            private readonly UnityEngine.Rendering.ShadowCastingMode _shadows;
            private readonly bool _receiveShadows;
            private readonly SkinQuality _quality;
            private readonly bool _updateWhenOffscreen;
            private readonly bool _motionVectors;
            private readonly UnityEngine.Rendering.LightProbeUsage _lightProbes;
            private readonly UnityEngine.Rendering.ReflectionProbeUsage _reflectionProbes;
            private readonly Transform _probeAnchor;

            private RendererSettings(SkinnedMeshRenderer template)
            {
                _valid               = true;
                _shadows             = template.shadowCastingMode;
                _receiveShadows      = template.receiveShadows;
                _quality             = template.quality;
                _updateWhenOffscreen = template.updateWhenOffscreen;
                _motionVectors       = template.skinnedMotionVectors;
                _lightProbes         = template.lightProbeUsage;
                _reflectionProbes    = template.reflectionProbeUsage;
                _probeAnchor         = template.probeAnchor;
            }

            public static RendererSettings From(SkinnedMeshRenderer template) =>
                template != null ? new RendererSettings(template) : default;

            public void ApplyTo(SkinnedMeshRenderer skin)
            {
                if (!_valid) return;
                skin.shadowCastingMode    = _shadows;
                skin.receiveShadows       = _receiveShadows;
                skin.quality              = _quality;
                skin.updateWhenOffscreen  = _updateWhenOffscreen;
                skin.skinnedMotionVectors = _motionVectors;
                skin.lightProbeUsage      = _lightProbes;
                skin.reflectionProbeUsage = _reflectionProbes;
                // Якорь проб — объект, который мог быть удалён вместе с прежним скином.
                if (_probeAnchor != null) skin.probeAnchor = _probeAnchor;
            }
        }
    }
}
