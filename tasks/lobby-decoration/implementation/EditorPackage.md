# Пакет выполнения в основном Unity Editor

09.10.2026. Подготовлен, ещё не выполнен. Main Editor уже открыт; worker не использовать.

1. Из main checkout проверить `python Tools/agents/coordination.py guard --token
   f1881ad91e72423a89ebce00fdb48880` (после истечения принципал обновляет допуск).
   Подтвердить ветку lobby-decoration. Сохранить свои dirty assets/scene, выйти из
   Play/prefab stage, завершить чужие проверки. Импортировать только новые файлы задачи.
2. Дождаться компиляции; Console errors записать. Открыть только Assets/Scenes/Lobby.unity.
   Меню Tools/VR Battlegrounds/Lobby Decoration/1. Снять исходные измерения.
3. До сборки запустить существующие LobbyLayoutTests, ShootingTargetTests,
   MapAlignmentTests; сохранить фактические passed/failed и10 примеров в reports/implementation/.
   Меню 3. Проверить Android compilation — результат android-compile.json с Passed/Errors.
4. Просмотреть размеры/материалы/LOD исходных моделей в preview. Особенно проверить
   SilverFir Bark/Atlas и отсутствие InternalErrorShader/неразрешённых material-version
   subassets. Только затем меню 2. Собрать прототип. Оно изменяет/сохраняет Lobby,
   собственный FieldCamp prefab,4 материала, OpenCanopy mesh и Profile.
5. В Play проверить оба арсенала закрытыми и раскрытыми, свободный центр, видимость
   10 мишеней и мониторов из двух направлений. Снять картинки с двух концов арены.
   Не давать штатным агентам авторить worker вместо этого Editor.
6. На Ambience директоре вызвать PreviewEpisode с Helicopter/Truck/Gunfire/Soldiers
   (через MCP execute_code только после project/info main, или Inspector debug tool).
   Для голоса и шагов отдельно выбрать вариант в profile временно, восстановив оба.
   Проверить тихий forest loop, узнаваемость/направление/дистанцию, паузы и отсутствие
   наложения эпизодов; новый ambience имеет ровно2 источника.
7. Отключить/включить Ambience, затем Lobby → другая карта → Lobby штатным MapLoader.
   Звук не остаётся после выхода и coroutine/AudioSource не дублируются.
8. Выйти из Play, сохранить сцену. Меню4. Перезапечь каталог; menu5. Штатная окклюзия
   только Lobby. Каталог/окклюзия сохраняют actual результаты в reports/implementation/.
   Повторить existing checks и Android compile, сравнить с baseline.
9. Quest2/3 — одинаковый режим до/после: CPU/GPU frame time, память, draw calls,
   AudioSource и shadow casters. Desktop/compile не заменяет Quest результаты.
10. Предъявить пользователю прототип/снимки/звук и actual logs. До явного принятия
    не менять игровые тесты и не создавать принятый коммит/пуш.

Уже выполненная offline проверка: Roslyn компилирует4 новых C# файла against установленной
Unity версии и текущих project DLL; exit0. Это проверка API/синтаксиса, не Unity/Android PASS.
Повтор: powershell -NoProfile -ExecutionPolicy Bypass -File
tasks/lobby-decoration/implementation/offline_compile.ps1 (policy меняется только для процесса).
