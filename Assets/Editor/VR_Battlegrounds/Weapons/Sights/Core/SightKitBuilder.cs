using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Единственный writer префабов комплектов прицелов <c>SightKit_*</c> (механика и коллиматоры, <see cref="Optics"/>)
    /// и импорта FBX механических комплектов.
    ///
    /// <para>
    /// Комплект — один объект на планке: корень → <c>Rear</c> (основание целика) → деталь целика и <c>Front</c>
    /// (основание мушки) → деталь мушки. Pivot детали стоит ровно на точке прицеливания (R — центр отверстия,
    /// F — вершина столбика), на детали — <see cref="SightAdjustablePart"/>. Подвижна одна деталь (столбик мушки),
    /// её ход — только по локальной Y. Половины двигаются только вдоль планки — локальной Z корня.
    /// </para>
    ///
    /// <para>
    /// Корень (точка посадки на планку): линия прицела лежит в плоскости x = 0 корня; y = 0 — нижняя плоскость
    /// основания целика (минимум bounds его меша); z = 0 — задняя кромка основания целика. TR15: основание мушки
    /// тоже стоит низом на y = 0, R→F вдоль планки — <see cref="Tr15SightRadius"/> (в префабе TR15 механики нет,
    /// значение выбрано разумным, пользователь раздвигает половины на стенде). MX68: обе половины сохраняют родное
    /// взаимное положение (основания в начале координат оружия MX68), Front стоит в той же точке, что Rear.
    /// </para>
    ///
    /// <para>
    /// Повторный запуск обновляет префаб на месте (GUID и fileID узлов сохраняются), подвижная деталь
    /// возвращается в собранную позицию (сдвиг 0). Чужие узлы и компоненты writer не перезаписывает — отказ.
    /// </para>
    /// </summary>
    public static class SightKitBuilder
    {
        public const string ArtFolder = "Assets/Art/Weapons/Sights/IronKits";
        public const string PrefabFolder = "Assets/Prefabs/Weapons/Sights";
        public const float Tr15SightRadius = .30f;
        public const float PivotTolerance = .00005f; // 0,05 мм
        private const string Tr15IronsightsMaterial = "Assets/Art/Weapons/Kinemation/Materials/M_TR15_Ironsights.mat";
        private const string PackWeapons = "Assets/ThirdParty/KINEMATION/TacticalShooterPack/Meshes/Weapons/";

        public sealed class HalfSpec
        {
            public string Fbx, BaseName, PartName, SourceFbx;
            public SightPartRole Role;
            public Vector3 ExpectedPivot; // точка детали в системе основания, м
            public float TravelDown, TravelUp;
            /// <summary>Деталь намеренно переставлена относительно исходника пака: наложение её не требует.</summary>
            public string ReposedNote;
        }

        public sealed class KitSpec
        {
            public string Name, PrefabPath, MaterialPath;
            public HalfSpec Rear, Front;
            public bool SharedOrigin;
        }

        public static readonly KitSpec[] Kits =
        {
            new KitSpec
            {
                Name = "SightKit_TR15", PrefabPath = PrefabFolder + "/SightKit_TR15.prefab", MaterialPath = Tr15IronsightsMaterial,
                Rear = new HalfSpec
                {
                    Fbx = ArtFolder + "/TR15_RearSight_Kit.fbx", BaseName = "RearSight_Base", PartName = "RearSight_Aperture",
                    SourceFbx = PackWeapons + "TR15/Attachments/Ironsights/SKM_TR15_RearSight.FBX",
                    Role = SightPartRole.Rear, ExpectedPivot = new Vector3(-.000004f, .029539f, -.005112f),
                    // Барабан диоптра повёрнут малым отверстием вверх (рабочее положение); в паке вверху большое.
                    ReposedNote = "барабан повёрнут малым отверстием вверх"
                },
                Front = new HalfSpec
                {
                    Fbx = ArtFolder + "/TR15_FrontSight_Kit.fbx", BaseName = "FrontSight_Base", PartName = "FrontSight_Post",
                    SourceFbx = PackWeapons + "TR15/Attachments/Ironsights/SKM_TR15_FrontSight.FBX",
                    Role = SightPartRole.Front, ExpectedPivot = new Vector3(-.000004f, .033979f, -.005219f),
                    // Docs/weapons/decisions.md «Прицелы», 2026-10-12 «Ход TR15»: столбик в модели выкручен выше целика
                    // (~4,4 мм), сведение AR-15 на 15 м требует −5,6 мм → ход вниз 8 мм, вверх 3 мм.
                    TravelDown = .008f, TravelUp = .003f
                }
            },
            new KitSpec
            {
                Name = "SightKit_MX68", PrefabPath = PrefabFolder + "/SightKit_MX68.prefab", MaterialPath = null, SharedOrigin = true,
                Rear = new HalfSpec
                {
                    Fbx = ArtFolder + "/MX68_IronSights_Kit.fbx", BaseName = "MX68_RearSight_Base", PartName = "MX68_RearSight_Aperture",
                    SourceFbx = PackWeapons + "MX68/Attachments/SM_IronSights_MX68.FBX",
                    Role = SightPartRole.Rear, ExpectedPivot = new Vector3(0f, .077229f, -.187349f)
                },
                Front = new HalfSpec
                {
                    Fbx = ArtFolder + "/MX68_IronSights_Kit.fbx", BaseName = "MX68_FrontSight_Base", PartName = "MX68_FrontSight_Post",
                    SourceFbx = PackWeapons + "MX68/Attachments/SM_IronSights_MX68.FBX",
                    Role = SightPartRole.Front, ExpectedPivot = new Vector3(0f, .076313f, .178597f),
                    TravelDown = .003f, TravelUp = .003f
                }
            }
        };

        /// <summary>
        /// Комплект-коллиматор (раунд 1, 2026-10-20): корень → узел оптики с мешем окна (2 submesh: корпус, окно) и
        /// <see cref="WeaponOpticView"/>. Подвижной детали нет. Меш окна и материал сетки делает <c>Tr15OpticBuilder</c>
        /// (входы этого writer). Привязку сетки к ShotSource оружия (<c>_source</c>, <c>_weapon</c>, дистанция) пишет
        /// <c>WeaponSightAuthoring</c> в экземпляр комплекта — в префабе комплекта сетка не привязана и выключена.
        /// </summary>
        public sealed class OpticKitSpec
        {
            public string Name, PrefabPath, NodeName, SightId, MeshPath, HousingMaterialPath, ReticleMaterialPath;
            /// <summary>Поворот меша относительно корня комплекта (+Z корня — вдоль планки к дулу, +Y — вверх).</summary>
            public Vector3 MeshEuler;
            public int LensMaterialIndex;
            /// <summary>Кратность увеличивающего прицела (<see cref="WeaponScopeCamera"/>); 0 — коллиматор без увеличения.</summary>
            public float ScopeMagnification;
        }

        public const string SettingsPath = "Assets/Data/Weapons/SightCalibration/WeaponSightCalibrationSettings.asset";

        public static readonly OpticKitSpec[] Optics =
        {
            new OpticKitSpec
            {
                Name = "SightKit_XPS2", PrefabPath = PrefabFolder + "/SightKit_XPS2.prefab", NodeName = "XPS2", SightId = "XPS2",
                MeshPath = "Assets/Art/Weapons/Kinemation/TR15/Meshes/TR15_XPS2_Optic.asset",
                HousingMaterialPath = "Assets/Art/Weapons/Kinemation/Materials/M_TR15_Attachments.mat",
                ReticleMaterialPath = "Assets/Art/Weapons/Kinemation/TR15/Materials/TR15_XPS2_Reticle.mat",
                // Как стоял узел SM_Attach_AR15_XPS2 в TR15 относительно ShotSource (до раунда 1): поворот X 90°.
                MeshEuler = new Vector3(90f, 0f, 0f), LensMaterialIndex = 1
            },
            // Оптика Barrett M82 (arsenal-combat-lineup, 2026-10-23): меш и сетку-перекрестие делает LowPolyOpticBuilder,
            // кратность — камера прицела (решение пользователя: малая RT, бюджет +1 мс GPU).
            new OpticKitSpec
            {
                Name = "SightKit_M82Scope", PrefabPath = PrefabFolder + "/SightKit_M82Scope.prefab", NodeName = "M82Scope", SightId = "M82Scope",
                MeshPath = VrBattlegrounds.Editor.Weapons.Sights.LowPolyOpticBuilder.Barrett.MeshPath,
                HousingMaterialPath = "Assets/ThirdParty/Low Poly Weapons VOL.2/Gun_MAT.mat",
                ReticleMaterialPath = VrBattlegrounds.Editor.Weapons.Sights.LowPolyOpticBuilder.Barrett.ReticleMaterialPath,
                MeshEuler = Vector3.zero, LensMaterialIndex = VrBattlegrounds.Editor.Weapons.Sights.LowPolyOpticBuilder.LensMaterialIndex,
                ScopeMagnification = 1.75f
            }
        };

        public static string[] OutputPaths()
        {
            var paths = new List<string>(Optics.Select(o => o.PrefabPath));
            foreach (var kit in Kits)
            {
                paths.Add(kit.PrefabPath);
                paths.Add(kit.Rear.Fbx + ".meta");
                paths.Add(kit.Front.Fbx + ".meta");
            }
            paths.AddRange(IronKitMaterials.OutputPaths());
            return paths.Distinct().ToArray();
        }

        [MenuItem("Tools/VR Battlegrounds/Weapons/Sights/Build Iron Sight Kits")]
        private static void BuildMenu()
        {
            try { GameLog.WeaponSystem.Info("[SightKitBuilder] " + Build()); }
            catch (Exception e) { GameLog.Error("[SightKitBuilder] " + e.Message); throw; }
        }

        /// <summary>Read-only: входные FBX, исходники пака, материалы и пути выхода. Не импортирует и не пишет.</summary>
        public static string Preflight()
        {
            var report = new StringBuilder();
            foreach (var kit in Kits)
            {
                foreach (var half in new[] { kit.Rear, kit.Front })
                {
                    if (!File.Exists(half.Fbx)) throw new InvalidOperationException("Нет FBX комплекта: " + half.Fbx);
                    if (!(AssetImporter.GetAtPath(half.SourceFbx) is ModelImporter))
                        throw new InvalidOperationException("Нет исходного FBX пака: " + half.SourceFbx);
                }
                if (kit.MaterialPath != null && AssetDatabase.LoadAssetAtPath<Material>(kit.MaterialPath) == null)
                    throw new InvalidOperationException("Нет материала комплекта: " + kit.MaterialPath);
                if (File.Exists(kit.PrefabPath) && AssetDatabase.LoadAssetAtPath<GameObject>(kit.PrefabPath) == null)
                    throw new InvalidOperationException("Путь префаба занят не префабом: " + kit.PrefabPath);
                report.Append(kit.Name).Append(": ").Append(ImporterDifference(kit.Rear) ?? "импорт целика совпадает")
                      .Append("; ").Append(ImporterDifference(kit.Front) ?? "импорт мушки совпадает").Append(". ");
            }
            report.Append(IronKitMaterials.Preflight());
            return report.ToString();
        }

        /// <summary>Импорт → проверка геометрии → материалы → префабы → readback. Возвращает сводку.</summary>
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Сборка комплектов прицелов — только в Edit Mode");
            Preflight();
            foreach (var kit in Kits) { ConfigureImporter(kit.Rear); ConfigureImporter(kit.Front); }
            var layouts = Kits.Select(Measure).ToArray(); // отказ до любой записи префабов
            Material mx68 = IronKitMaterials.Ensure();
            EnsureFolder(PrefabFolder);
            var report = new StringBuilder();
            for (int i = 0; i < Kits.Length; i++)
            {
                Material material = Kits[i].MaterialPath != null ? AssetDatabase.LoadAssetAtPath<Material>(Kits[i].MaterialPath) : mx68;
                report.Append(WritePrefab(Kits[i], layouts[i], material)).Append('\n');
                report.Append(Readback(Kits[i], layouts[i], material)).Append('\n');
            }
            report.Append(BuildOptics());
            return report.ToString().TrimEnd();
        }

        // ── Коллиматоры ─────────────────────────────────────────

        [MenuItem("Tools/VR Battlegrounds/Weapons/Sights/Build Optic Sight Kits")]
        private static void BuildOpticsMenu()
        {
            try { GameLog.WeaponSystem.Info("[SightKitBuilder] " + BuildOptics()); }
            catch (Exception e) { GameLog.Error("[SightKitBuilder] " + e.Message); throw; }
        }

        /// <summary>Только комплекты-коллиматоры: проверка входов → префаб → readback. FBX не импортирует.</summary>
        public static string BuildOptics()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Сборка комплектов прицелов — только в Edit Mode");
            var report = new StringBuilder();
            foreach (var optic in Optics)
            {
                ReadOptic(optic, out Mesh mesh, out Material housing, out Material reticle, out WeaponSightCalibrationSettings settings);
                EnsureFolder(PrefabFolder);
                Vector3 nodePosition = OpticNodePosition(optic, mesh);
                bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(optic.PrefabPath) != null;
                if (File.Exists(optic.PrefabPath) && !exists) throw new InvalidOperationException("Путь префаба занят не префабом: " + optic.PrefabPath);
                GameObject root = exists ? PrefabUtility.LoadPrefabContents(optic.PrefabPath) : new GameObject(optic.Name);
                try
                {
                    ValidateOwnedOptic(optic, root);
                    Node(root.transform, Vector3.zero, null, null);
                    Transform node = Node(Child(root.transform, optic.NodeName), nodePosition, mesh, housing);
                    node.localRotation = Quaternion.Euler(optic.MeshEuler);
                    var renderer = node.GetComponent<MeshRenderer>();
                    var materials = Enumerable.Repeat(housing, mesh.subMeshCount).ToArray();
                    materials[optic.LensMaterialIndex] = reticle;
                    renderer.sharedMaterials = materials;
                    // Крепление к планке — по нижней поверхности корпуса в системе корня (раунд 3, «Посадка в паз»).
                    var probe = new SurfaceProbe();
                    Matrix4x4 meshToRoot = Matrix4x4.TRS(nodePosition, Quaternion.Euler(optic.MeshEuler), Vector3.one);
                    probe.Add(mesh, meshToRoot);
                    SightMountInfo mount = SightRailMount.FindMount(probe, RotatedBounds(mesh.bounds, meshToRoot), optic.Name);
                    Node(Child(root.transform, SightRailMount.MountName), mount.Point, null, null);
                    report.Append(optic.Name).Append(" крепление: ").Append(mount).Append('\n');
                    var view = node.GetComponent<WeaponOpticView>();
                    if (view == null) view = node.gameObject.AddComponent<WeaponOpticView>();
                    var so = new SerializedObject(view);
                    so.FindProperty("_lensRenderer").objectReferenceValue = renderer;
                    so.FindProperty("_lensMaterialIndex").intValue = optic.LensMaterialIndex;
                    so.FindProperty("_source").objectReferenceValue = null;      // привязка — в экземпляре на оружии
                    so.FindProperty("_shotTypeIndex").intValue = 0;
                    so.FindProperty("_weapon").objectReferenceValue = null;
                    so.FindProperty("_sightId").stringValue = optic.SightId;
                    so.FindProperty("_settings").objectReferenceValue = settings;
                    so.FindProperty("_profile").objectReferenceValue = null;
                    so.FindProperty("_calibratedZeroDistance").floatValue = 0f;  // не сведён: сетка выключена
                    var scope = node.GetComponent<WeaponScopeCamera>();
                    if (optic.ScopeMagnification > 0f)
                    {
                        if (scope == null) scope = node.gameObject.AddComponent<WeaponScopeCamera>();
                        var scopeSo = new SerializedObject(scope);
                        scopeSo.FindProperty("_view").objectReferenceValue = view;
                        scopeSo.FindProperty("_magnification").floatValue = optic.ScopeMagnification;
                        scopeSo.ApplyModifiedPropertiesWithoutUndo();
                    }
                    else if (scope != null) UnityEngine.Object.DestroyImmediate(scope);
                    so.FindProperty("_scope").objectReferenceValue = optic.ScopeMagnification > 0f ? scope : null;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    if (PrefabUtility.SaveAsPrefabAsset(root, optic.PrefabPath) == null)
                        throw new InvalidOperationException("Не удалось сохранить " + optic.PrefabPath);
                }
                finally
                {
                    if (exists) PrefabUtility.UnloadPrefabContents(root);
                    else UnityEngine.Object.DestroyImmediate(root);
                }
                report.Append(ReadbackOptic(optic, nodePosition)).Append('\n');
            }
            return report.ToString().TrimEnd();
        }

        private static void ReadOptic(OpticKitSpec optic, out Mesh mesh, out Material housing, out Material reticle, out WeaponSightCalibrationSettings settings)
        {
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(optic.MeshPath);
            housing = AssetDatabase.LoadAssetAtPath<Material>(optic.HousingMaterialPath);
            reticle = AssetDatabase.LoadAssetAtPath<Material>(optic.ReticleMaterialPath);
            settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
            if (mesh == null || housing == null || reticle == null || settings == null)
                throw new InvalidOperationException($"{optic.Name}: нет меша окна, материала корпуса/сетки или настроек калибровки " +
                                                    "(меш и сетку делает «Обновить линзу прицела TR15» — Tr15OpticBuilder)");
            if (mesh.subMeshCount <= optic.LensMaterialIndex)
                throw new InvalidOperationException($"{optic.Name}: у меша {mesh.subMeshCount} submesh, окно — {optic.LensMaterialIndex}");
            if (reticle.shader == null || reticle.shader.name != WeaponOpticView.ShaderName)
                throw new InvalidOperationException($"{optic.Name}: материал сетки не на шейдере {WeaponOpticView.ShaderName}");
        }

        /// <summary>Точка посадки корня: низ корпуса (y = 0), задняя кромка (z = 0), ось окна в x = 0.</summary>
        private static Vector3 OpticNodePosition(OpticKitSpec optic, Mesh mesh)
        {
            Quaternion rotation = Quaternion.Euler(optic.MeshEuler);
            Bounds b = mesh.bounds;
            var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = rotation * new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                min = Vector3.Min(min, corner); max = Vector3.Max(max, corner);
            }
            return new Vector3(-(min.x + max.x) * .5f, -min.y, -min.z);
        }

        private static void ValidateOwnedOptic(OpticKitSpec optic, GameObject root)
        {
            var allowed = new HashSet<string> { root.name, optic.NodeName, SightRailMount.MountName };
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!allowed.Contains(t.name))
                    throw new InvalidOperationException($"{optic.PrefabPath}: чужой узел «{t.name}» — writer его не перезаписывает");
                foreach (var c in t.GetComponents<Component>())
                    if (c == null || !(c is Transform || c is MeshFilter || c is MeshRenderer || c is WeaponOpticView || c is WeaponScopeCamera))
                        throw new InvalidOperationException($"{optic.PrefabPath}: чужой компонент на «{t.name}»");
            }
        }

        private static string ReadbackOptic(OpticKitSpec optic, Vector3 nodePosition)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(optic.PrefabPath);
            if (asset == null) throw new InvalidOperationException("Readback: нет " + optic.PrefabPath);
            if (asset.GetComponentsInChildren<SightAdjustablePart>(true).Length > 0 || asset.GetComponentsInChildren<Collider>(true).Length > 0)
                throw new InvalidOperationException("Readback: у коллиматора не должно быть деталей механики и коллайдеров");
            var views = asset.GetComponentsInChildren<WeaponOpticView>(true);
            if (views.Length != 1) throw new InvalidOperationException("Readback: нужен ровно один WeaponOpticView");
            var scopes = asset.GetComponentsInChildren<WeaponScopeCamera>(true);
            if (scopes.Length != (optic.ScopeMagnification > 0f ? 1 : 0)
                || scopes.Length == 1 && !Mathf.Approximately(scopes[0].Magnification, optic.ScopeMagnification))
                throw new InvalidOperationException("Readback: камера кратности не совпала со спецификацией");
            if (asset.transform.Find(SightRailMount.MountName) == null) throw new InvalidOperationException("Readback: нет узла крепления " + SightRailMount.MountName);
            Transform node = asset.transform.Find(optic.NodeName);
            if (node == null || (node.localPosition - nodePosition).magnitude > 1e-7f || Quaternion.Angle(node.localRotation, Quaternion.Euler(optic.MeshEuler)) > 1e-3f)
                throw new InvalidOperationException("Readback: поза узла оптики не совпала");
            Vector3 size = node.localRotation * node.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            return $"{optic.Name}: корпус {Mathf.Abs(size.x) * 1000:0.0}×{Mathf.Abs(size.y) * 1000:0.0}×{Mathf.Abs(size.z) * 1000:0.0} мм (ш×в×д), " +
                   $"узел {optic.NodeName} {Format(nodePosition)}, сетка не привязана";
        }

        // ── Импорт ──────────────────────────────────────────────

        /// <summary>Отличие импорта комплекта от исходника пака (масштаб/оси) и от статического импорта; null — совпадает.</summary>
        private static string ImporterDifference(HalfSpec half)
        {
            var kit = AssetImporter.GetAtPath(half.Fbx) as ModelImporter;
            var src = (ModelImporter)AssetImporter.GetAtPath(half.SourceFbx);
            if (kit == null) return "FBX ещё не импортирован";
            var diff = new List<string>();
            if (kit.globalScale != src.globalScale) diff.Add("globalScale");
            if (kit.useFileScale != src.useFileScale) diff.Add("useFileScale");
            if (kit.useFileUnits != src.useFileUnits) diff.Add("useFileUnits");
            if (kit.bakeAxisConversion != src.bakeAxisConversion) diff.Add("bakeAxisConversion");
            if (kit.materialImportMode != ModelImporterMaterialImportMode.None) diff.Add("materialImportMode");
            if (kit.animationType != ModelImporterAnimationType.None || kit.importAnimation) diff.Add("animation");
            if (kit.importCameras || kit.importLights || kit.importBlendShapes || kit.addCollider) diff.Add("extras");
            return diff.Count == 0 ? null : "импорт отличается: " + string.Join(", ", diff);
        }

        private static void ConfigureImporter(HalfSpec half)
        {
            AssetDatabase.ImportAsset(half.Fbx, ImportAssetOptions.ForceSynchronousImport);
            if (ImporterDifference(half) == null) return;
            var kit = (ModelImporter)AssetImporter.GetAtPath(half.Fbx);
            var src = (ModelImporter)AssetImporter.GetAtPath(half.SourceFbx);
            // Масштаб и оси — как у исходника пака: наложение на оригинал проверяет VerifyOverlay.
            kit.globalScale = src.globalScale;
            kit.useFileScale = src.useFileScale;
            kit.useFileUnits = src.useFileUnits;
            kit.bakeAxisConversion = src.bakeAxisConversion;
            // Статические детали без своих материалов (их задаёт префаб комплекта), без анимации и коллайдеров.
            kit.materialImportMode = ModelImporterMaterialImportMode.None;
            kit.animationType = ModelImporterAnimationType.None;
            kit.importAnimation = false;
            kit.importCameras = false;
            kit.importLights = false;
            kit.importBlendShapes = false;
            kit.addCollider = false;
            kit.isReadable = false;
            kit.SaveAndReimport();
        }

        // ── Геометрия ───────────────────────────────────────────

        public sealed class HalfLayout
        {
            public Mesh BaseMesh, PartMesh;
            public Vector3 PartLocalPosition; // точка детали в системе основания
            public Vector3 NodeLocalPosition; // Rear/Front в системе корня комплекта
            public Vector3 BaseModelPosition; // основание в системе корня FBX
        }

        public sealed class KitLayout { public HalfLayout Rear, Front; }

        private struct Pair { public Transform Base, Part; public Transform Root; }

        /// <summary>Read-only разбор импортированных FBX: статичность, нулевые повороты, масштаб 1, pivot на R/F.</summary>
        public static KitLayout Measure(KitSpec kit)
        {
            Pair rear = Find(kit.Rear), front = Find(kit.Front);
            var layout = new KitLayout { Rear = Half(kit.Rear, rear), Front = Half(kit.Front, front) };
            Bounds rb = layout.Rear.BaseMesh.bounds, fb = layout.Front.BaseMesh.bounds;
            Vector3 r = layout.Rear.PartLocalPosition, f = layout.Front.PartLocalPosition;
            layout.Rear.NodeLocalPosition = new Vector3(-r.x, -rb.min.y, -rb.min.z);
            if (kit.SharedOrigin)
                layout.Front.NodeLocalPosition = layout.Rear.NodeLocalPosition // родное взаимное положение MX68
                    + layout.Front.BaseModelPosition - layout.Rear.BaseModelPosition;
            else
                layout.Front.NodeLocalPosition = new Vector3(-f.x, -fb.min.y,
                    layout.Rear.NodeLocalPosition.z + r.z + Tr15SightRadius - f.z);
            return layout;
        }

        private static Pair Find(HalfSpec half)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(half.Fbx);
            if (model == null) throw new InvalidOperationException("FBX комплекта не импортирован как модель: " + half.Fbx);
            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            Transform Single(string name)
            {
                var found = all.Where(t => t.name == name).ToArray();
                if (found.Length != 1) throw new InvalidOperationException($"{half.Fbx}: объектов «{name}» {found.Length}, нужен 1");
                return found[0];
            }
            if (model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
                throw new InvalidOperationException(half.Fbx + ": комплект должен быть статическим (без skinned mesh)");
            return new Pair { Base = Single(half.BaseName), Part = Single(half.PartName), Root = model.transform };
        }

        private static HalfLayout Half(HalfSpec half, Pair pair)
        {
            foreach (var t in new[] { pair.Base, pair.Part })
            {
                if (Quaternion.Angle(Quaternion.Inverse(pair.Root.rotation) * t.rotation, Quaternion.identity) > .01f)
                    throw new InvalidOperationException($"{half.Fbx}: у «{t.name}» ненулевой поворот {t.rotation.eulerAngles} — проверьте оси импорта");
                Vector3 s = t.lossyScale;
                if (Mathf.Abs(s.x - 1f) > 1e-4f || Mathf.Abs(s.y - 1f) > 1e-4f || Mathf.Abs(s.z - 1f) > 1e-4f)
                    throw new InvalidOperationException($"{half.Fbx}: у «{t.name}» масштаб {s} ≠ 1 — проверьте единицы импорта");
            }
            Mesh MeshOf(Transform t)
            {
                var filter = t.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException($"{half.Fbx}: у «{t.name}» нет меша");
                return filter.sharedMesh;
            }
            Vector3 pivot = pair.Base.InverseTransformPoint(pair.Part.position);
            if ((pivot - half.ExpectedPivot).magnitude > PivotTolerance)
                throw new InvalidOperationException($"{half.Fbx}: pivot «{half.PartName}» {Format(pivot)} ≠ ожидаемой точке {Format(half.ExpectedPivot)}");
            return new HalfLayout
            {
                BaseMesh = MeshOf(pair.Base), PartMesh = MeshOf(pair.Part), PartLocalPosition = pivot,
                BaseModelPosition = pair.Root.InverseTransformPoint(pair.Base.position)
            };
        }

        // ── Префаб ──────────────────────────────────────────────

        /// <summary>Bounds меша после матрицы (по 8 углам).</summary>
        private static Bounds RotatedBounds(Bounds b, Matrix4x4 m)
        {
            var result = new Bounds(m.MultiplyPoint3x4(b.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
                result.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
            return result;
        }

        /// <summary>Узел крепления <see cref="SightRailMount.MountName"/> под основанием: точка контакта и упора (из меша).</summary>
        private static string Mount(Transform node, Mesh baseMesh, string context)
        {
            var probe = new SurfaceProbe();
            probe.Add(baseMesh, Matrix4x4.identity);
            SightMountInfo info = SightRailMount.FindMount(probe, baseMesh.bounds, context);
            Node(Child(node, SightRailMount.MountName), info.Point, null, null);
            return context + " крепление: " + info;
        }

        private static string WritePrefab(KitSpec kit, KitLayout layout, Material material)
        {
            string mounts;
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(kit.PrefabPath) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(kit.PrefabPath) : new GameObject(kit.Name);
            try
            {
                ValidateOwned(kit, root);
                Node(root.transform, Vector3.zero, null, null);
                Transform rear = Node(Child(root.transform, "Rear"), layout.Rear.NodeLocalPosition, layout.Rear.BaseMesh, material);
                Transform front = Node(Child(root.transform, "Front"), layout.Front.NodeLocalPosition, layout.Front.BaseMesh, material);
                Part(Node(Child(rear, kit.Rear.PartName), layout.Rear.PartLocalPosition, layout.Rear.PartMesh, material), kit.Rear, layout.Rear);
                Part(Node(Child(front, kit.Front.PartName), layout.Front.PartLocalPosition, layout.Front.PartMesh, material), kit.Front, layout.Front);
                mounts = Mount(rear, layout.Rear.BaseMesh, kit.Name + " Rear") + "\n" + Mount(front, layout.Front.BaseMesh, kit.Name + " Front");
                if (PrefabUtility.SaveAsPrefabAsset(root, kit.PrefabPath) == null)
                    throw new InvalidOperationException("Не удалось сохранить " + kit.PrefabPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
            return mounts;
        }

        /// <summary>Префаб содержит только узлы и компоненты этого writer; иначе отказ без записи.</summary>
        private static void ValidateOwned(KitSpec kit, GameObject root)
        {
            var allowedNames = new HashSet<string> { root.name, "Rear", "Front", kit.Rear.PartName, kit.Front.PartName, SightRailMount.MountName };
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!allowedNames.Contains(t.name))
                    throw new InvalidOperationException($"{kit.PrefabPath}: чужой узел «{t.name}» — writer его не перезаписывает");
                foreach (var c in t.GetComponents<Component>())
                    if (c == null || !(c is Transform || c is MeshFilter || c is MeshRenderer || c is SightAdjustablePart))
                        throw new InvalidOperationException($"{kit.PrefabPath}: чужой компонент на «{t.name}»");
            }
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform Node(Transform node, Vector3 localPosition, Mesh mesh, Material material)
        {
            node.gameObject.layer = 0;
            node.gameObject.tag = "Untagged";
            node.localPosition = localPosition;
            node.localRotation = Quaternion.identity;
            node.localScale = Vector3.one;
            if (mesh == null) return node;
            // GetComponent в редакторе возвращает «фальшивый null»: оператор ?? его не распознаёт.
            var filter = node.GetComponent<MeshFilter>();
            if (filter == null) filter = node.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = node.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, mesh.subMeshCount)).ToArray();
            return node;
        }

        private static void Part(Transform node, HalfSpec half, HalfLayout layout)
        {
            var part = node.GetComponent<SightAdjustablePart>();
            if (part == null) part = node.gameObject.AddComponent<SightAdjustablePart>();
            var so = new SerializedObject(part);
            so.FindProperty("_role").enumValueIndex = (int)half.Role;
            so.FindProperty("_neutralLocalPosition").vector3Value = layout.PartLocalPosition;
            so.FindProperty("_travelDown").floatValue = half.TravelDown;
            so.FindProperty("_travelUp").floatValue = half.TravelUp;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string Readback(KitSpec kit, KitLayout layout, Material material)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(kit.PrefabPath);
            if (asset == null) throw new InvalidOperationException("Readback: нет " + kit.PrefabPath);
            var parts = asset.GetComponentsInChildren<SightAdjustablePart>(true);
            if (parts.Length != 2 || parts.Count(p => p.Role == SightPartRole.Rear) != 1 || parts.Count(p => p.IsAdjustable) != 1)
                throw new InvalidOperationException("Readback: нужно ровно две SightAdjustablePart (Rear/Front), подвижная одна");
            if (asset.GetComponentsInChildren<Collider>(true).Length > 0)
                throw new InvalidOperationException("Readback: у комплекта не должно быть коллайдеров");
            foreach (var part in parts)
            {
                HalfLayout half = part.Role == SightPartRole.Rear ? layout.Rear : layout.Front;
                if ((part.transform.localPosition - half.PartLocalPosition).magnitude > 1e-7f || Mathf.Abs(part.Offset) > 1e-7f
                    || part.transform.parent.name != (part.Role == SightPartRole.Rear ? "Rear" : "Front")
                    || (part.transform.parent.localPosition - half.NodeLocalPosition).magnitude > 1e-7f)
                    throw new InvalidOperationException("Readback: поза детали " + part.name + " не совпала");
                if (part.GetComponent<MeshRenderer>().sharedMaterials.Any(m => m != material))
                    throw new InvalidOperationException("Readback: материал детали " + part.name + " не совпал");
            }
            Vector3 r = layout.Rear.NodeLocalPosition + layout.Rear.PartLocalPosition;
            Vector3 f = layout.Front.NodeLocalPosition + layout.Front.PartLocalPosition;
            return $"{kit.Name}: R={Format(r)} F={Format(f)} (корень комплекта), R→F {(f.z - r.z) * 1000:0.0} мм, " +
                   $"F выше R на {(f.y - r.y) * 1000:0.00} мм, материал {material.name}";
        }

        // ── Наложение на исходник пака ──────────────────────────

        /// <summary>
        /// Read-only: обе модели в одной точке (корни FBX в начале координат со своей импортированной позой: у пака
        /// корень повёрнут осевым преобразованием, у комплекта оси запечены), для каждой вершины комплекта —
        /// расстояние до ближайшей вершины исходника пака и обратно. <paramref name="worstMatchedMm"/> — максимум по
        /// всем объектам, кроме намеренно переставленных деталей (<see cref="HalfSpec.ReposedNote"/>).
        /// </summary>
        public static string VerifyOverlay(out float worstMatchedMm, out float worstAllMm)
        {
            var report = new StringBuilder();
            worstMatchedMm = 0f; worstAllMm = 0f;
            foreach (var group in Kits.SelectMany(k => new[] { k.Rear, k.Front }).GroupBy(h => h.Fbx))
            {
                HalfSpec first = group.First();
                Vector3[] source = WorldVertices(AssetDatabase.LoadAssetAtPath<GameObject>(first.SourceFbx), null, out string sourceNote);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(first.Fbx);
                var kitAll = new List<Vector3>();
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    Vector3[] kit = WorldVertices(model, filter, out _);
                    kitAll.AddRange(kit);
                    float max = MaxNearest(kit, source) * 1000f;
                    string reposed = group.Where(h => h.PartName == filter.name).Select(h => h.ReposedNote).FirstOrDefault(n => n != null);
                    if (reposed == null) worstMatchedMm = Mathf.Max(worstMatchedMm, max);
                    worstAllMm = Mathf.Max(worstAllMm, max);
                    report.Append($"{Path.GetFileName(first.Fbx)}/{filter.name}: вершин {kit.Length}, макс. до исходника {max:0.0000} мм")
                          .Append(reposed != null ? " (намеренно: " + reposed + ")" : "").Append('\n');
                }
                float back = MaxNearest(source, kitAll.ToArray()) * 1000f;
                report.Append($"{Path.GetFileName(first.SourceFbx)} → комплект: вершин {source.Length}, макс. {back:0.0000} мм{sourceNote}\n");
            }
            return report.ToString().TrimEnd();
        }

        private static Vector3[] WorldVertices(GameObject model, MeshFilter only, out string note)
        {
            note = "";
            if (model == null) throw new InvalidOperationException("Нет модели для наложения");
            // Мировые координаты ассета: корень модели в начале координат со своей импортированной позой.
            Matrix4x4 toRoot = Matrix4x4.identity;
            var result = new List<Vector3>();
            IEnumerable<MeshFilter> filters = only != null ? new[] { only } : model.GetComponentsInChildren<MeshFilter>(true);
            foreach (var filter in filters)
                Append(result, filter.sharedMesh, toRoot * filter.transform.localToWorldMatrix);
            if (only != null) return result.ToArray();
            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Модель пака в позе привязки: bone·bindpose одинаков для всех костей — это и есть матрица меша.
                Mesh mesh = skinned.sharedMesh;
                Matrix4x4[] bind = mesh.bindposes;
                Transform[] bones = skinned.bones;
                Matrix4x4 matrix = skinned.transform.localToWorldMatrix;
                if (bones.Length > 0 && bind.Length == bones.Length && bones[0] != null)
                {
                    matrix = bones[0].localToWorldMatrix * bind[0];
                    for (int i = 1; i < bones.Length; i++)
                        if (bones[i] != null && !Close(bones[i].localToWorldMatrix * bind[i], matrix)) { note = " (кости не в позе привязки — сверка приблизительная)"; break; }
                }
                Append(result, mesh, toRoot * matrix);
            }
            return result.ToArray();
        }

        private static void Append(List<Vector3> result, Mesh mesh, Matrix4x4 matrix)
        {
            if (mesh == null) return;
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
                try
                {
                    data[0].GetVertices(vertices);
                    foreach (var v in vertices) result.Add(matrix.MultiplyPoint3x4(v));
                }
                finally { vertices.Dispose(); }
            }
        }

        private static bool Close(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 1e-5f) return false;
            return true;
        }

        private static float MaxNearest(Vector3[] from, Vector3[] to)
        {
            if (from.Length == 0) return 0f;
            if (to.Length == 0) return float.PositiveInfinity;
            float worst = 0f;
            foreach (var p in from)
            {
                float best = float.MaxValue;
                foreach (var q in to)
                {
                    float d = (p - q).sqrMagnitude;
                    if (d < best) best = d;
                }
                worst = Mathf.Max(worst, best);
            }
            return Mathf.Sqrt(worst);
        }

        // ── Общее ───────────────────────────────────────────────

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        public static string Format(Vector3 v) => $"({v.x:0.000000}, {v.y:0.000000}, {v.z:0.000000})";
    }
}
