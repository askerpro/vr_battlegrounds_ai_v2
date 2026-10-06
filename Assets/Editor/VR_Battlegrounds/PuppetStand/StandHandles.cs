using UnityEngine;
using VrBattlegrounds.DevTools.LegsCompare;

namespace VrBattlegrounds.Editor.PuppetStand
{
    /// <summary>
    /// Манипулятор стендов подбора клипов, как у стенда-кукловода: тело на полу (место и поворот ряда) и голова — его
    /// ребёнок на <paramref name="headHeight"/>; высоту головы стенд читает как высоту камеры игрока.
    /// </summary>
    public static class StandHandles
    {
        public static void Create(Transform parent, float headHeight, string headName, out Transform body, out Transform head)
        {
            body = new GameObject("PuppetBody (тело: двигай и крути)").transform;
            body.SetParent(parent, false);
            Primitive(PrimitiveType.Cylinder, "Gizmo", body, Vector3.zero, new Vector3(0.35f, 0.01f, 0.35f));
            Primitive(PrimitiveType.Cube, "Forward", body, new Vector3(0f, 0.01f, 0.25f), new Vector3(0.05f, 0.02f, 0.2f));
            head = new GameObject(headName).transform;
            head.SetParent(body, false);
            head.localPosition = new Vector3(0f, headHeight, 0f);
            Primitive(PrimitiveType.Cube, "Gizmo", head, Vector3.zero, new Vector3(0.12f, 0.08f, 0.16f));
        }

        /// <summary>
        /// Ручки контроллеров (дети тела), как у стенда-кукловода: поза кистей <paramref name="pose"/> от глаз
        /// <paramref name="head"/> в осях тела (<see cref="PuppetHands"/>, универсальные оси кисти UltimateXR).
        /// </summary>
        public static void CreateHands(Transform body, Transform head, PuppetHandPose pose, out Transform left, out Transform right)
        {
            left = Hand("PuppetLeftHand", body, head, PuppetHands.Get(pose, true));
            right = Hand("PuppetRightHand", body, head, PuppetHands.Get(pose, false));
        }

        private static Transform Hand(string name, Transform body, Transform head, Pose fromEyes)
        {
            var handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = name;
            Object.DestroyImmediate(handle.GetComponent<Collider>());
            handle.transform.SetParent(body, false);
            handle.transform.localPosition = head.localPosition + fromEyes.position;
            handle.transform.localRotation = fromEyes.rotation;
            handle.transform.localScale = Vector3.one * 0.07f;
            return handle.transform;
        }

        private static void Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
        }
    }
}
