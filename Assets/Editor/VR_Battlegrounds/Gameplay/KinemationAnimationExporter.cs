using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Детерминированный экспорт независимых механических каналов, включая постоянные Empty-позы.</summary>
    public static class KinemationAnimationExporter
    {
        public static WeaponMechanismMotion Export(KinemationWeaponRecipe recipe, KinemationWeapon source)
        {
            string name = recipe.Weapon.Name;
            string[] parts = name switch
            {
                "SRM12" => new[] { "Bolt", "Trigger" },
                "R08" => new[] { "Hammer", "Trigger", "Cylinder", "Ammo_01", "Ammo_02", "Ammo_03", "Ammo_04", "Ammo_05", "Ammo_06", "Ammo_07", "Ammo_08", "Ejector" },
                "AK105" => new[] { "Charger", "Trigger" },
                "MKR9" => new[] { "Bolt", "ChargingHandle", "Trigger" },
                "Viper" => new[] { "Bolt", "Hammer", "Trigger", "Charging_Handle" },
                "Herrington" => new[] { "Bolt", "Trigger" },
                "Mk14" => new[] { "Bolt", "BoltCharger", "Trigger" },
                "TR15" => new[] { "Bolt", "Charger", "MagRelease", "Trigger" },
                _ => throw new ArgumentException(name)
            };
            string empty = name switch { "Viper" => "A_W_WK-11_Viper_Fire_Empty", "TR15" => "A_W_TR15_Fire_Out", "Herrington" => "A_W_Herrington_11-87_Fire_Out", _ => null };
            var result = ScriptableObject.CreateInstance<WeaponMechanismMotion>();
            var fire = Sample(source, recipe.Weapon.ActionClip, parts, false);
            if (name == "SRM12") result.Manual = fire; else result.Fire = fire;
            if (empty != null) result.Empty = Sample(source, empty, parts, true);
            string folder = $"{KinemationWeapon.ArtRoot}/{name}";
            KinemationWeapon.EnsureFolder(folder);
            string path = $"{folder}/MechanismMotion.asset";
            var saved = AssetDatabase.LoadAssetAtPath<WeaponMechanismMotion>(path);
            if (saved == null) { AssetDatabase.CreateAsset(result, path); saved = result; }
            else { EditorUtility.CopySerialized(result, saved); UnityEngine.Object.DestroyImmediate(result); EditorUtility.SetDirty(saved); }
            return saved;
        }

        private static WeaponMechanismMotion.Cycle Sample(KinemationWeapon source, string clipName, string[] parts, bool hold)
        {
            AnimationClip clip = source.Clip(clipName);
            var times = new SortedSet<float> { 0f, clip.length };
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                foreach (Keyframe key in AnimationUtility.GetEditorCurve(clip, binding).keys) times.Add(Mathf.Clamp(key.time, 0f, clip.length));
            int count = Mathf.CeilToInt(clip.length * 120f);
            for (int i = 0; i <= count; i++) times.Add(Mathf.Min(i / 120f, clip.length));
            var tracks = new List<WeaponMechanismMotion.Track>();
            foreach (string part in parts)
            {
                if (!source.Parts.Contains(part)) throw new InvalidOperationException($"{clipName}: нет механической детали {part}");
                var identity = source.PartIdentity(part);
                var track = new WeaponMechanismMotion.Track { Part = part, SourcePath = identity.path, SourceBoneIndex = identity.boneIndex, AnimateRotation = part != "Trigger" };
                WeaponMechanismMotion.Key Read(float time)
                {
                    source.Sample(null); source.Sample(clipName, time);
                    Matrix4x4 pose = source.PartInBody(part);
                    Quaternion rotation = pose.rotation.normalized;
                    return new WeaponMechanismMotion.Key { Time = time, Position = pose.GetColumn(3), Rotation = rotation };
                }
                var sampled = times.ToDictionary(time => time, Read);
                bool accepted = false;
                for (int pass = 0; pass < 12; pass++)
                {
                    track.Keys = sampled.Values.OrderBy(key => key.Time).ToArray();
                    var added = new List<WeaponMechanismMotion.Key>();
                    for (int i = 0; i + 1 < track.Keys.Length; i++)
                    {
                        // Один midpoint пропускает изгибы по обе стороны симметричного интервала.
                        foreach (float fraction in new[] { 0.25f, 0.5f, 0.75f })
                        {
                            float time = Mathf.Lerp(track.Keys[i].Time, track.Keys[i + 1].Time, fraction);
                            var expected = Read(time);
                            Pose actual = track.Evaluate(time);
                            if (Vector3.Distance(actual.position, expected.Position) > 0.00075f || Quaternion.Angle(actual.rotation, expected.Rotation) > 0.35f)
                                added.Add(expected);
                        }
                    }
                    if (added.Count == 0) { accepted = true; break; }
                    foreach (var key in added) sampled[key.Time] = key;
                }
                if (!accepted) throw new InvalidOperationException($"{clipName}/{part}: не удалось уточнить временную сетку");
                // Контроль между экспортированными ключами ловит ошибку интерполяции и неверную систему координат.
                for (int i = 0; i + 1 < track.Keys.Length; i++)
                {
                    float time = (track.Keys[i].Time + track.Keys[i + 1].Time) * 0.5f;
                    source.Sample(null); source.Sample(clipName, time);
                    Matrix4x4 expected = source.PartInBody(part);
                    Pose actual = track.Evaluate(time);
                    if (Vector3.Distance(actual.position, expected.GetColumn(3)) > 0.001f || Quaternion.Angle(actual.rotation, expected.rotation) > 0.5f)
                        throw new InvalidOperationException($"{clipName}/{part}: экспорт превышает 1 мм / 0,5° на {time:F6}");
                }
                tracks.Add(track);
            }
            source.Sample(null);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId);
            return new WeaponMechanismMotion.Cycle { SourceGuid = guid, SourceLocalId = localId, Duration = clip.length, HoldEnd = hold, Tracks = tracks.ToArray() };
        }
    }
}
