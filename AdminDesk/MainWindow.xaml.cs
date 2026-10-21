using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdminDesk.Models;
using AdminDesk.ViewModels;

namespace AdminDesk;

/// <summary>
/// Code-behind главного окна. Содержит только тривиальную «проводку» событий UI к командам
/// ViewModel — деловая логика в MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    // ---------------- Навигация ----------------

    private void NavDevices_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Vm.IsDevicesView = true;
        SectionTitle.Text = "Устройства";
    }

    private void NavFolders_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Vm.IsDevicesView = false;
        SectionTitle.Text = "Полезные папки";
    }

    // ---------------- Устройства ----------------

    private void DeviceCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeviceEntry device } && Vm != null)
            Vm.SelectedDevice = device;
    }

    private void RdpButton_Click(object sender, RoutedEventArgs e)
    {
        // Останавливаем всплытие события, чтобы клик по кнопке не считался кликом по карточке.
        e.Handled = true;
        if (sender is Button { Tag: DeviceEntry device } && Vm != null)
        {
            Vm.SelectedDevice = device;
            Vm.ConnectRdpCommand.Execute(device);
        }
    }

    private void EditDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: DeviceEntry device } && Vm != null)
        {
            Vm.SelectedDevice = device;
            Vm.EditDeviceCommand.Execute(null);
        }
    }

    private void DeleteDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: DeviceEntry device } && Vm != null)
        {
            Vm.SelectedDevice = device;
            Vm.DeleteDeviceCommand.Execute(null);
        }
    }

    // ---------------- Папки ----------------

    private void FolderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderEntry folder } && Vm != null)
        {
            Vm.SelectedFolder = folder;
            Vm.OpenFolderCommand.Execute(folder);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: FolderEntry folder } && Vm != null)
        {
            Vm.SelectedFolder = folder;
            Vm.MoveFolderUpCommand.Execute(null);
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: FolderEntry folder } && Vm != null)
        {
            Vm.SelectedFolder = folder;
            Vm.MoveFolderDownCommand.Execute(null);
        }
    }

    private void EditFolderButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: FolderEntry folder } && Vm != null)
        {
            Vm.SelectedFolder = folder;
            Vm.EditFolderCommand.Execute(null);
        }
    }

    private void DeleteFolderButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: FolderEntry folder } && Vm != null)
        {
            Vm.SelectedFolder = folder;
            Vm.DeleteFolderCommand.Execute(null);
        }
    }
}
