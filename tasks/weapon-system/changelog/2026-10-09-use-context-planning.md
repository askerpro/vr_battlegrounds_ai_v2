# 2026-10-09 — планирование use-context

- Пользователь поручил запланировать backlog `use-context` и передать штатному субагенту.
- Сверены согласованные `weapon-use-context@1`/`weapon-equipment-binding@1` и запросы bots-fix 2085/2185.
- Read-only аудит установил VR-only sensor/AutomationContext и отсутствие binding в checkout владельца оружия.
  `WeaponEquipmentBinding` находится только в непринятом исходнике bots-fix; copying/activation не выполнялись.
- Добавлен документальный `use-context-planning`, зарегистрирован revision26, допуск epoch1.
  Игровой этап сохраняет `ejection-manual:merged` и `binding@1:active`; Core и `.meta` уточнены в writes.
- Ответ в inbox bots-fix **2246**: запрос точного source-пакета, joint DEVELOPMENT preview и owner-side policy.
  Native peer listing/Monitor отсутствуют, автоматическое уведомление не заявляется.
- `patch-reserve --sdk UltimateXR --minimum 63 --key use-context` вернул **70**.
  Это резервация, не выполненный SDK-патч; исторический 63 не считается проверенным номером этого ключа.
- Пользователь выбрал SDK `IUxrFirearmRootRecoilPolicy`, provider в binding: `!IsActive`.
  Решение записано в `Docs/weapons/decisions.md`; owner-side согласование bots-fix ожидается.
  Provider в WeaponSystem не добавляется. Планирование перерегистрировано как revision27, допуск epoch3.
- Preflight выявил отдельный барьер VR grant: SDK grab/release с `propagateEvents=false` не закрываются
  обычными событиями. До реализации нужен доказанный полный grab-lifetime, не постоянный VR sentinel.
- Подготовлен [план и бриф](../use-context.md). Следующий шаг — preflight исполнителя и проверка principal.
  Код, assets, gameplay-тесты, Unity и production activation в этом пакете не изменяются.
- Исполнитель: штатный `/root/use_context_implementer`, свежий контекст, `gpt-6.1-sol`, medium.
  Сейчас запись только в `reports/use-context-implementation/preflight.md`; реализация назначена ему
  для followup после подтверждения зависимостей, выбора policy и продуктового допуска.
- Preflight исполнителя прочитан principal и сопоставлен с исходным аудитом: источник silent VR grab-lifetime
  не доказан, очередь сейчас Drain-before-loss, binding API находится только у bots-fix. Продуктовые проверки
  не запускались. Этот отчёт — подготовка, не реализация; SDK/root providers ещё не существуют.
- `weapon-use-context@2` предложен в хабе, ACK владельца weapon-system записан; agree/active не выполнены.
  Bots-fix получил выбранную policy и запрос точного ACK/owner-side slice, событие **2265**.
