# range-photo-headless: фото попаданий тира только на машине с графикой, 2026-10-22

Снаряд UltimateXR симулирует каждая машина, поэтому `ShootingTarget.ProjectileContact` приходит и на выделенном
сервере (`-nographics`, NullGfxDevice). `ShootingRangePhotoCapture` снимал фото и там: `SubmitRenderRequest`
создавал URP-пайплайн и падал на `RenderTexture.Create` (Quest e2e lobby-gpu-tour — RED по логам сервера).
Теперь `ShootingRangePhotoCapture.CanRender` (`GraphicsDeviceType != Null`): без графики захват не подписывается
на контакты, студия/камера/RT не создаются, в лог одна строка Info. Хост и клиенты — без изменений.
Наблюдатели контакта мишени, которые рендерят, проверяют наличие графического устройства. Тест —
`Assets/Tests/EditMode/Maps/ShootingRangePhotoCaptureTests.cs`. Пользователь проверил фото в шлеме.
