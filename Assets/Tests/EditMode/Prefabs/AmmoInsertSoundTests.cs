using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Audio;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Звук вставки магазина или патрона один, и задаёт его приёмник — <see cref="AnchorSound" /> гнезда
    /// (своя нарезка пака, иначе дефолт категории ствола, роли или общий <see cref="MagazineAnchorSoundDefaults" />). Звук размещения на самом боеприпасе
    /// (<see cref="UxrAudioManipulation" />, унаследованный от доноров пака) играл вторым поверх звука гнезда.
    /// </summary>
    public class AmmoInsertSoundTests
    {
        private const string RegistryPath = "Assets/Data/Weapons/Resources/WeaponRegistry.asset";

        [Test]
        public void Есть_общий_звук_вставки()
        {
            Assert.IsNotNull(MagazineAnchorSoundDefaults.Instance, "Нет Resources/MagazineAnchorSoundDefaults.asset.");
            Assert.IsNotNull(MagazineAnchorSoundDefaults.Instance.InsertClip, "У MagazineAnchorSoundDefaults не задан звук вставки.");
        }

        /// <summary>
        /// Решение пользователя 2026-10-24: гнездо магазина каждого ствола звучит своей нарезкой пака; нет её — запасным звуком
        /// категории ствола (<see cref="WeaponFeedbackDefaults.MagazineInsert" />/<see cref="WeaponFeedbackDefaults.MagazineTakeOut" />),
        /// затем дефолтом роли и общим звуком <see cref="MagazineAnchorSoundDefaults" />. Итог у каждого гнезда реестра —
        /// и вставка, и снятие со звуком; без своего клипа вставки звучит именно дефолт категории ствола.
        /// </summary>
        [Test]
        public void Гнёзда_магазинов_звучат_своей_нарезкой_или_дефолтом_категории()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(RegistryPath);
            Assert.IsNotNull(registry, "Нет WeaponRegistry.");

            var failures = new List<string>();
            int sockets = 0;
            foreach (WeaponInfo info in registry.Weapons)
            {
                var firearm = info != null && info.WeaponPrefab != null ? info.WeaponPrefab.GetComponent<UxrFirearmWeapon>() : null;
                if (firearm == null) continue;
                for (int i = 0; i < firearm.TriggerCount; i++)
                {
                    if (!firearm.TryGetTriggerMagazineAnchor(i, out UxrGrabbableObjectAnchor anchor) || anchor == null) continue;
                    sockets++;
                    string where = $"{info.WeaponPrefab.name}/{anchor.name}";
                    var sound = anchor.GetComponent<AnchorSound>();
                    if (sound == null) { failures.Add($"{where}: нет AnchorSound — гнездо немое."); continue; }

                    if (sound.InsertClip == null) failures.Add($"{where}: итоговый звук вставки пуст.");
                    if (sound.TakeOutClip == null) failures.Add($"{where}: итоговый звук снятия пуст.");
                    if (sound.OwnInsertClip != null) continue;

                    var host = anchor.GetComponentInParent<WeaponSystem>(true);
                    WeaponFeedbackDefaults category = host != null ? host.FeedbackDefaults : null;
                    if (category == null)
                        failures.Add($"{where}: нет своей нарезки вставки и нет дефолтов категории ствола (хост WeaponSystem/FeedbackDefaults).");
                    else if (sound.InsertClip != category.MagazineInsert)
                        failures.Add($"{where}: без своей нарезки звучит '{(sound.InsertClip != null ? sound.InsertClip.name : "—")}', " +
                                     $"а не дефолт категории '{(category.MagazineInsert != null ? category.MagazineInsert.name : "—")}' ({category.name}).");
                }
            }

            Assert.Greater(sockets, 0, "Не нашлось ни одного гнезда магазина в реестре.");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void У_боеприпасов_нет_своего_звука_размещения()
        {
            var failures = new List<string>();
            var checkedPrefabs = new HashSet<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.MagazinePrefab == null || !checkedPrefabs.Add(info.MagazinePrefab)) continue;
                foreach (var audio in info.MagazinePrefab.GetComponentsInChildren<UxrAudioManipulation>(true))
                {
                    var clip = new SerializedObject(audio).FindProperty("_audioOnPlace._clip").objectReferenceValue;
                    if (clip != null)
                        failures.Add($"{info.MagazinePrefab.name}: свой звук размещения '{clip.name}' — вставка прозвучит дважды. " +
                                     "Звук вставки задаёт AnchorSound гнезда.");
                }
            }

            Assert.Greater(checkedPrefabs.Count, 0, "Не нашлось ни одного боеприпаса в WeaponInfo.");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }
    }
}
