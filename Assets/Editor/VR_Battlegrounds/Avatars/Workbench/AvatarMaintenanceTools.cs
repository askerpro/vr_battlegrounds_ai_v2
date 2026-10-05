using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    /// <summary>Обобщённые замены миграций: явная цель, preview scene и проверенное сохранение.</summary>
    internal static class AvatarMaintenanceTools
    {
        internal static string[] Scope(params string[] paths) => paths.Where(p => !string.IsNullOrEmpty(p))
            .SelectMany(p => AssetDatabase.IsValidFolder(p) ? AssetDatabase.FindAssets("", new[] { p }).Select(AssetDatabase.GUIDToAssetPath).Concat(new[] { p }) : new[] { p })
            .SelectMany(p => AssetDatabase.LoadMainAssetAtPath(p) && !AssetDatabase.IsValidFolder(p) ? AssetDatabase.GetDependencies(p, true) : new[] { p }).Distinct().ToArray();

        internal static string Checked(Action action)
        {
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            Application.logMessageReceived += capture;
            try { action(); if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors.Take(8))); return "Команда выполнена. Проверьте результат в Unity."; }
            finally { Application.logMessageReceived -= capture; }
        }
        internal static string Checked(Func<string> action)
        {
            string report = null;
            Checked(() => { report = action(); });
            if (report == null || report.StartsWith("ОТКАЗ", StringComparison.Ordinal)) throw new InvalidOperationException(report ?? "Сборщик не вернул результат.");
            return report;
        }
        internal static GameObject SavePrefab(GameObject root, string path)
        {
            AvatarScopedActions.RequireOwnPrefab(path);
            if (path.Contains("..")) throw new InvalidOperationException("Недопустимый prefab output.");
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) && !PrefabUtility.SaveAsPrefabAsset(root, path)) throw new InvalidOperationException("Новый prefab не сохранён: " + path);
            AvatarScopedActions.SetCanonicalAssetId(root, path);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (!saved) throw new InvalidOperationException("Prefab не сохранён: " + path);
            var identity = saved.GetComponent<Mirror.NetworkIdentity>();
            if (identity && VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.ReadOnDisk(path) !=
                Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path))))
                throw new InvalidOperationException("Canonical Mirror assetId не сохранён: " + path);
            return saved;
        }

        internal static string CloneBase(string sourcePath, string output, string removeModel)
        {
            RequireNewOutput(output);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source) throw new InvalidOperationException("Источник не найден.");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = Path.GetFileNameWithoutExtension(output);
                if (!string.IsNullOrWhiteSpace(removeModel))
                {
                    var child = root.transform.Find(removeModel);
                    if (!child) throw new InvalidOperationException("Нет указанного дочернего объекта модели: " + removeModel);
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                    // Извлечённый каркас не хранит ссылки на удалённые кости и renderers.
                    var avatar = root.GetComponent<UxrAvatar>();
                    if (avatar) { var so = new SerializedObject(avatar); so.FindProperty("_parentPrefab").objectReferenceValue = null; so.ApplyModifiedPropertiesWithoutUndo(); }
                }
                SaveNew(root, output);
                return "Создан независимый каркас: " + output;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        internal static void RequireNewOutput(string output)
        {
            AvatarScopedActions.RequireOwnPrefab(output);
            if (output.Contains("..") || File.Exists(output) || File.Exists(output + ".meta")) throw new InvalidOperationException("Укажите новый выходной prefab; существующий каркас не перезаписывается.");
        }
        internal static string CreateVariant(string basePath, string modelPath, string output)
        {
            RequireNewOutput(output);
            if (!AvatarLegsSetup.Chain(basePath).Contains(AvatarHandBases.NonSdkHands))
                throw new InvalidOperationException("Родные кисти модели требуют базу PlayerBase_NonSdkHands либо её вариант.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var parent = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (!source || !parent) throw new InvalidOperationException("База или модель не найдена.");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(parent, scene);
                if (root.GetComponentsInChildren<Animator>(true).Any(a => a.avatar && a.avatar.isHuman))
                    throw new InvalidOperationException("База уже содержит тело. Для нового тела выберите пустую базу кистей.");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                if (model.GetComponentsInChildren<UxrAvatar>(true).Length != 0) throw new InvalidOperationException("Выберите модель Humanoid без второго UxrAvatar.");
                model.transform.SetParent(root.transform, false); model.name = "Model";
                var animators = model.GetComponentsInChildren<Animator>(true).Where(a => a.avatar && a.avatar.isHuman && a.avatar.isValid).ToArray();
                if (animators.Length != 1) throw new InvalidOperationException("Модель должна содержать один валидный Humanoid.");
                var avatar = root.GetComponent<UxrAvatar>();
                if (!avatar) throw new InvalidOperationException("На базе отсутствует UxrAvatar.");
                UltimateXR.Avatar.Rig.UxrAvatarRig.SetupRigElementsFromAnimator(avatar.AvatarRig, animators[0]);
                avatar.TryToInferMissingRigElements();
                if (!avatar.AvatarRig.LeftArm.Hand.HasFullHandData() || !avatar.AvatarRig.RightArm.Hand.HasFullHandData())
                    throw new InvalidOperationException("Не размечены все пальцы родных кистей.");
                var so = new SerializedObject(avatar); so.FindProperty("_parentPrefab").objectReferenceValue = parent; so.ApplyModifiedPropertiesWithoutUndo();
                root.name = Path.GetFileNameWithoutExtension(output);
                if (!VrBattlegrounds.EditorTools.AvatarFingertipSetup.CanSetup(avatar)) throw new InvalidOperationException("Невозможно настроить fingertips.");
                VrBattlegrounds.EditorTools.AvatarFingertipSetup.Setup(avatar);
                FixAvatarRenderers.Setup(avatar);
                SaveNew(root, output);
                return "Создан новый вариант с родными кистями: " + output + ". Loadout и регистрация подключаются отдельными действиями.";
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        internal static void SaveNew(GameObject root, string output)
        {
            AvatarRigPreparation.EnsureFolder(Path.GetDirectoryName(output).Replace('\\', '/'));
            if (!PrefabUtility.SaveAsPrefabAsset(root, output)) throw new InvalidOperationException("Новый prefab не сохранён.");
            // GUID нового файла появляется после первой записи; вторая закрепляет его собственный Mirror ID.
            var saved = PrefabUtility.LoadPrefabContents(output);
            try { AvatarScopedActions.SetCanonicalAssetId(saved, output); if (!PrefabUtility.SaveAsPrefabAsset(saved, output)) throw new InvalidOperationException("Mirror ID не сохранён."); }
            finally { PrefabUtility.UnloadPrefabContents(saved); }
            AvatarScopedActions.NormalizeAndVerify(output);
        }
        internal static string Tracking(string path) => AvatarScopedActions.EditPrefab(path, avatar =>
        {
            var trackers = avatar.GetComponentsInChildren<UxrHandTracking>(true);
            foreach (var tracker in trackers)
            {
                var so = new SerializedObject(tracker);
                var left = so.FindProperty("_leftCalibrationData"); var right = so.FindProperty("_rightCalibrationData");
                if (left == null || right == null) throw new InvalidOperationException("Поля калибровки SDK изменились.");
                left.ClearArray(); right.ClearArray(); so.ApplyModifiedPropertiesWithoutUndo();
            }
            return "Сброшена калибровка tracking: " + trackers.Length + " компонентов.";
        });
        internal static string RefineBack(string path) => AvatarScopedActions.EditPrefab(path, avatar =>
        {
            var anchors = avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true).Where(a => a.name == "Anchor_Back").ToArray();
            var proximity = avatar.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Proximity_Back_R").ToArray();
            if (anchors.Length != 1 || proximity.Length != 1) throw new InvalidOperationException("Нужны единственные Anchor_Back и Proximity_Back_R; цель не изменена.");
            var so = new SerializedObject(anchors[0]);
            so.FindProperty("_dropProximityTransformUseSelf").boolValue = false;
            so.FindProperty("_dropProximityTransform").objectReferenceValue = proximity[0];
            so.FindProperty("_maxPlaceDistance").floatValue = .2f;
            so.ApplyModifiedPropertiesWithoutUndo(); return "Задний карман привязан к Proximity_Back_R.";
        });
        internal static string ExportPockets(string path, string outputFolder = "Assets/Prefabs/Player/Pockets")
        {
            if (!outputFolder.StartsWith("Assets/Prefabs/", StringComparison.Ordinal) || outputFolder.Contains("..")) throw new InvalidOperationException("Недопустимая область экспорта карманов.");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                string[] names = { "MagazinePocket", "Anchor_Hip_R", "Anchor_Back", "BackGrabProxy" };
                var nodes = names.Select(name => root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray()).ToArray();
                if (nodes.Any(n => n.Length != 1)) throw new InvalidOperationException("Для экспорта нужны четыре единственных кармана. Выходные файлы не изменены.");
                var bindings = AvatarPocketTemplateBindings.Capture(nodes.Select(n => n[0]).ToArray());
                AvatarRigPreparation.EnsureFolder(outputFolder);
                foreach (var node in nodes)
                {
                    var copy = UnityEngine.Object.Instantiate(node[0].gameObject, root.transform, false);
                    copy.transform.SetParent(null, false);
                    copy.name = node[0].name; copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    AvatarPocketTemplateBindings.ClearExternal(copy.transform, copy.name, bindings);
                    if (!PrefabUtility.SaveAsPrefabAsset(copy, outputFolder + "/" + copy.name + ".prefab")) throw new InvalidOperationException("Карман не сохранён: " + copy.name);
                    UnityEngine.Object.DestroyImmediate(copy);
                }
                AvatarPocketTemplateBindings.Save(bindings, outputFolder + "/Bindings.json");
                return "Экспортированы четыре шаблона карманов; исходный аватар не сохранён.";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        internal static string RebindGloves(string path, string[] rendererPaths) => AvatarScopedActions.EditPrefab(path, avatar =>
        {
            var bones = avatar.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.ToArray());
            var meshes = rendererPaths.Select(p => avatar.transform.Find(p)?.GetComponent<SkinnedMeshRenderer>()).ToArray();
            if (meshes.Length == 0 || meshes.Any(m => !m)) throw new InvalidOperationException("Укажите пути SkinnedMeshRenderer перчаток внутри выбранного аватара.");
            var refs = meshes.SelectMany(m => m.bones.Append(m.rootBone)).Where(b => b).ToArray();
            if (refs.Any(b => !bones.TryGetValue(b.name, out var match) || match.Length != 1)) throw new InvalidOperationException("Не найдены однозначные кости; перчатки не перепривязаны.");
            foreach (var mesh in meshes) { mesh.bones = mesh.bones.Select(b => b ? bones[b.name][0] : null).ToArray(); if (mesh.rootBone) mesh.rootBone = bones[mesh.rootBone.name][0]; }
            return "Перепривязаны перчатки: " + meshes.Length;
        });
        internal static string Skeleton(GameObject root)
        {
            var lines = new List<string>();
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                lines.Add(animator.name + ": Humanoid=" + (animator.avatar && animator.avatar.isValid && animator.avatar.isHuman));
                if (!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman) continue;
                foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                { var value = animator.GetBoneTransform(bone); lines.Add(bone + ": " + (value ? root.transform.InverseTransformPoint(value.position).ToString("F4") : "нет mapping")); }
            }
            lines.Add("Дочерние объекты: " + string.Join(", ", root.transform.Cast<Transform>().Select(t => t.name)));
            foreach (var camera in root.GetComponentsInChildren<Camera>(true)) lines.Add("Камера: " + AnimationUtility.CalculateTransformPath(camera.transform, root.transform));
            return string.Join("\n", lines);
        }
    }
}
