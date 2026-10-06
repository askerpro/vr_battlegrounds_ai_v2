using UnityEditor;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// <c>Tools/VR Battlegrounds/Debug/Clip Scrub Stand Play</c> — Play в сцене стенда перемотки клипов: живые аватары
    /// UltimateXR (верх — камера и кисти манипулятора, ноги — клип строки). Сцена — временная стартовая у
    /// <see cref="PlayModeStartFromOffline"/>; при возврате в Edit Mode он снимает её сам.
    /// </summary>
    public static class ClipScrubStandPlayMenu
    {
        private const string ScenePath = "Assets/Scenes/Dev/ClipScrub.unity";
        private const string PlayOwner = "clip-scrub-stand";

        [MenuItem("Tools/VR Battlegrounds/Debug/Clip Scrub Stand Play")]
        public static void Play()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null) { GameLog.Debug.Warning("[ClipScrubStandPlayMenu] Сначала соберите стенд: Clip Scrub Stand."); return; }
            if (!PlayModeStartFromOffline.TrySetTemporaryStartScene(scene, PlayOwner))
            {
                GameLog.Debug.Warning("[ClipScrubStandPlayMenu] Стартовую сцену уже занял другой стенд.");
                return;
            }

            EditorApplication.EnterPlaymode();
        }
    }
}
