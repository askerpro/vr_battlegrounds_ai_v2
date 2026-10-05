using VrBattlegrounds.Core;
using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.UI;
using System.Linq;

namespace VrBattlegrounds.EditorTools
{
    public class AvatarFingertipSetup : EditorWindow
    {
        private static void ShowWindow()
        {
            SetupFingertips();
        }

        private static void SetupFingertips()
        {
            int updatedCount = 0;
            
            // Allow setting up multiple selected avatars at once
            GameObject[] selectedObjects = Selection.gameObjects;
            
            if (selectedObjects.Length == 0)
            {
                GameLog.Player.Info("No objects selected, searching in preset folders for avatars...");
                string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Prefabs/Player" });
                selectedObjects = guids.Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
                
                if (selectedObjects.Length == 0)
                {
                    GameLog.Player.Warning("Please select one or more Avatar prefabs/objects in the Project or Hierarchy.");
                    return;
                }
            }

            foreach (var go in selectedObjects)
            {
                UxrAvatar avatar = go.GetComponent<UxrAvatar>();
                if (avatar == null)
                {
                    avatar = go.GetComponentInChildren<UxrAvatar>();
                }

                if (avatar == null)
                {
                    GameLog.Player.Info($"Skipping {go.name}: No UxrAvatar component found.");
                    continue;
                }

                if (Setup(avatar))
                {
                    EditorUtility.SetDirty(go);
                    updatedCount++;
                }
            }

            GameLog.Player.Info($"UI Fingertips setup complete. Updated {updatedCount} avatars.");
        }

        /// <summary>
        ///     Ставит или выравнивает <see cref="UxrFingerTip" /> на кончиках указательных пальцев
        ///     обеих рук. Шаг пайплайна аватара (<c>CustomAvatarPipelineMenu</c>) и пункт меню.
        ///     Возвращает, было ли что-то изменено.
        ///
        ///     <para>
        ///     Палец берётся из рига <see cref="UxrAvatar" />, а не из Humanoid-аниматора: риг — это
        ///     кисть, которую реально двигает UltimateXR. У аватара с кистями SDK (путь А) Humanoid
        ///     указывает на родную, отрезанную кость — кончик на ней не нажмёт ничего (так было у
        ///     Heavy: левый кончик на <c>index_03_l</c>, а не на <c>BigIKHandLeft</c>).
        ///     </para>
        /// </summary>
        public static bool CanSetup(UxrAvatar avatar)
        {
            if (!avatar) return false;
            return HasDirection(FindIndexTipBone(avatar, UxrHandSide.Left)) && HasDirection(FindIndexTipBone(avatar, UxrHandSide.Right));
        }
        private static bool HasDirection(Transform bone) => bone && bone.parent && (bone.position - bone.parent.position).sqrMagnitude >= 1e-6f;

        public static bool Setup(UxrAvatar avatar)
        {
            bool modified = false;

            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                Transform tipBone = FindIndexTipBone(avatar, side);

                if (tipBone == null)
                {
                    GameLog.Player.Warning($"[AvatarFingertipSetup] {avatar.name}: не найден указательный палец ({side}).");
                    continue;
                }

                modified |= EnsureFingertip(tipBone);
            }

            if (modified)
            {
                GameLog.Player.Info($"[AvatarFingertipSetup] {avatar.name}: кончики пальцев для UI настроены.");
            }

            return modified;
        }

