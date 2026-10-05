using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.UI.HUD;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Ставит наручные часы (<c>WristWatch_HUD</c>, T-46) на зарегистрированные аватары, у которых их нет.
    /// Часы — весь HUD игрока, аватар без часов не видит ни денег, ни нотификаций.
    ///
    /// <para>
    /// Поза переносится с эталона — аватара реестра, у которого часы уже есть (Optimized MEF): положение и
    /// поворот часов в «универсальных» осях предплечья UltimateXR (<see cref="UxrUniversalLocalAxes"/>
    /// выравнивает разные риги к одним осям), расстояния — в долях длины предплечья. Это стартовая поза:
    /// после установки посмотреть часы в сцене и подвинуть руками, если надо.
    /// </para>
    ///
    /// <para>
    /// Под замком редактора (<c>Tools/agents/unity-lock.sh</c>): правит префабы аватаров. После — прогнать
    /// <c>RegisteredAvatarBodyTests</c> и <c>WristDisplayTests</c>.
    /// </para>
    /// </summary>
    public static class WristWatchInstaller
    {
        private const string WatchPrefabPath = "Assets/Prefabs/Player/WristWatch_HUD.prefab";

        public static void InstallOnRegisteredAvatars()
        {
            var watchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WatchPrefabPath);
            if (watchPrefab == null)
            {
                GameLog.Error($"[{nameof(WristWatchInstaller)}] Нет префаба часов {WatchPrefabPath}.");
                return;
            }

            GameObject[] avatars = AssetDatabase.FindAssets("t:AvatarRegistry")
                                                .Select(AssetDatabase.GUIDToAssetPath)
                                                .Where(p => !p.StartsWith("Assets/ThirdParty/"))
                                                .Select(AssetDatabase.LoadAssetAtPath<AvatarRegistry>)
                                                .Where(r => r != null && r.avatars != null)
                                                .SelectMany(r => r.avatars)
                                                .Where(a => a != null && a.prefab != null)
                                                .Select(a => a.prefab)
                                                .Distinct()
                                                .ToArray();

            GameObject reference = avatars.FirstOrDefault(a => a.GetComponentInChildren<WristDisplay>(true) != null);
            if (reference == null)
            {
                GameLog.Error($"[{nameof(WristWatchInstaller)}] Ни у одного аватара реестра нет часов — не с чего взять позу.");
                return;
            }

            if (!TryGetReferencePose(reference, out WatchPose pose))
                return;

            int installed = 0;
            foreach (GameObject avatar in avatars.Where(a => a.GetComponentInChildren<WristDisplay>(true) == null))
            {
                if (Install(AssetDatabase.GetAssetPath(avatar), watchPrefab, pose)) installed++;
            }

            GameLog.UI.Info($"[{nameof(WristWatchInstaller)}] Часы поставлены на {installed} аватар(ов). Эталон позы — {reference.name}.");
        }

        /// <summary>Поза часов относительно предплечья, в универсальных осях и долях его длины.</summary>
        private struct WatchPose
        {
            public UxrHandSide Side;
            public bool OnHand;
            public Vector3 Offset;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        /// <summary>Один выбранный prefab; источник нормализованной позы часов задаётся явно.</summary>
        public static string InstallFor(string path, GameObject referencePrefab)
        {
            var target = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!target || !referencePrefab || !TryGetReferencePose(referencePrefab, out var pose))
                throw new System.InvalidOperationException("Выберите prefab аватара и эталон с настроенными часами.");
            if (target.GetComponentsInChildren<WristDisplay>(true).Length > 0)
                return "Часы уже присутствуют; установка не выполнялась.";
            var watch = AssetDatabase.LoadAssetAtPath<GameObject>(WatchPrefabPath);
            if (!watch || !Install(path, watch, pose)) throw new System.InvalidOperationException("Часы не установлены.");
            return "Часы установлены: " + path;
        }

        private static bool TryGetReferencePose(GameObject referencePrefab, out WatchPose pose)
        {
            pose = default;
            var avatar = referencePrefab.GetComponent<UxrAvatar>();
            // Корень вложенного WristWatch_HUD (табло WristDisplay — его потомок). Сверять путь именно ближайшего
            // корня экземпляра: табло само вложенный префаб, и его GetCorrespondingObjectFromSource тоже ведёт в
            // WristWatch_HUD.prefab — так 2026-10-02 эталоном стало табло, и киборг получил часы в 5000 раз меньше.
            Transform watch = referencePrefab.GetComponentInChildren<WristDisplay>(true).transform;
            while (watch != null && !(PrefabUtility.IsAnyPrefabInstanceRoot(watch.gameObject) &&
                                      PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(watch.gameObject) == WatchPrefabPath))
                watch = watch.parent;

            if (watch == null)
            {
                GameLog.Error($"[{nameof(WristWatchInstaller)}] У {referencePrefab.name} часы не из {WatchPrefabPath}.");
                return false;
            }

            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                if (!TryGetFrame(avatar, side, out Transform forearm, out Transform hand, out Quaternion frame, out float length)) continue;
                if (!watch.IsChildOf(forearm)) continue;

                Quaternion inverse = Quaternion.Inverse(frame);
                pose = new WatchPose
                {
                    Side = side,
                    OnHand = watch.IsChildOf(hand),
                    Offset = inverse * (watch.position - forearm.position) / length,
                    Rotation = inverse * watch.rotation,
                    Scale = watch.lossyScale / length
                };
                return true;
            }

            GameLog.Error($"[{nameof(WristWatchInstaller)}] Часы {referencePrefab.name} не на предплечье — не понять, к какой руке.");
            return false;
        }

        private static bool TryGetFrame(UxrAvatar avatar, UxrHandSide side, out Transform forearm, out Transform hand,
                                        out Quaternion frame, out float length)
        {
            frame = Quaternion.identity;
            length = 0f;
            UxrAvatarArm arm = side == UxrHandSide.Left ? avatar.AvatarRig.LeftArm : avatar.AvatarRig.RightArm;
            forearm = arm.Forearm;
            hand = avatar.GetHandBone(side);
            if (forearm == null || hand == null) return false;

            UxrUniversalLocalAxes axes = avatar.AvatarRigInfo.GetArmInfo(side).ForearmUniversalLocalAxes;
            if (axes == null) return false;

            frame = Quaternion.LookRotation(axes.WorldForward, axes.WorldUp);
            length = Vector3.Distance(forearm.position, hand.position);
            return length > 1e-4f;
        }

        private static bool Install(string path, GameObject watchPrefab, WatchPose pose)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var avatar = root.GetComponent<UxrAvatar>();
                if (avatar == null || !TryGetFrame(avatar, pose.Side, out Transform forearm, out Transform hand, out Quaternion frame, out float length))
                {
                    GameLog.Error($"[{nameof(WristWatchInstaller)}] {path}: нет UxrAvatar или предплечья — часы не поставить.");
                    return false;
                }

                var watch = (GameObject)PrefabUtility.InstantiatePrefab(watchPrefab, pose.OnHand ? hand : forearm);
                watch.transform.SetPositionAndRotation(forearm.position + frame * pose.Offset * length, frame * pose.Rotation);
                Vector3 parentScale = watch.transform.parent.lossyScale;
                Vector3 world = pose.Scale * length;
                watch.transform.localScale = new Vector3(world.x / parentScale.x, world.y / parentScale.y, world.z / parentScale.z);

                Workbench.AvatarScopedActions.SetCanonicalAssetId(root, path);
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new System.InvalidOperationException("Часы не сохранены: " + path);
                GameLog.UI.Info($"[{nameof(WristWatchInstaller)}] {path}: часы на {(pose.OnHand ? "кисти" : "предплечье")} ({pose.Side}). Проверить позу в сцене.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
                // SaveAsPrefabAsset затирает NetworkIdentity._assetId — вернуть канон (NetworkAssetIdOnDiskTests).
                VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { path }, false);
            }
        }
    }
}
