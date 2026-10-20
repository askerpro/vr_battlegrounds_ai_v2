# play-launch-inspector-lag: лаги окна Play Launch, 2026-10-20

Причина: `PlayLaunchWindow` строил `PlayLaunch.Plan` и `Status` в каждом OnGUI при безусловном `Repaint` 10 Гц.
`Plan(Lobby)` стоил ~1 с: почти всё время — рекурсивный `AssetDatabase.GetDependencies` сцены (~2400 зависимостей).
Исправлено (влито 24f806907): окно кэширует план/статус и перерисовывается по изменению показанного; `PlayLaunch`
кэширует разметку сцены (MapRoot/StandaloneSceneMarker), запись действительна при том же `GetAssetDependencyHash`,
весь кэш сбрасывается по `projectChanged`. Повторный план 2–3 мс.
Инвариант: дорогие проверки PlayLaunch (зависимости, preview guard) не вызывать покадрово из UI; кэш разметки один, у `PlayLaunch`.
Остаток: после каждого `projectChanged` открытое окно один раз строит холодный план ~1 с.
