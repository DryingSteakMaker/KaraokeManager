using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KaraokeManager.Views;

public partial class RoomCancelConfirmationWindow : Window
{
    public RoomCancelConfirmationWindow() => InitializeComponent();

    public RoomCancelConfirmationWindow(string roomName) : this() =>
        PromptText.Text = $"Hủy lượt sử dụng của {roomName}? Các món đã gọi sẽ bị xóa và không lập hóa đơn.";

    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close(false);
    private void Confirm_Click(object? sender, RoutedEventArgs args) => Close(true);
}
