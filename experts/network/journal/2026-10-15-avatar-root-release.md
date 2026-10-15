# Сетевой пакет с AvatarRoot admission принят пользователем к публикации

2026-10-15, network / Codex. Пользователь: «коммить и вливай» после отчёта о написанном коде и его проверках.
Решение разрешает интеграцию общего сетевого пакета; физическая Quest/gameplay-проверка не заявлялась.

- Реализован допуск root-события по Mirror netId + текущему SDK UniqueId до декодирования. После decode
  сервер сверяет фактическую цель; SDK execution и число decode сохранены. Детали и границы v1 —
  [контракт](../../../tasks/network-sync-hardening/contracts/avatar-root-event-admission-v1.md).
- Source-review без production blockers. Worker713: Unity/Android PASS; Windows E2E GREEN/enforce24/24.
  Устаревшее ожидание хэша существующего RemoteCallHashTests обновлено под Cmd(byte[],uint,Guid,sender):43519.
  Хэш и точная woven-сигнатура подтверждены по DLL уже проверенной сборки; историческая коллизия сохранена.
- Дополнительный повтор Network/Weapons — input0b06728381da4280d074f6c1165cf3158443f242, тикет717 —
  отменён в QUEUED после длительного ожидания первого импорта CLI-пилота716. Claim/begin не было.
  Интеграция по прямому разрешению пользователя с известной областью713; нового NUnit GREEN нет.
  Текущий статус и результат публикации точного SHA — в хабе network-sync-hardening/precache-server-guard.
  Полные локальные доказательства — tasks/network-sync-hardening/reports/avatar-root-merge/.
- Отдельно остаются runtime-попадание недоступного root в защиту, host/client, physical Quest и замеры CPU/GC/traffic.
  Регрессионные этапы не закрываются разрешением на вливание.
- Следующая выбранная задача — [перезапись _assetId](../../../tasks/mirror-preview-scene-objects/Readme.md).
  Её SDK-код не менялся и номер патча не резервировался. Порядок: после вливания текущего AvatarRoot этапа.
