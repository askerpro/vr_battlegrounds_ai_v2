#if UNITY_EDITOR
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Зоны досягаемости карманов своего аватара — прямо в шлеме, чтобы подбирать размеры,
    /// держа предмет в руке. Сплошная сфера — где предмет встанет в карман, вложенная
    /// сфера/коробка светлее — где рука возьмёт из кармана через прокси. Зона вспыхивает,
    /// когда условие выполнено: предмет в руке достаёт до кармана, ладонь достаёт до прокси.
    ///
    /// <para>
    /// Границы считает <see cref="AnchorReachZones" /> — то же, что проверяет UltimateXR, поэтому
    /// вспышка совпадает с моментом, когда игра реально примет или отдаст предмет (без учёта
    /// совместимости тегов у прокси: пустой карман не светится как «взять», но граница видна).
    /// </para>
    ///
    /// <para>
    /// Только редактор: Play Mode через Quest Link. Подобранные значения переносит в префаб
    /// <c>Tools/VR Battlegrounds/Avatars/Save Pocket Zones To Prefab</c>. Рисуется
    /// <see cref="Graphics.RenderMesh" /> без объектов в сцене — попадает во все камеры,
    /// включая оба глаза XR. Включается <c>Tools/VR Battlegrounds/Debug/Anchor Zones In Headset</c>.
    /// </para>
    /// </summary>
    public sealed class AnchorZonesDebugView : MonoBehaviour
    {
        private const float IdleAlpha   = 0.12f;
        private const float ActiveAlpha = 0.45f;
        private const float RefreshSeconds = 1f;

        private static AnchorZonesDebugView s_instance;
        private static bool s_enabled;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly List<UxrGrabbableObjectAnchor> _anchors = new List<UxrGrabbableObjectAnchor>();
        private readonly List<ReleasedItem> _released = new List<ReleasedItem>();
        private UxrAvatar _anchorsOwner;
        private float _nextRefresh;

        private Mesh _sphere;
        private Mesh _cube;
        private Material _material;
        private MaterialPropertyBlock _block;

        /// <summary>Показывать ли зоны. Включение создаёт скрытый объект-рисовальщик.</summary>
        public static bool Enabled
        {
            get => s_enabled;
            set
            {
                if (s_enabled != value)
                {
                    s_enabled = value;
                    GameLog.Debug.Info($"[AnchorZonesDebugView] Зоны карманов в шлеме: {(value ? "включены" : "выключены")}");
                }

                // Отдельно от смены значения: без перезагрузки домена флаг переживает вход
                // в Play Mode, а рисовальщик — нет.
                if (value && Application.isPlaying && s_instance == null)
                {
                    var host = new GameObject("[AnchorZonesDebugView]") { hideFlags = HideFlags.DontSave };
                    DontDestroyOnLoad(host);
                    s_instance = host.AddComponent<AnchorZonesDebugView>();
                }
            }
        }

        private void Awake()
        {
            _sphere = TakePrimitiveMesh(PrimitiveType.Sphere);
            _cube   = TakePrimitiveMesh(PrimitiveType.Cube);
            _block  = new MaterialPropertyBlock();

            // URP Unlit, прозрачный, двусторонний — сферу видно и изнутри, когда рука в зоне.
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "AnchorZonesDebug" };
            _material.SetFloat("_Surface", 1f);
            _material.SetFloat("_Blend", 0f);
            _material.SetFloat("_Cull", (float)CullMode.Off);
            _material.SetFloat("_ZWrite", 0f);
            _material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            _material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.renderQueue = (int)RenderQueue.Transparent;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (s_instance == this) s_instance = null;
        }

        private void OnEnable()
        {
            UxrGrabManager.Instance.ObjectReleased += GrabManager_ObjectReleased;
        }

        private void OnDisable()
        {
            if (UxrGrabManager.HasInstance) UxrGrabManager.Instance.ObjectReleased -= GrabManager_ObjectReleased;
        }

        // Отпускание запоминается, а разбирается в конце кадра: при удачной укладке SDK тоже
        // шлёт «отпущено» и кладёт предмет следом (PlaceObject), до LateUpdate.
        private void GrabManager_ObjectReleased(object sender, UxrManipulationEventArgs e)
        {
            if (!s_enabled || e.GrabbableObject == null || e.IsMultiHands) return; // вторая рука ещё держит
            _released.Add(new ReleasedItem { Item = e.GrabbableObject, DropPosition = e.GrabbableObject.DropProximityTransform.position });
        }

        private void LateUpdate()
        {
            if (!s_enabled) return;

            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            if (avatar == null) return;

            RefreshAnchors(avatar);
            ExplainMissedPlacements();

            foreach (UxrGrabbableObjectAnchor anchor in _anchors)
            {
                if (anchor == null || !AnchorRole.IsAvatarPocket(anchor)) continue;
                Color color = AnchorRole.GetColor(AnchorRole.Get(anchor));

                ReachZone place = AnchorReachZones.GetPlaceZone(anchor);
                Draw(place, color, IsHeldItemInPlaceZone(avatar, anchor));

                UxrGrabbableObject proxy = anchor.GrabProxy;
                if (proxy == null) continue;

                Color grabColor = Color.Lerp(color, Color.white, 0.5f);
                foreach (ReachZone zone in AnchorReachZones.GetGrabZones(proxy, avatar, UxrHandSide.Right))
                {
                    Draw(zone, grabColor, IsAnyPalmInZone(avatar, zone));
                }
            }
        }

        /// <summary>
        /// Предмет отпущен рядом с карманом (до трёх радиусов укладки), но в карман не встал —
        /// пишет в лог, почему: не хватило расстояния, карман занят, тег не подходит. В шлеме
        /// не видно, ошибся ли игрок на 5 см или карман вообще не принимает предмет.
        /// </summary>
        private void ExplainMissedPlacements()
        {
            if (_released.Count == 0) return;

            foreach (ReleasedItem released in _released)
            {
                if (released.Item == null) continue;

                if (released.Item.CurrentAnchor != null)
                {
                    GameLog.Debug.Verbose($"[AnchorZonesDebugView] '{released.Item.name}' встал в '{released.Item.CurrentAnchor.name}'.");
                    continue;
                }

                foreach (UxrGrabbableObjectAnchor anchor in _anchors)
                {
                    if (anchor == null) continue;

                    float distance = Vector3.Distance(released.DropPosition, anchor.DropProximityTransform.position);
                    if (distance > anchor.MaxPlaceDistance * 3f) continue;

                    string reason;
                    if (!anchor.IsCompatibleObject(released.Item))
                        reason = $"тег '{released.Item.Tag}' не входит в Compatible Tags кармана";
                    else if (anchor.CurrentPlacedObject != null && !anchor.AllowSwap)
                        reason = $"карман занят ('{anchor.CurrentPlacedObject.name}')";
                    else if (distance > anchor.MaxPlaceDistance)
                        reason = $"далеко: {distance:0.00} м при радиусе укладки {anchor.MaxPlaceDistance:0.00} м (не хватило {distance - anchor.MaxPlaceDistance:0.00} м)";
                    else
                        reason = "по расстоянию и тегу подходит — отказ в другом условии SDK (например, предмет с зависимостью от родителя)";

                    GameLog.Debug.Warning($"[AnchorZonesDebugView] '{released.Item.name}' отпущен у '{anchor.name}', но не встал: {reason}.");
                }
            }

            _released.Clear();
        }

        private struct ReleasedItem
        {
            public UxrGrabbableObject Item;
            public Vector3            DropPosition;
        }

        // Карманы берутся с аватара раз в секунду и при его смене: аватар пересоздаётся при
        // смене скина и команды, а искать компоненты каждый кадр незачем.
        private void RefreshAnchors(UxrAvatar avatar)
        {
            if (avatar == _anchorsOwner && Time.unscaledTime < _nextRefresh) return;

            _anchorsOwner = avatar;
            _nextRefresh  = Time.unscaledTime + RefreshSeconds;
            _anchors.Clear();
            avatar.GetComponentsInChildren(true, _anchors);

            // Только карманы: якоря предметов, лежащих в кармане (гнездо магазина оружия на
            // спине), тоже в иерархии аватара — без фильтра их зона уезжала с оружием в руке.
            _anchors.RemoveAll(anchor => !AnchorRole.IsAvatarPocket(anchor));
        }

        private static bool IsHeldItemInPlaceZone(UxrAvatar avatar, UxrGrabbableObjectAnchor anchor)
        {
            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                UxrGrabber grabber = avatar.GetGrabber(side);
                UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;

                if (held != null && anchor.IsCompatibleObject(held) && AnchorReachZones.IsInPlaceZone(anchor, held))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAnyPalmInZone(UxrAvatar avatar, ReachZone zone)
        {
            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                UxrGrabber grabber = avatar.GetGrabber(side);
                if (grabber != null && AnchorReachZones.Contains(zone, grabber.GetProximityTransform().position))
                {
                    return true;
                }
            }

            return false;
        }

        private void Draw(ReachZone zone, Color color, bool active)
        {
            Matrix4x4 matrix;
            Mesh      mesh;

            if (zone.Shape == ReachZoneShape.Box)
            {
                Transform t = zone.Box.transform;
                matrix = t.localToWorldMatrix * Matrix4x4.TRS(zone.Box.center, Quaternion.identity, zone.Box.size);
                mesh   = _cube;
            }
            else
            {
                matrix = Matrix4x4.TRS(zone.Center, Quaternion.identity, Vector3.one * (zone.Radius * 2f));
                mesh   = _sphere;
            }

            _block.SetColor(BaseColorId, new Color(color.r, color.g, color.b, active ? ActiveAlpha : IdleAlpha));
            Graphics.DrawMesh(mesh, matrix, _material, 0, null, 0, _block, ShadowCastingMode.Off, false);
        }

        private static Mesh TakePrimitiveMesh(PrimitiveType type)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Destroy(primitive);
            return mesh;
        }
    }
}
#endif
