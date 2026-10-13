using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Повторяемая сборка контроллера минимального Blaze-прокси бота (<see cref="BotCombatDriver"/>):
    /// <c>Assets/Resources/Bots/BotBlazeProxy.controller</c>, слой 0 с шестью состояниями без motion — Idle (по умолчанию),
    /// Move, Back, Left, Right, Shoot (имена, которые играет Blaze). Humanoid, клипы и маски не нужны: прокси — только
    /// навигация и решения Blaze, позу бота пишет BotAvatarDriver. Ассет коммитится (UnityEditor.Animations в плеере нет).
    /// </summary>
    public static class BotBlazeProxySetup
    {
        public const string Folder = "Assets/Resources/Bots";
        public const string ControllerPath = Folder + "/BotBlazeProxy.controller";

        /// <summary>Состояния слоя 0; первое — по умолчанию.</summary>
        public static readonly string[] States = { "Idle", "Move", "Back", "Left", "Right", "Shoot" };

        [MenuItem("Tools/VR Battlegrounds/Bots/Build Blaze Proxy Controller")]
        private static void BuildFromMenu() => GameLog.Player.Info(Build());

        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Сборка контроллера прокси Blaze только вне Play Mode.");
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Bots");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // Ровно один слой без маски и параметров: Blaze зовёт только CrossFade по имени состояния слоя 0.
            while (controller.layers.Length > 1) controller.RemoveLayer(controller.layers.Length - 1);
            while (controller.parameters.Length > 0) controller.RemoveParameter(controller.parameters[0]);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states) machine.RemoveState(child.state);
            foreach (ChildAnimatorStateMachine child in machine.stateMachines) machine.RemoveStateMachine(child.stateMachine);
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions) machine.RemoveAnyStateTransition(transition);
            foreach (AnimatorTransition transition in machine.entryTransitions) machine.RemoveEntryTransition(transition);

            for (int i = 0; i < States.Length; i++)
            {
                AnimatorState state = machine.AddState(States[i], new Vector3(250f, 50f * i));
                state.motion = null;
                state.speed = 1f;
                state.iKOnFeet = false;
                state.writeDefaultValues = true;
                if (i == 0) machine.defaultState = state;
            }

            EditorUtility.SetDirty(controller);
            // Общий редактор: сохраняется только ассет этого генератора.
            AssetDatabase.SaveAssetIfDirty(controller);
            return $"[BotBlazeProxySetup] PASS: {ControllerPath} — {States.Length} состояний без motion, по умолчанию {States[0]}; " +
                   $"Resources: {BotCombatDriver.ProxyControllerResource}.";
        }
    }
}
