
## Выпуск binary/MCP diagnostics

Источник инфраструктуры: `f3406ef3733d5d447258f811b3f3eb3a8c606771` (чистый закреплённый commit). Совмещённая проверка root: 111 passed, 1 skip, 0 failed. Исправлены сохранение binary/LFS и проекция execute_code error/hint; отдельный wheel проходит console verify-only без запуска сервера. Live MCP activation и игровая приёмка пока не выполнены.

Административный weapon handoff: revision 22, owner weapon-system-expert; stage/spec/history/ACK/SDK сохранены. Штатная публикация координатора генерирует его plan.json из canonical record; чужая игровая работа не принимается.

Final wheel SHA256: `3b1a451f67f5d7e6d56f8cce91fda0bed5e5102c30c8a599d7a8fcadbb60dd0a`. Console entrypoint version `10.2.0+vr2012.f3406ef3733d`, patched execute_code SHA `488cef28ca3bf64e6f393f084a24073135844a6c34bd64a3c72c2efa2eab7a1b`, 71 dependency pins verified.
