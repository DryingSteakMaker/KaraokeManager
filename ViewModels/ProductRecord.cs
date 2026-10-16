using System.Globalization;

namespace KaraokeManager.ViewModels;

public sealed record ProductRecord(
    string Code,
    string Name,
    string Category,
    string Unit,
    decimal SalePrice,
    decimal CostPrice)
{
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    public string SalePriceText => $"{SalePrice.ToString("N0", VietnameseCulture)} ₫";
    public string CostPriceText => $"{CostPrice.ToString("N0", VietnameseCulture)} ₫";
}
