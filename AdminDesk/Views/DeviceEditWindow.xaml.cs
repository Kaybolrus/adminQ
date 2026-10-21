using System.Windows;
using System.Windows.Controls;
using AdminDesk.Models;
using AdminDesk.Services;

namespace AdminDesk.Views;

/// <summary>
/// Диалог добавления/редактирования устройства. DataContext — редактируемая копия DeviceEntry.
/// Сохранение возможно только после успешной валидации (Validation.ValidateDevice).
/// </summary>
public partial class DeviceEditWindow : Window
{
    private readonly DeviceEntry _device;
    private readonly bool _isNew;

    public DeviceEditWindow(DeviceEntry device, bool isNew)
    {
        InitializeComponent();
        _device = device;
        _isNew = isNew;
        DataContext = device;

        TitleText.Text = isNew ? "Новое устройство" : $"Изменение: {device.Name}";

        // Заполняем выпадающие списки с человекочитаемыми подписями.
        TypeCombo.ItemsSource = new[]
        {
            new KeyValuePair<DeviceType, string>(DeviceType.Computer, "Компьютер"),
            new KeyValuePair<DeviceType, string>(DeviceType.Server, "Сервер"),
            new KeyValuePair<DeviceType, string>(DeviceType.NetworkDevice, "Сетевое устройство"),
            new KeyValuePair<DeviceType, string>(DeviceType.Other, "Другое")
        };
        TypeCombo.DisplayMemberPath = "Value";
        TypeCombo.SelectedValuePath = "Key";
        TypeCombo.SelectedValue = device.Type;

        MethodCombo.ItemsSource = new[]
        {
            new KeyValuePair<CheckMethod, string>(CheckMethod.Ping, "Ping (ICMP)"),
            new KeyValuePair<CheckMethod, string>(CheckMethod.TcpPort, "TCP-порт")
        };
        MethodCombo.DisplayMemberPath = "Value";
        MethodCombo.SelectedValuePath = "Key";
        MethodCombo.SelectedValue = device.CheckMethod;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Коммитим значения из ComboBox в модель перед валидацией.
        _device.Type = (DeviceType)(TypeCombo.SelectedValue ?? DeviceType.Computer);
        _device.CheckMethod = (CheckMethod)(MethodCombo.SelectedValue ?? CheckMethod.Ping);

        // Валидируем копию; другие устройства для проверки дублей берём из основного окна.
        var existing = (Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel)?.Devices
                       ?? Enumerable.Empty<DeviceEntry>();

        // Полное имя — обязательно: в WPF-проекте System.Windows.Controls.Validation
        // конфликтует с AdminDesk.Services.Validation (CS0104).
        string? error = Services.Validation.ValidateDevice(_device, existing);
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true; // закрывает диалог с результатом «Сохранить»
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
