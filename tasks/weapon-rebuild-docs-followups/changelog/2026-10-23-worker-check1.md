# 2026-10-23 — проверка на worker (ticket 1080, checkpoint a14ee4dc)

Код не закоммичен; снимок R bd55fb02 (Scar, MP5K, WeaponSightTable после пробных пересборок) не импортирован.

| Проверка | Итог |
|---|---|
| Компиляция | без ошибок |
| Scar: запись таблицы убрана → `BuildFull` | автосведение `SightFront` −2,0254 мм (прежняя запись −2,0254, Δ 0,002 мкм), отпечаток совпал, Z 15, запись добавлена; вертикаль на 15 м +92,54 → 0,00 мм |
| Scar: отпечаток в записи сдвинут на 1 мм → `BuildFull` | отказ «отпечаток ShotSource не совпал … «Сбросить запись» на стенде»; таблица и префаб возвращены побайтно |
| MP5K: `BuildFull` | тегов размечено 1, `_assetId` 1341743917 без изменений (нормализатору исправлять нечего — правка защитная) |
| EditMode: Sights, SightSplit, NetworkAssetIdOnDisk, GameTags, WeaponFeedback, HandsPackWeapon, Editor.Gameplay.Tests, Interaction | 398: 395 зелёных, 3 красных — все в `Tools/e2e/editmode-known-red.json` (WeaponSpreadTests ×2, HandsPackWeaponTests Scar), новых 0 |

Не проверено: кнопка стенда «Сбросить запись» глазами (UI) — на приёмке пользователем.
