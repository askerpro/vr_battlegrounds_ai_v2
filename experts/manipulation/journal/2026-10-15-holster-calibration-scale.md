# Размер оружия в кобурах сохраняется при калибровке; этап влит

Пользователь принял текущий вариант2026-10-15: «вообще отмени очередь и сразу вливай как есть,
считай принял работу». Ручная заявка720 отменена из QUEUED, epoch0; аренды/Play не было.

HolsterCalibrationScale на каждом аватаре, включая headless, подписывается на экземплярные
события AvatarCalibrationApplier. Запоминает масштаб предметов только в Primary/Secondary кобурах;
при неизменных SDK-ссылках и родителе компенсирует равномерный масштаб Dummy Forward.
Снятия с якоря, повторного размещения, сетевых событий и второго владельца grab-state нет.

Worker711: Android PASS0, scoped существующие79/79 PASS, все79 details сохранены до finish;
R ecdc65f8c6, received только Unity-generated meta. Независимое статическое ревью без P1/P2.
Новые регрессионные тесты кобур в этот принятый выпуск не добавлялись по поручению влить как есть.
Ручного runtime/Quest подтверждения нет, нового Unity-прогона после rebase74bd42f57 нет.
Точный source/meta совпадает с711; LF/staged checks8/8 PASS.

Принятый код29c96608027e74cb3b922fcaf32bdfd43895df30 влит fast-forward в origin/dev:
merge-request c0a1b70856b3433085fb514fe0c5a0f7 epoch3, merged/push_in_progress=false,
независимые ls-remote и fetch совпали. Все этапы grab-authority завершены, задача снята с активных
в expert.json. Публикация новой базы worker — штатным request-base, отдельно от отменённой ручной аренды.
