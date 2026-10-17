# GPU scene evidence r1: ревью и ACK

2026-10-15. Ответ на perf event8664, предоставляющий этап `gpu-scene-evidence`,
план performance rev11. Вердикт: **контракт r1 согласован потребителем scene-detail**.
`contract-ack --task scene-detail --owner level-design --contract gpu-scene-evidence
--revision 1` завершился exit0/ok=true; публикация
`15b3ee9e65bcaf533c239be7bedc2ca252c9fee1` в agents/status.
Владелец завершает contract-agree; это не active, приёмка реализации или Unity lease.

Прочитаны нормативные файлы в performance checkout:

- `tasks/perf-stress-scenario/contracts/gpu-scene-evidence.md`, SHA256
  `1b76562f3b1ee3de3ef05c60e55bb483b686235c54a2495809067e15da4a74c3`;
- одноимённый `.json`, SHA256
  `6753d34d5b93d0545aef2735f994487571dc7008516f595f5770f9f4eaf2c6b1`;
- точный stage плана rev11: Applier accessor, новый DTO+meta, Performance Editor
  exporter+meta и собственные материалы perf. Перед ACK хеши повторно проверены.

Контракт соответствует boundary8651: инструменты реализует performance в своём
checkout; writer/lifecycle/Collect/сцена/профиль/UI/Quality/SDK не меняются.
Detached snapshot сохраняет original/effective, baseline/current и many-to-many
identity. `mappingVerified` подтверждает соответствие effective сохранённому
original; при расхождении mapping=-1 с причиной. Наблюдённый LOD не выдумывается.
Editor exporter работает с уже загруженной сценой и не сохраняет/не загружает её.

Проверки будущей реализации, не дополнительные условия ACK:

- accessor относится к собственному активному `Current == this`, при отсутствии
  инициализации/владения возвращает false/null и причину;
- проверка mapping включает renderer refs/порядок, пороги и fade, не только длину
  массива; неполные/отсутствующие записи отражены в complete/limitations;
- snapshot не отдаёт внутренние mutable arrays; enabled/isVisible остаются сырыми
  состояниями, не доказательством draw; shared renderer сохраняет все связи;
- GUID/localID/hash/authoring ID привязаны к manifest; runtime InstanceID отдельно;
  неизвестные joins и light/caster данные unresolved;
- один вызов не меняет профиль, renderer, LOD, группы текстур или сцену;
  новые gameplay tests ждут пользовательской проверки по AGENTS.md.

Контрольный профиль/список40 групп остаются декларацией из
`contracts/lod-ab-control-v1.json`, не автоматически применённым runtime input.
Согласование не закрывает runtime/launch договор, captured mesh proof, реальный
Quest3 A/A/A-B или бюджет сцены. Код, assets и тесты здесь не менялись;
компиляция, сборки, Unity и захваты в этом ревью не запускались.
