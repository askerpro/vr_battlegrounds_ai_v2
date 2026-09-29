using System.Collections.Generic;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Силуэт ничьего снаряжения сквозь труп (T-35): оружие, оказавшееся под телом, видно контуром
    /// (<c>Shaders/LootXray.shader</c>, рисуется только в заслонённой части). Локально, на каждой машине.
    ///
    /// <para>
    /// <b>Только у трупа.</b> Силуэт включает труп — пока лежит, он «подсвечивает» ничьи предметы в
    /// радиусе <see cref="Corpse"/> (<see cref="Ping"/>); без свежего сигнала силуэт гаснет сам. Иначе
    /// это был бы «воллхак»: стволы по всей карте сквозь стены. Взятый в руку, в кобуру или изъятый
    /// предмет силуэт теряет сразу (<see cref="Wanted"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LootXray : MonoBehaviour
    {
        public const string MaterialResource = "LootXray";

        /// <summary>Сколько секунд силуэт живёт без нового сигнала трупа.</summary>
        public const float PingLifetime = 0.6f;

        private static Material _material;

        private readonly List<GameObject> _copies = new List<GameObject>();
        private UxrGrabbableObject _item;
        private float _pingedAt = float.NegativeInfinity;
        private bool _shown;

        /// <summary>Показывать ли силуэт: предмет ничей, и труп рядом подал сигнал недавно.</summary>
        public static bool Wanted(bool isLoose, float secondsSincePing) => isLoose && secondsSincePing <= PingLifetime;

        /// <summary>Труп рядом: показать силуэт <paramref name="item"/>, если это ничьё снаряжение.</summary>
        public static void Ping(UxrGrabbableObject item)
        {
            if (item == null || !EquipmentStrip.IsEquipment(item) || !LooseItems.IsLoose(item)) return;

            LootXray xray = item.GetComponent<LootXray>();
            if (xray == null) xray = item.gameObject.AddComponent<LootXray>();
            xray._pingedAt = Time.time;
        }

        public bool IsShown => _shown;

        private void Awake()
        {
            _item = GetComponent<UxrGrabbableObject>();
        }

        private void LateUpdate()
        {
            bool wanted = Wanted(_item != null && LooseItems.IsLoose(_item), Time.time - _pingedAt);
            if (wanted != _shown) SetShown(wanted);
        }

        public void SetShown(bool shown)
        {
            if (shown && _copies.Count == 0) BuildCopies();
            foreach (GameObject copy in _copies)
                if (copy != null) copy.SetActive(shown);
            _shown = shown;
        }

        /// <summary>Копии мешей предмета с материалом силуэта — дочерние, повторяют форму и движение.</summary>
        private void BuildCopies()
        {
            if (_material == null) _material = Resources.Load<Material>(MaterialResource);
            if (_material == null) return;

            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                var source = filter.GetComponent<MeshRenderer>();
                if (source == null || !source.enabled || filter.sharedMesh == null) continue;

                var copy = new GameObject("LootXray") { layer = filter.gameObject.layer };
                copy.transform.SetParent(filter.transform, false);
                copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

                var renderer = copy.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                _copies.Add(copy);
            }
        }

        private void OnDestroy()
        {
            foreach (GameObject copy in _copies)
                if (copy != null) Destroy(copy);
        }
    }
}
