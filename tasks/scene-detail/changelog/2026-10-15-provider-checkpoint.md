# Технический пакет SceneDetail для UI

Исправление брокера 7657 сняло blocker7183. Один штатный повтор selected-path checkpoint
на runtime1432984ac464614e7956aaba9f2eec78602e3f77 создал source
30901528d984fe18b5ad01f03ddb06a827f70c21 поверх B1812dc5bf22860e8302e858b1637e92b1df636e9.
HEAD/index основного checkout и fingerprints чужого вложенного checkout сохранены;
worker request/lease не использованы. Квитанция reports/ui-provider/checkpoint-retry.json.

Helper prepare_provider_grant.py проверил151 primary entry,112 protected texture-meta и140
changed texture-meta, пересечение GUID=0. Manifest SHA256
6fecb5b6125948b5568e0a0b2da13d56461243e8252efce5479a94ea1feb0692.
Неизменная Lobby.unity.meta допускается как companion с тем же Git blob/GUID и только
CRLF/LF разницей физического файла; изменённые primary и protected проверяются строго.
Helper-файлы этой задачи имеют локальный LF contract в tools/.gitattributes.

Это техническая композиция для «Перф-анализ → GPU», без принятого коммита и без runtime/UI/Quest
приёмки. Consumer получает только точные entries grant, весь source cherry-pick не разрешён.
