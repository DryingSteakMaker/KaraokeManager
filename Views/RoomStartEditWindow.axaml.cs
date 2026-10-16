using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Globalization;

namespace KaraokeManager.Views;

public sealed record RoomStartEditResult(string Date, string Time);

public partial class RoomStartEditWindow : Window
{
    public RoomStartEditWindow() => InitializeComponent();

    public RoomStartEditWindow(string date, string time) : this()
    {
        DateInput.Text = date;
        TimeInput.Text = time;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs args)
    {
        var dateText = DateInput.Text ?? string.Empty;
        var timeText = TimeInput.Text ?? string.Empty;
        if (!DateOnly.TryParseExact(dateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            ErrorText.Text = "Ngày phải theo định dạng dd/MM/yyyy.";
            return;
        }
        if (!TimeOnly.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            ErrorText.Text = "Giờ phải theo định dạng HH:mm.";
            return;
        }
        if (date.ToDateTime(time) > DateTime.Now)
        {
            ErrorText.Text = "Thời gian bắt đầu không được ở tương lai.";
            return;
        }
        Close(new RoomStartEditResult(dateText, timeText));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close(null);
}
