using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>Редактируемый объём ниши. Сборщик ограды оставляет здесь свободное место; сам объём не преграда.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class SpawnZoneBoundaryOpening : MonoBehaviour
    {
        public Bounds WorldBounds
        {
            get
            {
                var box = GetComponent<BoxCollider>();
                var result = new Bounds(transform.TransformPoint(box.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    result.Encapsulate(transform.TransformPoint(box.center + Vector3.Scale(box.size * .5f,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                return result;
            }
        }

        private void OnValidate() => GetComponent<BoxCollider>().isTrigger = true;
        private void OnDrawGizmosSelected()
        {
            var box = GetComponent<BoxCollider>();
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(.3f, .8f, 1f, .6f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
