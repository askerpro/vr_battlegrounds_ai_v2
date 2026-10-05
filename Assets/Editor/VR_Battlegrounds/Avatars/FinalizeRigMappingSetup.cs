using VrBattlegrounds.Core;
using UnityEditor;
using UnityEngine;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;

namespace VRBattlegrounds.Editor
{
    public class FinalizeRigMappingSetup
    {
        private const string LogPrefix = "[UXR Finalize Rig Mapping]";

        /// <summary>Явная SDK-цель. Родные кисти этим шагом переназначать нельзя.</summary>
        public static void Setup(UxrAvatar avatar)
        {
            if (!avatar || !avatar.transform.Find("BigIKHandLeft") || !FindBigIkHandRightRoot(avatar.transform))
                throw new System.InvalidOperationException("Для SDK mapping нужны обе явно установленные IK-кисти.");
            var integration = FindBigHandsIntegrationRoot(avatar.transform);
            if (!integration) throw new System.InvalidOperationException("Отсутствует BigHandsIntegration.");
            ForceMapBigHandsRig(avatar, integration);
            if (!avatar.AvatarRig.LeftArm.Hand.HasFullHandData() || !avatar.AvatarRig.RightArm.Hand.HasFullHandData())
                throw new System.InvalidOperationException("SDK mapping неполон; следующий шаг не выполняется.");
        }

        public static void Execute()
        {
            GameObject avatarObj = null;
            string avatarSource = null;

            if (Selection.activeGameObject != null)
            {
                avatarObj = Selection.activeGameObject;
                avatarSource = "Selection.activeGameObject";
            }
            else
            {
                VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} Этап 1: в иерархии ничего не выбрано — ищем AutoSetupAvatarTarget.");
                avatarObj = GameObject.Find("AutoSetupAvatarTarget");
                if (avatarObj != null)
                    avatarSource = "GameObject.Find(\"AutoSetupAvatarTarget\")";
            }

            if (avatarObj == null)
            {
                VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} Этап 2: AutoSetupAvatarTarget не найден — ищем первый UxrAvatar в сцене.");
                var foundUxrAvatar = Object.FindAnyObjectByType<UltimateXR.Avatar.UxrAvatar>();
                if (foundUxrAvatar != null)
                {
                    avatarObj = foundUxrAvatar.gameObject;
                    avatarSource = "Object.FindAnyObjectByType<UxrAvatar>()";
                }
            }

