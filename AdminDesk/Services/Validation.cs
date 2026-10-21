using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using AdminDesk.Models;

namespace AdminDesk.Services;

/// <summary>
/// Проверка корректности пользовательского ввода (требование ТЗ: не сохранять
/// пустые имена/адреса, некорректные IP и порты).
/// </summary>
public static class Validation
{
    /// <summary>Возвращает текст ошибки или null, если значение корректно.</summary>
    public static string? ValidateDevice(DeviceEntry device, IEnumerable<DeviceEntry> otherDevices)
    {
        if (string.IsNullOrWhiteSpace(device.Name))
            return "Имя устройства не может быть пустым.";

        if (device.Name.Trim().Length > 100)
            return "Имя устройства слишком длинное (максимум 100 символов).";

        if (string.IsNullOrWhiteSpace(device.Address))
            return "IP-адрес или DNS-имя не может быть пустым.";

        var address = device.Address.Trim();

        if (address.Length > 253)
            return "Адрес слишком длинный.";

        // Если введено именно то, что выглядит как IPv4 — проверяем строгую корректность.
        // Особый случай: адрес из только цифр и точек, но НЕ из четырёх октетов (например «1.2.3»)
        // — это тоже попытка ввести некорректный IP, а не DNS-имя.
        bool allDigitsOrDots = address.All(ch => char.IsDigit(ch) || ch == '.');
        if ((LooksLikeIpv4(address) || allDigitsOrDots) && !IsValidIPv4(address))
            return $"«{address}» похоже на IP-адрес, но не является корректным IPv4-адресом.";

        // Разрешённые символы для хоста: буквы, цифры, дефис, точка.
        // Это защищает от вставки произвольных символов, которые могли бы повлиять на аргументы внешних программ.
        foreach (var ch in address)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '-' || ch == '.' || ch == '_'))
                return "Адрес содержит недопустимые символы. Разрешены буквы, цифры, «-», «.» и «_».";
        }

        if (device.CheckMethod == CheckMethod.TcpPort)
        {
            if (device.TcpPort < 1 || device.TcpPort > 65535)
                return "TCP-порт должен быть в диапазоне 1–65535.";
        }

        var duplicate = otherDevices.Any(d =>
            !ReferenceEquals(d, device) &&
            string.Equals(d.Name?.Trim(), device.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (duplicate)
            return "Устройство с таким именем уже существует.";

        return null;
    }

    public static string? ValidateFolder(FolderEntry folder, IEnumerable<FolderEntry> otherFolders)
    {
        if (string.IsNullOrWhiteSpace(folder.Name))
            return "Название папки не может быть пустым.";

        if (string.IsNullOrWhiteSpace(folder.Path))
            return "Путь к папке не может быть пустым.";

        var path = folder.Path.Trim();

        // Разрешаем только локальные абсолютные пути (C:\...) и UNC-пути (\\server\share).
        // Запрещаем любые управляющие символы — они не могут встретиться в реальном пути,
        // но теоретически могли бы исказить поведение Win32 API при открытии.
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return "Путь содержит недопустимые символы.";

        bool isUnc = path.StartsWith(@"\\", StringComparison.Ordinal);
        bool isLocalAbsolute;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                isLocalAbsolute = Path.IsPathFullyQualified(path);
            }
            catch
            {
                isLocalAbsolute = false;
            }
        }
        else
        {
            // Вне Windows (тестовая сборка Core на Linux): проверяем форму «C:\...» напрямую.
            // Приложение поддерживается только на Windows, здесь это нужно лишь для юнит-тестов.
            isLocalAbsolute = path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
        }

        if (!isUnc && !isLocalAbsolute)
            return "Путь должен быть локальным абсолютным (например, C:\\Folder) или сетевым (\\\\server\\share).";

        // В путях запрещаем символы, которые не встречаются в реальных путях Windows,
        // но могут встречаться в повреждённом/злонамеренном JSON.
        if (path.Contains('\0') || path.Contains('`') || path.Contains("$("))
            return "Путь содержит недопустимые последовательности символов.";

        return null;
    }

    /// <summary>Проверяет, похоже ли значение на IPv4-адрес (четыре группы цифр через точку).</summary>
    private static bool LooksLikeIpv4(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 4)
            return false;
        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Length > 3)
                return false;
            foreach (var ch in part)
            {
                if (!char.IsDigit(ch))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Строгая проверка IPv4: каждая часть — число 0..255.</summary>
    private static bool IsValidIPv4(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 4)
            return false;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var octet) || octet < 0 || octet > 255)
                return false;
        }
        return true;
    }
}
