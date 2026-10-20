#if !VRBG_NO_E2E
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Devices;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DevTools.E2E;
using VrBattlegrounds.DevTools.E2E.Input;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Скриптовый ввод сетевого клиента (контракт tasks/bots-control-modes/contracts/xr-input.md), принятая веха 1.
    /// Поведение в сети проверяют e2e-сценарии <c>scripted-input-smoke</c> и <c>scripted-wall-purchase</c> на стенде;
    /// здесь — статические инварианты: встроенные сценарии, кадр «покоя», защита релиза и состав префабов.
    /// </summary>
    public class ScriptedInputTests
    {
        private static readonly string[] BuiltInScripts = { "input-smoke", "wall-purchase" };

        private static readonly string[] AvatarPrefabs =
        {
            "Assets/Prefabs/Player/Avatars/Bases/PlayerBase.prefab",
            "Assets/Prefabs/Player/Avatars/Ghost/GhostAvatar.prefab",
            "Assets/Prefabs/Player/Avatars/CyborgHands/MEF/MEF_cyborg_hands_Player.prefab",
            "Assets/Prefabs/Player/Avatars/CyborgHands/MEF/MEF_cyborg_hands_Player_Black.prefab",
            "Assets/Prefabs/Player/Avatars/CyborgHands/Cyborg/PlayerControllersCyborgAvatar.prefab",
        };

        [TestCaseSource(nameof(BuiltInScripts))]
        public void BuiltInScript_LoadsWithKnownOperationsAndUniqueLabels(string name)
        {
            InputScript script = InputScript.Load(name, null, out string error);

            Assert.That(error, Is.Null);
            Assert.That(script, Is.Not.Null);
            Assert.That(script.steps, Is.Not.Empty);
            Assert.That(script.steps[0].op, Is.EqualTo("activate"), "ввод подаётся до любых намерений");

            var labels = new HashSet<string>();
            for (int i = 0; i < script.steps.Length; i++)
            {
                Assert.That(InputScript.Operations, Does.Contain(script.steps[i].op), $"шаг {i + 1}");
                Assert.That(labels.Add(script.steps[i].Label(i)), Is.True, $"имя проверки шага {i + 1} не уникально");
            }
        }

        [Test]
        public void Load_RejectsUnknownOperationAndMissingFile()
        {
            string file = Path.Combine(Path.GetTempPath(), "scripted-input-unknown-op.json");
            File.WriteAllText(file, "{\"name\":\"x\",\"steps\":[{\"op\":\"activate\"},{\"op\":\"teleport\"}]}");
            try
            {
                Assert.That(InputScript.Load(null, file, out string error), Is.Null);
                Assert.That(error, Does.Contain("teleport"));
            }
            finally
            {
                File.Delete(file);
            }

            Assert.That(InputScript.Load("no-such-script", null, out string missing), Is.Null);
            Assert.That(missing, Does.Contain("не найден"));
        }

        [Test]
        public void RestFrame_StandsAtEyeHeightWithEverythingReleased()
        {
            DeviceFrame frame = DeviceFrame.Rest(1.6f);

            Assert.That(frame.HeadPosition, Is.EqualTo(new Vector3(0f, 1.6f, 0f)));
            Assert.That(frame.HeadRotation, Is.EqualTo(Quaternion.identity));
            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                DeviceHand hand = frame[side];
                Assert.That(hand.Pressed, Is.EqualTo(UxrInputButtons.None), side.ToString());
                Assert.That(hand.Position.y, Is.LessThan(frame.HeadPosition.y), side.ToString());
                Assert.That(hand.Trigger1D, Is.LessThan(0f), "ось берётся из кнопки");
                Assert.That(hand.Grip1D, Is.LessThan(0f), "ось берётся из кнопки");
            }

            Assert.That(frame.Left.Position.x, Is.LessThan(0f));
            Assert.That(frame.Right.Position.x, Is.GreaterThan(0f));
        }

        [Test]
        public void Gate_IsClosedWithoutE2EScriptedInput()
        {
            Assert.That(E2EScriptedInput.IsActive, Is.False);
            Assert.That(ScriptedInputGate.Allows, Is.False, "компоненты в префабах не должны работать без владельца");
        }

        [Test]
        public void Scenarios_AreRegistered()
        {
            Assert.That(E2ERunner.IsRegistered("scripted-input-smoke"), Is.True);
            Assert.That(E2ERunner.IsRegistered("scripted-wall-purchase"), Is.True);
        }

        [TestCaseSource(nameof(AvatarPrefabs))]
        public void AvatarPrefab_HasDisabledScriptedDeviceWithQuestSensors(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);

            ScriptedControllerInput[] inputs = prefab.GetComponentsInChildren<ScriptedControllerInput>(true);
            ScriptedControllerTracking[] trackings = prefab.GetComponentsInChildren<ScriptedControllerTracking>(true);
            Assert.That(inputs.Length, Is.EqualTo(1), "один источник ввода на аватар");
            Assert.That(trackings.Length, Is.EqualTo(1));

            ScriptedControllerInput input = inputs[0];
            ScriptedControllerTracking tracking = trackings[0];
            Assert.That(input.enabled, Is.False, "включает только E2EScriptedInput (R1: компонент лежит в префабе)");
            Assert.That(tracking.enabled, Is.False);
            Assert.That(tracking.gameObject, Is.SameAs(input.gameObject));
            Assert.That(tracking.HasLeftHandSensorSetup && tracking.HasRightHandSensorSetup, Is.True, "сенсоры Quest 3");

            var props = new SerializedObject(tracking);
            Assert.That(props.FindProperty("_smoothPosition").floatValue, Is.Zero, "сглаживание 0");
            Assert.That(props.FindProperty("_smoothRotation").floatValue, Is.Zero, "сглаживание 0");
            Assert.That(props.FindProperty("_updateAvatarLeftHand").boolValue && props.FindProperty("_updateAvatarRightHand").boolValue,
                Is.True, "запястья пишет штатный UxrControllerTracking");
        }
    }
}
#endif
