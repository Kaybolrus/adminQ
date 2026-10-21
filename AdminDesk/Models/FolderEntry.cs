using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AdminDesk.Models;

/// <summary>
/// Тип папки — влияет на выбор простой иконки в карточке.
/// </summary>
public enum FolderIconKind
{
    Folder,
    Installers,
    Shared,
    Work,
    Scan
}

/// <summary>
/// Запись «Полезная папка» — быстрый доступ к локальному или сетевому каталогу.
/// </summary>
public class FolderEntry : INotifyPropertyChanged
{
    private string _name = "";
    private string _path = "";
    private string _description = "";
    private FolderIconKind _iconKind = FolderIconKind.Folder;
    private int _order;

    [DisplayName("Название")]
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    /// <summary>Локальный путь (C:\...) или UNC-путь сетевой шары (\\server\share).</summary>
    [DisplayName("Путь")]
    public string Path
    {
        get => _path;
        set { if (_path != value) { _path = value; OnPropertyChanged(); } }
    }

    [DisplayName("Описание")]
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    [DisplayName("Иконка")]
    public FolderIconKind IconKind
    {
        get => _iconKind;
        set { if (_iconKind != value) { _iconKind = value; OnPropertyChanged(); OnPropertyChanged(nameof(IconGlyph)); OnPropertyChanged(nameof(IconKindDisplay)); } }
    }

    /// <summary>Положение карточки в списке (меньше — выше).</summary>
    [DisplayName("Порядок")]
    public int Order
    {
        get => _order;
        set { if (_order != value) { _order = value; OnPropertyChanged(); } }
    }

    /// <summary>Текстовый значок для ToolTip/подписи (не является путём и не выполняется).</summary>
    [JsonIgnore]
    public string IconKindDisplay => IconKind switch
    {
        FolderIconKind.Installers => "Установщики",
        FolderIconKind.Shared => "Общие файлы",
        FolderIconKind.Work => "Рабочие файлы",
        FolderIconKind.Scan => "Сканирование",
        _ => "Папка"
    };

    // ---------------- Вспомогательные свойства для интерфейса ----------------

    /// <summary>Простой текстовый значок для карточки (без внешних шрифтов и картинок).</summary>
    [JsonIgnore]
    public string IconGlyph => IconKind switch
    {
        FolderIconKind.Installers => "🛠",
        FolderIconKind.Shared => "👥",
        FolderIconKind.Work => "💼",
        FolderIconKind.Scan => "🖨",
        _ => "📁"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
