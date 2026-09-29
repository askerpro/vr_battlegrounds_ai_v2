// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.KAnimationCore.Editor.CurveImporter
{
    [Serializable]
    public struct CustomEditorCurveData
    {
        public AnimationCurve curve;
        public string relativePath;
        public string propertyName;
        public string targetTypeName;

        public CustomEditorCurveData(string relativePath, string propertyName, string targetTypeName, 
            AnimationCurve curve)
        {
            this.relativePath = relativePath;
            this.propertyName = propertyName;
            this.targetTypeName = targetTypeName;
            this.curve = curve;
        }
    }
    
    public class CurveProcessorUtility
    {
        public const string CustomCurvePrefix = "Curve";
        private const string StableCurvePrefix = "CurveId";
        private const string QuerySeparator = "~";
        private static readonly FieldInfo ClipInternalIdField = typeof(ModelImporterClipAnimation).GetField(
            "internalID", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly struct CurveQuery
        {
            public readonly bool usesLocalFileId;
            public readonly long localFileId;
            public readonly string clipName;
            public readonly CustomEditorCurveData curveData;

            public CurveQuery(bool usesLocalFileId, long localFileId, string clipName,
                CustomEditorCurveData curveData)
            {
                this.usesLocalFileId = usesLocalFileId;
                this.localFileId = localFileId;
                this.clipName = clipName;
                this.curveData = curveData;
            }
        }
        
        public static bool ApplyCurvesFromImporter(AnimationClip clip, ModelImporter importer)
        {
            if (clip == null || importer == null)
            {
                return false;
            }

            if (!importer.importAnimatedCustomProperties) return false;
            
            bool appliedAny = false;
            foreach (CustomEditorCurveData curveData in GetCurvesForClip(clip, importer).Values)
            {
                Type targetType = Type.GetType(curveData.targetTypeName);
                if (targetType == null) continue;

                string relativePath = string.IsNullOrEmpty(curveData.relativePath)
                    ? string.Empty
                    : curveData.relativePath;
                clip.SetCurve(relativePath, targetType, curveData.propertyName, curveData.curve);
                appliedAny = true;
            }

            return appliedAny;
        }

        public static bool CopyCurvesToImporter(AnimationClip sourceClip, ModelImporter targetImporter,
            string targetClipName, IEnumerable<CustomEditorCurveData> additionalCurves = null)
        {
            if (sourceClip == null || targetImporter == null || string.IsNullOrEmpty(targetClipName))
            {
                return false;
            }

            return CopyCurvesToImporter(CollectCurves(sourceClip, additionalCurves), targetImporter,
                targetClipName);
        }

        public static IReadOnlyCollection<CustomEditorCurveData> CollectCurves(AnimationClip sourceClip,
            IEnumerable<CustomEditorCurveData> additionalCurves = null)
        {
            var curves = new Dictionary<string, CustomEditorCurveData>(StringComparer.Ordinal);
            if (sourceClip != null) AddCustomCurveProperties(sourceClip, curves);

            if (additionalCurves != null)
            {
                foreach (CustomEditorCurveData curveData in additionalCurves)
                {
                    AddCurve(curves, curveData);
                }
            }

            return curves.Values.ToArray();
        }

        public static bool CopyCurvesToImporter(IEnumerable<CustomEditorCurveData> sourceCurves,
            ModelImporter targetImporter, string targetClipName)
        {
            if (targetImporter == null || string.IsNullOrEmpty(targetClipName)) return false;

            var curves = new Dictionary<string, CustomEditorCurveData>(StringComparer.Ordinal);
            if (sourceCurves != null)
            {
                foreach (CustomEditorCurveData curveData in sourceCurves)
                {
                    AddCurve(curves, curveData);
                }
            }

            bool hasLocalFileId = TryGetImporterClipLocalFileId(targetImporter, targetClipName,
                out long localFileId);
            bool isSingleClipImporter = GetImporterClipAnimations(targetImporter).Length == 1;
            var rewrittenBindings = new HashSet<string>(curves.Keys, StringComparer.Ordinal);

            string[] existingProperties = targetImporter.extraUserProperties ?? Array.Empty<string>();
            List<string> properties = existingProperties
                .Where(property => !ShouldRemoveCurveQuery(property, targetClipName, hasLocalFileId,
                    localFileId, rewrittenBindings, isSingleClipImporter, true))
                .ToList();

            foreach (KeyValuePair<string, CustomEditorCurveData> item in
                     curves.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                properties.Add(CreateCurveQuery(targetClipName, hasLocalFileId, localFileId, item.Value));
            }

            string[] newProperties = properties.ToArray();
            bool changed = !existingProperties.SequenceEqual(newProperties);

            if (changed) targetImporter.extraUserProperties = newProperties;
            if (curves.Count > 0 && !targetImporter.importAnimatedCustomProperties)
            {
                targetImporter.importAnimatedCustomProperties = true;
                changed = true;
            }

            return changed;
        }
        
        public static void WriteCurvesToImporter(AnimationClip clip, ModelImporter importer,
            List<CustomEditorCurveData> customCurves)
        {
            if (clip == null || importer == null || customCurves == null)
            {
                return;
            }

            List<CustomEditorCurveData> validCustomCurves = customCurves
                .Where(curveData => IsValidCurveData(curveData, true))
                .ToList();

            bool hasLocalFileId = TryGetClipLocalFileId(clip, importer, out long localFileId);
            bool isSingleClipImporter = GetImporterClipAnimations(importer).Length == 1;
            var rewrittenBindings = new HashSet<string>(
                validCustomCurves.Select(GetCurveBindingKey), StringComparer.Ordinal);

            string[] existingProperties = importer.extraUserProperties ?? Array.Empty<string>();
            List<string> extraUserProperties = existingProperties
                .Where(property => !ShouldRemoveCurveQuery(property, clip.name, hasLocalFileId,
                    localFileId, rewrittenBindings, isSingleClipImporter, false))
                .ToList();
            
            foreach (CustomEditorCurveData curveData in validCustomCurves)
            {
                extraUserProperties.Add(CreateCurveQuery(clip.name, hasLocalFileId, localFileId, curveData));
            }
            
            string[] newProperties = extraUserProperties.ToArray();

            if (!existingProperties.SequenceEqual(newProperties))
            {
                importer.extraUserProperties = newProperties;
            }

            if (!importer.importAnimatedCustomProperties) importer.importAnimatedCustomProperties = true;
            importer.SaveAndReimport();
        }
        
        public static bool TrySavingToFBX(AnimationClip clip, List<CustomEditorCurveData> customCurves)
        {
            if (!TryGetModelImporter(clip, out ModelImporter importer))
            {
                return false;
            }

            WriteCurvesToImporter(clip, importer, customCurves);
            return true;
        }

        public static bool TryGetModelImporter(AnimationClip clip, out ModelImporter importer)
        {
            importer = null;

            if (clip == null || !AssetDatabase.IsSubAsset(clip))
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(clip);
            importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;

            return importer != null;
        }
        
        private static bool TryParseCurveQuery(string property, out CurveQuery curveQuery)
        {
            curveQuery = default;

            if (string.IsNullOrEmpty(property))
            {
                return false;
            }

            int firstSeparator = property.IndexOf(QuerySeparator, StringComparison.Ordinal);
            int secondSeparator = firstSeparator < 0
                ? -1
                : property.IndexOf(QuerySeparator, firstSeparator + QuerySeparator.Length,
                    StringComparison.Ordinal);
            if (firstSeparator <= 0 || secondSeparator <= firstSeparator + QuerySeparator.Length ||
                secondSeparator >= property.Length - QuerySeparator.Length)
            {
                return false;
            }

            string prefix = property.Substring(0, firstSeparator);
            bool usesLocalFileId = string.Equals(prefix, StableCurvePrefix, StringComparison.Ordinal);
            if (!usesLocalFileId && !prefix.Contains(CustomCurvePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string owner = property.Substring(firstSeparator + QuerySeparator.Length,
                secondSeparator - firstSeparator - QuerySeparator.Length);
            long localFileId = 0;
            if (usesLocalFileId &&
                (!long.TryParse(owner, NumberStyles.Integer, CultureInfo.InvariantCulture, out localFileId) ||
                 localFileId == 0))
            {
                return false;
            }

            CustomEditorCurveData curveData;
            try
            {
                curveData = JsonUtility.FromJson<CustomEditorCurveData>(
                    property.Substring(secondSeparator + QuerySeparator.Length));
            }
            catch (ArgumentException)
            {
                return false;
            }

            curveQuery = new CurveQuery(usesLocalFileId, localFileId,
                usesLocalFileId ? string.Empty : owner, curveData);
            return true;
        }

        private static Dictionary<string, CustomEditorCurveData> GetCurvesForClip(AnimationClip clip,
            ModelImporter importer)
        {
            var stableCurves = new Dictionary<string, CustomEditorCurveData>(StringComparer.Ordinal);
            var namedCurves = new Dictionary<string, CustomEditorCurveData>(StringComparer.Ordinal);
            var singleClipFallbackCurves = new Dictionary<string, CustomEditorCurveData>(StringComparer.Ordinal);

            bool hasLocalFileId = TryGetClipLocalFileId(clip, importer, out long localFileId);
            bool isSingleClipImporter = GetImporterClipAnimations(importer).Length == 1;

            foreach (string property in importer.extraUserProperties ?? Array.Empty<string>())
            {
                if (!TryParseCurveQuery(property, out CurveQuery query) ||
                    !IsValidCurveData(query.curveData, true))
                {
                    continue;
                }

                if (query.usesLocalFileId)
                {
                    if (hasLocalFileId && query.localFileId == localFileId)
                    {
                        AddCurve(stableCurves, query.curveData);
                    }

                    continue;
                }

                if (string.Equals(query.clipName, clip.name, StringComparison.Ordinal))
                {
                    AddCurve(namedCurves, query.curveData);
                }
                else if (isSingleClipImporter)
                {
                    AddCurve(singleClipFallbackCurves, query.curveData);
                }
            }

            foreach (KeyValuePair<string, CustomEditorCurveData> curve in namedCurves)
            {
                singleClipFallbackCurves[curve.Key] = curve.Value;
            }

            foreach (KeyValuePair<string, CustomEditorCurveData> curve in stableCurves)
            {
                singleClipFallbackCurves[curve.Key] = curve.Value;
            }

            return singleClipFallbackCurves;
        }

        private static bool ShouldRemoveCurveQuery(string property, string clipName, bool hasLocalFileId,
            long localFileId, ISet<string> rewrittenBindings, bool isSingleClipImporter,
            bool removeAllOwnedCurves)
        {
            if (!TryParseCurveQuery(property, out CurveQuery query) ||
                !IsValidCurveData(query.curveData, true))
            {
                return false;
            }

            bool belongsToClip = query.usesLocalFileId
                ? hasLocalFileId && query.localFileId == localFileId
                : string.Equals(query.clipName, clipName, StringComparison.Ordinal);
            if (belongsToClip &&
                (removeAllOwnedCurves || rewrittenBindings.Contains(GetCurveBindingKey(query.curveData))))
            {
                return true;
            }

            return isSingleClipImporter && !query.usesLocalFileId &&
                   rewrittenBindings.Contains(GetCurveBindingKey(query.curveData));
        }

        private static void AddCustomCurveProperties(AnimationClip clip,
            IDictionary<string, CustomEditorCurveData> curves)
        {
            if (!TryGetModelImporter(clip, out ModelImporter importer) ||
                !importer.importAnimatedCustomProperties)
            {
                return;
            }

            foreach (KeyValuePair<string, CustomEditorCurveData> curve in GetCurvesForClip(clip, importer))
            {
                curves[curve.Key] = curve.Value;
            }
        }

        private static void AddCurve(IDictionary<string, CustomEditorCurveData> curves,
            CustomEditorCurveData curveData)
        {
            if (!IsValidCurveData(curveData, true)) return;

            curveData.relativePath ??= string.Empty;
            curves[GetCurveBindingKey(curveData)] = curveData;
        }

        private static string GetCurveBindingKey(CustomEditorCurveData curveData)
        {
            return $"{curveData.targetTypeName}|{curveData.relativePath ?? string.Empty}|{curveData.propertyName}";
        }

        private static string CreateCurveQuery(string clipName, bool hasLocalFileId, long localFileId,
            CustomEditorCurveData curveData)
        {
            string prefix = hasLocalFileId ? StableCurvePrefix : CustomCurvePrefix;
            string owner = hasLocalFileId
                ? localFileId.ToString(CultureInfo.InvariantCulture)
                : clipName;
            return $"{prefix}{QuerySeparator}{owner}{QuerySeparator}{JsonUtility.ToJson(curveData)}";
        }

        private static bool TryGetClipLocalFileId(AnimationClip clip, ModelImporter importer,
            out long localFileId)
        {
            if (EditorUtility.IsPersistent(clip) &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string _, out long assetLocalFileId) &&
                assetLocalFileId != 0)
            {
                localFileId = assetLocalFileId;
                return true;
            }

            return TryGetImporterClipLocalFileId(importer, clip.name, out localFileId);
        }

        private static bool TryGetImporterClipLocalFileId(ModelImporter importer, string clipName,
            out long localFileId)
        {
            localFileId = 0;
            int matches = 0;

            foreach (ModelImporterClipAnimation clipAnimation in GetImporterClipAnimations(importer))
            {
                if (!string.Equals(clipAnimation.name, clipName, StringComparison.Ordinal)) continue;

                matches++;
                if (TryGetClipAnimationInternalId(clipAnimation, out long candidateId))
                {
                    localFileId = candidateId;
                }
            }

            return matches == 1 && localFileId != 0;
        }

        private static ModelImporterClipAnimation[] GetImporterClipAnimations(ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            return clips != null && clips.Length > 0
                ? clips
                : importer.defaultClipAnimations ?? Array.Empty<ModelImporterClipAnimation>();
        }

        private static bool TryGetClipAnimationInternalId(ModelImporterClipAnimation clipAnimation,
            out long localFileId)
        {
            localFileId = 0;
            if (clipAnimation == null || ClipInternalIdField == null) return false;

            try
            {
                if (ClipInternalIdField.GetValue(clipAnimation) is not long internalId || internalId == 0)
                {
                    return false;
                }

                localFileId = internalId;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        private static bool IsValidCurveData(CustomEditorCurveData curveData, bool requireCurve)
        {
            if (requireCurve && curveData.curve == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(curveData.propertyName) && !string.IsNullOrEmpty(curveData.targetTypeName);
        }
    }
}