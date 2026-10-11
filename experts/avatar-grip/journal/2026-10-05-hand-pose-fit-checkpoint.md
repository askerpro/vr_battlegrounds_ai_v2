# Checkpoint анализатора хвата и SDK preview: preview MEF принят пользователем, игровой IK/Quest открыт

Коммит `f29fedc2014cb9acfe26acdc5fba9a0d6fef91a7` (2026-10-05): Editor-only анализ поверхности и
контактов, калибровка Cyborg/BigHands, MEF и исходных паков; SDK preview использует игровое
применение позы и Unity skinning (патч 45). Проверки по сообщению коммита: 63/63 native NUnit,
AndroidCompileGate PASS, Python 11/11; окружение/база прогона в сообщении не указаны — unknown.

Пользователь принял MEF preview; новая UI-приёмка и игровой IK/Quest остались открыты.
Коммит `173890fef1032b1fc268db597469b4b87cb7381e` (2026-10-05) вынес артефакты подгонки в локальные
отчёты `Docs/tasks/report/…/history` (вне Git).

Источники: `git show f29fedc2`, `git show 173890fe`; `Docs/UltimateXR/known-issues.md` «MEF:
переплетённые пальцы в grip preview при хорошем хвате в игре»; `Docs/UltimateXR/sdk-patches.md` «Патч 45».
