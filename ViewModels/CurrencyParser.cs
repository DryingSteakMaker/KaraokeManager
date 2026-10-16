using System.Globalization;
using System.Linq;

namespace KaraokeManager.ViewModels;

internal static class CurrencyParser
{
    public static bool TryParse(string? input, out decimal value)
    {
        var normalized = new string((input ?? string.Empty).Trim()
            .Where(character => character is not '.' and not ',' and not ' ' and not '\u00a0')
            .ToArray());
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}
