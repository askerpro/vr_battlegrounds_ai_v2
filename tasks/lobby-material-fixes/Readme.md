# Lobby: розовые материалы и эмиссия стекла через фиксеры аудитора

2026-10-22. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/lobby-material-fixes` (схема «задача = агент»).

## Цель

Исправить в Lobby 3 материала с Hidden/InternalErrorShader (MI_Emissive, MI_TopLight, No Name) и решить эмиссию M_Glass_Inst — через правила и фиксеры аудитора ассетов.

## Этапы

Этапы и области записи — в [plan.json](plan.json).

## Находки

- **Розовые материалы.** MI_Emissive, MI_TopLight (RuralTown) и No Name (PostApocalypticTown) — HDRP Lit: ссылка на шейдер
  `6e4ae4064600d784cac1e41a9e6f2e59` (HDRP/Lit), которого в URP-проекте нет. Так же сломаны MI_ElectricPole, MI_Lamp,
  MI_LampPost, MI_Lamp_02 того же пака (всего 7). В Lobby они **не рисуются**: назначены только в FBX через
  externalObjects, а игровые префабы (SM_TopLight, SM_Light2) ставят свои URP-материалы. Worker, ticket 1011: 0 слотов
  рендереров Lobby с InternalErrorShader. Аудитор видит их через `AssetDatabase.GetDependencies` сцены → FBX.
- **M_Glass_Inst.** URP Lit, прозрачный; `_EmissionColor` белый, GI `EmissiveIsBlack`, `_EMISSION` и карты эмиссии нет.
  В HDRP-источнике `_EmissiveColor` чёрный, лампа — вариант «Light Off» (PortableWorkLamp на верстаке WarehouseZone,
  3 слота). Белый цвет — остаток конвертации HDRP→URP, стекло в паке не светилось: правило `material.emission-off`
  срабатывало ложно, EmissionOffFixer сделал бы стекло белым светящимся.

## Решения пользователя (2026-10-22)

- Все 7 розовых HDRP-материалов — фиксером в Baked Lit (скриншот моделей после фикса принят).
- Стекло не трогать; правило уточнить. Предложенный критерий «`EmissiveIsBlack` без `_EmissionMap`» в прогоне по проекту
  (ticket 1015) снимал и задуманное свечение — WarmLampDiffuser (тёплый HDR), OwnerIndicator (бирюзовый); агент сузил его
  до белого цвета (остаток конвертации HDRP→URP): так перестают ловиться M_Glass_Inst и M_Glass_Bottle_Inst пака.
  Пользователь подтвердил суженный критерий (2026-10-22: «прими рекомендуемые действия, которые не требуют кропотливой
  поддержки в будущем»). WarmLampDiffuser и OwnerIndicator остаются находками правила — вне объёма задачи.

## Изменения

- `BakedLitMaterialFixer`: источник HDRP Lit без шейдера (InternalErrorShader + сохранённая `_BaseColorMap`) → Baked Lit по
  сохранённым свойствам (`_BaseColorMap` + тайлинг, `_BaseColor`, `_AlphaCutoffEnable/_AlphaCutoff`, `_DoubleSidedEnable`,
  `_EmissiveColor/_EmissiveColorMap` → Simple Lit); переводится и без рендереров в открытых сценах; откат пропускается
  (GUID исходного шейдера — в userData `missingShaderGuid`).
- `ShaderNotMobileRule`: находка «шейдер с ошибкой» отдаёт материал фиксеру (`WithFixTarget`, группа `shader:(ошибка)`),
  различает HDRP Lit.
- `EmissionOffRule`: `EmissiveIsBlack` с белым цветом без карты эмиссии — не находка.
- Манифест: QPM-SHADER-004, QPM-SHADER-006, журнал правил.
- Тесты `MaterialFixerTests`: `EmissionOff_IgnoresConversionWhiteWithoutMap`, `BakedLitFixer_ConvertsPinkHdrpLit_EvenWhenNotDrawn`.

## Проверки

- Worker ticket 1011 (2026-10-22): скриншоты стекла до/после эмиссии (временная копия материала), прогон фиксера по 7
  материалам — все applied; аренда истекла во время finish, закрыта `recover --force` (посторонний dirty-материал от
  временной сцены). Отчёты: `reports/lobby-material-fixes/` (вне Git).
- Ticket 1015: фиксер снова 7/7, Lobby — 0 розовых находок; прогон по проекту показал, что широкий критерий правила
  эмиссии снимает задуманное свечение (WarmLampDiffuser, OwnerIndicator) — критерий сужен до белого цвета. Аренда истекла
  во время прогона (без renew), закрыта `recover --force`; результат не доставлялся.
- Ticket 1022 (вход 8568e5ce5, база 30b90e08e): фиксер 7/7 applied (3 материала Lobby — после открытия Lobby: без открытой
  сцены фиксер пропускает материалы, используемые неоткрытой сценой сборки); папки паков и Assets/Materials/Lobby,
  Assets/Art/ArsenalStation: розовых 0, `material.emission-off` ловит WarmLampDiffuser и OwnerIndicator, M_Glass_Inst — нет.
  EditMode `VrBattlegrounds.Tests.AssetAudit`: 22/22 passed (вкл. `EmissionOff_IgnoresConversionWhiteWithoutMap`,
  `BakedLitFixer_ConvertsPinkHdrpLit_EvenWhenNotDrawn`). finish accepted, receive — 14 путей .mat/.meta. Коммит 490211fb9,
  после rebase на a1b487666 — 781cf543d.
- Ревью раунд 1 (781cf543d): performance — approve (замечания: writes, проверки в Readme, подтверждение суженного критерия,
  HDR-белый не учитывается сознательно); level-design — changes (writes этапа не объявлены). Writes объявлены (plan.json, ревизия 2).
- Heavy editor default: ticket 1025 (вход 69bbd6a95 = 781cf543d) — прогон остановлен решением пользователя 2026-10-22
  («heavy не нужен для твоей задачи, считай что пройдено») после scripted-input-smoke и scripted-wall-purchase (оба дошли до
  E2EFinished); verdict.json не получен. Изменения — Editor-код аудитора и 7 материалов, не рисуемых ни в одной сцене.