            if (avatarObj == null)
            {
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix} Аватар не найден: нет выделения, нет AutoSetupAvatarTarget, нет UxrAvatar на сцене. Шаг прерван.");
                return;
            }

            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} Этап «выбор аватара»: объект '{avatarObj.name}', источник: {avatarSource}, путь: {GetHierarchyPath(avatarObj.transform)}");

            Undo.RegisterFullObjectHierarchyUndo(avatarObj, "Setup UXR Avatar: 4. Finalize");

            UxrAvatar uxrAvatar = avatarObj.GetComponent<UxrAvatar>();
            if (uxrAvatar == null)
            {
                VrBattlegrounds.Core.GameLog.Player.Error($"{LogPrefix} На '{avatarObj.name}' нет компонента UxrAvatar. Запускали шаг 1 визарда?");
                return;
            }

            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} Этап «UxrAvatar»: компонент найден на '{avatarObj.name}'.");

            Transform bigHandsObj = FindBigHandsIntegrationRoot(avatarObj.transform);
            if (bigHandsObj == null)
            {
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix} Корень BigHandsIntegration / BigHandRightGeo не найден среди прямых детей аватара — привязка костей рук не выполнялась.");
            }
            else
            {
                VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} Этап «корень интеграции»: '{bigHandsObj.name}', путь: {GetHierarchyPath(bigHandsObj)}");
                ForceMapBigHandsRig(uxrAvatar, bigHandsObj);
            }

            EditorUtility.SetDirty(uxrAvatar);
            EditorUtility.SetDirty(avatarObj);
            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} Финиш: SetDirty(UxrAvatar), SetDirty(корень аватара). Проверьте Inspector и сохраните сцену/префаб при необходимости.");
        }

        /// <summary>
        /// Ищет корень интеграции больших рук среди прямых дочерних объектов аватара
        /// (стандартное имя BigHandsIntegration или геометрия правой руки BigHandRightGeo, с учётом клонов).
        /// </summary>
        private static Transform FindBigHandsIntegrationRoot(Transform avatarRoot)
        {
            string[] candidates =
            {
                "BigHandsIntegration(Clone)",
                "BigHandsIntegration",
            };

            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} Поиск корня среди прямых детей '{avatarRoot.name}' ({avatarRoot.childCount} дочерних):");
            foreach (string name in candidates)
            {
                Transform t = avatarRoot.Find(name);
                VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix}   • '{name}': {(t != null ? $"найден → {GetHierarchyPath(t)}" : "нет")}");
                if (t != null)
                    return t;
            }

            if (avatarRoot.childCount > 0)
            {
                var lines = new System.Text.StringBuilder();
                for (int i = 0; i < avatarRoot.childCount; i++)
                    lines.AppendLine($"      - {avatarRoot.GetChild(i).name}");
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix} Ни один из ожидаемых имён не совпал. Текущие прямые дочерние объекты:\n{lines}");
            }

            return null;
        }

        /// <summary>
        /// Прямой ребёнок аватара: IK правой руки (стандартные имена или геометрия BigHandRightGeo).
        /// </summary>
        private static Transform FindBigIkHandRightRoot(Transform avatarRoot)
        {
            return avatarRoot.Find("BigIKHandRight")
                ?? avatarRoot.Find("BigIKHandRight(Clone)")
                ?? avatarRoot.Find("BigHandRightGeo(Clone)")
                ?? avatarRoot.Find("BigHandRightGeo");
        }

        private static void ForceMapBigHandsRig(UxrAvatar uxrAvatar, Transform bigHandsIntegration)
        {
            var rig = uxrAvatar.AvatarRig;
            if (rig == null)
            {
                VrBattlegrounds.Core.GameLog.Player.Error($"{LogPrefix} UxrAvatar.AvatarRig == null — привязка невозможна.");
                return;
            }

            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} Этап «AvatarRig»: ссылка есть.");

            Transform leftHandT = uxrAvatar.transform.Find("BigIKHandLeft") ?? uxrAvatar.transform.Find("BigIKHandLeft(Clone)");
            Transform rightHandT = FindBigIkHandRightRoot(uxrAvatar.transform);

            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} IK-кости (прямые дети аватара): BigIKHandLeft → {(leftHandT != null ? GetHierarchyPath(leftHandT) : "НЕ НАЙДЕН")}");
            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} IK-кости (прямые дети аватара): правая (BigIKHandRight* / BigHandRightGeo*) → {(rightHandT != null ? GetHierarchyPath(rightHandT) : "НЕ НАЙДЕН")}");

            Transform leftHandChild = bigHandsIntegration.Find("LeftHand");
            Transform rightHandChild = bigHandsIntegration.Find("RightHand");
            UxrHandIntegration leftIntegration = leftHandChild != null ? leftHandChild.GetComponent<UxrHandIntegration>() : null;
            UxrHandIntegration rightIntegration = rightHandChild != null ? rightHandChild.GetComponent<UxrHandIntegration>() : null;

            if (leftHandChild == null)
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix} Под '{bigHandsIntegration.name}' нет дочернего Transform 'LeftHand' — левая UxrHandIntegration недоступна.");
            else if (leftIntegration == null)
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix} На '{leftHandChild.name}' нет компонента UxrHandIntegration.");

            if (rightHandChild == null)
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix} Под '{bigHandsIntegration.name}' нет дочернего Transform 'RightHand' — правая UxrHandIntegration недоступна.");
            else if (rightIntegration == null)
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix} На '{rightHandChild.name}' нет компонента UxrHandIntegration.");

            if (leftHandT != null)
                MapHandRig(rig.LeftArm.Hand, leftHandT, "_Left", leftIntegration, "левая");
            else
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix} Левая рука пропущена: нет BigIKHandLeft / BigIKHandLeft(Clone).");

            if (rightHandT != null)
                MapHandRig(rig.RightArm.Hand, rightHandT, "_Right", rightIntegration, "правая");
            else
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix} Правая рука пропущена: нет BigIKHandRight / BigIKHandRight(Clone) / BigHandRightGeo / BigHandRightGeo(Clone) среди прямых детей аватара.");
        }

        private static void MapHandRig(UltimateXR.Avatar.Rig.UxrAvatarHand handRig, Transform handRoot, string suffix, UxrHandIntegration integration, string sideLabel)
        {
            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix} Карта руки ({sideLabel}): корень IK '{handRoot.name}', интеграция {(integration != null ? $"'{integration.name}'" : "отсутствует")}");

            if (integration != null)
            {
                integration.TryToMatchHand();
                handRoot.position = integration.transform.position;
                handRoot.rotation = integration.transform.rotation;
                VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix}   TryToMatchHand + синхронизация позиции/поворота IK с интеграцией.");
            }
            else
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix}   ({sideLabel}) UxrHandIntegration нет — позиция IK не выровнена по интеграции.");

            Transform wrist = handRoot.Find("Wrist" + suffix);
            // У BigHandRightGeo кость запястья часто называется BigHandRight, а не Wrist_Right.
            if (wrist == null && suffix == "_Right")
                wrist = handRoot.Find("BigHandRight") ?? handRoot.Find("BigHandRight(Clone)");

            if (wrist == null)
            {
                string expected = suffix == "_Right"
                    ? $"'Wrist{suffix}', 'BigHandRight' или 'BigHandRight(Clone)'"
                    : $"'Wrist{suffix}'";
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix}   ({sideLabel}) Нет подходящего запястья ({expected}) под '{handRoot.name}' — ссылки на пальцы в Rig не выставлены.");
                return;
            }

            handRig.Wrist = wrist;
            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix}   ({sideLabel}) handRig.Wrist ← '{wrist.name}'");

            MapFinger(handRig.Thumb, wrist, "Thumb", suffix, 3, true);
            MapFinger(handRig.Index, wrist, "Index", suffix, 3, false);
            MapFinger(handRig.Middle, wrist, "Middle", suffix, 3, false);
            MapFinger(handRig.Ring, wrist, "Ring", suffix, 3, false);
            MapFinger(handRig.Little, wrist, "Little", suffix, 3, false);
            VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix}   ({sideLabel}) Пальцы: попытка назначения костей завершена (см. предупреждения по отсутствующим Palm).");
        }

        private static void MapFinger(UltimateXR.Avatar.Rig.UxrAvatarFinger fingerRig, Transform wrist, string fingerPrefix, string suffix, int numBones, bool isThumb)
        {
            string palmName = fingerPrefix + "_Palm" + suffix;
            Transform palm = wrist.Find(palmName);
            if (palm == null)
            {
                VrBattlegrounds.Core.GameLog.Player.Warning($"{LogPrefix}     Палец {fingerPrefix}: нет '{palmName}' под '{wrist.name}'.");
                return;
            }

            Transform b0 = palm.Find(fingerPrefix + "_0" + suffix);
            Transform b1 = b0 != null ? b0.Find(fingerPrefix + "_1" + suffix) : null;
            Transform b2 = b1 != null ? b1.Find(fingerPrefix + "_2" + suffix) : null;

            if (isThumb)
            {
                fingerRig.Metacarpal = null;
                fingerRig.Proximal = palm;
                fingerRig.Intermediate = b0;
                fingerRig.Distal = b1;
            }
            else
            {
                fingerRig.Metacarpal = palm;
                fingerRig.Proximal = b0;
                fingerRig.Intermediate = b1;
                fingerRig.Distal = b2;
            }

            if (isThumb)
                VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix}     Палец {fingerPrefix} (большой): palm ✓, промежуточные кости _0={(b0 != null)}, _1={(b1 != null)}.");
            else
                VrBattlegrounds.Core.GameLog.Player.Info($"{LogPrefix}     Палец {fingerPrefix}: palm ✓, звенья _0={(b0 != null)}, _1={(b1 != null)}, _2={(b2 != null)}.");

        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t.parent == null)
                return t.name;
            return GetHierarchyPath(t.parent) + "/" + t.name;
        }
    }
}
