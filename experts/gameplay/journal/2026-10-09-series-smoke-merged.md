# EditMode-смок серии из двух карт влит; живой series-smoke-e2e остаётся планом

Этап `map-runtime-bootstrap/series-smoke`: merged `f30643081e768a9337e9945df1b7b9fc2b9ab921`; тест добавлен
коммитом `cc8710d91` — `SeriesSmokeTests.Серия_из_двух_карт_от_старта_до_лобби`: настоящие Series, MapReferee,
EliminationMode; разминка → все фазы раунда → смена сторон → конец карты → «Следующая карта» → Closing/Retire →
вторая карта с сохранённым счётом → лобби. Проверено на worker (аренда 281): группа Modes 84/84.

Ограничения: нет аватаров, рендеров и Mirror между процессами — дефекты тел и сети (например, регрессия
рендереров Cyborg, задача avatar-renderer-regression) не ловит. Это закрывает план `series-smoke-e2e`
(planned; сценарий — gameplay, каркас и механизм шумов — launch-infra).

Источники: `tasks/map-runtime-bootstrap/Details.md` «Этапы `series-smoke` и `series-smoke-e2e`…»; хаб.
