# События смены масштаба калибровки

## Статус — 2026-10-15

Пользователь принял изменение: «считай, что я принял — вливай». Повторное подтверждение не требуется.
Ветка `avatar-ik/calibration-scale-events` на принятой базе `1812dc5bf22860e8302e858b1637e92b1df636e9`.
Код и новые тесты проверены в worker696; следующий шаг — verify/accept и штатный merge через брокер.
Отдельного отчёта о физической проверке в шлеме нет; прямое принятие пользователя не называется Quest-доказательством.

## Результат

`AvatarCalibrationApplier` остаётся единственным писателем масштаба и производных полей тела.
Экземплярные `ScaleChanging / ScaleChanged` синхронно окружают запись масштаба Dummy Forward и полей BodyIK,
при фактическом изменении больше1e-4, на каждой машине. Исключение подписчика логируется и не прерывает Apply.
Контракт `calibration-scale-events` r1 agreed; потребитель — manipulation / `grab-authority`.

После пользовательской приёмки добавлены9 тестов: порядок/состояние callbacks у своего и чужого аватара,
идемпотентность и пол, epsilon, сброс, фактический старый масштаб, исключения подписчиков и отсутствие тела.

## Проверка

Worker696/epoch591, Unity6000.4.1f1, вход `9898b546c4c031e7cd22ebb352523a1bf4b3c8b9`:
- импорт нового MonoScript с корректным32hex GUID — PASS;
- точные source/test hashes и оба загруженных типа — PASS; ошибок Console0;
- TestRunner discovery —4 класса/35 Runnable, включая9 новых;
- AndroidCompileGate — PASS, ошибок0;
- EditMode job `882e2dac869f4b1ca2f7f71ec988f5e2` —35/35 PASS, failed0/skipped0, все35 details сохранены;
- finish/receive: `576f0c633ed706a8faf774181d46d812956069d5`, paths=[], unexpected=[].

Полные отчёты — `reports/worker-696/`, вне Git. Это scoped EditMode/compile evidence, не весь проект и не Quest.
Предыдущие678/685/691 не подтверждали новые тесты. Ошибка собственного нового .cs.meta (GUID33hex)
исправлена на32hex; успешный импорт и выполнение9 новых случаев подтверждены696.

## Дальше

Verify/accept точного итогового SHA → merge-request/merge-execute → проверка origin/dev → публикация базы.
После вливания уведомить manipulation. Непринятый WIP прогрева сохранён отдельно; его live provider
Readme/plan остаются доступными и не входят в commit калибровки.

Подробности — [Details.md](Details.md), машинный план — [plan.json](plan.json),
контракт — [contracts/calibration-scale-events.spec.json](contracts/calibration-scale-events.spec.json).
