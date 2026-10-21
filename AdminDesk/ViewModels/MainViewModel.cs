using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AdminDesk.Models;
using AdminDesk.Services;

namespace AdminDesk.ViewModels;

/// <summary>
/// Главная ViewModel: список устройств и папок, поиск, команды CRUD, запуск RDP/Проводника,
/// управление мониторингом. Все изменения данных сразу сохраняются в JSON через DataService.
/// </summary>
public class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly DataService _dataService;
    private readonly AvailabilityMonitor _monitor = new();
    private AppData _data;

    private string _searchText = "";
    private bool _isDevicesView = true;
    private DeviceEntry? _selectedDevice;
    private FolderEntry? _selectedFolder;

    public ObservableCollection<DeviceEntry> Devices { get; } = new();
    public ObservableCollection<FolderEntry> Folders { get; } = new();

    /// <summary>Отфильтрованные по поиску представления для отображения в карточках.</summary>
    public ReadOnlyCollection<DeviceEntry>? VisibleDevices => FilterDevices();
    public ReadOnlyCollection<FolderEntry>? VisibleFolders => FilterFolders();

    public RelayCommand AddDeviceCommand { get; }
    public RelayCommand EditDeviceCommand { get; }
    public RelayCommand DeleteDeviceCommand { get; }
    /// <summary>Публичен для вызова из code-behind карточки устройства.</summary>
    public RelayCommand ConnectRdpCommand { get; }

    public RelayCommand AddFolderCommand { get; }
    public RelayCommand EditFolderCommand { get; }
    public RelayCommand DeleteFolderCommand { get; }
    /// <summary>Публичен для вызова из code-behind карточки папки.</summary>
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand MoveFolderUpCommand { get; }
    public RelayCommand MoveFolderDownCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _dataService = new DataService();
        var loadResult = _dataService.Load();
        _data = loadResult.Data;

        foreach (var d in _data.Devices) Devices.Add(d);
        foreach (var f in _data.Folders.OrderBy(f => f.Order)) Folders.Add(f);

        // Если файл был повреждён — пользователь получает понятное сообщение о восстановлении.
        if (loadResult.CorruptedOrMissing && loadResult.Message != null)
        {
            // Сообщение покажет App.xaml.cs после создания окна (MessageBox до окна недопустим аккуратно).
            LoadMessage = loadResult.Message;
        }

        AddDeviceCommand = new RelayCommand(_ => AddDevice());
        EditDeviceCommand = new RelayCommand(_ => EditDevice(), _ => SelectedDevice != null);
        DeleteDeviceCommand = new RelayCommand(_ => DeleteDevice(), _ => SelectedDevice != null);
        ConnectRdpCommand = new RelayCommand(device => ConnectRdp(device as DeviceEntry));

        AddFolderCommand = new RelayCommand(_ => AddFolder());
        EditFolderCommand = new RelayCommand(_ => EditFolder(), _ => SelectedFolder != null);
        DeleteFolderCommand = new RelayCommand(_ => DeleteFolder(), _ => SelectedFolder != null);
        OpenFolderCommand = new RelayCommand(folder => OpenFolder(folder as FolderEntry));
        MoveFolderUpCommand = new RelayCommand(_ => MoveFolder(-1), _ => SelectedFolder != null);
        MoveFolderDownCommand = new RelayCommand(_ => MoveFolder(1), _ => SelectedFolder != null);
    }

    /// <summary>Сообщение о проблемах загрузки данных (показывается один раз при старте).</summary>
    public string? LoadMessage { get; private set; }

    // ---------------- Навигация ----------------

    public bool IsDevicesView
    {
        get => _isDevicesView;
        set { if (_isDevicesView != value) { _isDevicesView = value; OnPropertyChanged(); } }
    }

    // ---------------- Поиск ----------------

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VisibleDevices));
                OnPropertyChanged(nameof(VisibleFolders));
            }
        }
    }

    private ReadOnlyCollection<DeviceEntry>? FilterDevices()
    {
        IEnumerable<DeviceEntry> query = Devices;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            query = query.Where(d =>
                d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Address.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        return query.ToList().AsReadOnly();
    }

    private ReadOnlyCollection<FolderEntry>? FilterFolders()
    {
        IEnumerable<FolderEntry> query = Folders.OrderBy(f => f.Order);
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            query = query.Where(f =>
                f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                f.Path.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        return query.ToList().AsReadOnly();
    }

    // ---------------- Выбор ----------------

    public DeviceEntry? SelectedDevice
    {
        get => _selectedDevice;
        set { if (_selectedDevice != value) { _selectedDevice = value; OnPropertyChanged(); RaiseCommands(); } }
    }

    public FolderEntry? SelectedFolder
    {
        get => _selectedFolder;
        set { if (_selectedFolder != value) { _selectedFolder = value; OnPropertyChanged(); RaiseCommands(); } }
    }

    // ---------------- Устройства: CRUD ----------------

    private void AddDevice()
    {
        var device = new DeviceEntry();
        var dialog = new Views.DeviceEditWindow(device, isNew: true)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            Devices.Add(device);
            SaveData();
            SelectedDevice = device;
            OnPropertyChanged(nameof(VisibleDevices));
        }
    }

    private void EditDevice()
    {
        if (SelectedDevice == null) return;

        // Редактируем копию, чтобы «Отмена» не меняла основной список.
        var clone = CloneDevice(SelectedDevice);
        var dialog = new Views.DeviceEditWindow(clone, isNew: false)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            CopyToDevice(clone, SelectedDevice);
            SaveData();
            OnPropertyChanged(nameof(VisibleDevices));
        }
    }

    private void DeleteDevice()
    {
        if (SelectedDevice == null) return;

        var answer = MessageBox.Show(Application.Current?.MainWindow,
            $"Удалить устройство «{SelectedDevice.Name}»?", "Подтверждение удаления",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        Devices.Remove(SelectedDevice);
        SelectedDevice = null;
        SaveData();
        OnPropertyChanged(nameof(VisibleDevices));
    }

    private void ConnectRdp(DeviceEntry? device)
    {
        // Кнопка в карточке передаёт конкретное устройство; fallback — выбранное.
        device ??= SelectedDevice;
        if (device == null || !device.RdpAllowed) return;

        try
        {
            // Повторная проверка адреса перед запуском внешней программы
            // (защита от повреждённых или отредактированных вручную данных).
            string? error = Validation.ValidateDevice(device, Enumerable.Empty<DeviceEntry>());
            if (error != null)
            {
                MessageBox.Show(Application.Current?.MainWindow, error,
                    "Некорректные данные устройства", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ExternalLauncher.StartRdp(device.Address.Trim());
            // mstsc.exe сам устанавливает соединение и показывает окно авторизации Windows.
            // Приложение не участвует в аутентификации, не запрашивает и не хранит пароли.
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current?.MainWindow,
                $"Не удалось запустить подключение к удалённому рабочему столу: {ex.Message}",
                "Ошибка запуска RDP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------- Папки: CRUD и порядок ----------------

    private void AddFolder()
    {
        var folder = new FolderEntry { Order = Folders.Count };
        var dialog = new Views.FolderEditWindow(folder, isNew: true)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            folder.Order = Folders.Count;
            Folders.Add(folder);
            SaveData();
            SelectedFolder = folder;
            OnPropertyChanged(nameof(VisibleFolders));
        }
    }

    private void EditFolder()
    {
        if (SelectedFolder == null) return;

        var clone = CloneFolder(SelectedFolder);
        var dialog = new Views.FolderEditWindow(clone, isNew: false)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            clone.Order = SelectedFolder.Order;
            CopyToFolder(clone, SelectedFolder);
            SaveData();
            OnPropertyChanged(nameof(VisibleFolders));
        }
    }

    private void DeleteFolder()
    {
        if (SelectedFolder == null) return;

        var answer = MessageBox.Show(Application.Current?.MainWindow,
            $"Удалить папку «{SelectedFolder.Name}»? Сама папка на диске НЕ удаляется — удаляется только запись из списка.",
            "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        Folders.Remove(SelectedFolder);
        SelectedFolder = null;
        ReindexFolders();
        SaveData();
        OnPropertyChanged(nameof(VisibleFolders));
    }

    private void MoveFolder(int delta)
    {
        if (SelectedFolder == null) return;

        var ordered = Folders.OrderBy(f => f.Order).ToList();
        int index = ordered.IndexOf(SelectedFolder);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count) return;

        // Меняем местами значения Order у соседних карточек.
        (ordered[index].Order, ordered[target].Order) = (ordered[target].Order, ordered[index].Order);
        SaveData();
        OnPropertyChanged(nameof(VisibleFolders));
    }

    private void OpenFolder(FolderEntry? folder)
    {
        folder ??= SelectedFolder;
        if (folder == null) return;

        try
        {
            string path = folder.Path.Trim();

            // Проверка пути перед запуском explorer: понятная ошибка вместо молчаливого отказа.
            string? error = Validation.ValidateFolder(folder, Enumerable.Empty<FolderEntry>());
            if (error != null)
            {
                MessageBox.Show(Application.Current?.MainWindow, error,
                    "Некорректные данные папки", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Directory.Exists для UNC-пути может ждать ответа сети несколько секунд —
            // выполняем проверку и запуск вне UI-потока, чтобы окно не подвешивалось.
            Task.Run(() =>
            {
                try
                {
                    if (!ExternalLauncher.DirectoryExists(path))
                    {
                        PostMessage($"Папка недоступна: {path}", MessageBoxImage.Warning);
                        return;
                    }
                    ExternalLauncher.OpenFolder(path);
                }
                catch (Exception ex)
                {
                    PostMessage($"Не удалось открыть папку: {ex.Message}", MessageBoxImage.Error);
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current?.MainWindow,
                $"Не удалось открыть папку: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReindexFolders()
    {
        int i = 0;
        foreach (var f in Folders.OrderBy(f => f.Order))
            f.Order = i++;
    }

    // ---------------- Мониторинг ----------------

    /// <summary>Запускает цикл мониторинга. Первая волна проверок стартует сразу, без ожидания минуты.</summary>
    public void StartMonitoring()
    {
        _monitor.Start(
            getDevicesToCheck: () => Devices.ToList(),
            onResult: (device, reachable, time) =>
            {
                // Обновление свойств, связанных с UI, обязано происходить на UI-потоке WPF.
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    device.Status = reachable ? AvailabilityStatus.Online : AvailabilityStatus.Offline;
                    device.LastCheckTime = time;
                });
            });
    }

    /// <summary>Помечает включённые устройства статусом «Проверка» перед первой волной.</summary>
    public void MarkChecking()
    {
        foreach (var d in Devices.Where(d => d.MonitorEnabled))
            d.Status = AvailabilityStatus.Checking;
    }

    // ---------------- Сохранение ----------------

    private void SaveData()
    {
        _data.Devices = Devices;
        _data.Folders = Folders;

        string? error = _dataService.Save(_data);
        if (error != null)
        {
            // Ошибку сохранения не подавляем — пользователь должен знать, что данные не записаны.
            MessageBox.Show(Application.Current?.MainWindow, error, "Ошибка сохранения данных",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------- Вспомогательное ----------------

    /// <summary>Показывает сообщение через Dispatcher — безопасно вызывать из фоновых потоков.</summary>
    private static void PostMessage(string text, MessageBoxImage icon)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
            MessageBox.Show(Application.Current.MainWindow, text, "Admin Desk",
                MessageBoxButton.OK, icon));
    }

    private static DeviceEntry CloneDevice(DeviceEntry source) => new()
    {
        Name = source.Name,
        Address = source.Address,
        Type = source.Type,
        Description = source.Description,
        MonitorEnabled = source.MonitorEnabled,
        CheckMethod = source.CheckMethod,
        TcpPort = source.TcpPort,
        RdpAllowed = source.RdpAllowed
    };

    private static void CopyToDevice(DeviceEntry source, DeviceEntry target)
    {
        target.Name = source.Name;
        target.Address = source.Address;
        target.Type = source.Type;
        target.Description = source.Description;
        target.MonitorEnabled = source.MonitorEnabled;
        target.CheckMethod = source.CheckMethod;
        target.TcpPort = source.TcpPort;
        target.RdpAllowed = source.RdpAllowed;
    }

    private static FolderEntry CloneFolder(FolderEntry source) => new()
    {
        Name = source.Name,
        Path = source.Path,
        Description = source.Description,
        IconKind = source.IconKind,
        Order = source.Order
    };

    private static void CopyToFolder(FolderEntry source, FolderEntry target)
    {
        target.Name = source.Name;
        target.Path = source.Path;
        target.Description = source.Description;
        target.IconKind = source.IconKind;
    }

    private void RaiseCommands()
    {
        EditDeviceCommand.RaiseCanExecuteChanged();
        DeleteDeviceCommand.RaiseCanExecuteChanged();
        EditFolderCommand.RaiseCanExecuteChanged();
        DeleteFolderCommand.RaiseCanExecuteChanged();
        MoveFolderUpCommand.RaiseCanExecuteChanged();
        MoveFolderDownCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Останавливает мониторинг при закрытии приложения.</summary>
    public async ValueTask DisposeAsync()
    {
        await _monitor.DisposeAsync();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
