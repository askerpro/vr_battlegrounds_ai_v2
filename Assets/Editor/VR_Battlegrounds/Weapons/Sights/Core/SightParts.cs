using System;
using System.Linq;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Поиск половин прицела и ShotSource и сборка входа решателя — общие для стенда комплекта и стенда родного
    /// прицела. Только чтение Transform, без записи; исключение — <see cref="BindOptic"/> (поля привязки коллиматора).
    /// </summary>
    public static class SightParts
    {
        /// <summary>
        /// Ровно две SightAdjustablePart (Rear и Front) в <paramref name="scope"/>; подвижная — не больше одной
        /// (null — подвижной детали нет, например PPK). Подвижная деталь — с нулевым локальным поворотом.
        /// </summary>
        public static void Find(GameObject scope, out SightAdjustablePart rear, out SightAdjustablePart front, out SightAdjustablePart moving)
        {
            var parts = scope.GetComponentsInChildren<SightAdjustablePart>(true);
            if (parts.Length != 2 || parts.Count(p => p.Role == SightPartRole.Rear) != 1 || parts.Count(p => p.Role == SightPartRole.Front) != 1)
                throw new InvalidOperationException($"{scope.name}: нужно ровно две SightAdjustablePart (Rear и Front), найдено {parts.Length}");
            rear = parts.Single(p => p.Role == SightPartRole.Rear);
            front = parts.Single(p => p.Role == SightPartRole.Front);
            var adjustable = parts.Where(p => p.IsAdjustable).ToArray();
            if (adjustable.Length > 1) throw new InvalidOperationException($"{scope.name}: подвижных деталей {adjustable.Length}, допускается одна");
            moving = adjustable.Length == 1 ? adjustable[0] : null;
            if (moving != null && Quaternion.Angle(moving.transform.localRotation, Quaternion.identity) > .01f)
                throw new InvalidOperationException($"{moving.name}: ход только по локальной Y — поворот детали должен быть нулевым");
        }

        public static Transform FindShotSource(GameObject root)
        {
            var sources = root.GetComponentsInChildren<UxrProjectileSource>(true);
            if (sources.Length != 1) throw new InvalidOperationException($"{root.name}: UxrProjectileSource {sources.Length}, нужен 1");
            var shotTypes = sources[0].ShotTypes;
            if (shotTypes == null || shotTypes.Count == 0 || shotTypes[0] == null || shotTypes[0].ShotSource == null)
                throw new InvalidOperationException($"{root.name}: нет ShotSource у типа выстрела 0");
            return shotTypes[0].ShotSource;
        }

        /// <summary>Коллиматор комплекта (<see cref="WeaponOpticView"/>) или null — комплект механический.</summary>
        public static WeaponOpticView FindOptic(GameObject scope)
        {
            var views = scope.GetComponentsInChildren<WeaponOpticView>(true);
            if (views.Length > 1) throw new InvalidOperationException($"{scope.name}: коллиматоров {views.Length}, допускается один");
            return views.Length == 1 ? views[0] : null;
        }

        /// <summary>Комплект-коллиматор: есть WeaponOpticView и нет деталей механического прицела.</summary>
        public static bool IsOpticKit(GameObject kit) => kit != null && FindOptic(kit) != null
            && kit.GetComponentsInChildren<SightAdjustablePart>(true).Length == 0;

        /// <summary>
        /// «Сведение» коллиматора: сетка конечного фокуса привязывается к ShotSource оружия и дистанции пристрелки
        /// (точка T = ShotSource + forward·Z). Двигать нечего — пишет только поля привязки WeaponOpticView.
        /// </summary>
        public static void BindOptic(WeaponOpticView view, UxrProjectileSource source, WeaponInfo weapon,
                                     WeaponSightCalibrationSettings settings, float zeroDistance)
        {
            if (view == null || source == null || settings == null) throw new InvalidOperationException("Привязка коллиматора: нет WeaponOpticView, UxrProjectileSource или настроек");
            var so = new SerializedObject(view);
            so.FindProperty("_source").objectReferenceValue = source;
            so.FindProperty("_shotTypeIndex").intValue = 0;
            so.FindProperty("_weapon").objectReferenceValue = weapon;
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.FindProperty("_profile").objectReferenceValue = null;
            so.FindProperty("_calibratedZeroDistance").floatValue = zeroDistance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Привязан ли коллиматор к этому источнику выстрела и дистанции (без профиля).</summary>
        public static string OpticBindingProblem(WeaponOpticView view, UxrProjectileSource source, WeaponInfo weapon, float zeroDistance)
        {
            var so = new SerializedObject(view);
            if (so.FindProperty("_source").objectReferenceValue != source) return "коллиматор не привязан к ShotSource этого оружия";
            if (so.FindProperty("_shotTypeIndex").intValue != 0) return "коллиматор привязан не к типу выстрела 0";
            if (weapon != null && so.FindProperty("_weapon").objectReferenceValue != weapon) return "коллиматор привязан к другому WeaponInfo";
            if (so.FindProperty("_profile").objectReferenceValue != null) return "у коллиматора назначен профиль — сведение комплекта профиль не использует";
            if (Math.Abs(so.FindProperty("_calibratedZeroDistance").floatValue - zeroDistance) > 1e-6f) return $"сетка сведена не на {zeroDistance} м";
            return null;
        }

        /// <summary>Центр окна коллиматора (bounds submesh линзы) в мире; ось окна — forward корня комплекта.</summary>
        public static Vector3 OpticLensCenter(WeaponOpticView view)
        {
            var so = new SerializedObject(view);
            var renderer = so.FindProperty("_lensRenderer").objectReferenceValue as Renderer;
            int index = so.FindProperty("_lensMaterialIndex").intValue;
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null || index < 0 || index >= filter.sharedMesh.subMeshCount)
                throw new InvalidOperationException($"{view.name}: нет меша окна коллиматора (submesh {index})");
            Mesh mesh = filter.sharedMesh;
            // Bounds submesh могут быть не посчитаны (SetTriangles без calculateBounds) — по вершинам окна, если меш читаем.
            Bounds lens = mesh.GetSubMesh(index).bounds;
            if (mesh.isReadable)
            {
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.GetIndices(index);
                if (indices.Length > 0)
                {
                    lens = new Bounds(vertices[indices[0]], Vector3.zero);
                    foreach (int i in indices) lens.Encapsulate(vertices[i]);
                }
            }
            return renderer.transform.TransformPoint(lens.center);
        }

        /// <summary>Вход решателя по текущей позе. Без подвижной детали — режим только измерения.</summary>
        public static SightAutoZeroInput Input(SightAdjustablePart rear, SightAdjustablePart front, SightAdjustablePart moving,
                                               Transform muzzle, double zeroDistance)
        {
            return new SightAutoZeroInput
            {
                Rear = rear.transform.position, Front = front.transform.position,
                MovingRole = moving != null ? moving.Role : SightPartRole.Front,
                MeasureOnly = moving == null,
                MoveAxisPerUnit = moving != null ? moving.transform.parent.TransformVector(moving.transform.localRotation * Vector3.up) : Vector3.zero,
                RearUp = rear.transform.up, FrontUp = front.transform.up,
                MuzzlePosition = muzzle.position, MuzzleForward = muzzle.forward, MuzzleUp = muzzle.up,
                ZeroDistance = zeroDistance,
                CurrentOffset = moving != null ? moving.Offset : 0, TravelDown = moving != null ? moving.TravelDown : 0,
                TravelUp = moving != null ? moving.TravelUp : 0
            };
        }
    }
}
