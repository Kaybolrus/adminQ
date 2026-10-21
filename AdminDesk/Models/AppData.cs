using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AdminDesk.Models;

/// <summary>
/// Корневая модель данных приложения. Сериализуется в JSON-файл в AppData.
/// Пароли и другие секреты здесь не хранятся (требование безопасности ТЗ).
/// </summary>
public class AppData : INotifyPropertyChanged
{
    private ObservableCollection<DeviceEntry> _devices = new();
    private ObservableCollection<FolderEntry> _folders = new();

    public ObservableCollection<DeviceEntry> Devices
    {
        get => _devices;
        set { if (_devices != value) { _devices = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<FolderEntry> Folders
    {
        get => _folders;
        set { if (_folders != value) { _folders = value; OnPropertyChanged(); } }
    }

    /// <summary>Версия схемы файла данных. Нужна, чтобы при будущих изменениях формата
    /// корректно мигрировать или понятнно сообщать о несовместимости.</summary>
    public int SchemaVersion { get; set; } = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
