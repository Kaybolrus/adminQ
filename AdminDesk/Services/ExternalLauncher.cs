using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

namespace AdminDesk.Services;

/// <summary>
/// Запуск внешних приложений Windows (клиент RDP и Проводник).
///
/// Принципы безопасности:
///  - UseShellExecute = false: команда НЕ формируется через cmd.exe/shell,
///    исполняемый файл задаётся явно, аргументы передаются отдельной строкой;
///  - адрес устройства попадает только в параметр /v: клиента mstsc.exe и проходит
///    валидацию во ViewModel перед вызовом;
///  - пароли не запрашиваются, не хранятся и не передаются — их здесь нет;
///  - папки открываются explorer.exe с параметром /select или просто как аргумент-путь;
///    содержимое папок не выполняется.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ExternalLauncher
{
    /// <summary>Запускает стандартный клиент RDP Windows к указанному адресу.</summary>
    public static void StartRdp(string address)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            UseShellExecute = false,
            CreateNoWindow = false
        };
        // Аргументы передаются отдельными элементами (без shell-обработки): адрес устройства
        // попадает строго в параметр /v: и не может «склеиться» с произвольной командой.
        psi.ArgumentList.Add("/v:" + address);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить mstsc.exe.");
        // Не ждём завершения: RDP-сессия живёт независимо от приложения.
    }

    /// <summary>Открывает папку в Проводнике Windows. Бросает исключение с понятным сообщением при ошибке.</summary>
    public static void OpenFolder(string path)
    {
        // Проверяем доступность пути перед открытием (для сетевых UNC это может занять время,
        // поэтому проверка выполняется в фоновом потоке на стороне вызывающего кода).
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Путь недоступен или не является папкой: {path}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        // Путь передаётся отдельным элементом ArgumentList (без shell-обработки),
        // содержимое папки не выполняется — Проводник только показывает каталог.
        psi.ArgumentList.Add(path);

        // Explorer возвращает ненулевой код выхода даже при успешном открытии окна,
        // поэтому допустим запуск без ожидания завершения.
        using var explorer = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить Проводник Windows.");
    }

    /// <summary>Проверяет существование каталога (локального или сетевого UNC).</summary>
    public static bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (IOException)
        {
            // Сетевой путь может быть недоступен — это не исключительная ситуация для проверки.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Нет прав на чтение шары — считаем путь недоступным для открытия.
            return false;
        }
    }
}
