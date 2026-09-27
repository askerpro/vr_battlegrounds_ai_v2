using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Звук вставки в якорь не играет сам при спавне (AUD-01).
    ///
    /// <para>
    /// UltimateXR включает объекты <c>Activate On …</c> якоря не только по действию игрока:
    /// <c>UxrGrabbableObjectAnchor.Start</c> и инициализация <c>UxrGrabManager</c> включают
    /// <c>ActivateOnPlaced</c>, если в якоре уже что-то лежит. <c>AudioSource</c> с
    /// <c>Play On Awake</c> на таком объекте звучит на каждом спавне: 16 заряженных M16
    /// на стенах арсенала давали одновременный «щелчок затвора» при старте каждой карты.
    /// </para>
    ///
    /// <para>
    /// Звук вставки играет <see cref="AnchorSound" /> по событию <c>Placed</c> —
    /// событие не приходит от стартового состояния.
    /// </para>
    /// </summary>
    public class AnchorActivationAudioTests
    {
        private static readonly string[] PrefabRoots = { "Assets/Prefabs" };

        [Test]
        public void AnchorActivatedObjects_HaveNoPlayOnAwakeAudio()
        {
            var failures = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", PrefabRoots))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                foreach (var anchor in root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
                {
                    var activated = new Dictionary<string, GameObject>
                    {
                        { nameof(anchor.ActivateOnPlaced), anchor.ActivateOnPlaced },
                        { nameof(anchor.ActivateOnEmpty), anchor.ActivateOnEmpty },
                        { nameof(anchor.ActivateOnCompatibleNear), anchor.ActivateOnCompatibleNear },
                        { nameof(anchor.ActivateOnCompatibleNotNear), anchor.ActivateOnCompatibleNotNear },
                        { nameof(anchor.ActivateOnHandNearAndGrabbable), anchor.ActivateOnHandNearAndGrabbable }
                    };

                    foreach (var pair in activated)
                    {
                        if (pair.Value == null) continue;

                        foreach (var source in pair.Value.GetComponentsInChildren<AudioSource>(true))
                        {
                            if (source.playOnAwake)
                                failures.Add($"{path} / {anchor.name}.{pair.Key} → {source.name}: Play On Awake");
                        }
                    }
                }
            }

            Assert.IsEmpty(failures, "AudioSource с Play On Awake на объектах, которые включает якорь:\n" +
                                     string.Join("\n", failures));
        }

        /// <summary>
        /// Гнездо магазина каждого оружия арсенала щёлкает при вставке и звучит, когда магазин
        /// выпадает — и от руки, и от кнопки выброса (<c>MagazineEject</c> вынимает без руки,
        /// поэтому <c>Take Out Only By Hand</c> снят). Источник — не на <c>Activate On Placed</c>:
        /// при хвате магазина рукой SDK выключает тот объект, и звук оборвался бы.
        /// </summary>
        [Test]
        public void Гнездо_магазина_оружия_звучит_при_вставке_и_выпадении()
        {
            var failures = new List<string>();
            int checkedAnchors = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:WeaponInfo"))
            {
                var info = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null) continue;

                var path = AssetDatabase.GetAssetPath(info.WeaponPrefab);

                foreach (var anchor in info.WeaponPrefab.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
                {
                    checkedAnchors++;
                    var sound = anchor.GetComponent<AnchorSound>();

                    if (sound == null) { failures.Add($"{path} / {anchor.name}: нет AnchorSound"); continue; }
                    if (sound.Source == null || sound.Source.clip == null) failures.Add($"{path} / {anchor.name}: нет источника или звука вставки");
                    if (sound.TakeOutClip == null) failures.Add($"{path} / {anchor.name}: нет звука выпадения (Take Out Clip)");
                    if (sound.TakeOutOnlyByHand) failures.Add($"{path} / {anchor.name}: Take Out Only By Hand включён — выброс кнопкой A/X будет беззвучным");
                    if (sound.Source != null && anchor.ActivateOnPlaced != null && sound.Source.transform.IsChildOf(anchor.ActivateOnPlaced.transform))
                        failures.Add($"{path} / {anchor.name}: источник на Activate On Placed — звук выпадения при хвате рукой оборвётся");
                }
            }

            Assert.Greater(checkedAnchors, 0, "Контроль: гнёзд магазина у оружия арсенала не найдено.");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void AnchorSound_ReferencesAudioSource()
        {
            var failures = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", PrefabRoots))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                foreach (var sound in root.GetComponentsInChildren<AnchorSound>(true))
                {
                    if (sound.Source == null)
                        failures.Add($"{path} / {sound.name}: не назначен AudioSource");
                    if (sound.GetComponent<UxrGrabbableObjectAnchor>() == null)
                        failures.Add($"{path} / {sound.name}: нет UxrGrabbableObjectAnchor");
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
