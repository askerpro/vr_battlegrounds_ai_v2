# Уборка хватов: документация автоанализа, экспортёра и редактора поз

2026-10-20. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/grip-docs-cleanup` (схема «задача = агент»).

## Цель

Перед закрытием hand-rig-quality разложить по Docs/ знания трёх направлений работ по хватам: (1) автоматический анализ качества хвата, (3) экспорт хватов из оружейных паков под кисть Cyborg (Grip Extraction), (4) ручная доводка в редакторе Hand Poses (IK, «Обхватить», рентген).

## Этапы

Этапы и области записи — в [plan.json](plan.json). Заготовленный этап `work` пишет только в `tasks/grip-docs-cleanup/**`: перед `begin` объявите нужные пути (`writes`) и перерегистрируйте план (`register`).

## Этап quality-analysis

2026-10-20. Направление 1 — автоматический анализ качества хвата.

**Сделано:**

- [Docs/grip-quality-analysis.md](../../Docs/grip-quality-analysis.md) — входной документ: состав анализатора
  (снимок Fit, знаковое расстояние, подушечки, полосы SDK, авто-тип, правила gross/notable, подсказка curl,
  лист ревью, предпросмотр правки), запуск через окно и API, калибровка `calib-fbb5bd5b-01`, чтение результата,
  ограничения с уроком R2, уровни доказательства, состояние на 2026-10-20. Каждое утверждение о поведении
  сверено с кодом `Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/`.
- [Docs/grip-known-issues.md](../../Docs/grip-known-issues.md) — реестр F1–F8 (аудит MEF) и G1–G8 с аватаром
  находки (MEF native или CyborgHands), причиной, доказательством, статусом и направлением исправления.
- Шапки со ссылками в `Docs/hand-pose-fit-tool.md` и `Docs/hand-pose-fit-analysis.md`; строка в
  `experts/avatar-grip/AGENT.md` (Details).

**Проверено статически на dev `ade7d06ef`** (чтение YAML `UxrGrabbableObject`, прямые записи и переопределения
вариантов; GUID базы CyborgHandsBase `71b74b0d08444f49bbb7aa2828d0890a`):

- записи CyborgHandsBase с позой и align есть у SDK Gun, Machinegun, Shotgun, их магазинов, Grenade и обеих
  ручек `Tablet_Base`; у `DogTagPanel` записей нет ни для одного аватара;
- пустая поза затворов у 11 стволов; общие позы SDK у проверенных магазинов (AR15, BrowningHiPower,
  патрон FabarmSDASS), цевья AR15 и помпы FabarmSDASS;
- данные хвата `MKR9.prefab` не менялись с 2026-10-14; `_localDisabledGameObjects` у
  `PlayerControllersCyborgAvatar` задан (3 объекта); этот аватар — вариант CyborgHandsBase;
- отказы `GrabPoseCoverageTests` прогона 772 разобраны по отчёту (43 из 56 отказов).

**Не перепроверено** (Unity, Play и шлем не запускались): геометрия хватов на CyborgHandsBase, прохождение
`GrabPoseCoverageTests`, `RegisteredAvatarBodyTests` и `CorpseTests` после реструктуризации 2026-10-18,
совпадение записей 9 стволов R2 в `1b2f88612` с результатом 712, захват ручек планшета в игре,
совместимость полос `contact-bands-v1` с изменённым 2026-10-16 снимком.

**Расхождения старых документов с кодом:**

- раздел «Встроенная диагностика SDK Hand Pose Editor» в `Docs/hand-pose-fit-tool.md` устарел: рентген-панель —
  заглушка (`HandPoseSdkContactDiagnostics.Enabled = false`), `HandPoseEditorCapture.Export` не вызывается;
- `Docs/hand-pose-fit-tool.md` описывает версию `mesh-fit-0.4-local-volume`, схему 2; в коде схема 4,
  `mesh-fit-0.6-grip-rules`; знаковые метрики, правила, лист ревью и предпросмотр там не описаны.
