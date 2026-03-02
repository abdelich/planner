using System.Net.Http;
using System.Text.Json;
using Planner.App.Models;

namespace Planner.App.Services;

public class ExchangeRateService
{
    private const string NbuApiAllUrl = "https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?json";
    private const string NbuApiUrl = "https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?valcode={0}&json";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private decimal? _sekPerUah;
    private DateTime? _cacheDate;
    private DailyRates? _dailyRates;

    public async Task<DailyRates?> GetDailyRatesAsync(CancellationToken ct = default)
    {
        var today = DateTime.Today;
        if (_dailyRates != null && _cacheDate == today)
            return _dailyRates;

        try
        {
            var response = await HttpClient.GetAsync(NbuApiAllUrl, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            var list = JsonSerializer.Deserialize<List<NbuRateItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (list == null) return _dailyRates;

            decimal? RateUah(string cc) => list.FirstOrDefault(x => string.Equals(x.Cc, cc, StringComparison.OrdinalIgnoreCase))?.Rate;
            var sekUah = RateUah("SEK");
            var usdUah = RateUah("USD");
            var eurUah = RateUah("EUR");

            if (sekUah is not { } rSek || rSek <= 0) return _dailyRates;
            var sekToUah = rSek;
            var usdToUah = usdUah is { } rUsd && rUsd > 0 ? rUsd : (decimal?)null;
            decimal? usdToSek = usdToUah.HasValue ? usdToUah.Value / rSek : null;
            decimal? eurToSek = eurUah is { } rEur && rEur > 0 ? rEur / rSek : null;

            var dateStr = list.FirstOrDefault()?.Exchangedate ?? today.ToString("dd.MM.yyyy");
            _dailyRates = new DailyRates(sekToUah, usdToUah, usdToSek, eurToSek, dateStr);
            _sekPerUah = 1m / rSek;
            _cacheDate = today;
            return _dailyRates;
        }
        catch
        {
            return _dailyRates;
        }
    }

    public async Task<decimal?> GetRateToUahAsync(string currencyCode, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(currencyCode)) return null;
        if (currencyCode == CurrencyInfo.UAH) return 1m;
        var rates = await GetDailyRatesAsync(ct);
        if (rates == null) return null;
        if (currencyCode == CurrencyInfo.SEK) return rates.SekToUah;
        if (currencyCode == CurrencyInfo.USD) return rates.UsdToUah;
        return null;
    }

    public async Task<decimal?> ConvertToCurrencyAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken ct = default)
    {
        var rates = await GetDailyRatesAsync(ct);
        return rates != null ? ConvertWithRates(amount, fromCurrency, toCurrency, rates) : null;
    }

    public static decimal? ConvertWithRates(decimal amount, string fromCurrency, string toCurrency, DailyRates rates)
    {
        if (string.IsNullOrEmpty(fromCurrency) || string.IsNullOrEmpty(toCurrency)) return null;
        if (fromCurrency == toCurrency) return amount;
        decimal rateToUah(string c) => c == CurrencyInfo.UAH ? 1m : c == CurrencyInfo.SEK ? rates.SekToUah : c == CurrencyInfo.USD ? (rates.UsdToUah ?? 0) : 0;
        var fromRate = rateToUah(fromCurrency);
        var toRate = rateToUah(toCurrency);
        if (fromRate == 0 || toRate == 0) return null;
        return amount * fromRate / toRate;
    }

    public async Task<decimal?> ConvertToUahAsync(decimal amount, string currencyCode, CancellationToken ct = default)
    {
        var rate = await GetRateToUahAsync(currencyCode, ct);
        return rate.HasValue ? amount * rate.Value : null;
    }

    public async Task<decimal?> GetSekPerUahAsync(CancellationToken ct = default)
    {
        _ = await GetDailyRatesAsync(ct);
        return _sekPerUah;
    }

    public async Task<decimal?> ConvertUahToSekAsync(decimal amountUah, CancellationToken ct = default)
    {
        var rate = await GetSekPerUahAsync(ct);
        return rate.HasValue ? amountUah * rate.Value : null;
    }

    public async Task<decimal?> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken ct = default)
    {
        if (fromCurrency == toCurrency) return amount;
        if (fromCurrency == CurrencyInfo.UAH && toCurrency == CurrencyInfo.SEK)
        {
            var sekPerUah = await GetSekPerUahAsync(ct);
            return sekPerUah.HasValue ? amount * sekPerUah.Value : null;
        }
        if (fromCurrency == CurrencyInfo.SEK && toCurrency == CurrencyInfo.UAH)
        {
            var uahPerSek = await GetRateToUahAsync(CurrencyInfo.SEK, ct);
            return uahPerSek.HasValue ? amount * uahPerSek.Value : null;
        }
        return null;
    }

    private sealed class NbuRateItem
    {
        public string Cc { get; set; } = "";
        public decimal Rate { get; set; }
        public string Exchangedate { get; set; } = "";
    }
}

public record DailyRates(decimal SekToUah, decimal? UsdToUah, decimal? UsdToSek, decimal? EurToSek, string Date);
