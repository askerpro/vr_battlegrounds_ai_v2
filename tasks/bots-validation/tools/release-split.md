# Отдельный выпуск готовых исправлений, 2026-10-19

Выпуск завершён: Floor080f137aa и helper48d38b9b2 опубликованы штатными
merge-execute и подтверждены ls-remote. Для точных коммитов получены новые
approve/ACK. На момент консолидации Handoff HEAD48d38b9b2 отстаёт от новой
origin/deva62039a26; index пустой, rebase не выполнен. Request832 DONE,
workeridle/baselinea620. QA остаётся открытой по явному
исключению пользователя. Ниже сохранены исходные технические кандидаты и scope.

Пользователь предложил выпустить независимые исправления до продолжения
исследования Play Mode/двух клиентов и передать исследование другому агенту.
Ниже подготовлены обратимые технические кандидаты; accepted commit/merge ещё нет.

Пользователь затем прямо поручил публикацию, а после автоматического отказа
из-за QA gate явно подтвердил «Разрешаю выпуск пола и helper без QA».
Публиковать только эти два scope; пользовательская QA остаётся открытой.

База после свежего fetch: HEAD=origin/dev=2e1b6a2fc342e894d4b8526287b804fa5e3093b0.
Рабочий index пуст; исходные dirty, включая сетевую диагностику, сохранены.

## Пол BotCombatStand

Кандидат0c89300d8f0520047142e9f5f0a1dc6ad605df32, только3пути:

- Assets/Editor/VR_Battlegrounds/Bots/BotCombatStandBuilder.cs;
- Assets/Scenes/Debug/BotCombatStand.unity;
- Assets/Data/Maps/MapRuntimeCatalog.asset.

Изменение5insert/4delete: Floor Ground3 в сцене и обоих маршрутах builder;
catalog пересчитан Unity. Второй catalog fingerprint относится к ранее отставшей
записи Lobby после принятого74bd42f57; сама Lobby не изменяется.
Файлы совпадают с native R818 и review candidate3d24f530.
Raw floor818: неизменённые EditMode99/99, catalog7maps/0errors.
Bots approve0d9bc89de37c4ce2 и gameplay approve980f9b24d8ca47e3 относятся
к3d24f530, сравнение scoped product diff с отдельным кандидатом пустое.
Перед фактической публикацией проверить привязку ACK к итоговому candidate/diff;
не выдавать старый ACK за разрешение любого нового полного diff.

Открыто: пользовательская проверка телепорта на Floor и качества поведения.
При подтверждении — отдельный accepted commit только этих3product paths и
необходимых task материалов; затем штатная FF-публикация. Остальная runtime
матрица от этого не становится завершённой. Сетевые изменения не включать.

## Сборочный helper

Кандидат73517e32722c047fd0ec3ff51219ba72136a4a60, только2пути:

- Tools/release/Build-Game.ps1;
- tasks/bots-validation/tools/reaggregate-input.json.

Helper совпадает с reviewed31b6c5ae, launch-infra approvefc5f8a0419674a40.
PS5.1/PS7 и principal reaggregation exit0, inventory3+237/SHA256/UXR проверены,
outputRoot указывает на проверенные архивы; old failed pair/UNKNOWN сохранены.
Свидетельства: reports/helper-fix/diagnosis.md и {ps7-final,ps51-final,principal-final}.
Технический candidate не требует включения исходников сетевой диагностики.

Открыто: userQA helper; после неё регрессии изменённого поведения по AGENTS.md,
актуальный review итогового diff и отдельный accepted commit/FF-публикация.
Это не приёмка APK/Quest или сетевого запуска. Для повторения — manual-qa.md.

## Оставить следующему агенту

External-runtime trace и временные диагностические материалы не смешивать
с этими исправлениями. Они сохранены в dirty и полном checkpointab273679.
Краткая передача исследования — startup-investigation-handoff.md.
Новый agent/session пока не запускался; текущая route epoch3 принадлежит principal.
Новый task-new для продолжения существующего этапа не нужен.
