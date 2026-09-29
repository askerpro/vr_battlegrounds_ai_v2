// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.KAnimationCore.Editor.CurveImporter
{
    public class CurvePostprocessor : AssetPostprocessor
    {
        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            var importer = assetImporter as ModelImporter;
            if (importer == null)
            {
                return;
            }
            
            CurveProcessorUtility.ApplyCurvesFromImporter(clip, importer);
        }
    }
}