// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Editor.Tools;
using UnityEditor;

namespace KINEMATION.TacticalShooterPack.Scripts.Editor
{
    public class TacticalDemoContent : DemoDownloaderTool
    {
        private int _selectedIndex = 1;
        
        private string[] _urls = new[]
        {
            "https://github.com/kinemation/demoes/releases/download/tac-fps/TacticalShooterPack_Scene_BiRP.unitypackage",
            "https://github.com/kinemation/demoes/releases/download/tac-fps/TacticalShooterPack_Scene_URP.unitypackage",
            "https://github.com/kinemation/demoes/releases/download/tac-fps/TacticalShooterPack_Scene_HDRP.unitypackage",
        };

        private string[] _renderPipelines = new[]
        {
            "Build In",
            "URP",
            "HDRP"
        };
        
        protected override string GetPackageUrl()
        {
            return _urls[_selectedIndex];
        }

        protected override string GetPackageFileName()
        {
            return "FPSAnimationPack_Demo";
        }

        protected override List<ContentLicense> GetContentLicenses()
        {
            return new List<ContentLicense>()
            {
                new ContentLicense()
                {
                    contentName = "Materials, meshes and textures",
                    tags = new List<Tag>()
                    {
                        new Tag("FREE COMMERCIAL USE"),
                        new Tag("CC 4.0", "", "https://creativecommons.org/licenses/by/4.0/"),
                    }
                }
            };
        }

        public override string GetToolName()
        {
            return "Tactical Shooter Pack";

        }

        public override void Render()
        {
            _selectedIndex = EditorGUILayout.Popup("Render Pipeline", _selectedIndex, _renderPipelines);
            base.Render();
        }

        public override string GetToolDescription()
        {
            return "Complete demo shooter scene environment.";
        }
    }
}