using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Явный список корней, созданных Apply выращивателя; владелец записи — его editor backend.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class BlockoutGeneratedSet : MonoBehaviour
    {
        [SerializeField, HideInInspector] private GameObject[] objects = Array.Empty<GameObject>();
        public IReadOnlyList<GameObject> Objects => Array.AsReadOnly(objects);
        public void Replace(IEnumerable<GameObject> roots)
        {
            var copied = roots?.ToArray() ?? throw new ArgumentNullException(nameof(roots));
            if (copied.Any(g => g == null) || copied.Distinct().Count() != copied.Length)
                throw new ArgumentException("Нужен явный список разных созданных корней.");
            objects = copied;
        }
    }
}
