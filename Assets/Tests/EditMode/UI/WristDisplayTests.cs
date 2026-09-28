using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.UI.HUD;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Табло на часах: цвет кольца ХП, формат времени и проводка префаба.
    /// Префаб кладут на часы аватара руками — битая ссылка в нём молча даст пустое табло.
    /// </summary>
    public class WristDisplayTests
    {
        private const string PrefabPath = "Assets/Prefabs/UI/HUD/WristDisplay.prefab";
        private const string OvalPrefabPath = "Assets/Prefabs/UI/HUD/WristDisplay_Oval.prefab";
        private const string WatchHudPath = "Assets/Prefabs/Player/WristWatch_HUD.prefab";

        /// <summary>
        /// Табло на часах MEF лежит на экране: смотрит наружу, как экран, и отстоит от него
        /// не больше чем на 2 мм. Уехавшее табло висит в воздухе или прячется в корпусе.
        /// </summary>
        [Test]
        public void WatchHud_DisplaySitsOnScreen()
        {
            var watch = AssetDatabase.LoadAssetAtPath<GameObject>(WatchHudPath);
            Assert.IsNotNull(watch, $"нет префаба {WatchHudPath}");

            var display = watch.GetComponentInChildren<WristDisplay>(true);
            Assert.IsNotNull(display, "в часах нет WristDisplay");

            var mesh = watch.GetComponent<MeshFilter>().sharedMesh;
            Transform root = watch.transform;
            Vector3 center = root.InverseTransformPoint(display.transform.position);
            // Canvas виден со стороны −forward: наружу смотрит −forward.
            Vector3 outward = root.InverseTransformDirection(-display.transform.forward).normalized;

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            float best = float.MaxValue, gap = float.NaN;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (Vector3.Dot(normals[i], outward) < 0.95f) continue;
                Vector3 offset = center - vertices[i];
                float along = Vector3.Dot(offset, outward);
                float across = (offset - outward * along).sqrMagnitude;
                if (across < best) { best = across; gap = along; }
            }

            Assert.IsFalse(float.IsNaN(gap), "под табло нет грани, смотрящей туда же, — табло повёрнуто не к экрану");
            Assert.That(Mathf.Sqrt(best), Is.LessThan(0.02f), "под табло нет экрана — табло съехало вбок");
            Assert.That(gap, Is.InRange(0f, 0.002f), $"табло в {gap * 1000f:F1} мм от экрана, нужно 0..2 мм");
        }

        [Test]
        public void HealthColor_FullIsGreen_EmptyIsRed_HalfIsYellow()
        {
            Color.RGBToHSV(WristDisplayFace.HealthColor(1f), out float hFull, out _, out _);
            Color.RGBToHSV(WristDisplayFace.HealthColor(0f), out float hEmpty, out _, out _);
            Color.RGBToHSV(WristDisplayFace.HealthColor(0.5f), out float hHalf, out _, out _);

            Assert.AreEqual(1f / 3f, hFull, 0.01f, "полное ХП — зелёный");
            Assert.AreEqual(0f, hEmpty, 0.01f, "пустое ХП — красный");
            Assert.AreEqual(1f / 6f, hHalf, 0.01f, "половина — жёлтый, а не грязный RGB-микс");
        }

        [Test]
        public void HealthFraction_ClampsAndHandlesZeroMax()
        {
            Assert.AreEqual(0.5f, WristDisplayFace.HealthFraction(50f, 100f), 1e-4f);
            Assert.AreEqual(1f, WristDisplayFace.HealthFraction(150f, 100f), 1e-4f);
            Assert.AreEqual(0f, WristDisplayFace.HealthFraction(-5f, 100f), 1e-4f);
            Assert.AreEqual(0f, WristDisplayFace.HealthFraction(50f, 0f), 1e-4f);
        }

        [Test]
        public void FormatTime_MinutesSeconds_RoundsUp()
        {
            Assert.AreEqual("02:05", WristDisplayFace.FormatTime(125f));
            Assert.AreEqual("00:01", WristDisplayFace.FormatTime(0.2f), "последняя доля секунды — ещё 00:01, а не 00:00");
            Assert.AreEqual("00:00", WristDisplayFace.FormatTime(0f));
            Assert.AreEqual("00:00", WristDisplayFace.FormatTime(-3f));
            Assert.AreEqual("--:--", WristDisplayFace.FormatTime(float.NaN));
        }

        [TestCase(PrefabPath)]
        [TestCase(OvalPrefabPath)]
        public void Prefab_IsWiredAndWorldSpace(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"нет префаба {path}");

            var canvas = prefab.GetComponent<Canvas>();
            Assert.IsNotNull(canvas, "корень — Canvas");
            Assert.AreEqual(RenderMode.WorldSpace, canvas.renderMode, "табло живёт в мире, на часах");

            var display = prefab.GetComponent<WristDisplay>();
            Assert.IsNotNull(display, "на корне WristDisplay");

            var so = new SerializedObject(display);
            var ring = so.FindProperty("_healthRing").objectReferenceValue as Image;
            Assert.IsNotNull(ring, "_healthRing не назначен");
            Assert.AreEqual(Image.Type.Filled, ring.type);
            Assert.AreEqual(Image.FillMethod.Radial360, ring.fillMethod, "ХП — кольцо по периметру");
            Assert.IsNotNull(ring.sprite, "у кольца нет спрайта");

            Assert.IsNotNull(so.FindProperty("_timeText").objectReferenceValue as TMP_Text, "_timeText не назначен");
            Assert.IsNotNull(so.FindProperty("_content").objectReferenceValue, "_content не назначен");

            foreach (var g in prefab.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(g.raycastTarget, $"{g.name}: табло не должно ловить лучи указателя UltimateXR");
        }
    }
}
