# Контракт weapon-equipment-binding@1 согласован с weapon-system

Хаб: контракт `weapon-equipment-binding`, владелец `bots-fix`, ревизия 1 — agreed 2026-10-08,
ACK bots-fix и weapon-system; providing_stage `equipment-foundation`, compatibility additive,
structural. Суть: WeaponEquipmentBinding на корне оружия (read-only IsActive, Owner, OwnerAlive, Epoch),
сервер — единственный писатель, SDK-хват и экипировка взаимоисключающие, один серверный snapshot,
вход подготовки — только RequestAutomationPreparation; readiness и Resolve — у weapon-system.
Связанные контракты weapon-system: `weapon-use-context` r2 и `weapon-equipment-shooter` r1 — agreed.

Состояние доказательств по контракту: source SOURCE APPROVED, локальная компиляция PASS; прогон303
(input `8bee2f3645ae7bd31ef110ecddd1572b49cde17e`) — 64/64 equipment/unarmed checks PASS, без стрельбы,
remote и Quest; дата прогона в источнике не указана. Активация (active) — после принятой foundation.

Источники: hub status (contracts[]); `tasks/bots-fix/Details.md` (копия владельца) — начало файла,
«Контракты и зависимости».
