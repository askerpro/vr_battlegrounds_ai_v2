using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Повторяемая сборка контроллеров Blaze из существующих Rifle/Pistol humanoid-клипов.</summary>
    public static class BotCombatSetup
    {
        private const string Folder = "Assets/Resources/Bots";
        private const string Rifle = "Assets/Art/Animations/Locomotion/MixamoRifle/";
        private const string Pistol = "Assets/Art/Animations/Locomotion/MixamoPistol/";
        private const string Blaze = "Assets/ThirdParty/Blaze AI/Demos/Assets/Animations/Cover Shooter/";

        [MenuItem("Tools/VR Battlegrounds/Bots/Build Blaze Combat Assets")]
        private static void BuildFromMenu() => GameLog.Player.Info(Build());

        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Сборка BotCombatAssets только вне Play Mode.");
            // Сначала проверяются все входы, затем изменяются только собственные генерируемые ассеты.
            var pistol = new Dictionary<string, AnimationClip>
            {
                ["Idle"] = Clip(Blaze + "Gun Aim.fbx"),
                ["Move"] = Clip(Blaze + "Walking.fbx"),
                ["Back"] = Clip(Blaze + "Walk Backwards.fbx"),
                ["Left"] = Clip(Blaze + "Left Strafe.fbx"),
                ["Right"] = Clip(Blaze + "Right Strafe.fbx"),
                ["Shoot"] = Clip(Blaze + "Shoot.fbx")
            };
            var rifle = new Dictionary<string, AnimationClip>
            {
                ["Idle"] = Clip(Rifle + "Rifle_IdleAiming.fbx"),
                ["Move"] = Clip(Rifle + "Rifle_WalkForward.fbx"),
                ["Back"] = Clip(Rifle + "Rifle_WalkBackward.fbx"),
                ["Left"] = Clip(Rifle + "Rifle_WalkLeft.fbx"),
                ["Right"] = Clip(Rifle + "Rifle_WalkRight.fbx"),
                // В MixamoRifle нет отдельного shoot: оружие стреляет штатным механизмом.
                ["Shoot"] = Clip(Rifle + "Rifle_IdleAiming.fbx")
            };
            // Пистолетный Mixamo тоже проверяем: это существующая альтернативная библиотека.
            Clip(Pistol + "Pistol_PistolIdle.fbx");
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Bots");
            var p = Controller(Folder + "/BotPistol.controller", pistol);
            var r = Controller(Folder + "/BotRifle.controller", rifle);
            string path = Folder + "/BotCombatAssets.asset";
            BotCombatAssets assets = AssetDatabase.LoadAssetAtPath<BotCombatAssets>(path);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<BotCombatAssets>();
                AssetDatabase.CreateAsset(assets, path);
            }
            assets.pistol = p;
            assets.rifle = r;
            EditorUtility.SetDirty(assets);
            // Общий редактор: сохраняем только три ассета этого генератора.
            AssetDatabase.SaveAssetIfDirty(p);
            AssetDatabase.SaveAssetIfDirty(r);
            AssetDatabase.SaveAssetIfDirty(assets);
            return "[BotCombatSetup] PASS: Rifle/Pistol, по 6 состояний без маски рук; игровые префабы не изменены.";
        }

        private static AnimationClip Clip(string path)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null || !clip.isHumanMotion || clip.length <= 0f)
                throw new InvalidOperationException("Нет пригодного humanoid-клипа: " + path);
            return clip;
        }

        private static AnimatorController Controller(string path, Dictionary<string, AnimationClip> clips)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states) machine.RemoveState(child.state);
            int i = 0;
            foreach (var entry in clips)
            {
                AnimatorState state = machine.AddState(entry.Key, new Vector3(250f, 50f * i++));
                state.motion = entry.Value;
                state.iKOnFeet = false;
                state.writeDefaultValues = true;
                if (entry.Key == "Idle") machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }
    }
}
