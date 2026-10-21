using System.Windows;
using System.Windows.Controls;
using AdminDesk.Models;
using AdminDesk.Services;

namespace AdminDesk.Views;

/// <summary>
/// Диалог добавления/редактирования папки. DataContext — редактируемая копия FolderEntry.
/// Сохранение возможно только после успешной валидации (Validation.ValidateFolder).
/// </summary>
public partial class FolderEditWindow : Window
{
    private readonly FolderEntry _folder;

    public FolderEditWindow(FolderEntry folder, bool isNew)
    {
        InitializeComponent();
        _folder = folder;
        DataContext = folder;

        TitleText.Text = isNew ? "Новая папка" : $"Изменение: {folder.Name}";

        IconCombo.ItemsSource = new[]
        {
            new KeyValuePair<FolderIconKind, string>(FolderIconKind.Folder, "📁 Обычная папка"),
            new KeyValuePair<FolderIconKind, string>(FolderIconKind.Installers, "🛠 Установщики"),
            new KeyValuePair<FolderIconKind, string>(FolderIconKind.Shared, "👥 Общие файлы"),
            new KeyValuePair<FolderIconKind, string>(FolderIconKind.Work, "💼 Рабочие файлы"),
            new KeyValuePair<FolderIconKind, string>(FolderIconKind.Scan, "🖨 Сканирование")
        };
        IconCombo.DisplayMemberPath = "Value";
        IconCombo.SelectedValuePath = "Key";
        IconCombo.SelectedValue = folder.IconKind;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _folder.IconKind = (FolderIconKind)(IconCombo.SelectedValue ?? FolderIconKind.Folder);

        string? error = Validation.ValidateFolder(_folder, Enumerable.Empty<FolderEntry>());
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
