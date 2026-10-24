using System.Collections.Generic;
using System.Linq;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Звук каждому якорю (решение пользователя: любое «положить» и «снять» звучит). Ставит <see cref="AnchorSound" />
    /// и его источник якорям без звука в префабах <c>Assets/Prefabs</c>. Уже настроенные якоря (карманы аватаров,
    /// гнёзда магазинов нашего оружия) не трогает. Проверка — <c>AnchorSoundCoverageTests</c>.
    ///
    /// <para>
    /// Якорь из вложенного префаба проекта правится в своём префабе, а не переопределением у родителя.
    /// </para>
    ///
    /// <para>
    /// Клипы в префаб не пишутся (решение пользователя 2026-10-23: звуковые дефолты — ассеты): звучит свой клип якоря
    /// (нарезка пака гнезда, <c>AnchorSoundOverride</c>) или дефолт в момент проигрывания — категории ствола
    /// (<c>WeaponFeedbackDefaults</c>), роли якоря (<see cref="MagazineAnchorSoundDefaults" />, роль — <see cref="AnchorRole.Get" />)
    /// или общий звук вставки. Установщик ставит только компонент и источник. Старую пару <see cref="PlaceClip" />/<see cref="TakeClip" />
    /// с префабов снимает <see cref="AnchorSoundRoleDefaultsCleanup" />.
    /// </para>
    ///
    /// <para>
    /// Сцены не правит: добавление компонентов в открытую сцену пересохраняло её с потерей переопределений
    /// <c>_uxrUniqueId</c> и тегов (дифф Lobby — 7 тыс. строк). Якорь, живущий только в сцене, — повод вынести
    /// предмет в префаб проекта; сэмплы UltimateXR в сценах убираются (так же требует <c>GrabPoseCoverageTests</c>).
    /// </para>
    /// </summary>
    public static class AnchorSoundInstaller
    {
        private const string Prefabs = "Assets/Prefabs";
        /// <summary>
        /// Пара клипов карманов и слотов, которую установщик раскладывал до дефолтов по роли (историческая: по ней
        /// <see cref="AnchorSoundRoleDefaultsCleanup" /> узнаёт старые копии; файл вставки удалён 2026-10-24).
        /// </summary>
        internal const string PlaceClip = "Assets/Audio/SFX/Universal/Landing_2_Amm.mp3";
        internal const string TakeClip = "Assets/Audio/SFX/Universal/Weapon_Select.flac";

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Apply Anchor Sounds")]
        public static void ApplyAll()
        {
            int prefabs = ApplyPrefabs();
            GameLog.Debug.Info($"[AnchorSoundInstaller] Звук поставлен якорям префабов: {prefabs}.");
        }

        /// <summary>Якоря префабов проекта; вложенные — в своих префабах (проходы до неподвижной точки).</summary>
        public static int ApplyPrefabs()
        {
            int total = 0;
            for (int pass = 0; pass < 4; pass++)
            {
                int changed = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Prefabs }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!asset.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true).Any(a => NeedsSound(a) && OwnedBy(a, path))) continue;

                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        foreach (UxrGrabbableObjectAnchor anchor in root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
                            if (NeedsSound(anchor) && OwnedBy(anchor, path) && Install(anchor)) changed++;
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
                total += changed;
                if (changed == 0) break;
            }
            return total;
        }

        /// <summary>Нет компонента или источника (клипы — дефолты в момент проигрывания).</summary>
        private static bool NeedsSound(UxrGrabbableObjectAnchor anchor)
        {
            AnchorSound sound = anchor.GetComponent<AnchorSound>();
            return sound == null || sound.Source == null;
        }

        /// <summary>Якорь принадлежит этому префабу, а не вложенному префабу проекта (тот правится у себя).</summary>
        internal static bool OwnedBy(UxrGrabbableObjectAnchor anchor, string prefabPath)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(anchor);
            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : prefabPath;
            return sourcePath == prefabPath || !sourcePath.StartsWith(Prefabs + "/");
        }

        /// <summary>
        /// Ставит звук: гнездо магазина в оружии — щелчок/выпадение (выпадение и от кнопки выброса), остальные —
        /// как у карманов. Источник — на самом якоре, объёмный: не на объекте Activate On Placed (SDK выключает его
        /// при хвате, и звук доставания оборвался бы).
        /// </summary>
        private static bool Install(UxrGrabbableObjectAnchor anchor)
        {
            bool magazineSocket = anchor.GetComponentInParent<UxrFirearmWeapon>(true) != null;
            AudioSource source = anchor.GetComponent<AudioSource>();
            if (source == null) source = anchor.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.minDistance = 0.5f;
            source.maxDistance = 12f;
            source.rolloffMode = AudioRolloffMode.Linear;

            // Не «??»: в редакторе GetComponent отсутствующего компонента — «поддельный null», мимо которого «??» проходит.
            AnchorSound sound = anchor.GetComponent<AnchorSound>();
            if (sound == null) sound = anchor.gameObject.AddComponent<AnchorSound>();
            var so = new SerializedObject(sound);
            if (so.FindProperty("_source").objectReferenceValue == null) so.FindProperty("_source").objectReferenceValue = source;
            so.FindProperty("_onlyByHand").boolValue = true;
            so.FindProperty("_takeOutOnlyByHand").boolValue = !magazineSocket;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
