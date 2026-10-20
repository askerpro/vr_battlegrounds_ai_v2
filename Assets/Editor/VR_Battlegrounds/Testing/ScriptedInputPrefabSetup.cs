using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Devices.Integrations.Meta;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.E2E.Input;
using VrBattlegrounds.Editor.Avatars.Workbench;

namespace VrBattlegrounds.EditorTools.TestStand
{
    /// <summary>
    /// Кладёт выключенные <see cref="ScriptedControllerInput"/> и <see cref="ScriptedControllerTracking"/> в префабы
    /// аватаров (контракт tasks/bots-control-modes/contracts/xr-input.md, R1: добавленный в рантайме ввод у других машин
    /// не резолвится). Живые тела получают компоненты через PlayerBase → CyborgHandsBase → варианты; призрак собран
    /// распаковкой и правится отдельно. Сенсоры — трансформы трекинга Quest 3 того же префаба. Повторный запуск
    /// обновляет настройку и не дублирует компоненты.
    /// </summary>
    public static class ScriptedInputPrefabSetup
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Testing/Скриптовый ввод в префабах аватаров";
        private const string ObjectName = "ScriptedInput";

        public static readonly string[] Prefabs =
        {
            "Assets/Prefabs/Player/Avatars/Bases/PlayerBase.prefab",
            "Assets/Prefabs/Player/Avatars/Ghost/GhostAvatar.prefab",
        };

        [MenuItem(MenuPath)]
        public static void SetupMenu()
        {
            string report = Setup();
            GameLog.Player.Info("[ScriptedInputPrefabSetup] " + report);
            EditorUtility.DisplayDialog("Скриптовый ввод", report, "OK");
        }

        /// <summary>Настраивает все префабы; возвращает отчёт. Исключение — префаб не подходит.</summary>
        public static string Setup()
        {
            var report = new List<string>();
            foreach (string path in Prefabs) report.Add(SetupPrefab(path));
            AssetDatabase.SaveAssets();
            return string.Join("\n", report);
        }

        private static string SetupPrefab(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<UxrAvatar>() == null) throw new InvalidOperationException($"{path}: нет UxrAvatar в корне.");

                UxrMetaTouchQuest3Tracking quest = root.GetComponentInChildren<UxrMetaTouchQuest3Tracking>(true);
                if (quest == null) throw new InvalidOperationException($"{path}: нет трекинга Quest 3 — сенсоры взять неоткуда.");

                var questProps = new SerializedObject(quest);
                var left = questProps.FindProperty("_leftHandSensor").objectReferenceValue as Transform;
                var right = questProps.FindProperty("_rightHandSensor").objectReferenceValue as Transform;
                if (left == null || right == null) throw new InvalidOperationException($"{path}: у трекинга Quest 3 не заданы сенсоры.");

                Transform container = quest.transform.parent != null ? quest.transform.parent : root.transform;
                ScriptedControllerTracking tracking = root.GetComponentInChildren<ScriptedControllerTracking>(true);
                ScriptedControllerInput input = root.GetComponentInChildren<ScriptedControllerInput>(true);
                GameObject host = tracking != null ? tracking.gameObject : input != null ? input.gameObject : null;
                bool created = host == null;
                if (created)
                {
                    host = new GameObject(ObjectName);
                    host.transform.SetParent(container, false);
                }

                if (input == null) input = host.AddComponent<ScriptedControllerInput>();
                if (tracking == null) tracking = host.AddComponent<ScriptedControllerTracking>();
                input.enabled = false;
                tracking.enabled = false;

                var props = new SerializedObject(tracking);
                props.FindProperty("_leftHandSensor").objectReferenceValue = left;
                props.FindProperty("_rightHandSensor").objectReferenceValue = right;
                props.FindProperty("_updateAvatarLeftHand").boolValue = true;
                props.FindProperty("_updateAvatarRightHand").boolValue = true;
                props.FindProperty("_smoothPosition").floatValue = 0f;
                props.FindProperty("_smoothRotation").floatValue = 0f;
                props.ApplyModifiedPropertiesWithoutUndo();

                AvatarMaintenanceTools.SavePrefab(root, path);
                return $"{path}: {(created ? "добавлен" : "обновлён")} {ObjectName} под '{container.name}', сенсоры '{left.name}'/'{right.name}', компоненты выключены.";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