        /// <summary>
        ///     Кость, на которую ставится кончик: концевая кость указательного пальца (ребёнок
        ///     дистальной фаланги), а если её нет — сама дистальная фаланга.
        /// </summary>
        private static Transform FindIndexTipBone(UxrAvatar avatar, UxrHandSide side)
        {
            Transform distal = null;

            UxrAvatarHand hand = avatar.GetHand(side);
            if (hand != null && hand.Index != null)
            {
                distal = hand.Index.Distal;
            }

            // Запасные пути для аватара без размеченных пальцев в риге.
            if (distal == null)
            {
                Animator animator = avatar.GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman)
                {
                    distal = animator.GetBoneTransform(side == UxrHandSide.Left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
                }
            }

            if (distal == null)
            {
                return FindBoneRecursive(avatar.transform, side == UxrHandSide.Left ? new[] { "index_03_l", "Index_Tip_Left" } : new[] { "index_03_r", "Index_Tip_Right" });
            }

            Transform tip = distal.Cast<Transform>().FirstOrDefault(child => child.GetComponent<UxrFingerTip>() == null);
            return tip != null ? tip : distal;
        }

        // Кончик, чей forward отклонён от пальца больше чем на 60°, считается испорченным и
        // выравнивается заново. Меньшие отклонения — ручная настройка (у киборга наклон 30°), их не трогаем.
        private const float MinAlignmentDot = 0.5f;

        // Сдвиг точки касания вперёд, когда концевой кости нет и кончик ставится на основание
        // последней фаланги: без него касание было бы у сустава, а не у подушечки.
        private const float DistalPushForward = 0.02f;

        /// <summary>
        ///     Создаёт кончик пальца для касания UI или выравнивает существующий, если он смотрит
        ///     не вдоль пальца. <see cref="UxrFingerTip.WorldDir" /> — это <c>forward</c> объекта:
        ///     <c>UxrFingerTipRaycaster</c> пускает луч вдоль него и отбрасывает касание под большим
        ///     углом к канвасу. Кончик, смотрящий вбок, не нажмёт ничего.
        /// </summary>
        private static bool EnsureFingertip(Transform fingerBone)
        {
            // Концевая кость (без детей, есть родитель-фаланга) — это уже сам кончик пальца;
            // иначе кость — основание последней фаланги, и точку касания надо вынести вперёд.
            bool    isTipBone = fingerBone.Cast<Transform>().All(child => child.GetComponent<UxrFingerTip>() != null);
            Vector3 direction = fingerBone.parent != null ? fingerBone.position - fingerBone.parent.position : Vector3.zero;

            // Порог — 1 мм. Раньше стояло sqrMagnitude > 0.001, то есть 3.2 см: фаланги короче
            // (у CC-скелета MEF — 2.5 см) молча оставались с нулевым поворотом, и палец не нажимал UI.
            if (direction.sqrMagnitude < 1e-6f)
            {
                GameLog.Player.Warning($"[AvatarFingertipSetup] '{fingerBone.name}': кость совпадает с родителем, направление пальца не определить.");
                return false;
            }

            Quaternion rotation = Quaternion.LookRotation(direction.normalized);
            Vector3    position = fingerBone.position + (isTipBone ? Vector3.zero : direction.normalized * DistalPushForward);

            UxrFingerTip existing = fingerBone.GetComponentInChildren<UxrFingerTip>();
            if (existing != null)
            {
                if (Vector3.Dot(existing.transform.forward, direction.normalized) >= MinAlignmentDot)
                {
                    return false; // Уже настроен
                }

                Undo.RecordObject(existing.transform, "Realign UxrFingerTip");
                existing.transform.SetPositionAndRotation(position, rotation);
                GameLog.Player.Info($"[AvatarFingertipSetup] '{fingerBone.name}': кончик смотрел не вдоль пальца — выровнен.");
                return true;
            }

            GameObject tipGo = new GameObject("UxrFingerTip");
            tipGo.transform.SetParent(fingerBone, false);
            tipGo.transform.SetPositionAndRotation(position, rotation);
            tipGo.AddComponent<UxrFingerTip>();
            return true;
        }

        private static Transform FindBoneRecursive(Transform root, params string[] names)
        {
            foreach (string name in names)
            {
                // Simple case-insensitive match
                if (root.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return root;
                }
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindBoneRecursive(root.GetChild(i), names);
                if (found != null) return found;
            }

            return null;
        }
    }
}
