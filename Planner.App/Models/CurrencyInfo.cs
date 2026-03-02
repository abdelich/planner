namespace Planner.App.Models;

public static class CurrencyInfo
{
    public const string SEK = "SEK";
    public const string UAH = "UAH";
    public const string USD = "USD";

    public static readonly IReadOnlyList<string> TransactionCurrencies = new[] { SEK, UAH, USD };

    public static readonly IReadOnlyList<string> DisplayCurrencies = new[] { UAH, SEK, USD };

    public static string DisplayName(string code) => code switch
    {
        SEK => "Шведская крона (SEK)",
        UAH => "Гривна (UAH)",
        USD => "Доллар (USD)",
        _ => code
    };

    public static string Symbol(string code) => code switch
    {
        SEK => " kr",
        UAH => " грн",
        USD => " $",
        _ => " " + code
    };
}
