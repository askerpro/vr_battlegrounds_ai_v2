using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Обратная по времени копия клипа: «встаёт» → «садится». У Mixamo такого экспорта нет (Mirror там — лево ↔ право).
    /// Кривые клипа (у humanoid — мышцы и корень) запекаются задом наперёд в <c>&lt;имя&gt;_Reverse.anim</c> рядом с
    /// исходником; настройки клипа (цикл, запекание корня, смещения) — как у исходника. Повторный запуск пересобирает
    /// копию на месте (GUID тот же). Высота корня «по стопам» у копии подгоняется смещением: первый кадр копии — на высоте
    /// последнего кадра исходника. Меню — для выделенных клипов или моделей; агенту — <see cref="ReverseAll"/>.
    /// </summary>
    public static class AnimationClipReverse
    {
        public const string Suffix = "_Reverse";

        [MenuItem("Tools/VR Battlegrounds/Avatars/Reverse Selected Clips (обратное время)")]
        private static void ReverseSelected()
        {
            var paths = Selection.objects.Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            int made = ReverseAll(paths);
            GameLog.Debug.Info($"[AnimationClipReverse] Обратных копий: {made} (выделено {paths.Length}).");
        }

        [MenuItem("Tools/VR Battlegrounds/Avatars/Reverse Selected Clips (обратное время)", true)]
        private static bool ReverseSelectedValidate() => Selection.objects.Any(o => o is AnimationClip || o is GameObject);

        /// <summary>Обратные копии главного клипа каждого пути (FBX или .anim). Возвращает число созданных/обновлённых копий.</summary>
        public static int ReverseAll(IEnumerable<string> assetPaths)
        {
            int made = 0;
            foreach (string path in assetPaths)
            {
                AnimationClip source = MainClip(path);
                if (source == null || source.name.EndsWith(Suffix)) continue;
                string target = Path.GetDirectoryName(path).Replace(Path.DirectorySeparatorChar, '/') + "/" + source.name + Suffix + ".anim";
                Reverse(source, target);
                made++;
            }

            AssetDatabase.SaveAssets();
            return made;
        }

        private static AnimationClip MainClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        /// <summary>Копия <paramref name="source"/> с обращённым временем в <paramref name="targetPath"/>.</summary>
        public static AnimationClip Reverse(AnimationClip source, string targetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetPath);
            AnimationClip clip = existing != null ? existing : new AnimationClip();
            clip.name = Path.GetFileNameWithoutExtension(targetPath);
            clip.ClearCurves();
            clip.frameRate = source.frameRate;
            float length = source.length;

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                AnimationUtility.SetEditorCurve(clip, binding, ReverseCurve(curve, length));
            }

            // Настройки клипа — как у исходника (цикл, запекание корня, смещения поворота и высоты).
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (existing == null) AssetDatabase.CreateAsset(clip, targetPath);
            else EditorUtility.SetDirty(clip);

            // Высота корня «по стопам» отсчитывается от первого кадра клипа, а у копии первый кадр — последний исходника:
            // смещение выходит другим (до 16 см у кандидатов сидения). Разница таза «конец исходника → начало копии» —
            // в смещение высоты корня копии.
            // Знак у Unity обратный: положительное смещение опускает позу. Две подгонки — на случай нелинейности.
            float target = HipsHeight(source, source.length);
            for (int pass = 0; pass < 2 && !float.IsNaN(target); pass++)
            {
                float gap = target - HipsHeight(clip, 0f);
                if (float.IsNaN(gap) || Mathf.Abs(gap) < 1e-3f) break;
                settings.level -= gap;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
            }

            return clip;
        }

        /// <summary>Высота таза над полом на кадре клипа — на копии рига MEF (как замеры генератора контроллера ног). NaN — нет рига.</summary>
        private static float HipsHeight(AnimationClip clip, float time)
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarLegsRigBaker.RigPath(AvatarMixamoLocomotionSetup.MainAvatarPrefab));
            if (rigPrefab == null) return float.NaN;
            GameObject rig = Object.Instantiate(rigPrefab);
            rig.hideFlags = HideFlags.HideAndDontSave;
            PlayableGraph graph = PlayableGraph.Create("AnimationClipReverse");
            try
            {
                Animator animator = rig.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetTime(Mathf.Clamp(time, 0f, clip.length));
                AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(playable);
                graph.Evaluate();
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                return hips != null ? hips.position.y : float.NaN;
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(rig);
            }
        }

        /// <summary>Ключи в обратном порядке: время t → length − t, касательные меняются местами со сменой знака.</summary>
        private static AnimationCurve ReverseCurve(AnimationCurve curve, float length)
        {
            Keyframe[] keys = curve.keys;
            var reversed = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe k = keys[keys.Length - 1 - i];
                reversed[i] = new Keyframe(length - k.time, k.value, -k.outTangent, -k.inTangent, k.outWeight, k.inWeight)
                {
                    weightedMode = k.weightedMode == WeightedMode.In ? WeightedMode.Out : k.weightedMode == WeightedMode.Out ? WeightedMode.In : k.weightedMode,
                };
            }

            return new AnimationCurve(reversed) { preWrapMode = curve.postWrapMode, postWrapMode = curve.preWrapMode };
        }
    }
}
