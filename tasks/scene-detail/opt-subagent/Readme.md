# Удешевление GPU-кадра Lobby (кроме пола) — субагент scene-detail

2026-10-20. Ветка `codex/scene-detail-opt-20261020` (worktree `F:/CodexWorktrees/71af-opt`), от a0432b240.
Цель: найти ~2 мс вне пола (кадр 10,8 мс, бюджет 7 мс). Пол/бетон/плитка (S_VPWorldAligned) — главный агент.
**Шлем не мерился:** все оценки ниже — гипотезы до A/B на Quest 3.

## Что найдено (аудит в Unity, тикеты 896/905)

- Весь свет Lobby запечён (14 источников, `Baked`), lightmaps non-directional. Значит Lit-шейдеры дают
  только GI + блик от reflection probe; Baked Lit визуально близок, но дешевле в разы.
- `S_BasicTextured` (пакет MilitaryCamp/RuralTown): переключатели — Boolean-свойства + Branch, не keywords.
  На каждом пикселе считаются BaseMap, NormalMap, ORMH, Emissive, Opacity и два triplanar-подграфа
  (≈11 выборок) + PBR. Отсюда texture fetch stall 40–55 % у скал/вышки. Импорт текстур уже ASTC 6x6, 512, mip.
- «Cube» 5 % — `Environment/OuterGround` 90×113 м, URP Lit (`OutsideGravel`, emission с чёрным цветом).
  Прочие Cube — гравий дорожек стрельбища (`RangeGravel`), балки и тент (`RoofMetal`, `RoofCloth`, `CanvasOlive`).
- Вышка: LOD0 142k, LOD3 17,8k, LOD4 8,9k треугольников; на 27–28 м выбирался LOD3.
- Ели: LOD3 (последний) 1477 tri; хвоя S_Foliage alpha-test + alpha-to-coverage.
- Зеркало: 768², 16 м; второй UXR-образец в `Scene/Rooms/.../SpawnRoom` неактивен.
- DistantGround: уже 1 выборка + smoothstep — дёшев, 1,6 % — это площадь.

## Что изменено

| Правка | Объём | Ожидаемо (гипотеза) | Визуальный риск |
|---|---|---|---|
| 91 материал статичного декора → URP Baked Lit (`LobbyGpuMaterialTier`) | S_BasicTextured 54, S_Foliage 1, URP Lit 36 | крупнейший рычаг: затронуто ≈55–60 % replay-тактов кадра; −1,5…3 мс | пропали блики probe, normal map, AO ORMH; Saturation/Contrast не переносятся (M_ArmoredVehicle_04 contrast 1,25); скалы без лайтмапа — плоские |
| Зеркало 768→512², 16→8 м (`LobbyGpuSceneTweaks`) | 1 активное зеркало | ≈−0,3 мс (0,64 мс × 0,56) | мягче отражение, дальше 8 м не отражается |
| Вышки: LOD4 ниже 0,65 высоты экрана | 3 LODGroup в сцене | −0,05…0,15 мс | LOD4 вблизи — к вышкам не подойти ближе ~25 м |

Пропущено инструментом (отчёт `tasks/scene-detail/reports/lobby-gpu-tier/`, вне Git): материалы вне папок
лобби (UXR, Arsenal, ConcreteWallMat), игровые (мишени), `LobbyMetal` (используется вне лобби), прозрачные,
world-aligned (`M_Floor_linoleum_a*`), прозрачность из отдельной карты (`M_Wire_*`, `M_MetalFloor`, `M_ToolRack`).

Откат: меню `VR Battlegrounds/Performance/Lobby GPU/Revert material tier` и `Revert scene tweaks` или git revert.

## Предложено без изменения

- Сетка забора `M_Wire_0{1,2}_BackdropCutout` (228 рендереров, забор ≈4,9 %): запечь Opacity в альфу BaseMap
  (новая текстура) → Baked Lit cutout.
- Декали `S_Decal` (`M_EdgeDecal`, `M_MeshDecal_01`, 354 рендерера, alpha-test): убрать дальние или Baked Lit cutout.
- Ели: импостор/billboard для дальнего леса или прореживание; A2C можно снять (MSAA-покрытие).
- DistantGround: сплошной цвет без текстуры — выигрыш мал.
- Глобальные (решает пользователь, кадр фрагментно-ограничен): MSAA 4→2 (оценка −1…1,5 мс, хуже края и
  A2C хвои), render scale 0,9 (−19 % пикселей), FFR выше 0,75. Цифры — оценки, не замер.

## Проверки

- Компиляция в worker (тикет 905): типы загружены, консоль без ошибок/предупреждений. Последние правки
  инструмента (alpha=1, только активные зеркала) внесены после лиза и в Unity не компилировались.
- Скриншоты Editor до/после (center-north, center-east, west-station): `tasks/scene-detail/reports/opt-subagent-screens/`.
  Зеркало ручным `Camera.Render` не отрисовывается — проверять в Play/шлеме.
- Тесты не писались (правило проекта: после пользовательской проверки).
- Не проверено: замер на шлеме, Android-сборка (варианты Baked Lit), вид зеркала, вид в шлеме.
