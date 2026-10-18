# bots-fix: приёмка удержания оружия в Editor

2026-10-18. Зона `manipulation`: SDK UxrGrabbableObject,
UxrGrabbableObjectAnchor и UxrGrabPointInfo; карта — `expert.json`.
Проверенный input `5489395093f9f36c89b79df95e1b3c2de561583b`.

Пользователь принял наблюдавшееся удержание оружия и стрельбу в локальном Editor
и разрешил коммит/вливание. Worker808: TestMap1, Host, 3 бота.
G805 того же input: ghost shots/grabs=0 и завершённый cleanup;
HandFit всех четырёх кистей `analysis_incomplete`, скрытый контакт не доказан.

Полная приёмка физического взаимодействия, Quest и внешней сети этим не заявлена;
оставшееся покрытие сохранено в [следующей итерации](../../../tasks/bots-fix/next-iteration.md).
[Решение и evidence](../../../tasks/bots-fix/changelog/2026-10-18-user-acceptance.md).
Журнал не заменяет zone review/ACK и не подтверждает публикацию.
