using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdminDesk.Models;

namespace AdminDesk.Services;

/// <summary>
/// Сохранение и загрузка данных приложения в JSON-файл в каталоге AppData
/// (%AppData%\AdminDesk\data.json).
///
/// Требования безопасности из ТЗ:
///  - данные НЕ хранятся в каталоге установки (он может быть недоступен для записи);
///  - запись выполняется во временный файл с последующей атомарной заменой основного;
///  - перед заменой создаётся резервная копия;
///  - повреждённый JSON НЕ перезаписывается молча: сохраняется его копия, пользователю
///    возвращается информация о проблеме, приложение продолжает работу с резервной копией
///    или стартовыми данными.
/// </summary>
public class DataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directory;
    private readonly string _filePath;
    private readonly string _tempPath;
    private readonly string _backupPath;

    public string FilePath => _filePath;

    public DataService()
    {
        // AppData текущего пользователя — гарантированно доступен на запись без прав администратора.
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdminDesk");
        _filePath = Path.Combine(_directory, "data.json");
        _tempPath = Path.Combine(_directory, "data.json.tmp");
        _backupPath = Path.Combine(_directory, "data.backup.json");
    }

    /// <summary>
    /// Результат загрузки: данные + признак того, что основной файл был повреждён.
    /// </summary>
    public sealed class LoadResult
    {
        public AppData Data { get; init; } = new();
        /// <summary>true — основной файл отсутствовал или не читался; использованы стартовые/резервные данные.</summary>
        public bool CorruptedOrMissing { get; init; }
        /// <summary>Текст понятного сообщения об ошибке для пользователя (null, если всё в порядке).</summary>
        public string? Message { get; init; }
    }

    /// <summary>Загружает данные. Никогда не бросает исключение наружу — ошибки возвращаются в LoadResult.</summary>
    public LoadResult Load()
    {
        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex)
        {
            return new LoadResult
            {
                Data = CreateSeedData(),
                CorruptedOrMissing = true,
                Message = $"Не удалось создать каталог данных: {ex.Message}. Будут использованы стартовые записи (без сохранения)."
            };
        }

        if (!File.Exists(_filePath))
        {
            // Файла ещё нет — первое запускаем со стартовыми записями и сразу сохраняем.
            var seed = CreateSeedData();
            Save(seed);
            return new LoadResult { Data = seed };
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions);
            if (data == null || data.Devices == null || data.Folders == null)
                throw new InvalidDataException("Файл данных не содержит ожидаемой структуры.");

            // Нормализуем порядок папок, если он был сломан ручным редактированием.
            NormalizeFolderOrder(data);
            return new LoadResult { Data = data };
        }
        catch (Exception ex)
        {
            // Повреждённый файл НЕ удаляем и НЕ перезаписываем молча: сохраняем копию для восстановления.
            string damagedCopyPath = TryPreserveDamagedFile();

            // Пробуем восстановить данные из резервной копии.
            var fromBackup = TryLoadBackup();
            if (fromBackup != null)
            {
                return new LoadResult
                {
                    Data = fromBackup,
                    CorruptedOrMissing = true,
                    Message = $"Основной файл данных повреждён ({ex.Message})." +
                              (damagedCopyPath != null ? $" Повреждённая копия сохранена: {damagedCopyPath}." : "") +
                              " Данные восстановлены из резервной копии."
                };
            }

            return new LoadResult
            {
                Data = CreateSeedData(),
                CorruptedOrMissing = true,
                Message = $"Основной файл данных повреждён ({ex.Message})," +
                          " а резервная копия отсутствует или тоже повреждена." +
                          (damagedCopyPath != null ? $" Повреждённая копия сохранена: {damagedCopyPath}." : "") +
                          " Использованы стартовые записи."
            };
        }
    }

    /// <summary>
    /// Безопасное сохранение: сериализация во временный файл, затем замена основного.
    /// Перед заменой текущий основной файл копируется в резервную копию.
    /// Возвращает текст ошибки или null при успехе. Исключения наружу не выпускаются.
    /// </summary>
    public string? Save(AppData data)
    {
        try
        {
            Directory.CreateDirectory(_directory);

            var json = JsonSerializer.Serialize(data, JsonOptions);

            // Запись во временный файл. File.Replace обеспечивает атомарную замену
            // на том же томе и создаёт системную резервную копию — дополнительно храним свою.
            File.WriteAllText(_tempPath, json);

            if (File.Exists(_filePath))
            {
                // Копия основного файла — наша собственная резервная копия (переживёт несколько сохранений подряд).
                try { File.Copy(_filePath, _backupPath, overwrite: true); }
                catch { /* резервная копия не критична для основного сохранения */ }

                File.Replace(_tempPath, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(_tempPath, _filePath, overwrite: true);
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Не удалось сохранить данные: {ex.Message}";
        }
    }

    /// <summary>Сохраняет копию повреждённого файла рядом с расширением .damaged-timestamp.
    /// Возвращает путь к копии или null, если сохранить не удалось.</summary>
    private string? TryPreserveDamagedFile()
    {
        try
        {
            string copy = _filePath + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(_filePath, copy, overwrite: false);
            return copy;
        }
        catch
        {
            return null;
        }
    }

    private AppData? TryLoadBackup()
    {
        try
        {
            if (!File.Exists(_backupPath))
                return null;
            var json = File.ReadAllText(_backupPath);
            var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions);
            if (data?.Devices == null || data.Folders == null)
                return null;
            NormalizeFolderOrder(data);
            return data;
        }
        catch
        {
            return null;
        }
    }

    private static void NormalizeFolderOrder(AppData data)
    {
        int i = 0;
        foreach (var folder in data.Folders.OrderBy(f => f.Order))
        {
            folder.Order = i++;
        }
    }

    /// <summary>
    /// Стартовые записи по ТЗ: создаются только при первом запуске (когда файла данных нет)
    /// или когда восстановление невозможно. Пользователь может изменить или удалить их через UI.
    /// </summary>
    public static AppData CreateSeedData()
    {
        var data = new AppData();

        data.Devices.Add(new DeviceEntry
        {
            Name = "alttest10",
            Address = "192.168.10.66",
            Type = DeviceType.Computer,
            Description = "Тестовая запись, созданная при первом запуске.",
            MonitorEnabled = true,
            CheckMethod = CheckMethod.Ping,
            RdpAllowed = true
        });

        data.Folders.Add(new FolderEntry { Name = "Установщики программ", Path = @"\\DS2\FS-1\install", IconKind = FolderIconKind.Installers, Order = 0, Description = "" });
        data.Folders.Add(new FolderEntry { Name = "Общие файлы", Path = @"\\DS2\FS-1", IconKind = FolderIconKind.Shared, Order = 1, Description = "" });
        data.Folders.Add(new FolderEntry { Name = "Рабочие файлы", Path = @"\\DS2\RAM", IconKind = FolderIconKind.Work, Order = 2, Description = "" });
        data.Folders.Add(new FolderEntry { Name = "Сканирование", Path = @"\\DS2\Scan", IconKind = FolderIconKind.Scan, Order = 3, Description = "" });

        return data;
    }
}
