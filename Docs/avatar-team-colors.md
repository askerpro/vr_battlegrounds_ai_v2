# Цвета команды на форме аватара

Форма аватара красится в цвета команды **шейдером на лету**, без запечённых текстур. Работает для аватаров,
у модели которых есть маска формы (сейчас — `Optimized_MEF_Player` и его вариант `_Black`).

## Как настроить цвета команды

Ассет команды (`Assets/Data/Teams/*_Team.asset`, `TeamData`):

| Поле | Что красит | Сила |
|---|---|---|
| `mainColor` | одежда — рубашка, штанины, балаклава | `mainUniformStrength` |
| `additionalColor` | экипировка — бронежилет, сумки и подсумки (без магазинов), ремни, лямки, наколенник | `additionalUniformStrength` |
| `helmetColor` | голова — каска и очки (первый признак «свой/чужой») | `helmetUniformStrength` |

Сила 0 — родной цвет модели, 1 — полный тон. Правка видна сразу, в том числе в Play Mode.
`mainColor` (бывший `color`) ещё и подсветка зон спавна и призрак выбывшего — у них альфа цвета = прозрачность;
для формы альфа не используется, силу задают слайдеры.

Сейчас: **Военные** — одежда угольная `0.13`, экипировка Wolf Grey `0.30/0.31/0.32`, каска и очки `0.13`;
**Повстанцы** — силы 0 (родной песочный).

## Как устроено

- **Маска** `Assets/Models/Avatars/MEF_Optimized/MEF_Optimized_TeamMask.png` (2048, RGB): R — одежда, G — экипировка,
  B — каска и очки. Генерируется конвейером `Tools/mef-avatar` из классов граней (`mask_cls` в `02_stageA_cleanup.py`):
  класс назначается по исходным кускам модели, магазины в подсумках отсекаются по тёмной текстуре. Руками не рисуется.
- **Шейдер** `VR Battlegrounds/Team Uniform Lit` (`Assets/Shaders/Avatars/`) — копия URP Lit 17.4 с одним изменением:
  albedo проходит `ApplyTeamColors` (`TeamUniformLitInput.hlsl`). Формула (в sRGB, как прежнее запекание):
  `яркость_пикселя / медиана_канала × цвет`, поверх — доля `_TeamWear` исходного пикселя, приведённого к яркости цвета
  (потёртости, разнотон ткани). Медианы `_TeamMedians` — константа `TeamColorVariantBuilder.Medians`; при пересборке
  модели пересчитать (скрипт — `Tools/mef-avatar/README.md`). Цена на Quest — одна выборка маски и десяток ALU.
  Проверено: компилируется под Vulkan и GLES3 (ForwardLit, ShadowCaster, DepthNormals).
- **`TeamUniformColors`** (`Assets/Scripts/Player/Avatars/`) на корне аватара: каждый кадр сравнивает цвета команды
  игрока с применёнными и при изменении пишет `_TeamMainColor`/`_TeamAdditionalColor`/`_TeamHelmetColor` в
  `MaterialPropertyBlock` рендереров на этом шейдере (альфа = сила). Пока команды нет — значения материала.
- **Материалы**: `MEF_Optimized.mat` — без перекраски (силы 0); `MEF_Optimized_Black.mat` — цвета Военных (их видно
  вне игры: меню, иконка, труп). Оба на общем albedo — отдельной текстуры на команду больше нет.
- **`TeamColorVariantBuilder`** (`Tools/VR Battlegrounds/Avatars/Team Color Variant/Build Optimized MEF Black`)
  переводит исходный материал на шейдер, ставит `TeamUniformColors` на исходный аватар, строит материал/вариант/данные.

## Обновление URP

Шейдер — копия `Lit.shader` и `LitInput.hlsl`. При обновлении URP взять свежие копии и перенести отличия: свойства
`_Team*` в Properties и CBUFFER, `#include "TeamUniformLitInput.hlsl"` во всех проходах, `Color.hlsl`, функции
`TeamTint`/`ApplyTeamColors` и вызов в `InitializeStandardLitSurfaceData`, без `CustomEditor`.

## Проверка

`TeamUniformTests`: шейдер без ошибок; тело каждого зарегистрированного аватара с маской — на шейдере формы с маской;
на аватаре есть `TeamUniformColors`; цвета с силой в альфе доходят до блока свойств.
