// VR Battlegrounds patch: файл добавлен в вендорный Mirror, в апстриме его нет.
// Описание и порядок переноса при обновлении Mirror — Docs/Mirror/mirror-patches.md.
using System;
using System.Net.Sockets;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

namespace Mirror
{
    /// <summary>
    /// Запрещает дочерним процессам наследовать серверный сокет.
    ///
    /// <para>
    /// В Windows процесс, запущенный через CreateProcess с наследованием дескрипторов,
    /// получает копии всех наследуемых дескрипторов родителя, а Mono создаёт сокеты
    /// наследуемыми. Если редактор запустил дочерний процесс (MCP-сервер, git, любой
    /// инструмент) пока сервер слушал порт, после остановки сервера порт остаётся
    /// за этим процессом, и следующий старт падает с «только одно использование адреса
    /// сокета». Владельцем порта Windows при этом показывает сам редактор.
    /// </para>
    /// </summary>
    public static class SocketInheritance
    {
        /// <summary>Снимает флаг наследования с дескриптора сокета. Вне Windows ничего не делает.</summary>
        public static void Disable(Socket socket)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (socket == null)
                return;

            // Неудача не должна мешать серверу стартовать: худший исход — прежнее поведение.
            try
            {
                SetHandleInformation(socket.Handle, HandleFlagInherit, 0);
            }
            catch (Exception)
            {
            }
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        const uint HandleFlagInherit = 0x00000001;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);
#endif
    }
}
