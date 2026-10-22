using NUnit.Framework;
using UnityEngine.Rendering;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Фото попадания на стрельбище (<see cref="ShootingRangePhotoCapture"/>). Снаряд симулирует каждая
    /// машина, поэтому контакт с мишенью приходит и на выделенном сервере; без графического устройства
    /// URP не создаёт RenderTexture (Quest e2e 2026-10-22: 2× «RenderTexture.Create failed» в логе сервера).
    /// Без устройства захват выключен, с устройством (шлем, хост, редактор) — работает.
    /// </summary>
    public class ShootingRangePhotoCaptureTests
    {
        [TestCase(GraphicsDeviceType.Null,       ExpectedResult = false, TestName = "Выделенный_сервер_без_фото")]
        [TestCase(GraphicsDeviceType.Vulkan,     ExpectedResult = true,  TestName = "Шлем_с_фото")]
        [TestCase(GraphicsDeviceType.OpenGLES3,  ExpectedResult = true,  TestName = "Шлем_GLES_с_фото")]
        [TestCase(GraphicsDeviceType.Direct3D11, ExpectedResult = true,  TestName = "Хост_на_ПК_с_фото")]
        public bool Фото_только_на_машине_с_графикой(GraphicsDeviceType device)
        {
            return ShootingRangePhotoCapture.CanRenderOn(device);
        }
    }
}
