# Контакт кистей: уточнение плана 2026-10-09

Пользователь выбрал практический кандидат: оружие ориентируется между кистями клипа;
пальцы могут оставаться из клипа. Точное соответствие SDK-позе пальцев сейчас не критерий.
В runtime Animator остаётся единственным владельцем bones, WeaponEquipmentBinding — предмета.
Heavy исключён. Нового VR-захвата, runtime IK или отдельного wrist writer не добавляем.

## Независимое ревью

`/root/clip_plan_review` вернул **APPROVED для кандидатной проверки одного постоянного fit**.
Это разрешение на диагностический probe, не доказательство правильного runtime-хвата.
Условия ревью: primary palm position зафиксирована; raw SDK align marker пересчитывается
через исходный grabber → ладонь; масштаб учитывается; SDK angular equality не требуется.
Один поворот не исправляет несовместимую длину. Нужны все states, фазы и переходы,
а также независимый контроль actual ShotSource, без растяжения оружия/рук.

## Кандидат и проверка

1. Использовать объявленные роли primary/support оружия. Точные SDK entries MEF-reference
   дают исходную геометрию; перенос для Cyborg явно помечать как reference candidate,
   не выдавать за существующий Cyborg SDK grip. Не применять неявный fallback к root.
2. Из raw align pose и grabber→анатомическая ладонь bridge получить физический contact proxy
   в координатах оружия. Анатомическая ладонь для диагностики — середина между wrist
   и средним положением Index/Middle/Ring proximal. Это proxy, не доказательство mesh contact.
3. На Idle сохранить основной palm contact. Повернуть primary→support к направлению
   right-palm→left-palm, вращая оружие вокруг primary. Roll выбрать по actual ShotSource
   относительно body.forward; не направлять ствол в левую ладонь.
4. Полученный wrist-local Pose неизменен во всех samples. Измерить primary/support residual,
   изменение направления и длины между palm proxies, span mismatch, actual muzzle aim error.
   Включить Idle/Move/Back/Left/Right/Shoot и переходы. Не масштабировать оружие под руки.
5. Снять ракурсы after-fit. При несовместимости показать худший state/phase и residual;
   не переносить ошибочный fit в продукт и не добавлять runtime LookAt как скрытую компенсацию.

## Порядок исполнения

Сначала read-only `tools/two-hand-fit-probe.cs`, offline compile, затем одна broker-транзакция
outside Play с cleanup/finish перед анализом. После maintenance-pause1812 Unity не вызываем
до сообщения worker-ready. Отчёты/PNG находятся в ignored reports.
Только после фактического результата выбираем минимальное изменение существующего authoring.
Пригодность одного Pose пока не доказана. Finger fitting и массовая калибровка — другой агент.
Будущие изменения общего grip input должны регенерировать производные NPC-данные и эти проверки.
