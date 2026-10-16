using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Globalization;

namespace KaraokeManager.Views;

public partial class OrderQuantityWindow : Window
{
    public OrderQuantityWindow() => InitializeComponent();

    public OrderQuantityWindow(string itemName, int quantity) : this()
    {
        ItemName.Text = $"Mặt hàng: {itemName}";
        QuantityBox.Text = quantity.ToString(CultureInfo.InvariantCulture);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close(null);

    private void Save_Click(object? sender, RoutedEventArgs args)
    {
        if (int.TryParse(QuantityBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) && quantity > 0)
            Close((int?)quantity);
        else ErrorText.Text = "Số lượng phải là số nguyên dương.";
    }
}
