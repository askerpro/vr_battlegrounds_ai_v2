# Исполнение Details.md — lobby-decoration

09.10.2026. База документации d274fa9361463a0804b255b7e581831415e6a978.

Ruling: код/ассеты подготавливаются offline, пока main Editor не доступен через MCP;
worker не используется без собственной аренды. Цена решения: живой прототип и техническая
приёмка остаются незавершёнными, а не заменяются данными другого checkout.

Ruling: проектный запрет игровых тестов до приёмки приоритетнее TDD-рецепта executing-plans.
Тесты не написаны/переписаны; принятый коммит/пуш не создавался.

Шаг 2: guard/checkout/read-only serialized baseline выполнены; живые bounds,
компиляция Editor и существующие Unity tests не выполнены. Подробности LayoutMeasurements.md.

Шаг 3: GLB world transforms (TRS/matrix), normalized quantized accessors и material groups
преобразованы в OBJ. Треугольники исходного шатра: 8+316+200+96+60; мешки468.
Нормали пересчитываются по world-space граням (исходники без текстур); мебель/растяжки
не используются в центральном прототипе. Один мешок12tris, 0.4201×0.1902×0.6201 м.
Выбраны ящик, генератор и SilverFir с Bark/Atlas: 40 copied/reused зависимостей.
GUID существующих адаптированных материалов сохраняется; существующие файлы не меняются.
Два unresolved GUID относятся к служебным material-version MonoBehaviour в исходных
.mat, не к texture/shader ссылкам. Это нужно проверить при реальном импорте Unity.
Импортированная область содержит около345 МБ исходников (текстуры/модели), не runtime memory.
Build ограничивает импорт своих текстур1024, тени декора отключены, деревья имеют cull LOD.
Визуал/shader/submesh/LOD/triangle budget в Unity ещё не проверены.

Шаг 4: подготовлен воспроизводимый авторинг, сцена пока не изменена.
Пакет обновляет только Environment/LobbyDecoration и существующий Ambience;
старый renderer Ceiling отключается только на экземпляре Lobby, физика сохраняется.
Новых интерактивных компонентов/коллайдеров/Ground/телепортации/NPC не создаётся.

Шаг 5: Profile asset и6 WAV подготовлены: лес stereo16.422с, helicopter mono9.256с,
truck mono19.091с, gunfire mono2с (НЧ/приглушение), steps mono2.88с, voice mono0.493с.
Три runtime класса: один директор/cooldown/пауза18–35с, один фон, один emitter;
четыре типа, голос альтернативен шагам, не вторым слоем. Движение/затухание только
локальные; нет RPC/сетевой записи. Повторное включение и смена активной сцены останавливают
старую coroutine/звук. Прослушивание и runtime lifecycle ещё не проверены.

Шаг 6: offline Roslyn C# compile четырёх файлов с Unity6000.4.1f1 и текущими project DLL
PASS (exit0); GUID scan новой области PASS (0duplicates). Это не Unity project compile,
AndroidCompileGate, существующие tests или Quest runtime. Пакет содержит отдельные меню
AndroidCompileGate.Run(), RebakeCatalog(out passed), штатный BakeScene только Lobby.
Рефлексия BakeScene останавливается при изменении API, Run(all maps) не вызывается.

Доказательства: reports/implementation/{converted-models.json,audio-manifest.json,
catalog-manifest.json,offline-integrity.json,offline-compile.txt,offline-compile.rsp}.
Исходные provenance/licence: reports/acquired-assets/manifest.json.

Поправка namespace: VrBattlegrounds.Environment.Audio затенял System.Environment в
соседнем коде VrBattlegrounds, вызывая CS0234 у StackTrace/GetCommandLineArgs.
Три новых класса переименованы в namespace VrBattlegrounds.LobbyAudio; обновлён
только using собственного authoring. Пути/GUID сохранены, чужие вызовы не менялись.
Свежий offline subset compile exit0; этот набор не компилирует всю игровую сборку.
