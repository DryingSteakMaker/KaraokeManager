using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KaraokeManager.Views;

public partial class RoomOpenConfirmationWindow : Window
{
    public RoomOpenConfirmationWindow() => InitializeComponent();

    public RoomOpenConfirmationWindow(string roomName, string? notice = null)
    {
        InitializeComponent();
        PromptText.Text = notice ?? $"Mở phòng {roomName}?";
        if (notice is not null)
        {
            Title = "Thông báo";
            CancelButton.IsVisible = false;
            OkButton.Content = "Đóng";
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs args) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close(false);
}
