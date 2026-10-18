# Реструктуризация аватаров — 2026-10-18

Задача `hand-rig-quality`, этап `prefab-hierarchy`, owner `avatar-grip`.
Пользователь принял ручную проверку иерархии, поз и Play Mode и разрешил commit/merge.

PlayerBase без тела/поз → CyborgHandsBase (прежний GUID, 65 поз, без MEF_Rig)
→ параллельные Controllers и MEF player → Black. Ghost остаётся под Controllers.
Прежние MEF и SDKHands перенесены в Unused, позы сохранены по GUID; тела трупов
из исходного WIP включены в тот же срез. MEF eyes 1.72025037 и все 45 записей
body SMR сохранены. Нового writer костей, состояния хвата или сетевого authority нет.

Native remap обновил 44 потребителя: 87 полей и 43 набора overrides. Статическое
ревью 35 weapon/tablet/SDK prefab: 571 authored grip строка неизменна; 31 свой
prefab меняет только selected-avatar PPtr. Четыре SDK samples также сериализовали
defaults и Rigidbody schema; значения и ограничения описаны в SDK patch85.
Recovery807 доставлен в свой checkout, 55/55 blobs совпали; аренда завершена.

Доказательства: `tasks/hand-rig-quality/reports/prefab-hierarchy-20261018/`:
`ticket807-result.json`, `consumer-review-r9/`, `visual-r9/`.
Editor static/compile, человеческая Play-проверка и физический Quest — разные
виды доказательств. EditMode и независимое зональное ревью выполняются перед merge.
Текущий статус интеграции — бриф хаба и Readme задачи; старые этапы задачи не закрыты.
