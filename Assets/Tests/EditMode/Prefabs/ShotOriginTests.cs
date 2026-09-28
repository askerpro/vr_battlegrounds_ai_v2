using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Пуля вылетает из дула, а не из середины оружия.
    ///
    /// <para>
    /// <b>Дефект.</b> UltimateXR рождает снаряд в <c>ShotSource</c>, а у оружия проекта он стоял в
    /// ствольной коробке: у M16 в 60 см от дула, у дробовика в 40, у пистолета в 21. След трассера
    /// первые кадры тянулся из середины оружия; пока ствол неподвижен, это спрятано в модели, но
    /// отдача (до 6°) или движение руки выносят кусок следа наружу — пуля «вылетает не из дула».
    /// </para>
    ///
    /// <para>
    /// <b>Цена.</b> Луч попадания тоже начинается у дула: ствол, упёртый в стену, стреляет сквозь неё.
    /// </para>
    /// </summary>
    public class ShotOriginTests
    {
        private const float MaxDistanceFromMuzzle = 0.03f;

        [Test]
        public void Снаряд_рождается_у_дула()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null) continue;

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(info.WeaponPrefab);
                if (source != null && AssetDatabase.GetAssetPath(source).StartsWith("Assets/ThirdParty/")) continue;

                var projectiles = info.WeaponPrefab.GetComponent<UxrProjectileSource>();
                if (projectiles == null) continue;

                float front = MeshFront(info.WeaponPrefab);

                foreach (UxrShotDescriptor shot in projectiles.ShotTypes)
                {
                    checks++;
                    float distance = Vector3.Distance(shot.ShotSource.position, shot.Tip.position);
                    if (distance > MaxDistanceFromMuzzle)
                        failures.Add($"{info.WeaponPrefab.name}: ShotSource в {distance * 100f:F0} см от дула (Tip)");

                    // Tip — на срезе ствола: у M16 он стоял на 3.4 см в глубине пламегасителя.
                    float tipBehind = front - info.WeaponPrefab.transform.InverseTransformPoint(shot.Tip.position).z * info.WeaponPrefab.transform.localScale.z;
                    if (tipBehind > MaxDistanceFromMuzzle)
                        failures.Add($"{info.WeaponPrefab.name}: Tip на {tipBehind * 100f:F1} см позади среза ствола");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Контроль: оружия проекта не найдено.");
            Assert.IsEmpty(failures, "Снаряд рождается не у дула:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Передний край мешей оружия вдоль ствола (ось Z корня), в метрах мира. Вложенный
        /// магазин не в счёт — он не торчит вперёд дула.
        /// </summary>
        private static float MeshFront(GameObject prefab)
        {
            Transform root = prefab.transform;
            float front = float.MinValue;

            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponentInParent<UxrFirearmMag>(true) != null) continue;

                Matrix4x4 m = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 v in filter.sharedMesh.vertices)
                    front = Mathf.Max(front, m.MultiplyPoint3x4(v).z);
            }

            return front * root.localScale.z;
        }
    }
}
