# Четыре сборочных checkout прогреты; reader UXR исправлен

2026-10-15. Матрица завершена на runtime SHA74bd42f5760b5e3f04d2e886a5685a2f52ff3d1d.
Все8GameBuilder PASS; UXR748f8ac1c2e18298,9681id/359files во всех проходах.

| Проход | Builder Android/Win64 | Pipeline exit | Время пары |
|---|---|---|---|
| Test cold | PASS/PASS | 1 — исходный дефект consumer | 90мин22с |
| Test repeat | PASS/PASS | 0 | 14мин11с |
| Prod cold | PASS/PASS | 0 | 85мин28с |
| Prod repeat | PASS/PASS | 0 | 17мин45с |

Первый pipeline отказал после успешных сборок: Read-UxrIdHash ожидал заголовок вместо
канонического raw16hex первой строки. Минимально исправлен consumer; writer/manifest не меняются.
Fixture18cases: RED15/18 → GREEN18/18, principal18/18; независимое review clean,
AST reader9/9 и реальный artifact проверены. Последующие3реальные pipeline exit0 на caller
AB74B435E3AA879AC447AA76CCD8EE34FE740FC164EDFD9A95B60378BA49C1BD.

Все8полных native logs сверены SHA256 с исходными логами. Сохранены команды/exit, UTC времена,
manifest/artifact hashes и exact generated diff. Prod cold230files и Test/Prod repeat464files
архивированы, каждый source/copy SHA256 совпал. Test cold binaries отдельно до repeat не
копировались; их full logs/manifest/metadata hashes доступны. Отчёты/артефакты вне Git:
reports/build-checkouts-validation/{matrix-result.json,final-checkout-state.json,artifacts/}.

После terminal/freshfree/no-lock восстановлены только свои catalog/UPM-lock/Win64URP generated
diff; все4checkout clean на74bd, Library/Build сохранены. Test Library creation times и
platform-state hashes до/после Prod совпали. Новых брокеров, резервирований и cache deletion нет.
По решению пользователя ordinary builds идут без брокера; busy/unknown требует проверки
через20–30мин. Physical Quest/runtime/GPU/FPS gate этой матрицей не закрывается.

Принятый DevTools6669db54f остаётся отдельным объёмом; его129/129 EditMode и публикация737DONE
не подменяют приёмку этого reader fix. Пользователь15.10 отдельно принял reader,9новых проверок
и отчётность матрицы, поручив коммит и штатное FF-вливание в origin/dev.

Выпуск: e83af8413de0ebd726df27a7321f23131c9cf073, FF от98eed6c1c, remoteSHA подтверждён.
Все8путей после последнего doc-onlyrebase побайтово равны принятому05b1b5b33.
Итоговая fixture18/18, EOLrange issues0, readerSHA256 совпал с3реальными успешными pipeline.
Localdev mirror обновлён без переключения пользовательского checkout.
