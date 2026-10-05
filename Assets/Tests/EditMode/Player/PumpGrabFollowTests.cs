using System.Reflection;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Помпа идёт за рукой сразу, даже если её взяли не ровно в точке хвата.
    ///
    /// <para>
    /// <b>Дефект.</b> Взял помпу <c>Shotgun_real</c> на несколько сантиметров впереди точки хвата —
    /// и первые сантиметры оттягивания она стоит: SDK ставит точку хвата помпы в руку, и помпа
    /// упирается в передний предел хода. Замер до правки: рука отошла на 3, 6 см — помпа 0; на 9 —
    /// 1 см. Рука при этом всегда прилипает к одной точке помпы — это не меняется.
    /// </para>
    ///
    /// <para>
    /// Руки ставятся абсолютно перед каждым кадром — как трекинг контроллеров в игре: после кадра
    /// SDK переставляет руку в точку хвата, и сдвигать её относительно — значит терять промах.
    /// </para>
    /// </summary>
    public class PumpGrabFollowTests
    {
        private const string Weapon = "Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab";
        private const string MefAvatarGuid = "b6fe59db941fa944696ece5e1aabc032";
        private const float MissedBy = 0.08f;

        [Test]
        public void Помпа_сразу_идёт_за_рукой_взятой_впереди_точки_хвата()
        {
            using var harness = new TwoHandGrabHarness(Weapon, AssetDatabase.GUIDToAssetPath(MefAvatarGuid), 0, grabMain: false);

            var pump = new SerializedObject(harness.Weapon.GetComponent<UxrShotgunPump>()).FindProperty("_pump").objectReferenceValue as UxrGrabbableObject;
            Assert.IsNotNull(pump, "Контроль: у дробовика есть помпа.");

            // Исходное место детали для ограничения хода SDK запоминает в Awake — в EditMode его нет.
            MethodInfo awake = typeof(UxrGrabbableObject).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            awake.Invoke(harness.Grabbable, null);
            awake.Invoke(pump, null);

            Transform right = harness.Right.transform;
            Vector3 rightPosition = right.position;
            Quaternion rightRotation = right.rotation;

            try
            {
                // Правая рука берёт рукоять, оружие встаёт в неё.
                TwoHandGrabHarness.Manager.GrabObject(harness.Right, harness.Grabbable, TwoHandGrabCases.MainPoint, false);
                Frame(harness, rightPosition, rightRotation, null, Vector3.zero, Quaternion.identity);
                Frame(harness, rightPosition, rightRotation, null, Vector3.zero, Quaternion.identity);

                // Левая берёт помпу на 8 см ближе к дулу, чем точка хвата.
                Vector3 back = harness.Weapon.transform.TransformDirection((pump.TranslationLimitsMin + pump.TranslationLimitsMax).normalized);
                pump.ComputeRequiredGrabberTransform(harness.Left, 0, out Vector3 snap, out Quaternion leftRotation, false);
                Vector3 leftAtGrab = snap - back * MissedBy;
                harness.Left.transform.SetPositionAndRotation(leftAtGrab, leftRotation);
                TwoHandGrabHarness.Manager.GrabObject(harness.Left, pump, 0, false);
                Frame(harness, rightPosition, rightRotation, harness.Left, leftAtGrab, leftRotation);

                float scale = harness.Weapon.transform.lossyScale.x;
                float travel = (pump.TranslationLimitsMin + pump.TranslationLimitsMax).magnitude * scale;

                foreach (float pull in new[] { 0.03f, 0.06f, 0.09f, 0.20f })
                {
                    Frame(harness, rightPosition, rightRotation, harness.Left, leftAtGrab + back * pull, leftRotation);
                    float moved = Vector3.Dot(pump.transform.localPosition, (pump.TranslationLimitsMin + pump.TranslationLimitsMax).normalized) * scale;
                    float expected = Mathf.Min(pull, travel);

                    Assert.That(moved, Is.EqualTo(expected).Within(0.004f),
                                $"Помпу взяли на {MissedBy * 100f:F0} см впереди точки хвата и оттянули на {pull * 100f:F0} см, " +
                                $"а она ушла на {moved * 100f:F1} см (ход {travel * 100f:F1} см).");
                }
            }
            finally
            {
                if (TwoHandGrabHarness.Manager.IsBeingGrabbed(pump)) TwoHandGrabHarness.Manager.ReleaseObject(null, pump, false);
            }
        }

        /// <summary>Кадр: руки на местах с трекинга, затем цикл манипуляции SDK.</summary>
        private static void Frame(TwoHandGrabHarness harness, Vector3 rightPosition, Quaternion rightRotation,
                                  UxrGrabber left, Vector3 leftPosition, Quaternion leftRotation)
        {
            harness.Right.transform.SetPositionAndRotation(rightPosition, rightRotation);
            if (left != null) left.transform.SetPositionAndRotation(leftPosition, leftRotation);
            harness.UpdateManipulation();
        }
    }
}
