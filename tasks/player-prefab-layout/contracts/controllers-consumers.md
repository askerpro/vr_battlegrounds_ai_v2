# Controllers canonicalization: источник и границы

2026-10-19. Canonical9877. Read-only census по всему Assets (`.prefab/.asset/.unity/.mat`), GUID `8e0e06323e4754f479a9f19788554652`; evidence `reports/test-repair/head-persistent833.json`, `head-identity-delta833.json`. Диагностика explorer head_ghost_diagnosis независимо сверена с native identity receipt.

3235 одинаковых ключей(path/type/slot),111замен fileID:24GameObject,73Transform,14SkinnedMeshRenderer. Все15root slots прежние, в том числе GO2232204837902353264, UxrAvatar4589610041099156173, NetworkIdentity8035579767161004474. GUID/meta прежние.

Вне Ghost10прямых PPtr в7файлах: Cyborg.asset1, TeamAvatarStrategy1, Managers3, SDKGun1/Grenade1/Machinegun1/Shotgun2. Все к прежним root GO/UxrAvatar; ни один из111IDs не используется. Ещё7GUID-строк поз в sample/donor — не ссылки на subobjects. Единственный зависимый variant Ghost использует14старых renderer IDs для material overrides. Он отделяется первым из исходного Controllers; новый Ghost не должен содержать PPtr на Controllers или SDK parent.

Таким образом native canonicalization внутренних IDs после отделения Ghost допускается с сохранением внешних ссылок, корня, GUID и семантики. Это не ручной YAML-remap. Native readback/проверки и независимое ревью обязательны; статический census не заменяет runtime/UserQA.
