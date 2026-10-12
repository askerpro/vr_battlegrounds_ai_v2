using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Таблица сведения механических прицелов (только редактор). Источник данных для
    /// <c>WeaponSightAuthoring.Apply</c>: где стоит комплект на оружии, где половины на планке и какой сдвиг
    /// подвижной детали. Пишет её только окно Sight Auto-Zero («Сохранить в таблицу»); префабы оружия таблица не
    /// меняет. Отпечаток ShotSource инвалидирует запись, если ствол или его цепочка сдвинулись после сведения.
    /// </summary>
    public sealed class WeaponSightTable : ScriptableObject
    {
        public const string AssetPath = "Assets/Data/WeaponSystem/Sights/WeaponSightTable.asset";

        [Serializable]
        public sealed class Entry
        {
            public WeaponInfo Weapon;
            [Tooltip("WeaponInfo.WeaponId на момент записи: диагностика, если ссылка потеряна.")]
            public string WeaponId;
            [Tooltip("Префаб комплекта SightKit_*; null — встроенный в модель прицел (две SightAdjustablePart в префабе оружия).")]
            public GameObject Kit;
            [Tooltip("Поза корня комплекта относительно MeshContainer оружия.")]
            public Vector3 KitLocalPosition;
            public Quaternion KitLocalRotation = Quaternion.identity;
            [Tooltip("Локальная Z половин Rear/Front вдоль планки (система корня комплекта).")]
            public float RearAlongRail, FrontAlongRail;
            [Tooltip("Сдвиг подвижной детали по её локальной Y от собранной позиции.")]
            public float MovingOffset;
            [Tooltip("Дистанция пристрелки, на которую сведено, м.")]
            public float ZeroDistance;
            public ShotSourceFingerprint ShotSource = new ShotSourceFingerprint();
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        public Entry Find(WeaponInfo weapon)
        {
            if (weapon == null) return null;
            foreach (var entry in _entries)
                if (entry != null && entry.Weapon == weapon) return entry;
            return null;
        }

        /// <summary>Заменяет запись оружия (одна запись на WeaponInfo). Сохранение ассета — на вызывающем.</summary>
        public void Set(Entry entry)
        {
            if (entry == null || entry.Weapon == null) throw new ArgumentException("Запись таблицы без WeaponInfo");
            entry.WeaponId = entry.Weapon.WeaponId;
            int index = _entries.FindIndex(e => e != null && e.Weapon == entry.Weapon);
            if (index >= 0) _entries[index] = entry;
            else _entries.Add(entry);
        }

        public static WeaponSightTable Load() => AssetDatabase.LoadAssetAtPath<WeaponSightTable>(AssetPath);

        /// <summary>Единственная точка создания ассета таблицы (пустой).</summary>
        public static WeaponSightTable LoadOrCreate()
        {
            var table = Load();
            if (table != null) return table;
            if (File.Exists(AssetPath))
                throw new InvalidOperationException("WeaponSightTable: путь занят ассетом другого типа: " + AssetPath);
            EnsureFolder(Path.GetDirectoryName(AssetPath).Replace('\\', '/'));
            table = CreateInstance<WeaponSightTable>();
            AssetDatabase.CreateAsset(table, AssetPath);
            AssetDatabase.SaveAssetIfDirty(table);
            return table;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>
    /// Локальные позы цепочки ShotSource → корень префаба (включая корень). Любое отличие означает, что ось
    /// выстрела сдвинулась после сведения, и запись таблицы требует повторного сведения.
    /// </summary>
    [Serializable]
    public sealed class ShotSourceFingerprint
    {
        public List<string> Names = new List<string>();
        public List<Vector3> LocalPositions = new List<Vector3>();
        public List<Quaternion> LocalRotations = new List<Quaternion>();
        public List<Vector3> LocalScales = new List<Vector3>();

        public bool IsEmpty => Names.Count == 0;

        public static ShotSourceFingerprint Capture(Transform shotSource, Transform root)
        {
            if (shotSource == null || root == null) throw new ArgumentNullException(shotSource == null ? nameof(shotSource) : nameof(root));
            var print = new ShotSourceFingerprint();
            for (Transform t = shotSource; t != null; t = t.parent)
            {
                print.Names.Add(t.name);
                print.LocalPositions.Add(t.localPosition);
                print.LocalRotations.Add(t.localRotation);
                print.LocalScales.Add(t.localScale);
                if (t == root) return print;
            }
            throw new ArgumentException("ShotSource не находится под корнем префаба");
        }

        /// <summary>null — совпадает; иначе описание первого расхождения.</summary>
        public string Compare(ShotSourceFingerprint other, float tolerance = 1e-6f)
        {
            if (other == null || other.Names.Count != Names.Count) return "Другая длина цепочки ShotSource → корень";
            for (int i = 0; i < Names.Count; i++)
            {
                if (Names[i] != other.Names[i]) return $"Другой узел цепочки: {Names[i]} ≠ {other.Names[i]}";
                if ((LocalPositions[i] - other.LocalPositions[i]).sqrMagnitude > tolerance * tolerance
                    || Quaternion.Angle(LocalRotations[i], other.LocalRotations[i]) > 1e-3f
                    || (LocalScales[i] - other.LocalScales[i]).sqrMagnitude > tolerance * tolerance)
                    return $"Поза узла {Names[i]} изменилась после сведения";
            }
            return null;
        }
    }
}
