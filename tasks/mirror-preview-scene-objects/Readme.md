# Прекратить повторную перезапись _assetId при редакторном сохранении префабов

2026-10-15. Владелец network, worktree F:/CodexWorktrees/network/Vr_Battlegrounds_ai.
Статус: **следующая задача после текущего исправления AvatarRoot гонки**, прямой выбор пользователя.
Код не менялся, SDK-номер не зарезервирован, Unity-заявки не подавались.

Исходная находка: OnValidate → NetworkIdentity.SetupIDs в preview-сцене LoadPrefabContents может присвоить
_assetId=0; SaveAsPrefabAsset сохраняет его, обычный импорт/OnValidate затем снова выставляет канон.
В origin/dev уже есть NetworkAssetIdNormalizer (9757c9195), но это нормализация диска, а не устранение этой причины.
Связанные owner-сообщения: avatar-grip6384/6429, weapon-system6378; прежнее обещание network6589/6647.

## Порядок будущей работы

1. После текущей задачи обновить source/inbox, воспроизвести обычный prefab и variant через штатные Unity API.
2. Выделить точное authoring-правило для prefab/PrefabStage/preview/scene instance/runtime: кто назначает
   канонический assetId и где его запрещено обнулять. Не заменять проблему общей перезаписью YAML/пакетной нормализацией.
3. Оценить общий корень с ошибочным sceneId/спавном preview-объектов; расширение scope обосновать отдельно,
   не включать скрыто все изменения из большого backlog-пункта.
4. Зарезервировать Mirror-патч перед SDK-кодом, журнал только здесь в changelog/; выполнить узкую правку.
5. Проверить компиляцию и существующие NetworkAssetIdOnDisk/сетевые проверки; пользователь повторяет
   загрузку → сохранение → повторный импорт обычного prefab и variant: стабильный канонический id,
   отсутствие ложного sceneId у prefab contents и отсутствие регрессии настоящих scene instances.
6. После подтверждения — необходимые регрессии и совместное вливание кода с task docs. Никакой отдельной
   публикации одних документов вместо исправления.

Будущие исходники: Assets/ThirdParty/Mirror/Core/NetworkIdentity.cs, при доказанной необходимости
Assets/Editor/VR_Battlegrounds/VersionControl/NetworkAssetIdNormalizer.cs. Общий helper сохранения и
NetworkScenePostProcess/Utils не добавляются автоматически: решение после проверки owner boundaries.
