# Патч Mirror 1: серверные сокеты не наследуются дочерними процессами редактора

`dda0cb72a752fc3e1d21eb47bd52c32ffaabc320`. `SocketInheritance.Disable` снимает `HANDLE_FLAG_INHERIT` с
серверных сокетов `KcpServer` и `NetworkDiscoveryBase` (новый файл `Core/SocketInheritance.cs` + два вызова,
маркер `VR Battlegrounds patch`). Только `UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN`; в Quest-сборке вызов пустой.
Клиентские сокеты не патчены.

Проверено автором: `SocketInheritanceTests` (EditMode) красные до правки, зелёные после. Платформа прогона —
Windows-редактор; Quest не проверялся (код там пустой).

При обновлении Mirror: перенести файл и два вызова; если в апстриме своё решение — патч удалить, тест оставить.

Источники: коммит выше; `Docs/Mirror/mirror-patches.md` «Патч 1».
