using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Детали источника и анимация механизма стволов пака Hands — общее для полного пути
    /// (<see cref="HandsPackWeaponBuilder.BuildFull" />) и легаси-сборки BrowningHiPower (<see cref="HandsPackLegacyRebuild" />).
    ///
    /// <para>
    /// Анимация механизма (<see cref="WeaponMechanismMotion" />, рецепт <see cref="HandsPackWeaponRecipe.MechanismMotion" />):
    /// дорожки деталей механизма (затвор, спуск, подвижные детали корпуса) из клипа выстрела пака — их проигрывает
    /// <see cref="WeaponMechanismVisuals" />, хост WeaponSystem переносит их в свой механизм. Порядок сборки: ствол без хоста
    /// (<c>Build(..., configureHost: false)</c>) → <see cref="Install" /> → хост
    /// (<see cref="HandsPackWeaponBuilder.ConfigureHost(string, HandsPackWeaponRecipe)" />): профиль HoldOpen без клипов затвора
    /// не настраивается.
    /// </para>
    /// </summary>
    public static class HandsPackMechanism
    {
        public const string Root = "Assets/Art/Weapons/HandsPack";

        /// <summary>Папка данных механизма ствола (в откате пересборки).</summary>
        public static string Folder(HandsPackWeaponRecipe r) => $"{Root}/{r.Name}";

        public static string MotionPath(HandsPackWeaponRecipe r) => $"{Folder(r)}/MechanismMotion.asset";

        /// <summary>Детали, которые рецепт собирает: корпус, спуск, механизм, магазин, статичные и дополнительные.</summary>
        public static IEnumerable<string> Parts(HandsPackWeaponRecipe r) =>
            new[] { r.BodyPart, r.TriggerPart, r.Action != HandsPackWeaponRecipe.ActionKind.None ? r.ActionPart : null, r.MagazinePart }
                .Concat(r.StaticParts ?? Array.Empty<string>()).Concat(r.ActionExtraParts ?? Array.Empty<string>())
                .Concat(r.MagazineExtraParts ?? Array.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)).Distinct();

        /// <summary>
        /// <see cref="HandsPackWeaponRecipe.StaticParts" /> null — все детали источника, которые рецепт не назвал иначе и не
        /// исключил (так статичные детали считала легаси-сборка: по фактическому <see cref="IWeaponModel" />, без догадок).
        /// </summary>
        public static void ResolveStaticParts(HandsPackWeaponRecipe r, HandsPackWeapon source)
        {
            if (r.StaticParts != null) return;
            r.StaticParts = source.Parts.Except(Parts(r).Concat(r.ExcludedSourceParts ?? Array.Empty<string>()))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Структура источника до первой записи: названные детали есть, собираемые не исключены, у каждой есть меш; при
        /// <see cref="HandsPackWeaponRecipe.ClaimAllSourceParts" /> — ни одной детали источника без решения рецепта.
        /// </summary>
        public static void ValidateSourceParts(HandsPackWeaponRecipe r, HandsPackWeapon source)
        {
            string[] available = source.Parts.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            string[] excluded = r.ExcludedSourceParts ?? Array.Empty<string>();
            string[] required = Parts(r).Concat(new[] { r.LoadingPart, r.MuzzlePart, r.ActionGripPart })
                .Concat(excluded).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            string[] missing = required.Except(available).ToArray();
            if (missing.Length != 0)
                throw new InvalidOperationException($"{r.Name}: source {source.ModelPath}; missing=[{string.Join(",", missing)}]; " +
                    $"available renderer parts=[{string.Join(",", available)}]");
            string[] overlap = Parts(r).Intersect(excluded).ToArray();
            if (overlap.Length != 0)
                throw new InvalidOperationException($"{r.Name}: source parts одновременно assembled/excluded=[{string.Join(",", overlap)}]");
            if (r.ClaimAllSourceParts)
            {
                string[] unclaimed = available.Except(Parts(r).Concat(excluded)).ToArray();
                if (unclaimed.Length != 0)
                    throw new InvalidOperationException($"{r.Name}: source {source.ModelPath}; unclaimed renderer parts=[{string.Join(",", unclaimed)}]");
            }
            foreach (string part in required) source.MeshOf(part);
        }

        /// <summary>
        /// Пишет <see cref="MotionPath" />: дорожки подвижных деталей из клипа выстрела. У помпы клип выстрела — ручной цикл
        /// (<c>Manual</c>: помпа и детали), выстрел — только спуск. <see cref="HandsPackWeaponRecipe.EmptyActionClip" /> —
        /// поза затвора на задержке (<c>Empty</c>). Существующий ассет перезаписывается на месте (GUID сохраняется).
        /// </summary>
        public static WeaponMechanismMotion Export(HandsPackWeaponRecipe r, HandsPackWeapon source)
        {
            string[] parts = Parts(r).Except(new[] { r.BodyPart, r.MagazinePart }).Distinct().ToArray();
            var result = ScriptableObject.CreateInstance<WeaponMechanismMotion>();
            try
            {
                var fire = KinemationAnimationExporter.Sample(source, source.Clip(r.ActionClip), parts, source.PartIdentity, false, true);
                if (r.Action == HandsPackWeaponRecipe.ActionKind.Pump)
                {
                    result.Manual = fire;
                    // Pump-cycle из FPS-пака не досылает патрон за игрока. Его Gate/цевьё сохраняются в Manual.
                    result.Fire = KinemationAnimationExporter.Sample(source, source.Clip(r.ActionClip), new[] { r.TriggerPart }, source.PartIdentity, false, true);
                }
                else result.Fire = fire;
                if (!string.IsNullOrEmpty(r.EmptyActionClip))
                    result.Empty = KinemationAnimationExporter.Sample(source, source.Clip(r.EmptyActionClip), parts, source.PartIdentity, true, true);
                string folder = Folder(r);
                KinemationWeapon.EnsureFolder(folder);
                string path = MotionPath(r);
                var saved = AssetDatabase.LoadAssetAtPath<WeaponMechanismMotion>(path);
                if (saved == null) { AssetDatabase.CreateAsset(result, path); return result; }
                EditorUtility.CopySerialized(result, saved); EditorUtility.SetDirty(saved);
                return saved;
            }
            finally
            {
                source.Sample(null);
                if (!AssetDatabase.Contains(result)) Object.DestroyImmediate(result);
            }
        }

        /// <summary>
        /// Ставит <see cref="WeaponMechanismVisuals" /> на собранный без хоста префаб <paramref name="path" />: привязки дорожек
        /// к деталям (деталь с коллайдером получает видимую копию <c>MechanismVisual</c>), ход затвора, расширенный до позы
        /// <c>Empty</c>, и области подсветки по центрам ладоней рук пака (как в <see cref="HandsPackWeaponBuilder.Build(HandsPackWeaponRecipe)" />).
        /// </summary>
        public static void Install(string path, HandsPackWeaponRecipe r, HandsPackWeapon source)
        {
            WeaponMechanismMotion motion = AssetDatabase.LoadAssetAtPath<WeaponMechanismMotion>(MotionPath(r)) ??
                throw new InvalidOperationException($"{r.Name}: нет анимации механизма {MotionPath(r)} — сначала Export");
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform body = root.transform.Find("MeshContainer/" + r.BodyObjectName);
                // Области хвата — по центрам ладоней рук пака, как в HandsPackWeaponBuilder.Build.
                var interaction = WeaponInteractionRecipes.For(root, r, HandsPackWeaponBuilder.PalmPoints(source, r.PoseClip, root.transform, body));
                if (r.InteractionFolder != null) interaction.AssetFolder = r.InteractionFolder;
                WeaponInteractionInstaller.Apply(root, interaction);
                var action = root.transform.Find(r.Action == HandsPackWeaponRecipe.ActionKind.Pump ? "Pump" : "Slide").GetComponent<UxrGrabbableObject>();
                if (!AutomaticWeaponSlideFeedback.TryGetSlideTravel(action, out _, out float originalLength) ||
                    float.IsNaN(originalLength) || float.IsInfinity(originalLength))
                    throw new InvalidOperationException($"{r.Name}: rebuilt action не содержит корректного ручного хода");
                var anchor = root.GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
                var tracks = (motion.Fire?.Tracks ?? Array.Empty<WeaponMechanismMotion.Track>())
                    .Concat(motion.Empty?.Tracks ?? Array.Empty<WeaponMechanismMotion.Track>()).GroupBy(t => t.Part).Select(g => g.First()).ToArray();
                var bindings = new List<WeaponMechanismVisuals.Binding>();
                foreach (var track in tracks)
                {
                    string name = track.Part == r.TriggerPart ? "Trigger" : HandsPackWeaponBuilder.Clean(track.Part);
                    Transform target = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == name).transform;
                    if (target.GetComponent<Collider>() != null)
                    {
                        var visual = new GameObject("MechanismVisual").transform;
                        visual.SetParent(target, false);
                        visual.gameObject.AddComponent<MeshFilter>().sharedMesh = target.GetComponent<MeshFilter>().sharedMesh;
                        visual.gameObject.AddComponent<MeshRenderer>().sharedMaterials = target.GetComponent<MeshRenderer>().sharedMaterials;
                        target.GetComponent<MeshRenderer>().enabled = false;
                        target = visual;
                    }
                    bindings.Add(new WeaponMechanismVisuals.Binding { Part = track.Part, Target = target, RestPosition = target.localPosition,
                        RestRotation = target.localRotation, Rotate = track.AnimateRotation });
                }
                if (motion.Empty != null && AutomaticWeaponSlideFeedback.TryGetSlideTravel(action, out Vector3 direction, out _))
                {
                    float length = originalLength;
                    source.Sample(null);
                    foreach (var track in motion.Empty.Tracks)
                    {
                        var binding = bindings.Single(b => b.Part == track.Part);
                        if (!binding.Target.IsChildOf(action.transform)) continue;
                        Vector3 rest = source.PartInBody(track.Part).GetColumn(3);
                        foreach (var key in track.Keys)
                            length = Mathf.Max(length, Vector3.Dot(root.transform.InverseTransformVector(body.TransformVector(key.Position - rest)), direction));
                    }
                    Vector3 travel = direction * (length + 0.004f);
                    action.TranslationLimitsMin = Vector3.Min(Vector3.zero, travel);
                    action.TranslationLimitsMax = Vector3.Max(Vector3.zero, travel);
                }
                var so = new SerializedObject(root.AddComponent<WeaponMechanismVisuals>());
                so.FindProperty("_motion").objectReferenceValue = motion;
                so.FindProperty("_body").objectReferenceValue = body;
                so.FindProperty("_slide").objectReferenceValue = action;
                so.FindProperty("_contactPart").objectReferenceValue = interaction.ActionGripPart;
                so.FindProperty("_magazineAnchor").objectReferenceValue = anchor;
                so.FindProperty("_originalSlideLength").floatValue = originalLength;
                var items = so.FindProperty("_bindings"); items.arraySize = bindings.Count;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var binding = bindings[i]; var item = items.GetArrayElementAtIndex(i);
                    item.FindPropertyRelative("Part").stringValue = binding.Part;
                    item.FindPropertyRelative("Target").objectReferenceValue = binding.Target;
                    item.FindPropertyRelative("RestPosition").vector3Value = binding.RestPosition;
                    item.FindPropertyRelative("RestRotation").quaternionValue = binding.RestRotation;
                    item.FindPropertyRelative("Rotate").boolValue = binding.Rotate;
                    item.FindPropertyRelative("InMagazine").boolValue = false;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                GameTagsTool.ApplyToHierarchy(root, path, new GameTagsTool.Result());
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
