Обновлено 2026-10-11. Кандидаты вне хаба (у зоны нет задачи хаба); порядок — приоритет. T = `Docs/tasks/`.

1. `menu-headset-acceptance` — шлем: скролл лучом/перетаскиванием и стиком с выключением телепорта руки, читаемость, удобство колонки, видимость «Админ» (коммит `cbf8453b8`); снимки «Обзора» по контекстам, 8–12 строк до скролла; касания после спавна бота — `T/T-32-menu-design-system.md` «Сделано», `T/T-33-tablet-overview-screen.md` «Что осталось» п.2, `Docs/UltimateXR/known-issues.md` «После спавна бота планшет перестаёт принимать касания».
2. `fingertip-miss` — пропуски касания: у `PlayerControllersCyborgAvatar` луч `UxrFingerTip` ≈30° от кости, `AvatarLoadoutTests` не ловит касание в движении; поза кончика — с avatar-grip — `Docs/ui-menu-architecture.md` «Проверка пропуска в шлеме».
3. `menu-context-warning` — «Нет префаба для текущего контекста. Меню не заспавнено» и menu-warnings (7 в аудите383): владелец выбора меню, переходные состояния; мёртвое поле `MenuInputHandler._menuHand` — `T/T-49-unity-warnings.md`, `experts/launch-infra/journal/2026-10-10-ports-383.md`.
4. `watch-headset` — часы в шлеме: вид `WristDisplay_Oval`, звук и вибрация левой руки, нотификации; киборг теперь ссылается на часы, статус теста `На_руке_часы_с_табло` unknown — `T/T-46-wrist-watch-hud.md` «Как проверить», «Что не проверено».
5. `slimui-retire` — остаток SlimUI (звук клика `MenuTheme.ClickSound`, deprecated API): инвентаризация ссылок, затем решение об удалении пака — `T/T-49-unity-warnings.md`.
6. `overview-questions` — что показывать призраку в «Обзоре»; кнопка «Калибровка» в блоке «Вы» в лобби — `T/T-33-tablet-overview-screen.md` «Открытые вопросы».

Пересечение: каркас `Tablet_Base.prefab` генерирует `MenuKitBuilder` — правка `haptics-system/item-clicks` должна пережить пересборку.
