# Влиты смена тела при смерти на верхнем уровне и отпечатки каталога, не зависящие от EOL

Дата: 2026-10-12. Оба этапа приняты пользователем в сессии эксперта.

## death-body-defer / deferred-body — origin/dev 160223c7b

- Смерть не доходила до клиентов: `ReplaceBody` уничтожал тело внутри `UxrActor.DieInternal` (BeginSync…EndSync),
  Relay отбрасывал событие. Теперь `AvatarManager` откладывает смену тела до `LateUpdate` (проверка
  `SyncCallDepth == 0 && !IsInsideStateSync` и при постановке, и при исполнении), тело выбирается по текущему
  состоянию; единственный отпускающий при смерти — сервер (`ReleaseBeforeDestroy` / `ReleaseHandsOnServer`).
- Ревью: network ACK 4388/4396, manipulation 4386; стенд manipulation 4537 PASS. Офлайн-компиляция без weaver.
- НЕ выполнены: Unity-сценарии PlayerDeathSignal/RoundReadinessMatch/AvatarSwapDeathReplication, ярус C network.
- Дальше: `accepted-tests`, `death-equipment-drop` (сброс снаряжения при смерти не доходит клиентам — согласие network 4396).

## catalog-fingerprint / env-independent — origin/dev e330b6162

- `MapRunPreflight.AssetHash` был `GetAssetDependencyHash` (сырые байты) — после EOL PhaseB зависел от рабочей копии:
  ложные `Catalog.*.Stale` и риск «разные сборки» клиент/сервер (каталог пересобирается при сборке).
  Теперь SHA256 содержимого + `.meta`, YAML с CRLF→LF, формат v2.
- Аренда 503: RebakeCatalog 7/7 без отказов в CRLF-копии; 14 отпечатков = расчёт по LF-blob Git; EditMode Maps 253
  (6 известных FAIL level-design вне этапа); Play лобби Ready. Не проверено: две реальные сборки из разных копий.
- weapon-system влил пресет лобби с одной строкой отпечатка (вариант А, b624fdb3c) до этого этапа; level-design
  `lobby-decoration/implementation` поставлен после этапа (порядок 4945).
- Дальше: `accepted-tests` (EditMode: CRLF и LF дают один отпечаток).
