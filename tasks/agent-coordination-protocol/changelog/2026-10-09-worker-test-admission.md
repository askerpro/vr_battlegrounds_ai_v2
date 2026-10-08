# Обязательная подготовка тестов worker

Agent-infra 46abc0bbc97aa76499e9dcf4cc9c089974f112f9, два изменённых файла:
editor_broker/mcp_gate.py и tests/test_editor_broker_mcp_gate.py.

run_tests запрещён до discard_untitled при Play/compile/import/tests или ready=False.
Terminal отказ обязательной подготовки возвращает structured deny с исходной причиной,
не переводит RUNNING и не пересылает тестовый RPC. Timeout и неопределённый ответ
не подавляются; повторов операции нет.

RED: 7 ожидаемых subtest failures. GREEN: gate 12/12; весь набор — 305 проверок,
299 passed, 6 skipped, 0 failures/errors, 538,683 с. Skips: symlink-ограничение ОС
и пять proxy transport тестов без mcp. Независимое ревью без Critical/Important.

Deployment ожидает штатного завершения аренды 255. Это техническое исправление
Python gate, а не доказательство полного cleanup, отсутствия modal или игровой приёмки.
