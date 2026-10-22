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
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Скриптовый ввод сетевого клиента (контракт tasks/bots-control-modes/contracts/xr-input.md), принятая веха 1.
    /// Поведение в сети проверяют e2e-сценарии <c>scripted-input-smoke</c>, <c>scripted-wall-purchase</c> и
    /// <c>scripted-combat-hit</c> (этап 2 эпика e2e-regression, итерация 1 принята 2026-10-20) на стенде; здесь —
    /// статические инварианты: встроенные сценарии (включая клиента-админа <c>scripted-admin-pause</c>, этап 3), шаги по
    /// клиентам, кадр «покоя», защита релиза и состав префабов.
    /// </summary>
    public class ScriptedInputTests
    {
        private static readonly string[] BuiltInScripts =
            { "input-smoke", "wall-purchase", "combat-hit", "combat-round", "combat-next-round", "admin-pause", "series-next-map" };

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

        [TestCase("{\"op\":\"grab-wall-item\",\"item\":\"rifle\"}", "rifle")]
        [TestCase("{\"op\":\"hold\",\"client\":-1}", "client")]
        [TestCase("{\"op\":\"shoot-enemy\",\"until\":\"wound\"}", "wound")]
        [TestCase("{\"op\":\"admin-command\",\"target\":\"Kick\"}", "Kick")]
        [TestCase("{\"op\":\"admin-command-denied\"}", "команда админа")]
        [TestCase("{\"op\":\"admin-start-series\",\"target\":\"elimination\"}", "maps")]
        [TestCase("{\"op\":\"expect-map\"}", "expect-map")]
        [TestCase("{\"op\":\"admin-command\",\"target\":\"NextMap\",\"repeat\":0}", "repeat")]
        public void Load_RejectsUnknownStepValuesAndNegativeClient(string step, string expected)
        {
            string file = Path.Combine(Path.GetTempPath(), "scripted-input-bad-step.json");
            File.WriteAllText(file, "{\"name\":\"x\",\"steps\":[{\"op\":\"activate\"}," + step + "]}");
            try
            {
                Assert.That(InputScript.Load(null, file, out string error), Is.Null);
                Assert.That(error, Does.Contain(expected));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Test]
        public void Step_ForClientRunsOnlyThere_ZeroRunsEverywhere()
        {
            var shared = new InputStep { op = "hold" };
            var shooter = new InputStep { op = "shoot-enemy", client = 1 };

            Assert.That(shared.AppliesTo(1) && shared.AppliesTo(2), Is.True);
            Assert.That(shooter.AppliesTo(1), Is.True);
            Assert.That(shooter.AppliesTo(2), Is.False);
        }

        [Test]
        public void CombatHit_OnlyClientOneShoots_BothBuyPistolsAndReportShot()
        {
            InputScript script = InputScript.Load("combat-hit", null, out string error);
            Assert.That(error, Is.Null);

            int shooters = 0, purchases = 0, shotSteps = 0;
            foreach (InputStep step in script.steps)
            {
                if (step.op == "shoot-enemy")
                {
                    shooters++;
                    Assert.That(step.client, Is.EqualTo(1), "стрелок — клиент 1, жертва — клиент 2 (оракул сервера)");
                    Assert.That(step.shots, Is.GreaterThan(0));
                }
                else
                {
                    Assert.That(step.client, Is.Zero, $"{step.op}: остальные шаги — у обоих клиентов (барьер ждёт всех)");
                }

                if (step.op == "grab-wall-item")
                {
                    purchases++;
                    Assert.That(step.item, Is.EqualTo("pistol"), "пистолет: одна пуля на выстрел, не очередь");
                }

                if (step.op == "step" && step.name == "shot") shotSteps++;
            }

            Assert.That(shooters, Is.EqualTo(1));
            Assert.That(purchases, Is.EqualTo(1));
            Assert.That(shotSteps, Is.EqualTo(1));
        }

        [Test]
        public void CombatRound_ReadyByTagThenShooterKillsAndTargetBecomesGhost()
        {
            InputScript script = InputScript.Load("combat-round", null, out string error);
            Assert.That(error, Is.Null);

            int tag = -1, inZone = -1, combat = -1, shoot = -1, eliminated = -1;
            for (int i = 0; i < script.steps.Length; i++)
            {
                InputStep step = script.steps[i];
                if (step.op == "grab-dog-tag") tag = i;
                if (step.op == "walk" && step.target == "own-spawn-point") inZone = i;
                if (step.op == "wait-server" && step.name == "combat") combat = i;
                if (step.op == "shoot-enemy") shoot = i;
                if (step.op == "expect-eliminated") eliminated = i;
            }

            Assert.That(tag, Is.GreaterThanOrEqualTo(0), "готовность — жетон, снятый рукой");
            Assert.That(script.steps[tag].client, Is.Zero, "жетон снимают оба (готовы все игроки команд)");
            Assert.That(script.steps[tag].hand, Is.EqualTo("left"), "правая рука держит оружие");
            Assert.That(inZone, Is.GreaterThan(tag), "Countdown держится, пока живой игрок вне своей зоны");
            Assert.That(combat, Is.GreaterThan(inZone));
            Assert.That(shoot, Is.GreaterThan(combat), "оружие стреляет только в Combat");
            Assert.That(script.steps[shoot].client, Is.EqualTo(1));
            Assert.That(script.steps[shoot].until, Is.EqualTo("kill"));
            Assert.That(script.steps[shoot].shots, Is.GreaterThanOrEqualTo(4), "Viper: 4 попадания в торс на 100 здоровья");
            Assert.That(eliminated, Is.GreaterThan(combat));
            Assert.That(script.steps[eliminated].client, Is.EqualTo(2), "выбывает жертва — клиент 2");
        }

        [Test]
        public void CombatNextRound_GhostsGoHomeThenBuyAndGetReadyAgain()
        {
            InputScript script = InputScript.Load("combat-next-round", null, out string error);
            Assert.That(error, Is.Null);

            int verified = Index(script, s => s.op == "wait-server" && s.name == "round-verified");
            int home = Index(script, s => s.op == "walk" && s.target == "own-spawn-point", verified);
            int equipment = Index(script, s => s.op == "wait-server" && s.name == "equipment-2", verified);
            int rebuy = Index(script, s => s.op == "grab-wall-item", equipment);
            int bought = Index(script, s => s.op == "step" && s.name == "grabbed-2", equipment);
            int tag = Index(script, s => s.op == "grab-dog-tag", bought);
            int zone = Index(script, s => s.op == "walk" && s.target == "own-spawn-point", tag);

            Assert.That(verified, Is.GreaterThan(0), "раунд 2 начинается после Resolution раунда 1");
            Assert.That(home, Is.GreaterThan(verified).And.LessThan(equipment),
                "призраки идут домой до Equipment: Setup ждёт всех в своих зонах, возрождение — входом в зону");
            Assert.That(script.steps[home].client, Is.Zero, "домой идут оба — стрелок выбыл молча в Resolution");
            Assert.That(rebuy, Is.GreaterThan(equipment), "снаряжение снято в Resolution — покупка заново");
            Assert.That(script.steps[rebuy].item, Is.EqualTo("pistol"));
            Assert.That(bought, Is.GreaterThan(rebuy), "grabbed-2 несёт netId покупки раунда 2 для кассы сервера");
            Assert.That(tag, Is.GreaterThan(bought), "готовность раунда 2 — снова жетоном");
            Assert.That(zone, Is.GreaterThan(tag), "Countdown держится, пока живой игрок вне своей зоны");
        }

        [Test]
        public void AdminPause_ClientOneIsAdmin_ClientTwoDenied_PauseThenResume()
        {
            InputScript script = InputScript.Load("admin-pause", null, out string error);
            Assert.That(error, Is.Null);

            int rights = Index(script, s => s.op == "admin-rights");
            int teams = Index(script, s => s.op == "wait-server" && s.name == "teams-ok");
            int goLive = Index(script, s => s.op == "admin-command" && s.target == "GoLive");
            int live = Index(script, s => s.op == "wait-server" && s.name == "match-live");
            int bought = Index(script, s => s.op == "wait-server" && s.name == "oracle-done");
            int denied = Index(script, s => s.op == "admin-command-denied");
            int pause = Index(script, s => s.op == "admin-command" && s.target == "Pause");
            int resume = Index(script, s => s.op == "admin-command" && s.target == "Resume");
            int home = Index(script, s => s.op == "walk" && s.target == "own-spawn-point", resume);

            Assert.That(rights, Is.GreaterThan(0).And.LessThan(teams), "права — до проверки сервера «у одного клиента»");
            Assert.That(script.steps[rights].client, Is.EqualTo(1), "админ — клиент 1, клиент 2 — обычный игрок");
            Assert.That(goLive, Is.GreaterThan(teams).And.LessThan(live), "матч начинает админ, а не сервер");
            Assert.That(denied, Is.GreaterThan(bought).And.LessThan(pause), "отказ проверяется в Live, до паузы админа");
            Assert.That(script.steps[denied].client, Is.EqualTo(2));
            Assert.That(script.steps[denied].target, Is.EqualTo("Pause"));
            Assert.That(resume, Is.GreaterThan(pause));
            Assert.That(home, Is.GreaterThan(resume), "переигранный раунд ждёт всех в своих зонах");
            foreach (InputStep step in script.steps)
                if (step.op == "admin-command" || step.op == "admin-rights")
                    Assert.That(step.client, Is.EqualTo(1), $"{step.op} {step.target}: команды планшета — только у админа");
        }

        [Test]
        public void SeriesNextMap_AdminStartsSeries_RepeatsNextMapDuringTransition_AllWaitForEachMap()
        {
            InputScript script = InputScript.Load("series-next-map", null, out string error);
            Assert.That(error, Is.Null);

            int rights = Index(script, s => s.op == "admin-rights");
            int teams = Index(script, s => s.op == "wait-server" && s.name == "teams-ok");
            int start = Index(script, s => s.op == "admin-start-series");
            int first = Index(script, s => s.op == "expect-map" && s.target == "TestMap2");
            int verified = Index(script, s => s.op == "wait-server" && s.name == "series-verified");
            int next = Index(script, s => s.op == "admin-command" && s.target == "NextMap");
            int second = Index(script, s => s.op == "expect-map" && s.target == "TestMap1");
            int mapVerified = Index(script, s => s.op == "wait-server" && s.name == "map-verified");
            int goLive = Index(script, s => s.op == "admin-command" && s.target == "GoLive");
            int zone = Index(script, s => s.op == "walk" && s.target == "own-spawn-zone", goLive);

            Assert.That(rights, Is.GreaterThan(0).And.LessThan(teams));
            Assert.That(start, Is.GreaterThan(teams), "серию начинают после выбора команд: команды переживают переход");
            Assert.That(script.steps[start].client, Is.EqualTo(1), "серию начинает админ");
            Assert.That(script.steps[start].target, Is.EqualTo("elimination"));
            Assert.That(script.steps[start].maps, Is.EqualTo(new[] { "TestMap2", "TestMap1" }),
                "карта стенда первой — серия грузит её заново, вторая — смена сцены");
            Assert.That(first, Is.GreaterThan(start).And.LessThan(verified));
            Assert.That(next, Is.GreaterThan(verified).And.LessThan(second), "«Следующая карта» — из разминки карты 1");
            Assert.That(script.steps[next].repeat, Is.GreaterThanOrEqualTo(2),
                "повтор кнопки одним кадром: второй приходит, пока загрузчик занят переходом");
            Assert.That(mapVerified, Is.GreaterThan(second).And.LessThan(goLive));
            Assert.That(zone, Is.GreaterThan(goLive), "ввод перенесён на аватар новой карты — клиенты идут в свои зоны");
            Assert.That(script.steps[first].client, Is.Zero, "новую карту ждут оба клиента");
            Assert.That(script.steps[second].client, Is.Zero, "новую карту ждут оба клиента");
        }

        [Test]
        public void AdminCommands_MatchMapCommand()
        {
            Assert.That(InputScript.AdminCommands, Is.EquivalentTo(System.Enum.GetNames(typeof(MapCommand))),
                "проверка сценария знает ровно команды планшета");
        }

        private static int Index(InputScript script, System.Func<InputStep, bool> match, int after = -1)
        {
            for (int i = after + 1; i < script.steps.Length; i++)
                if (match(script.steps[i])) return i;
            return -1;
        }

        [Test]
        public void ShootingSummary_FitsBarrierStepData()
        {
            var outcome = new ScriptedGunner.Outcome
            {
                Hit = true, Killed = true, Presses = int.MaxValue, Shots = int.MaxValue,
                HealthBefore = float.MaxValue, HealthAfter = float.MinValue, Distance = float.MaxValue
            };

            Assert.That(E2EStepRules.ValidateStep("shot", outcome.StepData()), Is.Null);
        }

        [TestCase(45f, 0f, 1f)]
        [TestCase(0f, 0f, 1f)]
        [TestCase(-30f, 0f, 1f)]
        [TestCase(45f, 90f, 0f)]
        [TestCase(0f, 180f, -1f)]
        public void WeaponLevel_IsRollAroundBarrel_NotPitch(float pitch, float roll, float expected)
        {
            var go = new GameObject("muzzle");
            try
            {
                go.transform.rotation = Quaternion.Euler(pitch, 30f, 0f) * Quaternion.AngleAxis(roll, Vector3.forward);
                Assert.That(ScriptedGunner.Level(go.transform), Is.EqualTo(expected).Within(1e-3f),
                    "наклон ствола вниз/вверх не делает оружие «не ровным», крен вокруг ствола — делает");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
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
            Assert.That(E2ERunner.IsRegistered("scripted-combat-hit"), Is.True);
            Assert.That(E2ERunner.IsRegistered("scripted-combat-round"), Is.True);
            Assert.That(E2ERunner.IsRegistered("scripted-combat-next-round"), Is.True);
            Assert.That(E2ERunner.IsRegistered("scripted-admin-pause"), Is.True);
            Assert.That(E2ERunner.IsRegistered("scripted-series-next-map"), Is.True);
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
