# Фиксированные опорные точки вместо площади

2026-10-15: пользователь отменил ручную разметку/площадь как критерий. По одной материальной точке
на ладонной коже около середины каждой фаланги; допуск2мм к поверхности предмета. Одна кисть —
источник, вторая — проверенное inferred зеркало. Это не разрешение на Apply игровых поз.

До изменений сохранены24файла площади с SHA256 в drafts/contact-area-20261015: masks/coverage,
старое окно/capture, прежняя IK-ветка, контракт и инструменты. Существующие отчёты735/740/PNG сохранены.
Черновое area окно перенесено в меню «Черновики»; типы сохранены для восстановления/backcompat.

Новые ContactPoints: независимый от предмета reference capture, mid-segment palmar ray, фиксированный
Source/submesh/original triangle+barycentric, mirror между анатомическими рамками, JSON/PNG и окно
«Опорные точки контакта». Missing/stale/duplicates/невалидная геометрия не дают PASS.
Нулевая площадь object faces исключается из nearest query с явным счётчиком; sign unknown=null.
Measure читает текущий BakeMesh и оценивает только фиксированные точки; legacy area/rules не запускаются.
CreateIkBindings передаёт весь проверенный набор ожидаемых фаланг; автоматический review не подделывается.

В существующий editor IK добавлен явный FixedPointBand criterion без XYZ/normal attraction и area
objective/gates. Fullskin collision/reliability/review — отдельные guards; TargetsReached≠Solved.
При достигнутой полосе numerical stop не тратит budget на отсутствие человеческого review.

Offline42sources/406refs с двумя C#6smoke bodies:0errors/0warnings, unchanged sources.
Managed: core51/51, mirror21/21 (после4исправленных дефектов), pointIK31/31+legacy12/12+preview16/16.
UI isolatedcompile7sources/406refs0/0. Это не Unity/BakeMesh/игра/Quest приёмка.
749DONE/received: input9ccf8af1c2bf82f458f65ff2f87eca689d99b607, base018e63d4fcc8c97355165474f0e080abd34aa79a,
R79ce8464d5c3b35cbc574479289e1716f6622fa1. Изменены только4Unity-generated meta; unexpected=[];
19native artifacts скопированы из архива с проверкой SHA256. Правая14/15 (thumb/proximal missing),
зеркало11/15 (дополнительно thumb/distal и ambiguous index/proximal, little/proximal). Полный набор не готов.
Existing HandMeshCaptureTests3/3PASS. Своё окно закрыто, preview leftovers0; worker освобождён.
Отчёт с реальными изображениями: reports/phalanx-contact-unity/749-Review.md.

Пользователь уточнил порядок: не создавать/переписывать никакие тесты нового или изменённого
функционала, включая временные smoke/managed probes, до его проверки качества. Правило расширено
в корневом AGENTS.md; ранее сделанные пробы остаются историческими результатами, не приёмкой.
Pistol support hand отложена: её контакт с ведущей кистью рассматривается после подтверждения
хороших хватов основного метода; в текущую оценку её не включать.
Native plan tools/phalanx-contact-unity-plan.json; передача исправленного предпросмотра и accepted commit ещё не выполнены.
