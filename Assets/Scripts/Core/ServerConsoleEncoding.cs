#if UNITY_SERVER && UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Переключает консоль Windows Dedicated Server на UTF-8.
    ///
    /// Unity пишет лог в stdout байтами UTF-8, а консоль Windows по умолчанию читает их
    /// в OEM-кодировке (866): русский текст превращается в «╨Ю╤В╨┐╤А╨░╨▓╨║╨░». Ручной обход —
    /// <c>chcp 65001</c> перед запуском; здесь то же самое делает сам сервер до первой сцены.
    /// В остальных сборках код пустой.
    /// </summary>
    public static class ServerConsoleEncoding
    {
#if UNITY_SERVER && UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const uint Utf8CodePage = 65001;

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleOutputCP(uint codePage);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Apply()
        {
            // Без консоли (сервер запущен службой) вызов просто вернёт false — это не ошибка.
            SetConsoleOutputCP(Utf8CodePage);
        }
#endif
    }
}
