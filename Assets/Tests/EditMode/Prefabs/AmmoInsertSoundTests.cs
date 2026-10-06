using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Audio;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Звук вставки магазина или патрона один, и задаёт его приёмник — <see cref="AnchorSound" /> гнезда
    /// (свой клип или общий <see cref="MagazineAnchorSoundDefaults" />). Звук размещения на самом боеприпасе
    /// (<see cref="UxrAudioManipulation" />, унаследованный от доноров пака) играл вторым поверх звука гнезда.
    /// </summary>
    public class AmmoInsertSoundTests
    {
        [Test]
        public void Есть_общий_звук_вставки()
        {
            Assert.IsNotNull(MagazineAnchorSoundDefaults.Instance, "Нет Resources/MagazineAnchorSoundDefaults.asset.");
            Assert.IsNotNull(MagazineAnchorSoundDefaults.Instance.InsertClip, "У MagazineAnchorSoundDefaults не задан звук вставки.");
        }

        /// <summary>Решение пользователя: магазины звучат одним общим звуком; свой звук — только у окна приёма патрона.</summary>
        [Test]
        public void Гнёзда_магазинов_звучат_общим_звуком()
        {
            var failures = new List<string>();
            int anchors = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                var firearm = info != null && info.WeaponPrefab != null ? info.WeaponPrefab.GetComponent<UxrFirearmWeapon>() : null;
                if (firearm == null) continue;
                var triggers = new SerializedObject(firearm).FindProperty("_triggers");
                for (int i = 0; i < triggers.arraySize; i++)
                {
                    var anchor = triggers.GetArrayElementAtIndex(i).FindPropertyRelative("_ammunitionMagAnchor").objectReferenceValue as UxrGrabbableObjectAnchor;
                    var sound = anchor != null ? anchor.GetComponent<AnchorSound>() : null;
                    if (sound == null) continue;
                    anchors++;
                    if (sound.Source != null && sound.Source.clip != null)
                        failures.Add($"{info.WeaponPrefab.name}/{anchor.name}: свой звук вставки '{sound.Source.clip.name}' вместо общего.");
                }
            }

            Assert.Greater(anchors, 0, "Не нашлось ни одного гнезда магазина.");
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
