using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using KaraokeManager.ViewModels;
using Avalonia.Threading;
using System.Linq;

namespace KaraokeManager.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, RoomDetailWindow> roomDetailWindows = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ChooseShopLogo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn logo quán",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Ảnh") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } } },
        });
        if (files.Count > 0) viewModel.SetShopLogoPath(files[0].Path.LocalPath);
    }

    private async void RoomCard_DoubleTapped(object? sender, TappedEventArgs args)
    {
        if (sender is not Control { DataContext: RoomViewModel room } || DataContext is not MainViewModel viewModel)
            return;

        if (room.Status == RoomStatus.Available)
        {
            var answer = await new RoomOpenConfirmationWindow(room.Name).ShowDialog<bool>(this);
            if (!answer) return;
            if (viewModel.BookingManager.HasReservationAtRoomNow(room.Code))
            {
                await new RoomOpenConfirmationWindow(room.Name, $"Phòng {room.Name} đang có lịch đặt trong thời điểm này.").ShowDialog<bool>(this);
                return;
            }
            room.StartSession();
        }

        if (roomDetailWindows.TryGetValue(room.Code, out var existingWindow))
        {
            existingWindow.WindowState = WindowState.Normal;
            existingWindow.Show();
            existingWindow.Topmost = true;
            existingWindow.Activate();
            Dispatcher.UIThread.Post(() => existingWindow.Topmost = false, DispatcherPriority.Background);
            return;
        }

        OpenRoomDetail(viewModel, room);
    }

    private void OpenRoomDetail(MainViewModel viewModel, RoomViewModel room)
    {
        var detailViewModel = new RoomDetailViewModel(viewModel, room);
        var roomWindow = new RoomDetailWindow { DataContext = detailViewModel, Title = $"Chi tiết {room.Name}" };
        roomDetailWindows.Add(room.Code, roomWindow);
        roomWindow.Closed += (_, _) =>
        {
            detailViewModel.CloseCommand.Execute(null);
            roomDetailWindows.Remove(room.Code);
        };
        roomWindow.Show();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        foreach (var childWindow in roomDetailWindows.Values.ToArray()) childWindow.Close();
        roomDetailWindows.Clear();
        base.OnClosing(e);
    }
}
