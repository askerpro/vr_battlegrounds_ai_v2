# Контейнерный терминал

Подробный анализ задачи; статус и план — в [Readme.md](Readme.md).
Тема задана пользователем 2026-10-06. Планировка, размер и число игроков не выбраны.
Сцены и блокаута нет. Разделы: [подбор ассетов](#подбор-ассетов),
[звуковая атмосфера](#звуковая-атмосфера).

| Цель | Мы здесь | Осталось выполнить | Технические подробности |
|---|---|---|---|
| Арена среди штабелей контейнеров работающего порта в ясный день | Тема и время суток заданы; подобраны референсы, ассеты и звуковые события | Сделать блокаут по размерам ISO-контейнеров и проверить ходьбой в шлеме; выбрать стиль ассетов (свой/JakeBradley или пак ITHappy) | `Docs/level-design-workflow.md`, `level-design-principles.md`, `map-evaluation.md` |

## Замысел

Бой идёт на площадке контейнерного терминала среди штабелей, ясным солнечным днём.
Порт продолжает работать: за периметром у причала стоит контейнеровоз, над ним
ходят причальные краны, по проездам проезжают ричстакеры и тягачи, кричат чайки.
Игрок видит небольшой кусок огромного терминала.

Главные признаки темы:

- Штабели контейнеров разных цветов — основные укрытия и стены лабиринта.
- Силуэты причальных кранов (STS) и козловых RTG над штабелями и у воды.
- Контейнеровоз у причала и открытое море за периметром.
- Жёлтая разметка проездов, номера рядов, бетонные блоки, кнехты на кромке причала.
- Жёсткое солнце, короткие контрастные тени, яркое небо.
- Живой порт создаётся звуковыми событиями, а не множеством анимаций.

## Игровая арена и укрытия

- **Два уровня укрытия.** Полное — контейнер (2,59 м, high-cube 2,90 м). По пояс —
  блок «нью-джерси» (≈ 0,81 м).
- **Размеры ISO для блокаута.** 20 футов — 6,06 × 2,44 × 2,59 м, 40 футов —
  12,19 × 2,44 × 2,59 м. Для физической ходьбы удобна сетка 6 м.
- **Главный геймплейный ориентир — CoD «Shipment».** Квадрат, внутри четыре блока
  контейнеров, крест проходов, по периметру открытые и полуоткрытые контейнеры.
  Масштаб близок к компактной физической арене.
- **Открытые контейнеры** («сквозные», с распахнутыми створками) — проходы и
  укрытия с двумя выходами. Наклонные контейнеры у стен дают разнообразие силуэта.
- **Вне арены:** причал, судно, STS-краны, RTG над соседними блоками, мачты освещения.
  Ни один из этих объектов не нужен на уровне игрока.
- **Динамический элемент (идея из BO2 «Cargo»), не утверждён.** Контейнер на спредере
  крана медленно опускается или поднимается за пределами досягаемости. Влияние на
  линии обзора требует отдельного решения по механике.

## Референсы

Проверка 2026-10-06. Это визуальные источники, а не ассеты проекта.
Fandom-вики отвечают 402/403 на автоматические запросы, поэтому сведения о картах
взяты из выдачи поиска и Википедии.

### Реальные терминалы

| Источник | Что брать для карты |
|---|---|
| [Гамбург, Eurogate и Burchardkai, аэрофото](https://commons.wikimedia.org/wiki/File:Aerial_image_of_the_Eurogate_and_Burchardkai_container_terminals_(view_from_the_southeast).jpg) | Композиция: причал → ряд STS → блоки штабелей с проездами → мачты. Палитра контейнеров «в массе» |
| [Терминал Altenwerder (CTA)](https://commons.wikimedia.org/wiki/Category:Container_Terminal_Altenwerder) | STS вблизи, AGV, аэрофото, разметка под краном |
| [Straddle carriers](https://commons.wikimedia.org/wiki/Category:Straddle_carriers) | П-образная рама: «арка» на границе арены |
| [Reach stackers](https://commons.wikimedia.org/wiki/Category:Reach_stackers) | Машина с контейнером на стреле: укрытие с просветом снизу |
| [RTG](https://commons.wikimedia.org/wiki/File:Rubber_tyred_gantry_crane_(RTG)_pic-2.JPG), [Gantry cranes](https://commons.wikimedia.org/wiki/Category:Gantry_cranes) | Силуэт над блоком штабеля, жёлтые полосы RTG на асфальте |
| [Container cranes](https://commons.wikimedia.org/wiki/Category:Container_cranes) | Окраска STS (красно-белая, синяя), спредеры |
| [APM Terminals Rotterdam](https://commons.wikimedia.org/wiki/Category:APM_Terminals_(Rotterdam)) | Стенка причала: кнехты и отбойники, суда у причала |

### Игровые карты

| Источник | Что брать для карты |
|---|---|
| [CoD «Shipment»](https://en.wikipedia.org/wiki/Shipment_(Call_of_Duty)), [Activision Map Intel](https://blog.activision.com/call-of-duty/2019-12/Modern-Warfare-Tactical-Map-Intel-Shipment) | Основной ориентир планировки для компактной арены; есть ремейк для Contractors VR |
| [BF3 «Noshahr Canals»](https://battlefield.fandom.com/wiki/Noshahr_Canals), [MP1st](https://mp1st.com/features/battlefield-best-tdm-map-and-how-it-was-made) | Контейнерный лабиринт с боковыми проходами, кран у воды; солнечный день — референс по свету |
| [CoD BO2 «Cargo»](https://callofduty.fandom.com/wiki/Cargo_(Black_Ops_II)) | Подвижные контейнеры, перекрывающие обзор |
| [CS:GO «Shipped»](https://counterstrike.fandom.com/wiki/Shipped), [Pavlov «Container Yard»](https://pavlovwiki.com/index.php/Default_Maps) | Вариант «бой на палубе»; эталон масштаба контейнерной площадки в VR (только PC) |

## Предложения для дальнейшего проектирования

- Начать с блокаута из кубов ISO-размеров по схеме Shipment и проверить его ходьбой
  в шлеме. Ассеты подбирать под принятую планировку.
- Свет: одно направленное солнце, запечённое освещение, ambient от неба.
  Контейнеры двух-трёх насыщенных цветов на одном атласе (без MaterialPropertyBlock,
  он ломает SRP Batcher).
- Задник (краны, судно, мачты) статичный, без отбрасываемых теней и коллайдеров,
  объединён в 1–2 меша. Подвижные краны — Animator, не Static и не occluder.
- Вода только за периметром: дешёвая opaque-плоскость с прокруткой нормалей,
  без глубины и отражений.
- Звук: тихий фон и редкие события (см. [звуковую атмосферу](#звуковая-атмосфера)).
- Реальные логотипы судоходных компаний на текстурах не использовать.

## Подбор ассетов

Цель — компактная арена среди штабелей контейнеров работающего порта ясным днём.
Подробности темы — в [замысле карты](#замысел). Проверка 2026-10-06.
Ассеты не скачивались и не импортировались. Пригодность для Quest не проверялась.

Итог. Из наших паков годятся контейнеры, бетонные блоки, ангары и эстакады Industrial Set,
но они лежат только в проекте-каталоге. Кранов, судна, воды, причала и портовой
техники нет нигде. Бесплатный маршрут: набор JakeBradley (CC BY) плюс свои контейнеры
и вода. Цельный платный вариант — пак ITHappy Port ($99).

### Существующие ассеты проекта

Проверка 2026-10-06 по файлам, без Unity: размеры, треугольники и вид не измерялись, кроме значений из прежних документов.
С 2026-10-05 полные паки находятся в отдельном проекте `F:/UnityProjects/EnvironmentPackCatalog`.
В игровом `Assets/env_packs` осталось 16 используемых ассетов.

- **[И]** — игровой проект; путь от корня `Vr_Battlegrounds_ai`.
- **[К]** — проект каталога; путь от `EnvironmentPackCatalog/Assets/env_packs/`. Переносить выборочно с сохранением GUID по `Docs/level-design/environment-pack-policy.md`.
- Сокращения: `IND` = `RPG_FPS_game_assets_industrial`, `PAT` = `PostApocalypticTown/URP/Art/Prefabs`.

> Контейнеров в игровом проекте **нет**. Путь `Assets/env_packs/RPG_FPS_game_assets_industrial/Containers/...` в `lobby-field-range-assets.md` устарел: теперь это [К].

| Роль | Статус | Пути | Заметки |
|---|---|---|---|
| Морские контейнеры | Есть [К] | `IND/Containers/Cargo_container_v1/`: `LD1close`, `LD1open`, `LD1through`, `LD1doorL`, `LD1doorR`, `LD2close`, `LD2open`, `LD2through`; материалы `Source/Cargo_container_v1/v2/v3.mat`; `PAT/SM_Shipping_Container.prefab` | Ядро темы. `LD1close` — 8×3×3 м, 12 треугольников, URP Simple Lit. У open-вариантов отдельные створки. Три материала, вероятно три цвета (не проверено). Штабели собирать самим |
| Бетонные ограждения, блоки | Есть [К] / частично [И] | [К] `IND/Fences/Road_blocks/Road_block_v1`; `IND/Fences/Concrete_fences/` (`Concrete_fence_v1_*` — 11 шт., `v2_*` — `Gate`/`L`/`S`/`S_half`/`end`, `UNIConcrete_fence_v1_*` — 5 шт.); [И] `Assets/Prefabs/LevelDesign/Decorated/ConcreteFence_LowBase_Soft.prefab` | `Road_block_v1` ≈ 2,35×1,07×0,89 м; `wall_set_v2` ≈ 18,7×3,3×0,72 м. Чистый промышленный стиль |
| Склады, ангары | Есть [К] | `IND/Buildings/Industrial/Hangars/Hangar_v1…v7`, `Hangars_interior` (около 60 префабов, v5 модульный: TP/EP) | Основной кандидат для складов терминала. Размеры не измерены |
| Эстакады, мостки, лестницы | Есть [К] | `IND/Other_props/Support_set/Support_set_v1/` (36 префабов); `HouseForge_01/URP/Art/Prefabs/Metal/SM_Metal_Stairs*`; `AbandonedFactory/Art/Prefabs/SM_Stairs_01a/b/c`, `SM_Railing_01a` | Опоры, мосты и перила для верхнего уровня |
| Заборы и ворота | Есть [К] | `Concrete_fence_v2_Gate`; `AbandonedFactory/.../SM_Fence_01a/02a/03a` (сетка, ≈ 2,22×1,11 м, 889 треугольников); [И] текстура `Lattice_v1`, `Assets/Art/ArsenalBoundary/FenceSteel.mat` | Шлагбаума нет |
| Поддоны, ящики, бочки | Есть [К] / [И] | [И] `Decorated/PalletFence_TwoLevels_Soft`, `WoodenBox_Low_Soft`; [К] `IND/Other_props/Palets/*`, `IND/Boxes/Wooden_box_v1/*`, `IND/Barrels/*` | Мелкие укрытия и декор |
| Промышленный декор | Есть [К] / [И] | [И] `Decorated/Generator_Mid_Hard`; [К] `IND/Oil_tanks/Oil_tank_v1/v2`, `IND/Dumpsters/*`, `IND/Other_props/Electric_box_v1–v3`, `Pipes_set_v1_*` | `Oil_tank_v2` ≈ 1,74×5,18×1,71 м |
| Асфальт, бетон, дороги | Частично [К] / [И] | [К] `IND/Textures/Asphalt/Seamless_asphalt_v1`, `Road_with_pavements_v1`; `IND/Roads/Road_sets/Road_set_v1/` (около 20 префабов); `IND/Roads/Floor_elevation_sets/UNIFloor_elevation_set_v1/`; [И] `Assets/Materials/ConcreteWall/ConcreteFloorMat.mat`, `Assets/Art/Materials/ServiceYard/ServiceYard_Concrete.mat` | Портовой разметки (жёлтые линии, номера рядов, hazard-полосы) нет: нужны декали |
| Канаты, цепи | Частично [К] | `HauntedFarmHouse/.../SM_Chain_*`, `SM_Rope_Bundle_01/02`; `HouseForge_01/.../SM_MetalChain_*` | Деревенский/хоррор-стиль; швартовых канатов нет |
| Освещение | Частично [К] | `RuralTown/.../SM_LampPost`, `SM_Electric_Pole`; `AbandonedFactory/.../SM_Lamp_01a`, `SM_CoverLight_01`; `PAT/SM_Light1–3` | Высоких прожекторных мачт нет. Днём это фоновые силуэты |
| Небо | Частично [К] | `IND/Textures/Skyboxes/Skybox_v5.mat`, `Skybox_v9.mat` (cubemap 4096×2048, LDR); `AbandonedFactory/.../M_URPSkybox.mat` (Procedural) | Содержимое скайбоксов v5/v9 не просмотрено. Встроенный Procedural sky подойдёт как основа |
| Техника | Частично [К] | `HauntedFarmHouse/.../SM_Truck_01` | Ржавый пикап, 55 969 треугольников. Для порта не подходит |

#### Чего нет совсем

Портальный кран (STS/RTG) и любые краны; судно; вода и море (шейдер, материал); причальная стенка, кнехты, кранцы; погрузчик, ричстакер, тягач, контейнерное шасси; прожекторные мачты; птицы; портовая разметка; будка КПП и шлагбаум; солнечный HDRI высокого разрешения.

#### Стиль

- **Industrial Set** — низкополигональный и относительно чистый; это ядро темы.
- PostApocalypticTown, HauntedFarmHouse, AbandonedFactory, DestructedBuildings — ржавые и разрушенные; только как отдельные акценты.
- HouseForge, ModularWoodenBuildings, RuralTown — деревенские; из них брать только лестницы, фонари и цепи.

### Внешние ассеты

Обозначения: ✅ — проверено на странице или через API Sketchfab (лицензия, скачиваемость,
число треугольников); 📣 — заявлено автором; ❓ — не проверено.
Fab.com отвечает 403 на автоматические запросы: его листинги нужно проверять вручную.
Asset Store EULA разрешает коммерческое использование. CC BY требует указать автора в титрах.

#### Цельные паки «порт»

| Ассет | Цена, лицензия | Характеристики | Роль и адаптация |
|---|---|---|---|
| [ITHappy — Port Low Poly](https://assetstore.unity.com/packages/3d/environments/industrial/port-low-poly-asset-pack-by-ithappy-265400) | $99 ✅, EULA | 📣 216 моделей, 272K треугольников на весь пак, 8 материалов, текстуры 1–2K; ✅ URP, 6000.0.28f1, v2.4 от 11.08.2025 | **Лучший платный кандидат под Quest.** 📣 контейнеры 20/40 футов, краны, контейнеровозы, ричстакер, погрузчик, тягач, кнехты, барьеры, буксир. Стилизованный: проверить совместимость со стилем оружия и аватаров |
| [Megapoly.Art — Port Pack](https://assetstore.unity.com/packages/3d/environments/industrial/megapoly-art-port-pack-204638) | $34.99 ✅ | ✅ только Built-in, 2020.3 | Запасной вариант; материалы переводить на URP вручную |
| [MoslemAlipour — Realistic Modular Container Port](https://assetstore.unity.com/packages/3d/props/realistic-modular-container-port-environment-353276) | $29 ✅ | ✅ URP, 2,9 ГБ, 15.01.2026 | Реализм; только как донор отдельных мешей с децимацией |
| [Synty — SIMPLE Port](https://assetstore.unity.com/packages/3d/environments/industrial/simple-port-cartoon-assets-49993) | $9.99 ✅ | 1,9 МБ, 2023 | Мультяшный, разве что дальний план |
| [Leartes — Container Port Environment](https://assetstore.unity.com/packages/3d/environments/industrial/container-port-environment-shipping-port-cargo-ship-harbor-dock--309998) | $99.99 ✅ | 5,8 ГБ, ПК-уровень | Не рекомендуется для Quest |

#### Причальный кран, портовая техника

| Ассет | Цена, лицензия | Треугольники | Роль и адаптация |
|---|---|---|---|
| [JakeBradley — Ship To Shore Crane](https://sketchfab.com/3d-models/ship-to-shore-crane-401069fccf814da7a4f9ce5a51487548) | Бесплатно, CC BY ✅ | 9,8K ✅ | **Бесплатный выбор №1** для задника. У автора тот же стиль у ричстакера, straddle carrier и контейнеров |
| [JakeBradley — Reach Stacker](https://sketchfab.com/3d-models/reach-stacker-dc941fb9354b4e1f83db307d0fca352a) | Бесплатно, CC BY ✅ | 6,0K ✅ | Статичное укрытие внутри арены, box-коллайдер |
| [JakeBradley — Straddle Carrier](https://sketchfab.com/3d-models/straddle-carrier-63f733b8d7784ad095849cb60d4df374) | Бесплатно, CC BY ✅ | 10,5K ✅ | «Арка» над проходом или ориентир на краю |
| [VIS Games — Container crane](https://assetstore.unity.com/packages/3d/props/container-crane-2275) | $4.99 ✅ | ❓; ✅ URP, 6000.0.24f1, v1.7 2025 | Самый свежий платный кран; 114 МБ, пережать текстуры |
| [CG Duck — Port container crane](https://assetstore.unity.com/packages/3d/props/industrial/port-container-crane-103935) | $5 ✅ | 📣 6,7K | Unity 5.6 (2017), материалы конвертировать |
| [PolySquid — Cargo Crane](https://assetstore.unity.com/packages/3d/environments/industrial/cargo-crane-74117) | $9.99 ✅ | ❓ | Запасной; тип крана сверить по скриншотам |
| [knitro_vedant — Truck Container Pack](https://sketchfab.com/3d-models/truck-container-pack-low-poly-asia-000b6d08ad7b4f4198dcab2790008553) | Бесплатно, CC BY ✅ | 5,6K ✅ | Тягач с контейнером. ⚠️ Логотипы Maersk/Hapag-Lloyd/Evergreen — текстуры заменить |
| [Linder Media — Low Poly Forklift 01](https://assetstore.unity.com/packages/3d/vehicles/land/low-poly-forklift-01-161954), [Cargo Truck](https://assetstore.unity.com/packages/3d/vehicles/land/low-poly-cargo-truck-131820) | ❓ | ❓ | Платные запасные погрузчик и грузовик |
| Fab: [Harbor Crane Pack](https://www.fab.com/listings/52d12ea2-fbe1-4d02-9b4d-9ccaf512de88), [Large Harbor Crane](https://www.fab.com/listings/94777267-7001-42e8-9e0d-65cb4587bea5) | ❓ | 📣 low poly, PBR 2K | Проверить лицензию и FBX вручную |

#### Контейнеровоз (задник)

| Ассет | Цена, лицензия | Треугольники | Роль и адаптация |
|---|---|---|---|
| [LavaWave — Container Ship](https://sketchfab.com/3d-models/container-ship-4f7cf10593824061bfe2e36ffd823cde) | Бесплатно, CC BY ✅ | 20,4K ✅ | Уже с контейнерами — лучший силуэт; тени выключить |
| [Javier.Fernandez — Low Poly Cargo Ship](https://sketchfab.com/3d-models/low-poly-cargo-ship-4c22cbaf01c1427f8ab60b3a07b1b32c) | Бесплатно, CC BY ✅ | 2,4K ✅ | Самый лёгкий, для дальнего плана |
| [muyayaconcept — Low-Poly General Cargo Vessel](https://sketchfab.com/3d-models/low-poly-general-cargo-vessel-game-ready-ce8df28df8f04f43991cc7c3c64e3479) | Бесплатно, CC BY ✅ | 4,0K ✅ | Сухогруз: контейнеры ставить на палубу самим |
| [JakeBradley — Shipping Boat](https://sketchfab.com/3d-models/shipping-boat-e66cf923abf84a4ab461209d65c04e02) | Бесплатно, CC BY ✅ | 88K ✅ | Совпадает по стилю с краном; децимировать до ≤ 20K |

#### Контейнеры

Открывающиеся двери на петлях ни в одном внешнем паке не подтверждены. У нашего
Industrial `Cargo_container_v1` есть open-варианты с отдельными створками.

| Ассет | Цена, лицензия | Треугольники | Роль и адаптация |
|---|---|---|---|
| [JakeBradley — Container Red](https://sketchfab.com/3d-models/container-red-c6a0372261ca47e68664a69a1c7c060e) / [Blue](https://sketchfab.com/3d-models/container-blue-7da7790601f94793aa44a09c2d78c608) | Бесплатно, CC BY ✅ | 2,4K ✅ | Стиль крана; цвета — перекраской текстуры |
| [Strifey7 — Low Poly Shipping Container](https://sketchfab.com/3d-models/low-poly-shipping-container-9788d57fe5c240c796e80b7edcef6f70) | Бесплатно, CC BY ✅ | 1,8K ✅ | Вымышленный логотип |
| [jeandiz — Shipping containers](https://sketchfab.com/3d-models/shipping-containers-cc3f7136710f4905905eae1d10ac50b7) | Бесплатно, CC BY ✅ | 48 ✅ | Дальние штабели |
| [Rupsis — Low Poly Shipping Containers](https://blendswap.com/blend/6091) | Бесплатно, CC0 ✅ | 📣 считанные полигоны | Дальние штабели, без атрибуции |
| [Calvin Weibel — Free Shipping Containers](https://assetstore.unity.com/packages/3d/environments/industrial/free-shipping-containers-18315) | Бесплатно, EULA ✅ | ❓ | 2016, материалы Standard → URP |
| [Mobile Gaming Assets — Extreme Low poly Containers](https://assetstore.unity.com/packages/3d/props/extreme-low-poly-containers-pack-mobile-containers-328283) | $5 ✅ | ❓; ✅ URP, 6000.0.54f1 | Хороший платный «контейнер вблизи» |
| [DYC — Low Poly PBR Shipping Containers](https://assetstore.unity.com/packages/3d/props/industrial/low-poly-pbr-shipping-containers-pack-351172) | $7.95 ✅ | ❓; ✅ URP, 6000.3.1f1, 2026 | Самый свежий URP-пак |
| [Poly Haven — Container Side](https://polyhaven.com/a/container_side) | CC0 ✅ | — | Текстура для собственного контейнера |

#### Причал, блоки, мачты

| Ассет | Цена, лицензия | Треугольники | Роль и адаптация |
|---|---|---|---|
| [Adelprince69 — Bollard](https://sketchfab.com/3d-models/bollard-adelprince-f9461dcdaa4a4b2f94130356b9fdb74e) | Бесплатно, CC BY ✅ | 1,6K ✅ | Кнехт на кромке причала |
| [Poly Haven — Old Tyre](https://polyhaven.com/a/old_tyre), [Lifebuoy](https://polyhaven.com/a/lifebuoy), [Ocean Buoy](https://polyhaven.com/a/ocean_buoy) | CC0 ✅ | 2,9K / 10,7K / 12,2K ✅ | Покрышки как отбойники; круг и буй децимировать |
| [valsekamerplant — PSX jersey barrier](https://sketchfab.com/3d-models/psx-style-jersey-barrier-3c7eeed2345542648a7af1b27a3082fb) | Бесплатно, CC BY ✅ | 62 ✅ | Укрытие по пояс; возможна замена текстуры. Альтернатива — наш `Road_block_v1` [К] |
| [jeffkolada — Jersey Barrier with Fence](https://sketchfab.com/3d-models/concrete-jersey-barrier-with-fence-85c3967de8c24357acc4371339e73c2e) | Бесплатно, CC BY ✅ | 248 ✅ | Блок с сеткой: укрытие с просветом |
| [alex20010804 — Lighting Mast](https://sketchfab.com/3d-models/lighting-mast-single-model-from-asset-pack-0594bfb94a2c43ecbef30fe28a3831b8) | Бесплатно, CC BY ✅ | 14,7K ✅ | Высокая мачта для задника, только меш |
| Стенка причала, отбойники | — | — | Пригодных бесплатных не найдено; сделать самим (бетонная кромка + D-профиль) |

#### Вода и небо

| Ассет | Цена, лицензия | Характеристики | Роль и адаптация |
|---|---|---|---|
| Свой шейдер | — | — | **Рекомендуется:** opaque-плоскость, 1–2 прокручиваемые нормали, fresnel к цвету неба, без глубины и отражений |
| [BitGem — URP Stylized Water (Proto)](https://assetstore.unity.com/packages/vfx/shaders/urp-stylized-water-shader-proto-series-187485) | Бесплатно ✅ | ✅ URP | Проверить зависимость от Depth/Opaque Texture |
| [Houidisoft — Simple stylized water](https://assetstore.unity.com/packages/vfx/shaders/simple-stylized-water-393128) | Бесплатно ✅ | ✅ URP, 6000.3.8f1, 2026 | Свежий, отзывов нет |
| [Staggart — Stylized Water 3](https://assetstore.unity.com/packages/vfx/shaders/stylized-water-3-287769) | $25 по скидке ✅ | ✅ URP, 6000.0.68f1, 2026 | Лучший платный; 📣 работает на Quest 3 |
| [Poly Haven — docklands_02](https://polyhaven.com/a/docklands_02) | CC0 ✅ | HDRI ясного дня в доках | Скайбокс или запекание; на Quest — LDR-кубемап. Также `small_harbour_morning` |

### Рекомендуемый порядок

1. Блокаут из кубов ISO-размеров по схеме Shipment, проверка ходьбой в шлеме.
2. Контейнеры: наш Industrial `Cargo_container_v1` [К] (перенести с GUID) или свой контейнер на текстуре `container_side`; дальние штабели — jeandiz/Rupsis.
3. Задник набором JakeBradley (CC BY): STS-кран, ричстакер, straddle carrier — единый стиль.
4. Судно — LavaWave Container Ship; запасной — Javier.Fernandez.
5. Блоки — наш `Road_block_v1` [К] или valsekamerplant; кнехты — Adelprince69; мачта — alex20010804.
6. Вода — свой простой шейдер; небо — `docklands_02`.

Платные альтернативы: ITHappy Port ($99) целиком; контейнеры Extreme Low poly ($5) или DYC ($7.95);
кран VIS Games ($4.99); вода Stylized Water 3 ($25).

### Отсеяно

| Кандидат | Причина |
|---|---|
| CG Duck — Port container crane (Sketchfab), martinwaters — St.Petersburg Port Crane, Vorago — Gantry Crane, PolyPlex — Bollards Pack | Не скачиваются или без лицензии |
| Chakra — Semi Truck Container Trailer | Не скачивается; 1,07M треугольников |
| fabio.ardito — Container Trailer Chassis | CC BY-SA, 663K треугольников |
| BluE_BrO — Straddle Carrier | $45, 97K треугольников, 4K-текстуры |
| feivelyn — Cargo Asset Set, swissch — Office of a Crane Operator | CC BY-NC — нельзя в коммерческом проекте |
| Прочие gantry-краны Sketchfab (alanUscanga, speedtwo, Randonavt, jhervianto) | Не причальные и/или 132K–997K треугольников |
| TIBITEK — Container Props Pack | Только Built-in |
| Bretwalda Port Pack, Harbor Crane Set, Kobra Gantry Crane | Заброшены (2016–2018) |
| Eonvox — FREE Low Poly Shipping Container | «Created with AI», один меш |
| Kenney City Kit Industrial, Watercraft Kit | Нет контейнеров и кранов |
| Pavlov «Container Yard» | Только референс, на Quest нет |
| Все листинги Fab.com | 403, не проверены |

## Звуковая атмосфера


### Базовый фон

1. **Открытый порт** — 2D-петля общего гула и ветра, нарезка 60–90 с из [fortom «Dockside Soundscape»](https://freesound.org/people/fortom/sounds/274604/) или [keepthatPMA «Port on sunday»](https://freesound.org/people/keepthatPMA/sounds/511158/) (обе CC0). Ориентир громкости −24…−28 dBFS, подобрать в шлеме.
2. **Плеск у причала** — 2–3 моно 3D-источника вдоль кромки: [TRP «Water lapping under dock»](https://freesound.org/people/TRP/sounds/574869/) или [Eakoontz «Water Lapping Dock»](https://freesound.org/people/Eakoontz/sounds/151747/).

### События

| Событие | Источники | Длит. | Интервал | Пространственность | Ограничения |
|---|---|---|---|---|---|
| Крик одной-двух чаек | [Snapper4298](https://freesound.org/people/Snapper4298/sounds/166703/) + нарезка [Kinoton Seagull Swarm](https://freesound.org/people/Kinoton/sounds/468080/) | 1–5 с | 15–45 с | 3D-точка над водой, 15–30 м высоты | ≤ 2 одновременно, кулдаун 10 с; тише шагов |
| Стая чаек пролетает | нарезка Kinoton / TeTeNoise | 5–10 с | 120–300 с | Короткая дуга | Не одновременно с гудком |
| Далёкий гудок судна | [coalcon Fog Horn](https://freesound.org/people/coalcon/sounds/636075/), [Kinoton Cruise Ship Horn 3x](https://freesound.org/people/Kinoton/sounds/352374/), `ALARM_Foghorn_full` | 3–10 с | 120–300 с | 3D далеко (200–600 м), сильный LPF | Самое громкое событие; не во время боя |
| Кран: мотор, сигнал, опускание спредера, лязг контейнера | `ENGINE_Diesel_*_loop`, `MACHINE_Digger_Lorry_loop`, `IMPACTS/Metal/*`, `CHAIN_*`; сигнал — фрагменты [klankbeeld Container-port](https://freesound.org/s/323424/) (CC BY) | 8–20 с | 45–120 с | 3D-точка на кране | Одна последовательность за раз; лязг ≤ −12 dB к выстрелам |
| Погрузчик проезжает и сдаёт назад с бипами | [Soundaddiction Gabelstapler](https://freesound.org/people/Soundaddiction/sounds/493082/), [kyles forklift](https://freesound.org/people/kyles/sounds/451866/), `ENGINE_Diesel_*`; бипы — [tambascot](https://freesound.org/people/tambascot/sounds/196567/), [peridactyloptrix](https://freesound.org/people/peridactyloptrix/sounds/221927/) | 10–25 с | 60–150 с | 3D, движется по пути между штабелями | Бипы прорезают микс: −6 dB, не ближе 40 м |
| Далёкий грузовик или тягач | [kyles truck/APC pass by](https://freesound.org/people/kyles/sounds/405086/), [AdrianoAnjos small truck](https://freesound.org/people/AdrianoAnjos/sounds/442528/), `CAR_Drive_By_*` | 6–15 с | 40–100 с | 3D по сплайну дороги за забором, доплер 0,3–0,5 | Не одновременно с погрузчиком |
| Порыв ветра | `WIND_Storm_Blowing_01…04_loop` (кусок 3–6 с с фейдом) | 3–6 с | 30–90 с | 2D | Только вне боя |

### Кандидаты

| Источник | Лицензия | Содержимое | Роль |
|---|---|---|---|
| [fortom «Dockside Soundscape»](https://freesound.org/people/fortom/sounds/274604/) | CC0 ✔ | 7:56, стерео | Основной фон |
| [keepthatPMA «Port on sunday»](https://freesound.org/people/keepthatPMA/sounds/511158/) | CC0 ✔ | 3:04, стерео | Тихий вариант фона |
| [flood-mix «Baltimore Harbor»](https://freesound.org/people/flood-mix/sounds/413334/) | CC0 ✔ | 1:33, стерео | Фон |
| [Pfannkuchn «Distant harbour, Hamburg»](https://freesound.org/people/Pfannkuchn/sounds/342878/) | CC0 ✔ | 6:28, стерео | Подложка; запись ночная — проверить |
| [klankbeeld «Container-port ambience»](https://freesound.org/s/323424/), [«far crane port»](https://freesound.org/people/klankbeeld/sounds/621191/) | CC BY 4.0 ✔ | Краны, бипы; чайки, волны | Нарезка сигналов крана, атрибуция |
| [Kinoton «Seagull Swarm at Port»](https://freesound.org/people/Kinoton/sounds/468080/), [TeTeNoise «Seagulls by the sea»](https://freesound.org/people/TeTeNoise/sounds/517678/) | CC0 ✔ | 1:50 / 3:14 | Нарезка чаек |
| [Snapper4298 sea gulls](https://freesound.org/people/Snapper4298/sounds/166703/) (+166707, 166709) | CC0 ✔ | 7 с, моно | Одиночные крики, готовы к 3D |
| [juskiddink «Flock of seagulls»](https://freesound.org/people/juskiddink/sounds/98479/) | CC BY 4.0 ✔ | 60 с | Стая, атрибуция |
| [coalcon Fog Horn](https://freesound.org/people/coalcon/sounds/636075/), [Kinoton Cruise Ship Horn 3x](https://freesound.org/people/Kinoton/sounds/352374/), [monotraum](https://freesound.org/people/monotraum/sounds/208714/), [TiredHippo](https://freesound.org/people/TiredHippo/sounds/317386/) | CC0 ✔ | 6–34 с | Гудки (coalcon — m4a, конвертировать) |
| [tambascot backup beeps](https://freesound.org/people/tambascot/sounds/196567/), [SEF7](https://freesound.org/people/SEF7/sounds/702098/), [TRP](https://freesound.org/people/TRP/sounds/567294/), [C-V](https://freesound.org/people/C-V/sounds/523413/), [florianreichelt](https://freesound.org/people/florianreichelt/sounds/459993/) | CC0 ✔ (C-V и florianreichelt — по фильтру поиска) | 16–80 с | Бипы заднего хода |
| [Soundaddiction «Gabelstapler Komplett»](https://freesound.org/people/Soundaddiction/sounds/493082/), [kyles forklift](https://freesound.org/people/kyles/sounds/451866/) | CC0 ✔ | 15 с / 1:20 | Погрузчик (kyles записан в помещении) |
| [kyles truck/APC pass by](https://freesound.org/people/kyles/sounds/405086/) | CC0 ✔ | 1:31, моно | Тяжёлый грузовик, также для лобби |
| [Bon_Vivant_Pictures «Beeps & Brakes»](https://freesound.org/people/Bon_Vivant_Pictures/sounds/449080/) | CC0 ✔ | 1:03 | Бипы и пневмотормоза |
| [TRP water lapping](https://freesound.org/people/TRP/sounds/574869/), [Eakoontz](https://freesound.org/people/Eakoontz/sounds/151747/) | CC0 ✔ | 45 / 61 с | Плеск о причал |
| [Sonniss GDC 2015](https://archive.org/details/SonnissGameAudioGDC): «Original Sounds — Docks», «Kpow — Industrial Disquiet» | Royalty-free, коммерция ✔ | Лязг контейнеров в трюме, грузовики в доках, день с погрузчиком | Лязг и проезды |
| [freetousesounds «Container Port of Hong Kong»](https://freetousesounds.bandcamp.com/album/harbor-industrial-sounds-container-port-of-hong-kong) | от $5, royalty-free | 11 треков, ~27 мин | Платно. Записано ночью: фон не годится, нарезка событий — да |

**Стартовый набор.** Фон — fortom (90 с) и 2–3 точки плеска. События — чайки Snapper4298 и Kinoton,
гудок Kinoton и coalcon, бипы tambascot, погрузчик Soundaddiction, проезд kyles 405086, кран из
библиотеки (`ENGINE_Diesel_*`, `IMPACTS/Metal`, `CHAIN_*`). Всё CC0, кроме опционального klankbeeld (CC BY).

### Платные

| Источник | Цена, лицензия | Содержимое | Статус |
|---|---|---|---|
| [freetousesounds — Container Port of Hong Kong](https://freetousesounds.bandcamp.com/album/harbor-industrial-sounds-container-port-of-hong-kong) | от $5, royalty-free, без атрибуции | 11 треков, ~27 мин: грузовики, бипы кранов, контейнеры | Проверено; запись ночная — только нарезка событий |
| [Inland Port (toneglow)](https://sonniss.com/sound-effects/inland-port/) / [ASE](https://www.asoundeffect.com/sound-library/inland-port/) | ~$18,20 по акции (обычно $26), royalty-free | 42 файла, 3,5 ГБ, ~104 мин, Дуйсбург: краны, straddle carriers, терминал | Заявлено. **Первая покупка, если бесплатного не хватит** |
| [BOOM Library — Harbor & Industrial](https://www.boomlibrary.com/sound-effects/harbor-industrial/) | ~$139 | 146 файлов, 4,3 ГБ | Заявлено |
| ASE: [Docks](https://www.asoundeffect.com/sound-library/docks/), [Dock Ambiences and SFX](https://www.asoundeffect.com/sound-library/dock-ambiences-sfx/), [The Shipyard](https://www.asoundeffect.com/sound-library/the-shipyard/), [Industrial Harbor](https://www.asoundeffect.com/sound-library/industrial-harbor/) | Цены не получены | Краны, контейнеры, грузовики, сирены | Заявлено (страницы не открылись) |
| Asset Store «Ambisonic Ambience (Harbour/Cove)» | ~$4,99 | Гавань | Заявлено; ambisonic на Quest не нужен |


### Реализация в Unity и Quest (общая для серии карт)


Предложение, не утверждённая архитектура.

**Структура.**
- `AmbienceProfile` (ScriptableObject) на сцену: петли фона и список `AmbientEvent { clips[], weight, cooldown, minInterval..maxInterval, spatial: 2D/Point/Path, spawnPoints/spline, volumeRange, pitchRange, exclusiveGroup, blockedByCombat }`.
- `AmbienceDirector` — один на сцену, локальный `MonoBehaviour`. Звук косметический, сеть не нужна: каждый клиент играет свою случайную последовательность. Общие для всех события (один и тот же пролёт вертолёта) — по желанию, через `[ClientRpc]` с seed и временем старта.

**Планировщик.**
- Тикер раз в 1–2 с. Когда прошло «окно тишины», выбрать событие взвешенным рандомом из тех, у которых истёк кулдаун и свободна группа.
- Группы: «Тяжёлое» (вертолёт, грузовик, гудок, выпуск печи, сирена) — одно за раз; «Фон» (птицы, чайки, радио) — до двух.
- После тяжёлого события пауза 20–40 с. Один клип не повторять подряд; pitch ±5 %, громкость ±2 dB.

**Голоса (Quest 2/3).** На эмбиент не больше 8 реальных голосов: 1–2 петли фона, до 4 ближайших 3D-петель,
до 2 событий; остальное — оружию, шагам и голосовому чату. `AudioSource.priority` эмбиента 160–200.
Дальние петли выключать по дистанции (проверка раз в 0,5 с).

**Импорт.**
- Длинные петли фона (> 10 с, стерео): Streaming, Vorbis ~50–60 %; стримов одновременно 1–2.
- 3D-петли 2–15 с: Compressed In Memory, Vorbis ~70 %, Force To Mono.
- Короткие события (< 3 с): Decompress On Load, моно.
- Длинные события (вертолёт): Compressed In Memory, моно, Preload Audio Data.
- Все 3D — моно. Готовые стерео-пролёты играть 2D или свести в моно и вести по сплайну.
- 96/192 кГц и 24/32 бит пересчитать в 44,1 кГц / 16 бит до импорта.

**Пространственность.** 3D: Spatial Blend 1, Logarithmic Rolloff, Min distance 3–10 м.
Пролёт: Doppler 0,5–1, движение по `SplineContainer` или Lerp между точками за 15–40 с.
Meta XR Audio spatializer — только на крупных событиях, не на фоне.

**Микшер.** Группа `Ambience` (`Bed`, `Events`) отдельно от `Weapons`, `Footsteps`, `Voice`.
Дакинг от `Weapons`: −6…−10 dB, attack 50 мс, release 1–2 с. Во время боя тяжёлые события не стартуют,
интервалы ×2. Фон −20…−26 dBFS RMS; на группе срезать 2–4 кГц на 2–3 dB, чтобы шаги и выстрелы читались.

**Мелочи.** Фон «дышит»: медленная модуляция громкости ±2 dB с периодом 20–40 с. Готовые стерео-пролёты
в 3D не ставить: получается двойной доплер и неверная панорама. Последовательности (кран: мотор → бипы → удар)
делать одной корутиной внутри события. Снапшот «Combat» выключает события, которые игрок может принять
за игровые сигналы: далёкую стрельбу, артиллерию, сирены, бипы. Дебаг-переключатели (показать точки и сплайны,
запустить событие X) — в EditorPrefs и меню `Tools/VR Battlegrounds/Debug`, не в git.

**Атрибуция.** CC BY (klankbeeld, juskiddink, InspectorJ, Benboncan, Airborne80, kev_durr Arc Welding) — в титры и файл кредитов.
CC0, Sonniss и Universal Sound FX атрибуции не требуют.


### Отсеянные звуки


| Что | Почему |
|---|---|
| [jgrzinich forest gunfire](https://freesound.org/s/397370/), [SoundLover16 Steel works](https://freesound.org/people/SoundLover16/sounds/430895/), Orange Free Sounds foghorn | CC BY-NC — коммерция запрещена |
| BBC Sound Effects (RemArc) | Только некоммерческое использование |
| mil0001 Helicopter Flyby | Синтез из голоса, нереалистично |
| freetousesounds Hong Kong Port как фон | Ночная запись; только нарезка событий |
| Бандлы Sonniss 2016–2024 | Состав не проверен (Cloudflare); бандла 2025 не было |
| BigSoundBank «Gulls on the Harbor», ZapSplat | 403, лицензия не проверена |
| Universal Sound FX `BATTLES/Medieval_*` | Средневековье, не к лобби |
| `AMBIENCE_City_Street_Calm_Day_loop` в лобби | Город, не лес и стрельбище — заменить |
