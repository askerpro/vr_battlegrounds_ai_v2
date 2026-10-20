# Детали: лаги окна Play Launch

2026-10-20.

## Причины (до исправления, base 646170c47)

- `PlayLaunchWindow.cs:16` — `OnInspectorUpdate() => Repaint()` без условия: окно перерисовывается ~10 раз в секунду,
  каждая перерисовка — минимум два вызова OnGUI (Layout + Repaint), плюс вызов на каждое событие мыши/клавиатуры.
- `PlayLaunchWindow.cs:63` — `PlayLaunch.Plan(json)` в каждом OnGUI. `PlayLaunch.Resolve` (`PlayLaunch.cs:170`) делает
  `AssetDatabase.LoadAssetAtPath`, рекурсивный `AssetDatabase.GetDependencies(сцена, true)` + LINQ `Contains`,
  загрузку `MapRuntimeCatalog`, `PreviewSceneNetworkGuard.DescribeRefusal()` (`Resources.FindObjectsOfTypeAll<NetworkIdentity>()`
  по всем объектам в памяти) и SHA256 с `GetAssetDependencyHash`. Сам guard задокументирован как «не каждый кадр».
- `PlayLaunchWindow.cs:25` — `PlayLaunch.Status()` в каждом OnGUI: чтение файла манифеста стенда (`StandManifest.Read`,
  исключение FileNotFound в простое, `Process.GetProcessById` при прогоне), сериализация/разбор JSON статуса,
  после любого прогона — `Directory.GetFiles` каталога архивов логов.
- `PlayLaunchWindow.cs:29` — `PlayLaunchSettings.RequestActive` в каждом OnGUI: ещё одно чтение манифеста + SessionState/JSON.
- `PlayLaunchWindow.cs:52` — `PlayLaunchSettings.ProfilePath` каждый OnGUI создаёт `PlayLaunchProfileStore` (`Path.GetFullPath`).
- Мелочь: массивы вариантов `Choice` создавались в каждом OnGUI.

## Исправление (только окно, backend PlayLaunch не менялся)

- Статус и `RequestActive` кэшируются; обновляются в `OnInspectorUpdate` (10 Гц) и перед Layout после действия/смены Play Mode.
  `Repaint()` — только если изменились показанные поля (State/Owner/Error/RunId) или доступность кнопок.
- План кэшируется по JSON черновика; пересчёт — при правке черновика, по событиям (смена активной сцены, open/close сцены,
  Prefab Mode open/closing, Build Settings, projectChanged, playModeStateChanged), после любой кнопки и не реже раза в 2 с
  вне Play/чужого запроса (через Repaint: скрытая вкладка план не считает). В Repaint план не пересчитывается
  (Layout и Repaint одного кадра видят один план).
- «Play один раз с этим конфигом» пересчитывает план в момент нажатия и передаёт свежий PlanHash — как прежде.
- Подписки снимаются в `OnDisable`.

## Остаточный риск

- Изменение, не дающее события (например, `PrefabUtility.LoadPrefabContents` агентом), отражается в подсказке плана
  с задержкой до 2 с. Запуск всё равно проверяется заново в `PlayLaunch.Play`.
- `PlayLaunch.Tick` (EditorApplication.update) в прогоне вызывает `Status()` каждый кадр редактора — вне окна, не менялось.
