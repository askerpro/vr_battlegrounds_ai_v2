using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar.Controllers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// <c>Tools/VR Battlegrounds/Avatars/Setup Legs</c> — ноги всех аватаров: решатель ноги UltimateXR и шаги клипами ходьбы
    /// (патчи UltimateXR 35–37, раздел «Ноги» <see cref="UxrStandardAvatarController"/>). Генерация, а не ручная настройка:
    /// повторный запуск приводит префабы к тому же виду.
    ///
    /// <list type="number">
    /// <item>Контроллер клипов ходьбы — <see cref="AvatarMixamoLocomotionSetup.EnsureController"/> (импорт клипов Mixamo,
    /// контроллер с нуля).</item>
    /// <item>Для каждого префаба, который сам владеет телом аватара (<see cref="BodyOwners"/>: в цепочке вариантов
    /// зарегистрированного аватара — самый верхний префаб в <see cref="PlayerFolder"/> с humanoid-Animator): копия рига
    /// (<see cref="AvatarLegsRigBaker"/>), в контроллере — <c>Use Leg IK (native)</c>, копия рига, контроллер клипов, высота
    /// лодыжки над подошвой по мешу обуви (<see cref="MeasureAnkleHeight"/>), рука <c>LimitHandReach</c> (против «резиновых
    /// рук»: с наклоном корпуса из клипа плечи уходят от кистей, и <c>ExtendForearm</c> растягивал предплечье на 7–12 %),
    /// стойка по оружию <see cref="AvatarStanceFromGrabs"/>.</item>
    /// <item>Прежние ноги — прочь из префабов (и из вариантов): Legs Animator и его мост, конвейер T-42, драйвер эксперимента,
    /// пустые ссылки на удалённые скрипты.</item>
    /// <item>Варианты получают всё наследованием: их переопределения этих полей снимаются.</item>
    /// </list>
    ///
    /// Проверка — <c>AvatarLoadoutTests.Ноги_аватара_настроены</c>.
    /// </summary>
    public static class AvatarLegsSetup
    {
        public const string PlayerFolder = "Assets/Prefabs/Player";

        /// <summary>Компоненты прежних ног (по имени типа: их скриптов в проекте может уже не быть).</summary>
        public static readonly string[] ObsoleteLegComponents =
            { "LegsAnimator", "DrivenLegsAnimator", "LegsAnimatorUxrBridge", "AvatarBodyPipeline", "AvatarUxrLegsDriver" };

        /// <summary>Поля контроллера, которые ведёт утилита: у вариантов переопределения снимаются.</summary>
        private static readonly string[] ManagedProperties = { "_useNativeLegIK", "_legs.locomotionRig", "_legs.locomotionController", "_legs.hasKneelPose", "_legs.ankleHeight" };

        [InitializeOnLoadMethod]
        private static void BindDiagnostics() => UxrLegsDiagnostics.WarningSink =
            (message, context) => GameLog.Player.Warning("[UxrLegs] " + message, context);

        public static void Run()
        {
            AnimatorController controller = AvatarMixamoLocomotionSetup.EnsureController();
            if (controller == null)
            {
                GameLog.Error("[AvatarLegsSetup] Контроллер клипов ходьбы не собран — см. ошибки выше.");
                return;
            }

            var report = new List<string>();
            List<string> owners = BodyOwners().ToList();
            foreach (string path in owners) SetupOwner(path, controller, report);
            foreach (string path in Descendants(owners)) CleanVariant(path, report);

            AssetDatabase.SaveAssets();
            GameLog.Player.Info($"[AvatarLegsSetup] Ноги настроены: {string.Join(", ", owners.Select(Path.GetFileNameWithoutExtension))}.\n" + string.Join("\n", report));
        }

        /// <summary>
        /// Префабы всех аватаров игры: из реестров (скины и призрак) и из всех ассетов <see cref="AvatarData"/> — в том числе
        /// пока не включённых в реестр (Heavy, US Marine): включат — ноги уже готовы.
        /// </summary>
        public static IEnumerable<string> RegisteredAvatarPaths()
        {
            IEnumerable<AvatarData> fromRegistries = AssetDatabase.FindAssets("t:AvatarRegistry")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith("Assets/ThirdParty/"))
                .Select(AssetDatabase.LoadAssetAtPath<AvatarRegistry>)
                .Where(r => r != null)
                .SelectMany(r => r.avatars.Append(r.ghost));
            IEnumerable<AvatarData> allData = AssetDatabase.FindAssets("t:AvatarData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith("Assets/ThirdParty/"))
                .Select(AssetDatabase.LoadAssetAtPath<AvatarData>);

            return fromRegistries.Concat(allData)
                .Where(d => d != null && d.prefab != null)
                .Select(d => AssetDatabase.GetAssetPath(d.prefab))
                .Distinct();
        }

        /// <summary>
        /// Владельцы тела: в цепочке вариантов зарегистрированного аватара — самый верхний префаб в <see cref="PlayerFolder"/>
        /// с humanoid-Animator (MEF_Base_Avatar, Heavy_Soldier_Base_Avatar, PlayerControllersCyborgAvatar). Выше — база без
        /// тела (PlayerBase) или чужой ассет (пак, сэмпл UltimateXR), ниже — варианты-скины.
        /// </summary>
        public static IEnumerable<string> BodyOwners()
        {
            var owners = new HashSet<string>();
            foreach (string path in RegisteredAvatarPaths())
            {
                string owner = null;
                foreach (string link in Chain(path))
                {
                    if (!link.StartsWith(PlayerFolder + "/") || link.Contains("/Experimental/")) break;
                    if (HumanoidAnimator(AssetDatabase.LoadAssetAtPath<GameObject>(link)) != null) owner = link;
                }

                if (owner != null) owners.Add(owner);
            }

            return owners.OrderBy(p => p);
        }

        /// <summary>Тот же контракт владельца, что у массовой настройки; выбранный вариант не становится вторым владельцем.</summary>
        public static string BodyOwnerOf(string path) => Chain(path).TakeWhile(p => p.StartsWith(PlayerFolder + "/") && !p.Contains("/Experimental/"))
            .LastOrDefault(p => HumanoidAnimator(AssetDatabase.LoadAssetAtPath<GameObject>(p)) != null);

        public static string[] VariantsOf(string owner) => RegisteredAvatarPaths().SelectMany(Chain).Distinct()
            .Where(p => p != owner && Chain(p).Contains(owner)).OrderBy(p => p).ToArray();

        /// <summary>Настройка одного владельца с явно выбранным контроллером. Миграция старых компонентов — отдельное действие.</summary>
        public static string SetupFor(string owner, AnimatorController controller)
        {
            if (controller == null || BodyOwnerOf(owner) != owner) throw new System.InvalidOperationException("Укажите body owner и существующий AnimatorController.");
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(owner);
            if (root == null || root.GetComponent<UxrStandardAvatarController>() == null) throw new System.InvalidOperationException("У владельца нет UxrStandardAvatarController.");
            if (root.GetComponentsInChildren<Component>(true).Any(c => c && IsObsolete(c.GetType())))
                throw new System.InvalidOperationException("На владельце есть прежние legs writers. Сначала выполните явную миграцию владельца в Обслуживании.");
            var report = new List<string>();
            SetupOwner(owner, controller, report, false);
            return string.Join("\n", report);
        }

        public static string CleanVariantsFor(string owner, string[] explicitVariants)
        {
            var report = new List<string>();
            if (explicitVariants == null || explicitVariants.Any(p => p == owner || !Chain(p).Contains(owner)))
                throw new System.InvalidOperationException("Список вариантов не соответствует выбранному владельцу.");
            foreach (string path in explicitVariants)
            {
                CleanVariant(path, report);
                Workbench.AvatarScopedActions.NormalizeAndVerify(path);
            }
            return string.Join("\n", report);
        }

        public static string CleanOwnerFor(string owner)
        {
            if (BodyOwnerOf(owner) != owner) throw new System.InvalidOperationException("Выберите владельца тела.");
            var root = PrefabUtility.LoadPrefabContents(owner);
            var report = new List<string>();
            try
            {
                if (RemoveObsoleteLegs(root, report, owner))
                {
                    Workbench.AvatarScopedActions.SetCanonicalAssetId(root, owner);
                    if (!PrefabUtility.SaveAsPrefabAsset(root, owner)) throw new System.InvalidOperationException("Миграция владельца не сохранена.");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Workbench.AvatarScopedActions.NormalizeAndVerify(owner);
            return string.Join("\n", report);
        }

        /// <summary>Цепочка вариантов: сам префаб, его база, база базы…</summary>
        public static IEnumerable<string> Chain(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            while (prefab != null)
            {
                yield return AssetDatabase.GetAssetPath(prefab);
                prefab = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            }
        }

        public static Animator HumanoidAnimator(GameObject root) =>
            root == null ? null : root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.avatar != null && a.avatar.isHuman);

        private static IEnumerable<string> Descendants(List<string> owners)
        {
            var result = new HashSet<string>();
            foreach (string path in RegisteredAvatarPaths())
                foreach (string link in Chain(path))
                {
                    if (owners.Contains(link)) break;
                    result.Add(link);
                }

            return result.OrderBy(p => p);
        }

        /// <summary>
        /// Есть ли в контроллере ног поза на колене: смешивание по <c>Legs_Crouch</c>, где при 1 играет не то же, что при 0
        /// (у стоек без приседа поддерево одно). Для <c>AvatarLegs_Locomotion</c> — да (колено набора винтовки), для
        /// <c>AvatarLegs_FinalIK</c> — нет (присед там по своему параметру VRIK, не <c>Legs_Crouch</c>).
        /// </summary>
        public static bool ControllerHasKneelPose(RuntimeAnimatorController runtime)
        {
            var controller = runtime as AnimatorController;
            if (controller == null) return false;
            foreach (AnimatorControllerLayer layer in controller.layers)
            foreach (ChildAnimatorState state in layer.stateMachine.states)
                if (HasCrouchBlend(state.state.motion)) return true;
            return false;
        }

        private static bool HasCrouchBlend(Motion motion)
        {
            if (!(motion is BlendTree tree)) return false;
            if (tree.blendParameter == UxrLegLocomotion.CrouchParam && tree.children.Length >= 2)
            {
                Motion stand = tree.children.OrderBy(c => c.threshold).First().motion;
                Motion crouch = tree.children.OrderBy(c => c.threshold).Last().motion;
                if (crouch != null && crouch != stand) return true;
            }
            return tree.children.Any(c => HasCrouchBlend(c.motion));
        }

        private static void SetupOwner(string path, AnimatorController controller, List<string> report, bool removeObsolete = true)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Animator animator = HumanoidAnimator(root);
                var uxr = root.GetComponent<UxrStandardAvatarController>();
                if (animator == null || uxr == null)
                {
                    report.Add($"{path}: нет humanoid-Animator или UxrStandardAvatarController — пропущен");
                    return;
                }

                if (removeObsolete) RemoveObsoleteLegs(root, report, path);

                GameObject rig = AvatarLegsRigBaker.Bake(animator.gameObject, controller, AvatarLegsRigBaker.RigPath(path), report);
                if (rig == null) throw new System.InvalidOperationException("Locomotion rig не создан: " + path);
                float ankle = MeasureAnkleHeight(root, uxr, out string ankleNote);

                var so = new SerializedObject(uxr);
                so.FindProperty("_useNativeLegIK").boolValue = true;
                SerializedProperty legs = so.FindProperty("_legs");
                legs.FindPropertyRelative(nameof(UxrLegsSettings.locomotionRig)).objectReferenceValue = rig;
                legs.FindPropertyRelative(nameof(UxrLegsSettings.locomotionController)).objectReferenceValue = controller;
                if (ankle > 0f) legs.FindPropertyRelative(nameof(UxrLegsSettings.ankleHeight)).floatValue = ankle;
                legs.FindPropertyRelative(nameof(UxrLegsSettings.hasKneelPose)).boolValue = ControllerHasKneelPose(controller);
                so.ApplyModifiedPropertiesWithoutUndo();

                if (root.GetComponent<AvatarStanceFromGrabs>() == null) root.AddComponent<AvatarStanceFromGrabs>();

                Workbench.AvatarScopedActions.SetCanonicalAssetId(root, path);
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new System.InvalidOperationException("Ноги владельца не сохранены: " + path);
                report.Add($"{Path.GetFileNameWithoutExtension(path)}: копия рига {Path.GetFileName(AvatarLegsRigBaker.RigPath(path))}, лодыжка над подошвой {ankle:0.000} м ({ankleNote})");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Вариант: прежние ноги — прочь, переопределения полей ног и руки — сняты (берутся у базы).</summary>
        private static void CleanVariant(string path, List<string> report)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = RemoveObsoleteLegs(root, report, path);
                var uxr = root.GetComponent<UxrStandardAvatarController>();
                if (uxr != null && PrefabUtility.IsPartOfPrefabInstance(uxr))
                {
                    var so = new SerializedObject(uxr);
                    foreach (string name in ManagedProperties)
                    {
                        SerializedProperty p = so.FindProperty(name);
                        if (p == null || !p.prefabOverride) continue;
                        PrefabUtility.RevertPropertyOverride(p, InteractionMode.AutomatedAction);
                        report.Add($"{Path.GetFileNameWithoutExtension(path)}: снято переопределение {name}");
                        changed = true;
                    }
                }

                if (changed)
                {
                    Workbench.AvatarScopedActions.SetCanonicalAssetId(root, path);
                    if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new System.InvalidOperationException("Миграция варианта не сохранена: " + path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Компоненты прежних ног (по имени типа и наследникам) и пустые ссылки на удалённые скрипты.</summary>
        private static bool RemoveObsoleteLegs(GameObject root, List<string> report, string path)
        {
            // Мост требует Legs Animator (RequireComponent) — удаление в порядке зависимостей.
            List<string> removed = AvatarLegsRigBaker.DestroyComponents(root, c => IsObsolete(c.GetType()));
            foreach (string name in removed) report.Add($"{Path.GetFileNameWithoutExtension(path)}: удалён {name}");
            bool changed = removed.Count > 0;

            return changed;
        }

        private static bool IsObsolete(System.Type type)
        {
            for (System.Type t = type; t != null; t = t.BaseType)
                if (ObsoleteLegComponents.Contains(t.Name)) return true;
            return false;
        }

        /// <summary>
        /// Высота лодыжки над подошвой обуви, м, без масштаба: кость стопы над самой низкой вершиной меша, привязанной к
        /// стопе или пальцам (вершины ботинка), в позе префаба. Меши — включённые, у <c>LODGroup</c> — только LOD0. Обе
        /// стопы, среднее. 0 — меша обуви нет (остаётся прежнее значение).
        /// </summary>
        public static float MeasureAnkleHeight(GameObject root, UxrStandardAvatarController uxr, out string note)
        {
            var avatar = root.GetComponent<UltimateXR.Avatar.UxrAvatar>();
            var feet = new[] { avatar.AvatarRig.LeftLeg, avatar.AvatarRig.RightLeg }.Where(l => l.Foot != null).ToArray();
            if (feet.Length == 0)
            {
                note = "нет костей стоп";
                return 0f;
            }

            var lodHidden = new HashSet<Renderer>();
            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null) lodHidden.Add(r);
            }

            float scale = Mathf.Max(root.transform.lossyScale.y, 1e-4f);
            var heights = new List<float>();
            int vertices = 0;
            foreach (var leg in feet)
            {
                var footBones = new HashSet<Transform>(leg.Foot.GetComponentsInChildren<Transform>(true));
                float lowest = float.MaxValue;
                foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (!smr.enabled || smr.sharedMesh == null || lodHidden.Contains(smr)) continue;
                    BoneWeight[] weights = smr.sharedMesh.boneWeights;
                    Transform[] bones = smr.bones;
                    if (weights.Length == 0) continue;

                    var baked = new Mesh();
                    smr.BakeMesh(baked, true);
                    Vector3[] points = baked.vertices;
                    Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for (int i = 0; i < points.Length && i < weights.Length; i++)
                    {
                        int b = weights[i].boneIndex0;
                        if (b < 0 || b >= bones.Length || bones[b] == null || !footBones.Contains(bones[b])) continue;
                        lowest = Mathf.Min(lowest, toWorld.MultiplyPoint3x4(points[i]).y);
                        vertices++;
                    }

                    Object.DestroyImmediate(baked);
                }

                if (lowest < float.MaxValue) heights.Add((leg.Foot.position.y - lowest) / scale);
            }

            if (heights.Count == 0)
            {
                note = "нет вершин меша на стопах";
                return 0f;
            }

            note = $"по {vertices} вершинам обуви, Л/П {string.Join(" / ", heights.Select(h => h.ToString("0.000")))}";
            return heights.Average();
        }
    }
}
