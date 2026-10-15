using System;
using System.Collections.Generic;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Запись хвата на предмет тем же механизмом, что <c>HandsPackPoseImporter.AssignToGrabPoint</c>: align
    /// <c>Grabs/&lt;источник&gt;/&lt;поза&gt;/&lt;аватар&gt;/Left|Right</c> (<see cref="HandsPackPoseImporter.AlignTransform" />), запись
    /// <c>UxrGripPoseInfo</c> по GUID цели (поза, blend 0, align L/R), прежние align — <see cref="HandsPackPoseImporter.RemoveUnused" />
    /// по флагу профиля. Сохранение — <c>SaveAsPrefabAsset</c> под <see cref="GripPrefabIdentityGuard" />.
    /// </summary>
    public static class GripRecordWriter
    {
        /// <param name="weaponPath">Префаб предмета</param>
        /// <param name="points">Рассчитанные точки этого предмета (без ошибок)</param>
        /// <param name="poses">Поза по имени: записанный ассет (Apply) или временный (Preview)</param>
        /// <param name="save">false — предпросмотр: копия в памяти, выгружается без сохранения</param>
        /// <returns>Что записано (или было бы записано) — пути align и записи</returns>
        public static List<string> WriteWeapon(string weaponPath, IReadOnlyList<GripExtractionResult> points, GripTargetProfile profile, string recordGuid,
                                               IReadOnlyDictionary<string, UxrHandPoseAsset> poses, bool save, List<string> warnings)
        {
            var                        written = new List<string>();
            Dictionary<string, string> before  = save ? GripPrefabIdentityGuard.Snapshot(weaponPath) : null;
            GameObject                 root    = PrefabUtility.LoadPrefabContents(weaponPath);
            try
            {
                if (!root.TryGetComponent(out UxrGrabbableObject grabbable))
                {
                    throw new KeyNotFoundException($"{weaponPath}: нет UxrGrabbableObject на корне");
                }

                foreach (GripExtractionResult point in points)
                {
                    UxrGrabPointInfo grabPoint = grabbable.GetGrabPoint(point.GrabPoint) ?? throw new KeyNotFoundException($"{weaponPath}: нет точки хвата {point.GrabPoint}");
                    string           source    = point.Point.AlignSource;
                    Transform        left      = HandsPackPoseImporter.AlignTransform(root.transform, point.Pose, profile.AlignAvatarName, UxrHandSide.Left, source);
                    Transform        right     = HandsPackPoseImporter.AlignTransform(root.transform, point.Pose, profile.AlignAvatarName, UxrHandSide.Right, source);
                    Place(root.transform, left, point.Hand(nameof(UxrHandSide.Left)));
                    Place(root.transform, right, point.Hand(nameof(UxrHandSide.Right)));
                    BindToPrefab(left, weaponPath);
                    BindToPrefab(right, weaponPath);

                    grabPoint.CheckAddGripPoseInfo(recordGuid);
                    UxrGripPoseInfo grip     = grabPoint.GetGripPoseInfo(recordGuid);
                    Transform       oldLeft  = grip.GripAlignTransformHandLeft;
                    Transform       oldRight = grip.GripAlignTransformHandRight;
                    grip.HandPose                    = poses[point.Pose];
                    grip.PoseBlendValue              = 0f;
                    grip.GripAlignTransformHandLeft  = left;
                    grip.GripAlignTransformHandRight = right;

                    if (profile.RemoveReplacedAligns)
                    {
                        HandsPackPoseImporter.RemoveUnused(root, oldLeft, left);
                        HandsPackPoseImporter.RemoveUnused(root, oldRight, right);
                    }
                    else
                    {
                        KeepNote(root.transform, oldLeft, left, weaponPath, warnings);
                        KeepNote(root.transform, oldRight, right, weaponPath, warnings);
                    }

                    string record = $"{weaponPath}#grab{point.GrabPoint}/record {recordGuid}";
                    written.Add($"{record}: pose {point.Pose}, align {GripExtractor.PathOf(left, root.transform)}, {GripExtractor.PathOf(right, root.transform)}");
                }

                if (save)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, weaponPath, out bool success);
                    if (!success)
                    {
                        throw new InvalidOperationException($"SaveAsPrefabAsset не сохранил {weaponPath}");
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (save)
            {
                NormalizeNetworkAssetId(weaponPath);
                GripPrefabIdentityGuard.Verify(weaponPath, before);
            }

            return written;
        }

        /// <summary>
        /// <c>SaveAsPrefabAsset</c> из изолированной сцены пишет <c>NetworkIdentity._assetId: 0</c>, а сторож по
        /// загруженному ассету этого не видит (Mirror в памяти восстанавливает канон). Возвращает канон на диск
        /// штатным <see cref="VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer" /> и сверяет файл:
        /// число = <c>NetworkIdentity.AssetGuidToUint</c> от GUID префаба (условие zone-ack weapon-system).
        /// </summary>
        private static void NormalizeNetworkAssetId(string weaponPath)
        {
            string text = System.IO.File.ReadAllText(weaponPath);
            if (!text.Contains("_assetId")) return;

            VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { weaponPath }, false);
            uint canonical = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(weaponPath)));
            string saved = System.IO.File.ReadAllText(weaponPath);
            System.Text.RegularExpressions.Match onDisk = System.Text.RegularExpressions.Regex.Match(saved, @"^  _assetId: (\d+)", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (!onDisk.Success)
            {
                // Вариант префаба: число лежит в переопределении.
                onDisk = System.Text.RegularExpressions.Regex.Match(saved, @"propertyPath: _assetId\r?\n\s+value: (\d+)", System.Text.RegularExpressions.RegexOptions.Multiline);
            }

            if (!onDisk.Success || onDisk.Groups[1].Value != canonical.ToString())
            {
                throw new InvalidOperationException($"{weaponPath}: _assetId на диске {(onDisk.Success ? onDisk.Groups[1].Value : "нет")}, канон {canonical}");
            }
        }

        /// <summary>
        /// Компонент align, созданный в <c>LoadPrefabContents</c>, сохраняется без prefab provenance
        /// (<c>__prefabGuid</c> пуст, <c>__isInPrefab</c>=0), в отличие от остальных точек хвата префаба.
        /// Проставляет GUID префаба, не меняя уже записанный ID; у существующих компонентов ничего не меняет.
        /// </summary>
        private static void BindToPrefab(Transform align, string weaponPath)
        {
            if (!align.TryGetComponent(out UxrGrabbableObjectSnapTransform snap)) return;
            var    serialized = new SerializedObject(snap);
            string prefabGuid = AssetDatabase.AssetPathToGUID(weaponPath);
            if (serialized.FindProperty("__isInPrefab").boolValue && serialized.FindProperty("__prefabGuid").stringValue == prefabGuid) return;

            string stored = serialized.FindProperty("_uxrUniqueId").stringValue;
            snap.SetEditorUniqueId(Guid.TryParse(stored, out Guid id) && id != Guid.Empty ? id : Guid.NewGuid(), true, prefabGuid);
        }

        /// <summary>Мировая поза align от корня — как HandsPackGripAligner.Place (SetPositionAndRotation).</summary>
        private static void Place(Transform root, Transform align, GripHandResult hand)
        {
            if (hand == null)
            {
                throw new InvalidOperationException($"{align.name}: нет расчёта руки");
            }

            align.SetPositionAndRotation(hand.Align.WorldPosition(root), hand.Align.WorldRotation(root));
        }

        private static void KeepNote(Transform root, Transform old, Transform current, string weaponPath, List<string> warnings)
        {
            if (old != null && old != current)
            {
                warnings.Add($"{weaponPath}: прежний align {GripExtractor.PathOf(old, root)} оставлен (removeReplacedAligns=false).");
            }
        }
    }
}
