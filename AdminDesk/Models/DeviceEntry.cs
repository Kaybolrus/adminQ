using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AdminDesk.Models;

/// <summary>
/// Тип устройства (отображается в интерфейсе как человекочитаемая строка).
/// </summary>
public enum DeviceType
{
    Computer,
    Server,
    NetworkDevice,
    Other
}

/// <summary>
/// Способ проверки доступности устройства.
/// </summary>
public enum CheckMethod
{
    /// <summary>ICMP Ping.</summary>
    Ping,
    /// <summary>TCP-подключение к указанному порту (полезно, если Ping запрещён политикой сети).</summary>
    TcpPort
}

/// <summary>
/// Статус доступности устройства, отображаемый в карточке.
/// </summary>
public enum AvailabilityStatus
{
    /// <summary>Устройство ещё не проверялось с момента запуска приложения.</summary>
    NotChecked,
    /// <summary>Проверка выполняется прямо сейчас.</summary>
    Checking,
    /// <summary>Устройство ответило на проверку.</summary>
    Online,
    /// <summary>Устройство не ответило за время тайм-аута.</summary>
    Offline
}

/// <summary>
/// Запись об устройстве (компьютер или сервер) в списке администратора.
/// </summary>
public class DeviceEntry : INotifyPropertyChanged
{
    private string _name = "";
    private string _address = "";
    private DeviceType _type = DeviceType.Computer;
    private string _description = "";
    private bool _monitorEnabled = true;
    private CheckMethod _checkMethod = CheckMethod.Ping;
    private int _tcpPort = 3389;
    private bool _rdpAllowed = true;

    // Поля состояния мониторинга не сохраняются в JSON — они живут только во время работы приложения.
    private AvailabilityStatus _status = AvailabilityStatus.NotChecked;
    private DateTime? _lastCheckTime;

    [DisplayName("Имя")]
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    /// <summary>IP-адрес или DNS-имя устройства.</summary>
    [DisplayName("Адрес")]
    public string Address
    {
        get => _address;
        set { if (_address != value) { _address = value; OnPropertyChanged(); } }
    }

    [DisplayName("Тип")]
    public DeviceType Type
    {
        get => _type;
        set { if (_type != value) { _type = value; OnPropertyChanged(); OnPropertyChanged(nameof(TypeDisplay)); } }
    }

    [DisplayName("Описание")]
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    /// <summary>Выполнять ли автоматическую проверку доступности этого устройства.</summary>
    [DisplayName("Мониторинг")]
    public bool MonitorEnabled
    {
        get => _monitorEnabled;
        set { if (_monitorEnabled != value) { _monitorEnabled = value; OnPropertyChanged(); } }
    }

    [DisplayName("Способ проверки")]
    public CheckMethod CheckMethod
    {
        get => _checkMethod;
        set { if (_checkMethod != value) { _checkMethod = value; OnPropertyChanged(); } }
    }

    /// <summary>TCP-порт для проверки способом TcpPort (1..65535).</summary>
    [DisplayName("TCP-порт")]
    public int TcpPort
    {
        get => _tcpPort;
        set { if (_tcpPort != value) { _tcpPort = value; OnPropertyChanged(); } }
    }

    /// <summary>Разрешён ли запуск RDP к этому устройству (показывать кнопку «Подключить RDP»).
    /// Наличие кнопки не гарантирует, что RDP-сервис на устройстве доступен — это проверяет mstsc.exe.</summary>
    [DisplayName("RDP разрешён")]
    public bool RdpAllowed
    {
        get => _rdpAllowed;
        set { if (_rdpAllowed != value) { _rdpAllowed = value; OnPropertyChanged(); } }
    }

    // ---------------- Состояние мониторинга (не сериализуется) ----------------

    [JsonIgnore]
    public AvailabilityStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(StatusBrushKey));
            }
        }
    }

    [JsonIgnore]
    public DateTime? LastCheckTime
    {
        get => _lastCheckTime;
        set { if (_lastCheckTime != value) { _lastCheckTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(LastCheckDisplay)); } }
    }

    // ---------------- Вспомогательные свойства для интерфейса ----------------

    public string TypeDisplay => Type switch
    {
        DeviceType.Computer => "Компьютер",
        DeviceType.Server => "Сервер",
        DeviceType.NetworkDevice => "Сетевое устройство",
        _ => "Другое"
    };

    public string StatusDisplay => Status switch
    {
        AvailabilityStatus.Online => "Доступен",
        AvailabilityStatus.Offline => "Нет ответа",
        AvailabilityStatus.Checking => "Проверка",
        _ => "Не проверялся"
    };

    /// <summary>Ключ кисти из словаря ресурсов для цветовой индикации статуса.</summary>
    public string StatusBrushKey => Status switch
    {
        AvailabilityStatus.Online => "StatusOnlineBrush",
        AvailabilityStatus.Offline => "StatusOfflineBrush",
        AvailabilityStatus.Checking => "StatusCheckingBrush",
        _ => "StatusUnknownBrush"
    };

    public string LastCheckDisplay =>
        LastCheckTime.HasValue ? LastCheckTime.Value.ToString("HH:mm:ss") : "—";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
