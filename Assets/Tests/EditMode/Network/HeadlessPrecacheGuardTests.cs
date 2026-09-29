using NUnit.Framework;
using UnityEngine.Rendering;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Прогрев UltimateXR на выделенном сервере размножал свои копии с удвоением на каждом спавне
    /// аватара и вешал сервер (<see cref="HeadlessPrecacheGuard"/>). Без графики — выключен, с графикой
    /// (хост, шлем, редактор) — как в SDK.
    /// </summary>
    public class HeadlessPrecacheGuardTests
    {
        [TestCase(true,  GraphicsDeviceType.Null,       ExpectedResult = false, TestName = "Выделенный_сервер_без_прогрева")]
        [TestCase(true,  GraphicsDeviceType.Vulkan,     ExpectedResult = false, TestName = "Batchmode_с_устройством_без_прогрева")]
        [TestCase(false, GraphicsDeviceType.Null,       ExpectedResult = false, TestName = "Без_устройства_рендера_без_прогрева")]
        [TestCase(false, GraphicsDeviceType.Vulkan,     ExpectedResult = true,  TestName = "Шлем_с_прогревом")]
        [TestCase(false, GraphicsDeviceType.Direct3D11, ExpectedResult = true,  TestName = "Хост_на_ПК_с_прогревом")]
        public bool Прогрев_только_на_машине_с_графикой(bool batchMode, GraphicsDeviceType graphics)
        {
            return HeadlessPrecacheGuard.ShouldPrecache(batchMode, graphics);
        }

        /// <summary>
        /// Без прогрева сервер обязан сам зарегистрировать выключенные компоненты с id — их регистрировал
        /// обход сцены прогревом (в том числе на неактивных объектах, где Awake не вызывался).
        /// Включённые не трогаются — их регистрирует их собственный Awake.
        /// </summary>
        [Test]
        public void Выключенные_компоненты_с_id_на_неактивных_объектах_регистрируются()
        {
            UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();

            try
            {
                var inactive = new UnityEngine.GameObject("Inactive");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(inactive, scene);
                var disabled = inactive.AddComponent<UltimateXR.Manipulation.UxrGrabbableObjectAnchor>();
                disabled.enabled = false;
                inactive.SetActive(false);

                var active = new UnityEngine.GameObject("Active");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(active, scene);
                active.AddComponent<UltimateXR.Manipulation.UxrGrabbableObjectAnchor>();

                Assert.That(HeadlessPrecacheGuard.RegisterDisabledUniqueIds(scene), Is.EqualTo(1),
                            "Регистрируется ровно выключенный компонент, как в UxrManager.AddScenePrecachedInstances");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
