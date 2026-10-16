# Точечное сохранение Grip Extraction

Пользователь 2026-10-15 одобрил дизайн и повторный R2 Apply+Fit.
В `GripExtraction.Write` первый общий `SaveAssets` заменён на `SaveAssetIfDirty`
только для фактических поз текущего запроса; финальный общий save удалён.
Оружейные префабы и необязательный registry сохраняются существующими writers.
Загруженные weapon prefabs дополнительно не сохраняются: их UXR identities в памяти
могут отличаться от сохранённых, нормализация NetworkIdentity выполняется по диску.

Helper R2 блокирует dirty targets, фиксирует foreign dirty persistent objects
по instanceID и охраняет их dirty-флаги, байты и meta. Очистки чужих dirty-флагов нет.
Scope остаётся: 9 weapon prefabs, 17 существующих поз, без Player/Base/meta и MKR9.

Статическое ревью call graph не выявило блокеров. Unity-компиляция, существующие
проверки и фактический Apply+Fit ещё выполняются; это не игровая/Quest приёмка.
Новые тесты до пользовательской проверки не пишутся согласно AGENTS.md.

Unity701: refresh/компиляция и AndroidCompileGate PASS (0 ошибок);
существующие EditMode IDs/hand-chain 17/17 PASS, 0 FAIL, 0 SKIP.
Job `3e7a2aa929a444ca9fa2b2137e5f59b2`, полные 17 результатов сохранены.
Cleanup PASS; DONE/received R`56057601606d4a34769ec9a3db03ed91e02a19fa`, paths/unexpected пусты.
Проверки подтвердили компиляцию/идентификаторы/цепочку, авторинг и Quest остаются отдельно.

R2 Apply702 остановлен до Run: persistent dirty object без `AssetDatabase.GetAssetPath`.
Неверным было допущение helper, что каждый persistent объект имеет физический файл.
Уточнён только набор diskhash: пустые пути исключены из него; сами объекты остаются
в исходном per-instance снимке и проверке path/persistent/dirty после Run.
Все непустые foreign paths требуют file+meta и входят в hashes/unexpected.
Никакие чужие dirty-флаги не очищались. Cleanup PASS, DONE/received
R`d5881d37fe248f26999b3c3583622c6da6002da7`, paths/unexpected пусты.

703 также остановлен до Run: `Library/unity default resources` — виртуальный путь без meta.
Cleanup PASS, DONE/received R`2b1218ec1a6d8ab8dd292509035a0a1d6ea16ea5`, paths/unexpected пусты.
Уточнение guard обобщено на disk-state, без списка исключений: существующие foreign
файлы сравниваются по SHA256, отсутствующие — по null; meta проверяется тем же способом.
Появление, удаление или изменение файла даст unexpected. Строгие Measure-входы сохраняют
требование существования и прежнего hash. Pathless/virtual объекты остаются под per-instance
проверкой path/persistent/dirty. До guards сохраняется полный preflight.json с типами и путями.
