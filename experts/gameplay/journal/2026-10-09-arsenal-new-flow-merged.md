# Новый флоу арсенала на всех картах принят пользователем в Unity и влит

Этап `arsenal-generator/composer-presentation`: merged `eb0f09065ac9f29f7f73d3f78106a5e5cf39b8e3`; код
`bef20c5d03ccb66b0b81e3e69b0e48a3c75e760c`, данные всех карт `4107a18bf1d47f0b1294ea9df782eacfc8744135`.
Принято пользователем в Unity 2026-10-09. Решения: авторский формат станций удалён, обратной совместимости нет;
позы — в раскладке слота `ArsenalSlotLayout` (умолчание у префаба слота, своя у оружия); генератор не знает
размеров оружия.

Проверено: аренда 266 — Play-проба 26/26 (Lobby 25 слотов, Common 10), хост Lobby → TestMap1 → TestMap1 →
Lobby без ошибок, Android PASS, окклюзия всех карт; аренды 275–276 — тесты арсенала, экономики и адаптера 149
без отказов задачи, остаток прогона 727 с чужими отказами.

Ограничения: шлем, два клиента и поздний вход — этап `acceptance` (planned). `ArsenalMapGeometryTests` TestMap1:
`TrayFrontLip` пересекает `LD_Beam_Low` на 1,3–1,5 мм — было до задачи.

Источники: `tasks/arsenal-generator/Readme.md` «Статус», «Проверка»; `Details.md`; отчёт (вне Git) `tasks/arsenal-generator/reports/new-flow/lease266-report.md`; хаб.
