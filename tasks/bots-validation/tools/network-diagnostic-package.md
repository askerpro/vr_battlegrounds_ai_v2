# Подготовленный пакет сети, 2026-10-18

Пользователь подтвердил оба шага: helper/reaggregation и сетевой trace.
Новых/переписанных тестов до userQA не добавлять. Техническая проверка не принимает продукт.

Unity input `5877d8825edd6a77fe14a8a4f23839fc067835fd`, ticket829.
Base/HEAD/origin/dev после fetch: `2e1b6a2fc342e894d4b8526287b804fa5e3093b0`.
Новый `.cs.meta` диагностического editor helper получает только Unity;
единственный разрешённый serialized output — этот `.meta`.

1. Штатные watch-ticket → claim → begin → guard. Не вмешиваться в предшествующий828.
2. Подтвердить завершённую Editor-компиляцию; существующий AndroidCompileGate.Run,
   сохранить результат до return. При неизвестном исходе не повторять.
3. Два случая из external-diagnostic-cases.json через PlayLaunch Plan→Play→Status:
   discovery8, затем localhost8. Один Play на случай, замороженный PlanHash.
   Прежние роли/seed/timeout/readiness; добавлен только диагностический флаг.
4. Сохранять статусы, roster и полные per-process логи, startup-<pid>.jsonl;
   raw UTC/Stopwatch метки и интервалы между завершениями NetworkEarlyUpdate
   не являются performance baseline или тестовым PASS.
5. После каждого случая штатный Cancel/Stop, CleanupPassed/Playing/Owner,
   архив логов; не держать Play ради анализа. После всего пакета guard idle и
   немедленные finish/receive, включая неуспешный результат. Затем анализ.
6. Сохранить bridge journal соответствующего интервала read-only. SDK/worker
   инфраструктуру не менять. Невозможность чтения сообщить сопровождающему.

Существующие локальные проверки .NET не исполнились: ProtocolHarness не включает
E2EDiagnosticSession/E2EDiagnosticInput, EditorBinding stubs не содержат kcp2k и
TransportError. Fixtures/expectations не менялись. Их отказы не доказывают отказ
компиляции продукта; отдельная проверка в Unity обязательна.
