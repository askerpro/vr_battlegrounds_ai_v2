# bots-control-modes: скриптовый ввод сетевых клиентов (xr-input веха 1), 2026-10-20

Влито `43332888b`. Обычный сетевой клиент в Editor/MPPM получает ввод «шлема» от сценария:
`Assets/Scripts/Debug/E2E/Input/` (DeviceFrame, ScriptedControllerInput/Tracking — выключены в PlayerBase и
GhostAvatar, включает только E2EScriptedInput; ScriptedInputGate + VRBG_NO_E2E), пилот, JSON-сценарии
(`Input/Scripts/*.json`), сценарии `scripted-input-smoke` (S+1) и `scripted-wall-purchase` (S+2, серверный оракул
кассы); меню `Tools/VR Battlegrounds/Testing/Скриптовый ввод в префабах аватаров`. PlayLaunch и стенд не менялись:
ввод включает сам сценарий после `run-e2e`. Инварианты: один источник ввода на процесс, один писатель камеры,
ходьба только головой. Открыто к шагу плеера: `-e2eInput scripted`, загрузка сценария вне `Application.dataPath`,
взаимоисключение с E2EHeadPose по `Armed` (замечание ревью). Дальнейшее — эпик `e2e-regression`.
