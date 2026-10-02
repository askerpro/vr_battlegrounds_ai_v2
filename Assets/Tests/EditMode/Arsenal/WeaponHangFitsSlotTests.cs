using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Оружие на стене арсенала висит внутри своей перфопанели.
    ///
    /// <para>
    /// Слот ставит предмет в якорь со смещением <see cref="WeaponInfo.WeaponPositionOffset" />.
    /// У M16 оно было подобрано до уменьшения до реального размера, и дуло торчало над панелью
    /// на 24 см. Проверяется габарит мешей оружия в висячей позе против габарита
    /// <c>PegboardSection</c> слота, в осях якоря.
    /// </para>
    /// </summary>
    public class WeaponHangFitsSlotTests
    {
        private const string Wall = "Assets/Prefabs/Arsenal/StandardArsenalWall.prefab";
        private const float Tolerance = 0.01f;

        [Test]
        public void Оружие_на_стене_не_выходит_за_панель_слота()
        {
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(Wall);
            var failures = new List<string>();
            int checks = 0;

            foreach (var slot in wall.GetComponentsInChildren<FirearmSlotController>(true))
            {
                var so     = new SerializedObject(slot);
                var info   = so.FindProperty("_weaponInfo").objectReferenceValue as WeaponInfo;
                var anchor = (so.FindProperty("_itemAnchor").objectReferenceValue as Component)?.transform;
                Transform panel = FindDeep(slot.transform, "PegboardSection");
                if (info == null || info.WeaponPrefab == null || anchor == null || panel == null) continue;

                checks++;
                Bounds room = BoundsIn(anchor, panel.GetComponentsInChildren<Renderer>(true), null);
                Matrix4x4 hang = Matrix4x4.TRS(info.WeaponPositionOffset, Quaternion.Euler(info.WeaponRotationOffset), Vector3.one);
                Bounds weapon = BoundsIn(info.WeaponPrefab.transform, info.WeaponPrefab.GetComponentsInChildren<Renderer>(true), hang);

                // Вдоль панели (z) и поперёк (y); по x оружие лежит перед панелью.
                for (int axis = 1; axis <= 2; axis++)
                {
                    if (weapon.min[axis] < room.min[axis] - Tolerance || weapon.max[axis] > room.max[axis] + Tolerance)
                        failures.Add($"{slot.name} ← {info.name}: по оси {"xyz"[axis]} оружие {weapon.min[axis]:F3}..{weapon.max[axis]:F3}, " +
                                     $"панель {room.min[axis]:F3}..{room.max[axis]:F3}");
                }

                if (weapon.min.x < room.max.x - Tolerance)
                    failures.Add($"{slot.name} ← {info.name}: оружие утоплено в панель ({weapon.min.x:F3} < {room.max.x:F3})");
            }

            Assert.That(checks, Is.GreaterThan(0), "Контроль: слотов с панелью и оружием не найдено.");
            Assert.IsEmpty(failures, "Оружие на стене выходит за панель — поправь WeaponPositionOffset в WeaponInfo:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Габарит мешей в осях <paramref name="space" />. <paramref name="root" /> — поза корня
        /// оружия в этих осях; для панели — null, она уже в иерархии слота.
        ///
        /// <para>
        /// Раньше панель узнавалась по единичной позе — и оружие с нулевым смещением на стене (тоже единичная поза)
        /// считалось без масштаба корня: у <c>Revolver</c> (×0,71) и <c>Uzi</c> (×0,21) габарит выходил в 1,4 и 4,8 раза больше.
        /// </para>
        /// </summary>
        private static Bounds BoundsIn(Transform space, Renderer[] renderers, Matrix4x4? root)
        {
            var bounds = new Bounds();
            bool any = false;

            foreach (var r in renderers)
            {
                Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;

                // Панель: мир → оси якоря. Оружие: меш → корень префаба → висячая поза.
                Matrix4x4 m = root == null
                    ? space.worldToLocalMatrix * r.transform.localToWorldMatrix
                    : root.Value * Matrix4x4.Scale(space.localScale) * space.worldToLocalMatrix * r.transform.localToWorldMatrix;

                Bounds b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }

            return bounds;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
