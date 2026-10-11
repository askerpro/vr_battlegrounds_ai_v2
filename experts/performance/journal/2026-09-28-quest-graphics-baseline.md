# Quest получил свой уровень качества и URP-ассет вместо сэмплового URP-Balanced

Коммит `1375ca711e69c1de94b876bbc3f60358cec04413` (в origin/dev). Уровень качества Quest — по умолчанию
для Android; `Assets/URPDefaultResources/Quest.asset`: Render Scale 1.0, тени от основного света, MSAA 4x.
Раньше Android брал сэмпловый URP-Balanced (Render Scale 0.75, без теней). В сообщении коммита — 72/72 FPS
на Quest 3; сцена, роль и число игроков этого замера в источнике не указаны — unknown.

Состояние на `716743336` (чтение ассетов): Quest.asset — HDR выкл., depth/opaque texture выкл., SRP Batcher
вкл., dynamic batching выкл., тень 1024, 1 каскад, дистанция 20 м, доп. свет per-pixel ≤ 2, мягкие тени выкл.
`Default_Forward_Renderer`: Intermediate Texture = Always (влияет на FFR, см. `Docs/perf-stress-test.md`
«Дальше»). `ProjectSettings/GraphicsSettings.asset` по-прежнему ссылается на сэмпловый
`Assets/ThirdParty/UltimateXR/Samples/FullScene/Settings/URP/URP-Balanced.asset` как default pipeline;
оба уровня качества задают свой ассет — влияние default не проверялось (unknown).

Источники: `git show 1375ca711`; `ProjectSettings/QualitySettings.asset`; `Assets/URPDefaultResources/`.
