# Продолжение реструктуризации префабов — план 2026-10-18

Цель и инварианты: [контракт](../contracts/cyborg-prefab-hierarchy-v2.md).
Исполнение: главный агент, ограниченные аудиты и непересекающиеся правки субагентов.
Новые тесты до пользовательской проверки запрещены AGENTS.md.

- [x] Rebase на origin/dev 8dfccb580, оба WIP-коммита сохранены.
- [x] Статически сверить Unity/UXR цепочки, MEF graft, poses и входящие ссылки.
- [x] Обновить собственный маршрут и разобрать inbox. Mode off подтверждён.
- [x] Подготовить native Unity migration с сохранением resolved leaves до записи предков,
  резервированием собственных outputs, защитой входящих fileID и откатом при отказе.
- [x] Убрать устаревшие writer entry points, обновить canonical base и подписи workbench.
- [x] Offline-компиляция Editor assembly: 333 sources / 405 references, 0 errors / 36 warnings.
  Доказательства: `../reports/prefab-hierarchy-20261018/compile-final/compile-result.json`
  и `compile.log`; metadata прочитаны из worker, Unity не запускался.
- [x] Независимое статическое ревью: исправлен P1 — root Variant распаковывается
  повторно до снятия всей ancestry connection, nested MEF_Rig сохраняется.
- [ ] В собственной broker-аренде выполнить подготовленный пакет, получить диагностику,
  сразу finish/receive; сохранить реальные результаты отдельно от статического аудита.
- [ ] Проверка качества пользователем в Unity/шлеме. Затем актуализация тестов,
  ревью и принятый commit/integration.

Обновление 2026-10-18: MCP восстановлен. Native781 завершён с отказом Convert root
preview scene; rollback подтверждён, finish/receive завершены, префабы не менялись.
Реально подтверждены65поз и2489входящих PPtr. Причина найдена в UnityCsReference6000.4:
PrefabUtility.cs2384–2388 отклоняет любой root preview scene независимо от unpack.
Исправление: временный host только для собственного transient snapshot, возврат
parent/TRS/sibling в finally перед Save. Offline compile333/405:0errors36warnings.
Далее новый input/ticket; повтор старой операции не выполнялся.

Исторический блокер Unity до восстановления: инструменты Unity MCP отсутствовали.
Canonical infra_issue #8986 направлен agent-coordination-protocol; route-resolve:
recipient_cleared, native endpoint не подтверждён. Самостоятельный ремонт/обход запрещён.
Runtime VERSION bcd591040c02dff494b8fcff63d63c4c8260df14. Заявок Unity не подавали.
