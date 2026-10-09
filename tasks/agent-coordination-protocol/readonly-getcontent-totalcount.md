# Чтение Get-Content с TotalCount без begin

Source pin: `e0bc9238bd5a60d6ab509d749f1f764d0dc647e3`, parent `3eada1f`.

Живой владелец vr-test-stand (событие2441) подтвердил watch-ticket, Git show/stat и check-ignore после merged, но Get-Content -Encoding UTF8 tasks/vr-test-stand/Readme.md -TotalCount 3 блокировался PERMIT до выполнения ОС. Причина воспроизведена: совместимый implicit Windows Bash envelope не выбирал полноценный PowerShell parser при наличии TotalCount. Чистый exec_command уже разрешал чтение; скрытое отображение клиентского envelope не выдаётся за доказанное.

Исправление меняет только selector: допускает одну числовую пару TotalCount в прежних literal/UTF8 формах. Полный существующий readonly parser продолжает проверять аргументы и путь. Подстановки, редиректы, неизвестные/дублирующиеся параметры, составные мутации, явный Bash и Linux не расширены. Автор:5 specific проверок PASS, включая accepted/expired/unregistered/merged и native PowerShell ровно3 строки. Независимое ревью CLOSED:3 focused проверки PASS за6,869с, diff-check чистый. Живое подтверждение владельца выполняется после deployment.

Модель/семейство клиента остаются информационными; чтение не требует регистрации или begin. OS READ runtime — отдельный нерешённый доступ: профиль только чтения подготовлен, не применён без подтверждения пользователя. ACL этим исправлением не меняются.
