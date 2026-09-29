// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Editor.Widgets;
using KINEMATION.TacticalShooterPack.Scripts.Player;
using UnityEditor;

namespace KINEMATION.TacticalShooterPack.Scripts.Editor
{
    [CustomEditor(typeof(TacticalShooterPlayer))]
    public class TacticalShooterPlayerEditor : UnityEditor.Editor
    {
        private TabInspectorWidget _tabWidget;

        private void OnEnable()
        {
            _tabWidget = new TabInspectorWidget(serializedObject);
            _tabWidget.Init();
        }

        public override void OnInspectorGUI()
        {
            _tabWidget.OnGUI();
        }
    }
}