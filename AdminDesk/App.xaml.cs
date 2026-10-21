using System.IO;
using System.Windows;
using AdminDesk.ViewModels;

namespace AdminDesk;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Непойманные исключения не должны аварийно закрывать приложение (требование ТЗ).
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"Произошла непредвиденная ошибка: {args.Exception.Message}\n" +
                "Приложение попытается продолжить работу. Данные сохранены при каждом изменении.",
                "Admin Desk — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            // Логируем в файл рядом с данными — молча не глотаем.
            try
            {
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AdminDesk", "error.log");
                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Unhandled: {args.ExceptionObject}\r\n");
            }
            catch
            {
                // Если даже лог записать не удалось — остаётся только показать сообщение ниже.
            }

            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Критическая ошибка: {ex.Message}\nПодробности: %AppData%\\AdminDesk\\error.log",
                    "Admin Desk", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        _viewModel = new MainViewModel();
        var window = new MainWindow { DataContext = _viewModel };

        // Показываем предупреждение о проблемах загрузки данных, если оно есть.
        if (!string.IsNullOrEmpty(_viewModel.LoadMessage))
        {
            window.ContentRendered += (_, _) =>
                MessageBox.Show(window, _viewModel.LoadMessage, "Admin Desk — восстановление данных",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        window.Closed += async (_, _) =>
        {
            // Корректная остановка мониторинга при закрытии приложения.
            if (_viewModel != null)
                await _viewModel.DisposeAsync();
        };

        window.Show();

        // Первая волна проверок выполняется сразу при запуске, без ожидания 60 секунд.
        _viewModel.MarkChecking();
        _viewModel.StartMonitoring();
    }
}
