# Обновление технического пакета UI

Canonical UI7800 просит свежий grant под consumer12 / implementation epoch3 и текущую базу
d449f19832b1c2b0f6bb9c9ada3b9db4a4a2b299. Старый grant rev1 адресован consumer10 и истёк.

Штатный selected-path checkpoint создал cd2f0c632ee65b124d4abac41de45f0d5eb72ab5 поверх
новой базы. HEAD/index основного checkout и fingerprints чужого вложенного checkout сохранены;
Unity/worker не использованы. Квитанция reports/ui-provider-rev2/checkpoint.json.

Capture helper получил параметры label/out/event/expected-base: прежняя квитанция не
перезаписывается, чужая reports-область запрещена, изменение базы до операции останавливает её.
Это подготовка технической композиции для актуальных снимков меню, без игровой приёмки.
Итог публикации и bindings сохраняются в reports/ui-provider-rev2/ после регистрации статуса.
