using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Avatars;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Легаси-сборка BrowningHiPower из FBX пака Hands без регистрации новых предметов и правки аватаров: затвор на
    /// задержке (клип <c>Shot_end</c>, профиль HoldOpen) и ход, расширенный до этой позы (<see cref="HandsPackMechanism.Install" />).
    /// AR15 и FabarmSDASS переведены на полный рецепт (<see cref="HandsPackWeaponBuilder.Full" />, решение 2026-10-23);
    /// снимков доноров больше нет — всё, что не выводится из пака, задаёт рецепт <see cref="Browning" /> и общие значения
    /// <see cref="HandsPackBuildDefaults" />.
    /// </summary>
    public static class HandsPackLegacyRebuild
    {
        private const string Gun = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower.prefab";
        private const string GunMag = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower_Magazine.prefab";

        /// <summary>
        /// Пистолет Browning Hi-Power, магазин на 13. Теги, размер и шаблон магазина — явно (раньше — из снимка
        /// <c>Gun_real</c>): масштаб сборки 0,68 — корпус снимка (легаси-длина = габарит × этот масштаб), шаблон магазина —
        /// свой <c>BrowningHiPower_Magazine</c>, вспышка у дула — <c>Muzzle_Pistol</c>, как у принятой сборки. Ёмкость — баланс.
        /// Статичные детали — все остальные детали источника; неназванной детали источника быть не должно.
        /// </summary>
        public static HandsPackWeaponRecipe Browning
        {
            get
            {
                HandsPackWeaponRecipe r = HandsPackWeaponBuilder.Pistol(new HandsPackWeaponRecipe
                {
                    Name = "BrowningHiPower", PrefabFolder = "BrowningHiPower", Folder = "Hands_Gun", BodyPart = "Base_mesh",
                    PoseClip = "Aiming_Idle", ActionClip = "Shot", EmptyActionClip = "Shot_end", TriggerPart = "Trigger_mesh", ActionPart = "Gate_mesh",
                    RootScale = 0.68f, StaticParts = null, ClaimAllSourceParts = true,
                    SupportGrip = true, MechanismMotion = true, MuzzleEffect = HandsPackWeaponBuilder.MuzzlePistol,
                    UxrTag = "Gun", MagazineName = "BrowningHiPower_Magazine", MagazinePart = "Magazine_mesh", MagazineTag = "MagGun",
                    MagazineCapacity = 13,
                    ReadinessProfile = WeaponSystemAuthoring.DetachableHoldOpenProfile,
                    // Выстрел — тот же звук пака, что звучал у принятой сборки (9Run_Set/Shot 4 — побайтовая копия 8Pistol/Shot 4).
                    PackSounds = new HandsPackSoundRecipe
                    {
                        Shot = HandsPackAudio.Pack + "8Pistol/Shot 4.mp3", Reload = HandsPackAudio.Pack + "8Pistol/Reload_Full.mp3",
                        ReloadClip = "Recharge", Bolt = HandsPackAudio.Pack + "8Pistol/Zatvor.mp3"
                    }
                });
                r.MagazineBase = GunMag; // шаблон первой сборки — свой магазин (он и так шаблон, пока существует)
                return r;
            }
        }

        private static readonly string[] Targets = { Gun, GunMag };

        private static void OpenWorkbench() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenMaintenance(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Build);

        /// <param name="poses">
        /// Хваты переносятся из текущих префабов — только <see cref="WeaponGripPoseMode.KeepCurrent" />. Позы и хваты
        /// аватаров сборка не регистрирует (явная команда <c>AvatarGripRegistration</c> из <c>_orig</c>); она готовит
        /// исходные позы пака и snap-точки <c>_orig</c> (<see cref="PackOrigPoses" />).
        /// </param>
        public static void RebuildAll(WeaponGripPoseMode poses = WeaponGripPoseMode.KeepCurrent)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Пересборка Hands запрещена в Play Mode");
            WeaponGripCarryOver.RequireBuilderMode(poses, "HandsPackLegacyRebuild");
            foreach (string path in Targets)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new InvalidOperationException($"Отсутствует baseline: {path}");
            HandsPackWeaponRecipe recipe = Browning;

            // Движение пишет preflight до пересборки: при отказе оно возвращается (иначе остались бы файлы с новыми rid
            // managed reference).
            var motion = WeaponRebuildTransaction.Begin(Array.Empty<string>(), new[] { HandsPackMechanism.Folder(recipe) });
            try
            {
                // Ошибка структуры источника должна обнаружиться до перезаписи игрового префаба.
                GameLog.WeaponSystem.Info(Preflight(recipe));
                // Движение, записанное preflight, — до копий пересборки: иначе область ствола начнётся несохранённой.
                OwnAssetSave.Save(new[] { HandsPackMechanism.Folder(recipe) });
                Rebuild(recipe, poses);
            }
            catch
            {
                GameLog.WeaponSystem.Error($"[HandsPackLegacyRebuild] движение возвращено:\n{motion.Rollback()}");
                throw;
            }
        }

        private static void Rebuild(HandsPackWeaponRecipe recipe, WeaponGripPoseMode poses)
        {
            using var source = new HandsPackWeapon(recipe.Folder, recipe.BodyPart);
            HandsPackMechanism.ResolveStaticParts(recipe, source);
            string path = HandsPackWeaponBuilder.WeaponPath(recipe);
            string magPath = HandsPackWeaponBuilder.MagazinePath(recipe);
            var outputs = HandsPackWeaponBuilder.RebuildOutputs(recipe);
            // Всё или ничего: отказ возвращает файлы ствола.
            WeaponRebuildFinish.Run("HandsPackLegacyRebuild", path, magPath, outputs, () =>
            {
                var identity = HandsPackWeaponBuilder.PrefabIdentity.Capture(path, magPath);
                // Хваты снимаются до сборки: она перезаписывает оружие и магазин.
                WeaponGripCarryOver.Snapshot grips = WeaponGripCarryOver.Capture(path, magPath);
                // Звуки — до геометрии и на своём экземпляре модели: импорт новых WAV обновляет базу ассетов.
                using (var timing = new HandsPackWeapon(recipe.Folder, recipe.BodyPart))
                    HandsPackAudio.MakeSounds(recipe, timing);
                // Хост WeaponSystem — после клипов затвора: профиль HoldOpen без них не настраивается.
                HandsPackWeaponBuilder.Build(recipe, source, configureHost: false);
                ApplyMagazine(magPath);
                HandsPackMechanism.Install(path, recipe, source);
                HandsPackWeaponBuilder.ConfigureHost(path, recipe);
                identity.Verify();
                ValidateReferences(path, magPath);
                WeaponGripCarryOver.Restore(grips, poses, HandsPackWeaponBuilder.MagazineBaseGeometry(recipe), HandsPackWeaponBuilder.DroppedGripAvatars);
            });
            GameLog.WeaponSystem.Info($"[HandsPackLegacyRebuild] {recipe.Name}: FBX, рецепт и generic motion сохранены");
        }

        private static string Preflight(HandsPackWeaponRecipe recipe)
        {
            using var source = new HandsPackWeapon(recipe.Folder, recipe.BodyPart);
            HandsPackMechanism.ResolveStaticParts(recipe, source);
            HandsPackMechanism.ValidateSourceParts(recipe, source);
            source.Clip(recipe.PoseClip);
            source.Clip(recipe.ActionClip);
            if (!string.IsNullOrEmpty(recipe.EmptyActionClip)) source.Clip(recipe.EmptyActionClip);
            if (!string.IsNullOrEmpty(recipe.PackSounds?.ReloadClip)) source.Clip(recipe.PackSounds.ReloadClip);
            foreach (string sound in HandsPackAudio.Sources(recipe))
                if (AssetDatabase.LoadAssetAtPath<AudioClip>(sound) == null) throw new InvalidOperationException($"{recipe.Name}: нет звука пака {sound}");
            Matrix4x4 placement = HandsPackWeaponBuilder.BodyPlacement(recipe, source);
            PartMotion action = source.Measure(recipe.ActionPart, recipe.ActionClip, recipe.RestClip);
            Vector3 actionTravel = HandsPackWeaponBuilder.ResolveActionTravel(recipe, source, placement, out bool recipeTravel);
            var trigger = HandsPackWeaponBuilder.ResolveTriggerRotation(recipe, source.Measure(recipe.TriggerPart, recipe.ActionClip, recipe.RestClip));
            var motion = HandsPackMechanism.Export(recipe, source);
            return $"{recipe.Name}: {source.ModelPath}; body={recipe.BodyPart}; action={recipe.ActionPart}; trigger={recipe.TriggerPart}; " +
                   $"static=[{string.Join(",", recipe.StaticParts)}]; " +
                   $"excluded=[{string.Join(",", recipe.ExcludedSourceParts ?? Array.Empty<string>())}]; " +
                   $"Fire={motion.Fire.SourceGuid}/{motion.Fire.SourceLocalId}; Empty={(motion.Empty == null ? "нет" : motion.Empty.SourceGuid)}; " +
                   $"Manual={(motion.Manual == null ? "нет" : motion.Manual.SourceGuid)}; sourceTravel={action.Far:F5}; sourceActionRotation={action.MaxAngle:F5}; " +
                   $"actionTravel={actionTravel:F5}/{actionTravel.magnitude:F5}; actionOrigin={(recipeTravel ? "recipe (нет в паке)" : "source clip")}; " +
                   $"trigger={trigger.Axis}/{trigger.Degrees:F5}; triggerOrigin={(trigger.FromRecipe ? "recipe (нет в паке)" : "source clip")}; " +
                   $"rootScale={recipe.RootScale:F5}; finalRootScale={recipe.FinalRootScale:F5}; mag={recipe.MagazineName}/{recipe.MagazineCapacity}";
        }

        private static void ApplyMagazine(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                WeaponInteractionInstaller.ApplyMagazine(root);
                GameTagsTool.ApplyToHierarchy(root, path, new GameTagsTool.Result());
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ValidateReferences(string weapon, string magazine)
        {
            var definition = AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponInfo>)
                .Single(info => info.WeaponPrefab != null && AssetDatabase.GetAssetPath(info.WeaponPrefab) == weapon);
            if (definition.MagazinePrefab == null || AssetDatabase.GetAssetPath(definition.MagazinePrefab) != magazine)
                throw new InvalidOperationException($"WeaponInfo потерял ссылку на магазин: {weapon}");
            GameObject root = PrefabUtility.LoadPrefabContents(weapon);
            try
            {
                if (root.GetComponentsInChildren<Animator>(true).Length != 0 || root.GetComponentsInChildren<Animation>(true).Length != 0)
                    throw new InvalidOperationException($"Осталась legacy animation: {weapon}");
                if (root.GetComponentsInChildren<NetworkIdentity>(true).Length != 1)
                    throw new InvalidOperationException($"Вложенный NetworkIdentity: {weapon}");
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root) != 0)
                    throw new InvalidOperationException($"Missing script: {weapon}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
