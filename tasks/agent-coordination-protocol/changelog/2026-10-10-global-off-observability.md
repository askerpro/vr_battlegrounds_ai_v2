# Global off: наблюдение вместо блокировок

Пользователь поручил временно выключить мешающий coordination enforcement для всех
агентов. Штатный configure mode off снимает hooks/SCOPE, сохраняя FIFO/Unity leases.
Source `53f6760cb27a5b54e7aee1b8c34e0d98743f5389`, parent9e5fa445, добавляет аудит off:
always ALLOW до emergency intercept, counterfactual read-only would-block, существующая
таблица redacted audit/stats/log; unregistered bootstrap без регистрации. Ошибки аудита
не блокируют, ожидание SQLite lock ограничено100мс, missingDB не создаётся.

Source final34/34 PASS; legacy+new111 PASS до ограниченного уточнения SQLite, не полный
suite финального дерева. Независимое ревью CLOSED:34/34 focused PASS31,162с; frozen53f6760/moduleSHA25694d9491659dd06d7db4d366f130be34b81669d7609afb3f8f70e417796729ef3 совпадают.
Нет изменений broker leases/proxy/Unity/results/ownership/ACK. Это техническая приёмка
инфраструктуры, не игровая. Native worker/modeoff client проверяются после deployment.
