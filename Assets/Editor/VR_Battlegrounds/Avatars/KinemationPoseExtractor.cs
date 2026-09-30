using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Кадр рук на оружии из пака KINEMATION Tactical Shooter (T-39): поза пальцев и корпус оружия в
    /// универсальных осях ладони — то же, что <see cref="HandsPackPoseExtractor" /> даёт для пака Hands, поэтому
    /// дальше работают те же <see cref="HandsPackGripAligner" /> и <see cref="HandsPackPoseImporter" />.
    ///
    /// <para>
    /// Риг — <c>SKM_Operator</c> со скелетом UE5-манекена: кисть <c>hand_r</c>, пальцы
    /// <c>thumb|index|middle|ring|pinky_01..03_r</c> (пястные <c>*_metacarpal</c> не берутся — у рук пака
    /// Hands их нет, поза описывается тремя фалангами). Клипа прицеливания в паке нет: руки на оружии
    /// в клипе <c>A_FP_&lt;ствол&gt;_Idle</c>.
    /// </para>
    ///
    /// <para>
    /// Оружие пак вешает на кость <c>ik_hand_gun</c> с поворотом <c>weaponRotationOffset</c> из настроек ствола
    /// (<c>TacAnimSettings_&lt;ствол&gt;</c>, у всех стволов пака (90, 0, 0)): так ствол смотрит вперёд, а
    /// указательный палец лежит на спуске (проверено на Mk14, SRM-12, AK105, R08 — 2–3 см до спуска).
    /// </para>
    /// </summary>
    public static class KinemationPoseExtractor
    {
        public const string Character = "Assets/ThirdParty/KINEMATION/TacticalShooterPack/Meshes/Character/Operator/SKM_Operator.fbx";
        public const string WeaponBone = "ik_hand_gun";

        private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinky" };

        /// <param name="clip">Клип персонажа пака (<c>Animations/&lt;ствол&gt;/Character/A_FP_*_Idle</c>)</param>
        /// <param name="time">Время кадра, с</param>
        /// <param name="side">Рука пака</param>
        /// <param name="weaponPrefab">Префаб оружия пака <c>Prefabs/Weapons/W_*.prefab</c></param>
        /// <param name="bodyBone">Кость корпуса в префабе оружия (<c>Body</c>)</param>
        /// <param name="weaponRest">Клип покоя оружия (<c>A_W_*_Idle</c>) или <c>null</c> — поза префаба</param>
        public static HandsPackPoseExtractor.Sample Extract(AnimationClip clip, float time, UxrHandSide side, GameObject weaponPrefab, string bodyBone,
                                                           AnimationClip weaponRest = null)
        {
            var character = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Character));
            character.hideFlags = HideFlags.HideAndDontSave;
            var weapon = (GameObject)Object.Instantiate(weaponPrefab);
            weapon.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                Dictionary<string, Transform> bones = character.GetComponentsInChildren<Transform>(true)
                                                               .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                UxrAvatarArm arm = BuildArm(bones, side);

                // Оси — по позе модели до кадра клипа (так же SDK считает их у аватара).
                HandsPackPoseExtractor.SolveAxes(arm.Hand, side, out UxrUniversalLocalAxes handAxes, out UxrUniversalLocalAxes fingerAxes);

                clip.SampleAnimation(character, time);

                Transform gun = Bone(bones, WeaponBone);
                weapon.transform.SetParent(gun, false);
                weapon.transform.localPosition = Vector3.zero;
                weapon.transform.localRotation = WeaponRotationOffset(weaponPrefab);
                weapon.transform.localScale = Vector3.one;
                Animator weaponAnimator = weapon.GetComponentInChildren<Animator>(true);
                if (weaponRest != null && weaponAnimator != null) weaponRest.SampleAnimation(weaponAnimator.gameObject, 0f);
                VrBattlegrounds.Editor.Gameplay.KinemationWeapon.SampleFullMagazines(weapon, weaponAnimator);

                Transform body = weapon.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == bodyBone)
                                 ?? throw new KeyNotFoundException($"{weaponPrefab.name}: нет кости корпуса {bodyBone}");

                Matrix4x4 hand     = Matrix4x4.TRS(arm.Hand.Wrist.position, handAxes.UniversalRotation, Vector3.one);
                Matrix4x4 relative = hand.inverse * Matrix4x4.TRS(body.position, body.rotation, Vector3.one);

                return new HandsPackPoseExtractor.Sample
                {
                    Hand         = new UxrHandDescriptor(arm, handAxes, fingerAxes),
                    BodyPosition = relative.GetColumn(3),
                    BodyRotation = relative.rotation,
                    BodyScale    = 1f
                };
            }
            finally
            {
                Object.DestroyImmediate(weapon);
                Object.DestroyImmediate(character);
            }
        }

        /// <summary>
        /// Поворот оружия на кости <see cref="WeaponBone" /> — поле <c>weaponRotationOffset</c> настроек ствола.
        /// Читается сериализацией: код пака живёт в <c>Assembly-CSharp</c>, редакторной сборке проекта недоступен.
        /// </summary>
        public static Quaternion WeaponRotationOffset(GameObject weaponPrefab)
        {
            foreach (MonoBehaviour mb in weaponPrefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                SerializedProperty settings = new SerializedObject(mb).FindProperty("tacWeaponSettings");
                if (settings == null || settings.objectReferenceValue == null) continue;
                SerializedProperty offset = new SerializedObject(settings.objectReferenceValue).FindProperty("weaponRotationOffset");
                if (offset != null) return offset.quaternionValue;
            }

            throw new KeyNotFoundException($"{weaponPrefab.name}: нет настроек ствола с weaponRotationOffset");
        }

        private static UxrAvatarArm BuildArm(Dictionary<string, Transform> bones, UxrHandSide side)
        {
            string s   = side == UxrHandSide.Left ? "l" : "r";
            var    arm = new UxrAvatarArm();
            arm.Hand.Wrist = Bone(bones, $"hand_{s}");

            UxrAvatarFinger[] fingers = { arm.Hand.Thumb, arm.Hand.Index, arm.Hand.Middle, arm.Hand.Ring, arm.Hand.Little };
            for (int f = 0; f < fingers.Length; f++)
            {
                fingers[f].SetupFingerBones(Enumerable.Range(1, 3).Select(n => Bone(bones, $"{FingerNames[f]}_0{n}_{s}")).ToList());
            }

            return arm;
        }

        private static Transform Bone(Dictionary<string, Transform> bones, string name)
        {
            if (!bones.TryGetValue(name, out Transform bone))
            {
                throw new KeyNotFoundException($"В модели {Character} нет кости {name} — это не скелет UE5-манекена пака KINEMATION.");
            }

            return bone;
        }
    }
}
