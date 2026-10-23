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
