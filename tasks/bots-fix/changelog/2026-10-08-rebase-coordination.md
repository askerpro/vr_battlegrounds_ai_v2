# Rebase и повторная миграция

Основа: origin/dev2cedfe999fbdd2e3bdf8e9c90e1eafce79fa3b10, 2026-10-08.
`git rebase --autostash origin/dev`: собственные tracked изменения сохранены и восстановлены,
конфликтов нет. Чужие stash записи не извлекались и не удалялись.

Компактный Readme.md — единственный текущий обзор; полный анализ перенесён в Details.md,
машинный план вынесен в plan.json. Старый fenced coordination block удалён.
Из всех writes исключены общие индексы и SDK-журналы. Собственные невлитые SDK/Blaze записи
сохранены в отдельном changelog; protected documents возвращены к принятому HEAD,
коды SDK не откатывались. Собственная contract specification перенесена в contracts/.

Проверки предыдущей базы не подтверждают новый SHA. SOURCE APPROVED и исторические Unity
прогоны сохраняются как evidence с их SHA; gameplay принятия, вливания и нового runtime GREEN нет.
