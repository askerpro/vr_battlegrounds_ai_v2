# Черновик оценки площади контакта

Сохранён 2026-10-15 по прямому запросу пользователя. Активный критерий заменён опорными точками
фаланг с допуском ±2мм. Это снимок разработки площади до переключения, не принятая production-версия.

Вложенные пути сохраняют исходную структуру. manifest.json содержит SHA256 и размер всех24файлов:
маска, интегратор площади, capture/window, прежняя ветка IK, контракт и инструменты компиляции/smoke.
Копии .cs лежат вне Assets и не участвуют в Unity compilation. Оригинальные игровые assets не изменены.

Проверки: managed coverage30/30, Unity735 IK12/12 и existing3/3; общий area/source smoke740 FAIL.
Unity740 Viper mask API2cases PASS, Uzi source PASS; Uzi target1degenerate, Viper source nonuniform String.
Полные отчёты и PNG сохранены отдельно вне Git:

- [Unity735](../../reports/contact-authoring-integration-20261015/735-Review.md)
- [Unity740 с PNG внутри](../../reports/contact-authoring-integration-20261015/740-Review.md)
- [Offlinecompile39/406](../../reports/contact-authoring-source-mask-20261015-r2-final/compile-result.json)

До возвращения к площади потребуются anatomical masks, source/target correspondence, исправление
геометрических ограничений и человеческая проверка. Копии не заменяют эти проверки.
