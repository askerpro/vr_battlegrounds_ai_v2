using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor
{
    /// <summary>
    /// Предпросмотр табло лазерной сетки (T-47) в открытых сценах без Play Mode. Табло строятся в игре сами
    /// (<see cref="LaserGridScreens"/> на префабе зоны, при первом показе сетки) — здесь та же раскладка,
    /// с примером текста закупки, чтобы посмотреть расстановку на карте. В сцену не сохраняется
    /// (<c>HideFlags.DontSave</c>); повторный вызов убирает предпросмотр.
    /// </summary>
    public static class LaserGridScreensPreview
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Gameplay/Preview Laser Grid Screens (open scenes)";

        private static readonly List<LaserGridScreens> s_previewed = new List<LaserGridScreens>();
        private static readonly List<GameObject> s_roots = new List<GameObject>();

        [MenuItem(MenuPath)]
        public static void Toggle()
        {
            if (s_roots.Count > 0)
            {
                Clear();
                return;
            }

            if (Application.isPlaying)
            {
                GameLog.Debug.Warning("[LaserGridScreensPreview] В Play Mode табло строит игра — предпросмотр не нужен.");
                return;
            }

            var sample = new LaserGridRoundInfo
            {
                Phase = LaserGridBoardPhase.Equipment, Seconds = 42, Round = 3, TotalRounds = 8, HasScore = true,
                Own = 2, Enemy = 1, TeamPlayers = 4, TeamPending = 2, PendingKey = 1
            };
            string common = LaserGridBoardRules.Common(sample, new[] { "Петя", "Вася" }).ToRichText();
            string personal = LaserGridBoardRules.Personal(sample,
                new LaserGridOwnerInfo { HasOwner = true, Name = "Петя", Pending = true, InZone = true, Alive = true }).ToRichText();

            foreach (LaserGridScreens screens in Object.FindObjectsByType<LaserGridScreens>(FindObjectsInactive.Exclude))
            {
                if (screens.GetComponent<TeamSpawnZone>().HomeTeam == null) continue;

                screens.Build();
                foreach (LaserGridBoardView view in screens.CommonBoards) view.SetText(common);
                foreach (LaserGridBoardView view in screens.PersonalBoards) view.SetText(personal);

                foreach (Transform t in screens.Root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags = HideFlags.DontSave;

                s_previewed.Add(screens);
                s_roots.Add(screens.Root);
                GameLog.Debug.Info($"[LaserGridScreensPreview] {screens.name}: общих {screens.CommonBoards.Count}, " +
                                   $"персональных {screens.PersonalCount}.", screens);
            }

            if (s_roots.Count == 0)
                GameLog.Debug.Warning("[LaserGridScreensPreview] В открытых сценах нет командных зон с LaserGridScreens.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            UnityEditor.Menu.SetChecked(MenuPath, s_roots.Count > 0);
            return true;
        }

        private static void Clear()
        {
            foreach (GameObject root in s_roots)
                if (root != null) Object.DestroyImmediate(root);
            s_roots.Clear();
            s_previewed.Clear();
        }
    }
}
