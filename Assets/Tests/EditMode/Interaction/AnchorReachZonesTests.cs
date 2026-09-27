using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// Зоны досягаемости якорей — то, что рисуют гизмо и отладочный вид в шлеме.
    ///
    /// <para>
    /// Что доказывает. Отрисовка полезна, только пока совпадает с тем, что на самом деле
    /// проверяет UltimateXR. Поэтому зона укладки сверяется с решением самого SDK
    /// (<c>UxrGrabbableObject.CanBePlacedOnAnchor</c>) по обе стороны границы: разойдись
    /// расчёт с SDK хоть на миллиметр — один из двух случаев станет красным.
    /// </para>
    /// </summary>
    public class AnchorReachZonesTests : MirrorTestHarness
    {
        private const float Epsilon = 0.001f;

        private UxrGrabbableObjectAnchor CreateAnchor(float maxPlaceDistance)
        {
            UxrGrabbableObjectAnchor anchor = CreateObject("Anchor").AddComponent<UxrGrabbableObjectAnchor>();
            anchor.MaxPlaceDistance = maxPlaceDistance;
            return anchor;
        }

        private UxrGrabbableObject CreateItem(Vector3 position)
        {
            GameObject item = CreateObject("Item");
            item.transform.position = position;
            return item.AddComponent<UxrGrabbableObject>();
        }

        [Test]
        public void Граница_зоны_укладки_совпадает_с_решением_SDK()
        {
            UxrGrabbableObjectAnchor anchor = CreateAnchor(0.25f);
            anchor.transform.position = new Vector3(1f, 2f, 3f);

            UxrGrabbableObject inside  = CreateItem(anchor.transform.position + Vector3.right * (0.25f - Epsilon));
            UxrGrabbableObject outside = CreateItem(anchor.transform.position + Vector3.right * (0.25f + Epsilon));

            Assert.IsTrue(inside.CanBePlacedOnAnchor(anchor), "Контроль: SDK обязан принять предмет внутри радиуса.");
            Assert.IsFalse(outside.CanBePlacedOnAnchor(anchor), "Контроль: SDK обязан отвергнуть предмет снаружи радиуса.");

            Assert.IsTrue(AnchorReachZones.IsInPlaceZone(anchor, inside),
                "Предмет, который SDK положит в якорь, отрисовка показывает вне зоны.");
            Assert.IsFalse(AnchorReachZones.IsInPlaceZone(anchor, outside),
                "Предмет, который SDK в якорь не положит, отрисовка показывает внутри зоны.");

            ReachZone zone = AnchorReachZones.GetPlaceZone(anchor);
            Assert.AreEqual(ReachZoneShape.Sphere, zone.Shape);
            Assert.AreEqual(0.25f, zone.Radius, 1e-6f);
        }

        [Test]
        public void Центр_зоны_укладки_берётся_из_DropProximityTransform_якоря()
        {
            UxrGrabbableObjectAnchor anchor = CreateAnchor(0.1f);
            GameObject dropPoint = CreateObject("DropPoint");
            dropPoint.transform.SetParent(anchor.transform, false);
            dropPoint.transform.localPosition = new Vector3(0f, 0.3f, 0f);

            SetPrivateField(anchor, "_dropProximityTransformUseSelf", false);
            SetPrivateField(anchor, "_dropProximityTransform", dropPoint.transform);

            // Предмет у самой точки якоря, но далеко от точки укладки: SDK его не примет.
            UxrGrabbableObject item = CreateItem(anchor.transform.position);
            Assert.IsFalse(item.CanBePlacedOnAnchor(anchor), "Контроль: SDK меряет от DropProximityTransform, а не от якоря.");

            Assert.AreEqual(dropPoint.transform.position, AnchorReachZones.GetPlaceZone(anchor).Center,
                "Центр зоны обязан совпадать с точкой, от которой меряет SDK.");
            Assert.IsFalse(AnchorReachZones.IsInPlaceZone(anchor, item));
        }

        /// <summary>
        /// Якорь внутри предмета (гнездо магазина оружия) — не карман аватара, даже когда предмет
        /// лежит в кармане и потому висит в иерархии аватара. Иначе вид в шлеме рисовал зону
        /// гнезда как зону аватара, и она уезжала вместе с оружием, взятым со спины.
        /// </summary>
        [Test]
        public void Якорь_внутри_предмета_не_карман_аватара()
        {
            GameObject avatar = CreateObject("Avatar");
            avatar.AddComponent<UltimateXR.Avatar.UxrAvatar>();

            GameObject pocket = CreateObject("Anchor_Back");
            pocket.transform.SetParent(avatar.transform);
            UxrGrabbableObjectAnchor backAnchor = pocket.AddComponent<UxrGrabbableObjectAnchor>();

            // Оружие лежит в кармане: UltimateXR подвешивает его к якорю, внутрь аватара.
            UxrGrabbableObject weapon = CreateItem(Vector3.zero);
            weapon.transform.SetParent(pocket.transform);
            GameObject magWell = CreateObject("MagAnchor");
            magWell.transform.SetParent(weapon.transform);
            UxrGrabbableObjectAnchor magAnchor = magWell.AddComponent<UxrGrabbableObjectAnchor>();

            Assert.IsTrue(AnchorRole.IsAvatarPocket(backAnchor), "Контроль: карман на аватаре — карман.");
            Assert.AreEqual(AnchorRoleKind.Primary, AnchorRole.Get(backAnchor));

            Assert.IsFalse(AnchorRole.IsAvatarPocket(magAnchor),
                "Гнездо магазина оружия, лежащего в кармане, посчитано карманом аватара.");
            Assert.AreEqual(AnchorRoleKind.World, AnchorRole.Get(magAnchor));
        }

        [Test]
        public void Зона_хвата_по_расстоянию_это_сфера_MaxDistanceGrab()
        {
            UxrGrabbableObject proxy = CreateItem(new Vector3(0f, 1f, 0f));
            proxy.GetGrabPoint(0).GrabProximityMode = UxrGrabProximityMode.UseProximity;
            proxy.GetGrabPoint(0).MaxDistanceGrab   = 0.15f;

            var zones = AnchorReachZones.GetGrabZones(proxy, null, UxrHandSide.Right);

            Assert.AreEqual(1, zones.Count, "Одна точка хвата — одна зона.");
            Assert.AreEqual(ReachZoneShape.Sphere, zones[0].Shape);
            Assert.AreEqual(0.15f, zones[0].Radius, 1e-6f);
            Assert.IsTrue(AnchorReachZones.Contains(zones[0], proxy.transform.position + Vector3.up * (0.15f - Epsilon)));
            Assert.IsFalse(AnchorReachZones.Contains(zones[0], proxy.transform.position + Vector3.up * (0.15f + Epsilon)));
        }

        [Test]
        public void Зона_хвата_в_режиме_коробки_это_сам_BoxCollider()
        {
            UxrGrabbableObject proxy = CreateItem(Vector3.zero);
            BoxCollider box = proxy.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.1f, 0f);
            box.size   = new Vector3(0.2f, 0.1f, 0.4f);

            proxy.GetGrabPoint(0).GrabProximityMode = UxrGrabProximityMode.BoxConstrained;
            proxy.GetGrabPoint(0).GrabProximityBox  = box;

            var zones = AnchorReachZones.GetGrabZones(proxy, null, UxrHandSide.Left);

            Assert.AreEqual(ReachZoneShape.Box, zones[0].Shape);
            Assert.AreSame(box, zones[0].Box);
            Assert.IsTrue(AnchorReachZones.Contains(zones[0], new Vector3(0.09f, 0.1f, 0.19f)), "Точка у угла внутри коробки.");
            Assert.IsFalse(AnchorReachZones.Contains(zones[0], new Vector3(0.11f, 0.1f, 0f)), "Точка за гранью X снаружи.");
            Assert.IsFalse(AnchorReachZones.Contains(zones[0], new Vector3(0f, 0.1f, 0f) + Vector3.up * 0.06f), "Точка за гранью Y снаружи.");
        }
    }
}
