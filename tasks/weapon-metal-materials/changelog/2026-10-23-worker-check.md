# Проверка в worker, 2026-10-23

База `efeac3fc2fbb7da9afd43eb77c688e211971d5ef`; коммит изменений `9fd92a48c`.

| Тикет | Вход (checkpoint) | Что | Итог |
|---|---|---|---|
| 1099 | `e0fa46698` | компиляция без ошибок; `RepackAll` + кадры Lobby до/после | result `24e6f5770`, 20 PNG, receive |
| 1102 | `592ed6095` | просмотр пользователем в Lobby, подбор `_BaseColor` в памяти | без изменений; принято 0,055 / ×1,6 |
| 1105 | `7d97f8d6d` | `tools/apply_base_tint.cs` | 13 `.mat`, по строке `_BaseColor`, receive |
| 1108 | `574b63bda` | EditMode `KinemationMaterialsTests` 3/3 passed; Play Lobby (PlayLaunch host) — пользователь «принято» | finish отказал: чужой dirty `M_AK105_Body.mat` после Play; закрыто `recover --force` |
| 1110 | `ab7f5b281` | импорт для `.meta` теста | `KinemationMaterialsTests.cs.meta`, receive |

Приёмка пользователем: вид `M_*` в арсенале Lobby (Scene view) и в Play — «принято».
Наблюдение: после Play в Lobby `M_AK105_Body.mat` в worker помечен dirty (источник не найден; явной записи в
общие материалы в `Assets/Scripts`/`Assets/Editor` нет).

## После вливания 23212444a

| Тикет | Вход | Что | Итог |
|---|---|---|---|
| 1112 | `4d60deb8c` | компиляция `23212444a` (IronKitMaterials) | без ошибок (`compilation_failed=false`, консоль 0) |
| 1113 | `23212444a` | request-base | FAILED: restore Lobby — «Несохранённые сцены/ассеты»; закрыт сопровождающим `recover --force` |
| 1115 | `2fd6b3983` | диагностика: Lobby + пауза | dirty `M_AK105_Body/Rail`, `M_R08_Body`, `M_TR15_Ironsights`, `M_SRM-12_Body`; единственное расхождение память/файл — `_Color` := `_BaseColor` ([tools/dirty_probe.cs](../tools/dirty_probe.cs)); `recover --force` |
| 1117 | `16cfaf4a3` | `_Color` = `_BaseColor` в 13 `.mat`; повторно Offline → Lobby + 10 с | 0 dirty-материалов, сцена чистая; receive 13 `.mat` |

Причина: у 13 материалов поднят `_BaseColor`, а легаси-зеркало `_Color` осталось 1; загрузка сцены синхронизирует
`_Color` и помечает материал dirty, worker не может вернуть сцену. Вид не меняется (URP Lit рисует по `_BaseColor`).
