using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Planner.App.Models;

namespace Planner.App.Services;

public sealed class GraphicalReportService : IDisposable
{
    private readonly PlannerService _planner = new();
    private readonly ExchangeRateService _exchange = new();

    public async Task<string> OpenAsync(string domain, AssistantReportPeriodKind kind, DateTime periodStart, string? targetCurrency = null)
    {
        var normalizedDomain = NormalizeDomain(domain);
        var reportCurrency = NormalizeReportCurrency(targetCurrency);
        var data = await BuildAsync(normalizedDomain, kind, periodStart.Date, reportCurrency);
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = BuildWindow(normalizedDomain, kind, periodStart.Date, reportCurrency, data);
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.Closed += (_, _) => Dispose();
            window.Show();
            window.Activate();
        });
        return $"Графический отчет открыт: {data.Title}";
    }

    private async Task<GraphicalReportData> BuildAsync(string domain, AssistantReportPeriodKind kind, DateTime periodStart, string targetCurrency)
    {
        var normalized = NormalizeDomain(domain);
        var (from, toExclusive, label) = ResolveRange(kind, periodStart);
        var title = normalized switch
        {
            "finance" => $"Графический финансовый отчет ({label})",
            "goals" => $"Графический отчет по целям ({label})",
            "reminders" => $"Графический отчет по напоминаниям ({label})",
            _ => $"Графический отчет ({label})"
        };
        var data = new GraphicalReportData(title, $"{from:dd.MM.yyyy} - {toExclusive.AddDays(-1):dd.MM.yyyy}");

        if (normalized is "finance" or "general")
            await AppendFinanceAsync(data, from, toExclusive, targetCurrency);
        if (normalized is "goals" or "general")
            await AppendGoalsAsync(data, kind, from, toExclusive);
        if (normalized is "reminders" or "general")
            await AppendRemindersAsync(data, from);

        if (data.Sections.Count == 0)
            data.Sections.Add(new GraphicalReportSection("Данные", [new GraphicalReportBar("Нет данных для отчета", 0, "0")]));

        return data;
    }

    private async Task AppendFinanceAsync(GraphicalReportData data, DateTime from, DateTime toExclusive, string targetCurrency)
    {
        var transactions = await _planner.GetTransactionsAsync(from, toExclusive);
        data.Metrics.Add(new GraphicalReportMetric("Операций", transactions.Count.ToString(CultureInfo.InvariantCulture)));
        data.Metrics.Add(new GraphicalReportMetric("Валюта отчета", targetCurrency));
        if (transactions.Count == 0)
        {
            data.Sections.Add(new GraphicalReportSection("Финансы", [new GraphicalReportBar("Операций нет", 0, "0")]));
            return;
        }

        var converted = await ConvertTransactionsAsync(transactions, targetCurrency);
        if (converted == null)
        {
            data.Metrics.Add(new GraphicalReportMetric("Конвертация", "курс недоступен"));
            AppendUnconvertedFinance(data, transactions);
            return;
        }

        if (!string.IsNullOrWhiteSpace(converted.RatesDate))
            data.Metrics.Add(new GraphicalReportMetric("Курс НБУ", converted.RatesDate));

        var income = converted.Transactions.Where(x => x.Source.Category.Type == TransactionType.Income).Sum(x => x.Amount);
        var expense = converted.Transactions.Where(x => x.Source.Category.Type == TransactionType.Expense).Sum(x => x.Amount);
        data.Metrics.Add(new GraphicalReportMetric("Доход", FormatMoney(income, targetCurrency)));
        data.Metrics.Add(new GraphicalReportMetric("Расход", FormatMoney(expense, targetCurrency)));
        data.Metrics.Add(new GraphicalReportMetric("Маржа", FormatMoney(income - expense, targetCurrency)));

        var expenses = converted.Transactions
            .Where(x => x.Source.Category.Type == TransactionType.Expense)
            .GroupBy(x => x.Source.Category.Name)
            .Select(x => new GraphicalReportBar(x.Key, (double)x.Sum(t => t.Amount), FormatMoney(x.Sum(t => t.Amount), targetCurrency)))
            .OrderByDescending(x => x.Value)
            .ToList();
        if (expenses.Count > 0)
            data.Sections.Add(new GraphicalReportSection("Расходы по категориям", expenses));

        var incomeBars = converted.Transactions
            .Where(x => x.Source.Category.Type == TransactionType.Income)
            .GroupBy(x => x.Source.Category.Name)
            .Select(x => new GraphicalReportBar(x.Key, (double)x.Sum(t => t.Amount), FormatMoney(x.Sum(t => t.Amount), targetCurrency)))
            .OrderByDescending(x => x.Value)
            .ToList();
        if (incomeBars.Count > 0)
            data.Sections.Add(new GraphicalReportSection("Доходы по категориям", incomeBars));
    }

    private async Task<ConvertedGraphicalSnapshot?> ConvertTransactionsAsync(
        IReadOnlyList<Transaction> transactions,
        string targetCurrency)
    {
        var needsRates = transactions.Any(x => CurrencyKey(x.Currency) != targetCurrency);
        DailyRates? rates = null;
        if (needsRates)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            rates = await _exchange.GetDailyRatesAsync(cts.Token);
            if (rates == null)
                return null;
        }

        var rows = new List<ConvertedGraphicalTransaction>(transactions.Count);
        foreach (var tx in transactions)
        {
            var sourceCurrency = CurrencyKey(tx.Currency);
            var amount = sourceCurrency == targetCurrency
                ? tx.Amount
                : ExchangeRateService.ConvertWithRates(tx.Amount, sourceCurrency, targetCurrency, rates!);
            if (!amount.HasValue)
                return null;

            rows.Add(new ConvertedGraphicalTransaction(tx, amount.Value));
        }

        return new ConvertedGraphicalSnapshot(rows, rates?.Date);
    }

    private static void AppendUnconvertedFinance(GraphicalReportData data, IReadOnlyList<Transaction> transactions)
    {
        foreach (var group in transactions.GroupBy(x => CurrencyKey(x.Currency)).OrderBy(x => x.Key))
        {
            var income = group.Where(x => x.Category.Type == TransactionType.Income).Sum(x => x.Amount);
            var expense = group.Where(x => x.Category.Type == TransactionType.Expense).Sum(x => x.Amount);
            data.Metrics.Add(new GraphicalReportMetric($"Доход {group.Key}", $"{income:N2}"));
            data.Metrics.Add(new GraphicalReportMetric($"Расход {group.Key}", $"{expense:N2}"));
            data.Metrics.Add(new GraphicalReportMetric($"Маржа {group.Key}", $"{income - expense:N2}"));
        }

        var expenses = transactions
            .Where(x => x.Category.Type == TransactionType.Expense)
            .GroupBy(x => $"{x.Category.Name} ({CurrencyKey(x.Currency)})")
            .Select(x => new GraphicalReportBar(x.Key, (double)x.Sum(t => t.Amount), $"{x.Sum(t => t.Amount):N2}"))
            .OrderByDescending(x => x.Value)
            .ToList();
        if (expenses.Count > 0)
            data.Sections.Add(new GraphicalReportSection("Расходы по категориям", expenses));

        var incomeBars = transactions
            .Where(x => x.Category.Type == TransactionType.Income)
            .GroupBy(x => $"{x.Category.Name} ({CurrencyKey(x.Currency)})")
            .Select(x => new GraphicalReportBar(x.Key, (double)x.Sum(t => t.Amount), $"{x.Sum(t => t.Amount):N2}"))
            .OrderByDescending(x => x.Value)
            .ToList();
        if (incomeBars.Count > 0)
            data.Sections.Add(new GraphicalReportSection("Доходы по категориям", incomeBars));
    }

    private async Task AppendGoalsAsync(GraphicalReportData data, AssistantReportPeriodKind kind, DateTime from, DateTime toExclusive)
    {
        var stats = await _planner.GetGoalStatsForRangeAsync(from, toExclusive.AddDays(-1));
        data.Metrics.Add(new GraphicalReportMetric("Отметок целей", stats.TotalCompletions.ToString(CultureInfo.InvariantCulture)));
        data.Metrics.Add(new GraphicalReportMetric("Дней с целями", stats.DaysWithCompletions.ToString(CultureInfo.InvariantCulture)));

        if (kind == AssistantReportPeriodKind.Month)
        {
            using var status = new GoalStatusService();
            var periodGoals = await status.GetPeriodGoalStatusesForMonthAsync(from.Year, from.Month);
            var recurringGoals = await status.GetRecurringGoalStatusesForMonthAsync(from.Year, from.Month);
            var bars = new List<GraphicalReportBar>();
            bars.AddRange(periodGoals.Select(x => new GraphicalReportBar(x.Goal.Title, x.Target <= 0 ? 0 : 100.0 * x.Current / x.Target, $"{x.Current}/{x.Target}")));
            bars.AddRange(recurringGoals.Select(x => new GraphicalReportBar(x.Goal.Title, x.DueDays <= 0 ? 0 : 100.0 * x.Completions / x.DueDays, $"{x.Completions}/{x.DueDays}")));
            if (bars.Count > 0)
                data.Sections.Add(new GraphicalReportSection("Прогресс целей", bars, 100));
            return;
        }

        data.Sections.Add(new GraphicalReportSection("Цели", [
            new GraphicalReportBar("Отметки выполнения", stats.TotalCompletions, stats.TotalCompletions.ToString(CultureInfo.InvariantCulture)),
            new GraphicalReportBar("Дни с отметками", stats.DaysWithCompletions, stats.DaysWithCompletions.ToString(CultureInfo.InvariantCulture))
        ]));
    }

    private async Task AppendRemindersAsync(GraphicalReportData data, DateTime periodStart)
    {
        var reminders = await _planner.GetRemindersMonthlyStatsAsync(periodStart.Year, periodStart.Month);
        var completed = reminders.Sum(x => x.Completed);
        var total = reminders.Sum(x => x.Total);
        data.Metrics.Add(new GraphicalReportMetric("Напоминаний", reminders.Count.ToString(CultureInfo.InvariantCulture)));
        data.Metrics.Add(new GraphicalReportMetric("Слотов выполнено", $"{completed}/{total}"));

        if (reminders.Count == 0)
            return;

        var bars = reminders
            .Select(x => new GraphicalReportBar(x.Title, x.Total <= 0 ? 0 : 100.0 * x.Completed / x.Total, $"{x.Completed}/{x.Total}"))
            .OrderBy(x => x.Value)
            .ToList();
        data.Sections.Add(new GraphicalReportSection("Выполнение напоминаний", bars, 100));
    }

    private Window BuildWindow(
        string domain,
        AssistantReportPeriodKind kind,
        DateTime periodStart,
        string targetCurrency,
        GraphicalReportData data)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var window = new Window
        {
            Title = data.Title,
            Width = 900,
            Height = 700,
            MinWidth = 640,
            MinHeight = 480,
            Background = Brush("#f5f5f5"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = scroll
        };
        scroll.Content = BuildContent(window, scroll, domain, kind, periodStart, targetCurrency, data);
        return window;
    }

    private StackPanel BuildContent(
        Window window,
        ScrollViewer scroll,
        string domain,
        AssistantReportPeriodKind kind,
        DateTime periodStart,
        string targetCurrency,
        GraphicalReportData data)
    {
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = data.Title,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#212121"),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock
        {
            Text = data.Subtitle,
            Foreground = Brush("#616161"),
            Margin = new Thickness(0, 4, 0, 18)
        });

        if (IsFinanceReportDomain(domain))
            root.Children.Add(CurrencySelector(window, scroll, domain, kind, periodStart, targetCurrency));

        var metricWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var metric in data.Metrics)
            metricWrap.Children.Add(MetricCard(metric));
        root.Children.Add(metricWrap);

        foreach (var section in data.Sections)
            root.Children.Add(SectionCard(section));

        return root;
    }

    private UIElement CurrencySelector(
        Window window,
        ScrollViewer scroll,
        string domain,
        AssistantReportPeriodKind kind,
        DateTime periodStart,
        string targetCurrency)
    {
        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 18)
        };
        row.Children.Add(new TextBlock
        {
            Text = "Валюта отчета:",
            Foreground = Brush("#616161"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        var combo = new System.Windows.Controls.ComboBox
        {
            Width = 120,
            ItemsSource = CurrencyInfo.DisplayCurrencies,
            SelectedItem = targetCurrency,
            VerticalAlignment = VerticalAlignment.Center
        };
        combo.SelectionChanged += async (_, _) =>
        {
            if (combo.SelectedItem is not string nextCurrency ||
                string.Equals(nextCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
                return;

            combo.IsEnabled = false;
            try
            {
                var nextData = await BuildAsync(domain, kind, periodStart, nextCurrency);
                window.Title = nextData.Title;
                scroll.Content = BuildContent(window, scroll, domain, kind, periodStart, nextCurrency, nextData);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    window,
                    "Не удалось перестроить отчет: " + ex.Message,
                    "Отчет",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                combo.IsEnabled = true;
            }
        };
        row.Children.Add(combo);
        return row;
    }

    private static Border MetricCard(GraphicalReportMetric metric)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = metric.Label, Foreground = Brush("#616161"), FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = metric.Value, Foreground = Brush("#1a237e"), FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) });
        return Card(panel, new Thickness(0, 0, 10, 10), 180);
    }

    private static Border SectionCard(GraphicalReportSection section)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#212121"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var max = Math.Max(1, section.MaxValue ?? section.Bars.Max(x => Math.Abs(x.Value)));
        foreach (var bar in section.Bars)
            panel.Children.Add(BarRow(bar, max));
        return Card(panel, new Thickness(0, 0, 0, 12), double.NaN);
    }

    private static Grid BarRow(GraphicalReportBar bar, double max)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var label = new TextBlock { Text = bar.Label, Foreground = Brush("#212121"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var progress = new System.Windows.Controls.ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Min(100, Math.Abs(bar.Value) / max * 100),
            Height = 18,
            Foreground = Brush(bar.Value < 0 ? "#c62828" : "#2e7d32"),
            Background = Brush("#eeeeee"),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        Grid.SetColumn(progress, 1);
        grid.Children.Add(progress);

        var value = new TextBlock { Text = bar.ValueText, Foreground = Brush("#616161"), FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    private static Border Card(UIElement child, Thickness margin, double width)
    {
        return new Border
        {
            Width = double.IsNaN(width) ? double.NaN : width,
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = Brush("#e0e0e0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Margin = margin,
            Child = child
        };
    }

    private static (DateTime From, DateTime ToExclusive, string Label) ResolveRange(AssistantReportPeriodKind kind, DateTime periodStart)
    {
        return kind switch
        {
            AssistantReportPeriodKind.Week => (GetWeekStart(periodStart), GetWeekStart(periodStart).AddDays(7), $"неделя {GetWeekStart(periodStart):dd.MM} - {GetWeekStart(periodStart).AddDays(6):dd.MM}"),
            AssistantReportPeriodKind.Month => (new DateTime(periodStart.Year, periodStart.Month, 1), new DateTime(periodStart.Year, periodStart.Month, 1).AddMonths(1), $"{periodStart:MM.yyyy}"),
            _ => (periodStart.Date, periodStart.Date.AddDays(1), periodStart.ToString("dd.MM.yyyy"))
        };
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-diff);
    }

    private static string CurrencyKey(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? CurrencyInfo.UAH : currency.Trim().ToUpperInvariant();
    }

    private static string NormalizeDomain(string domain)
    {
        return string.IsNullOrWhiteSpace(domain) ? "general" : domain.Trim().ToLowerInvariant();
    }

    private static bool IsFinanceReportDomain(string domain)
    {
        return NormalizeDomain(domain) is "finance" or "general";
    }

    private static string NormalizeReportCurrency(string? currency)
    {
        var value = CurrencyKey(currency);
        return CurrencyInfo.DisplayCurrencies.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? value
            : CurrencyInfo.UAH;
    }

    private static string FormatMoney(decimal amount, string currency)
    {
        return $"{amount:N2} {currency}";
    }

    private static SolidColorBrush Brush(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    public void Dispose()
    {
        _planner.Dispose();
    }

    private sealed record ConvertedGraphicalSnapshot(
        IReadOnlyList<ConvertedGraphicalTransaction> Transactions,
        string? RatesDate);

    private sealed record ConvertedGraphicalTransaction(Transaction Source, decimal Amount);
}

public sealed record GraphicalReportData(string Title, string Subtitle)
{
    public List<GraphicalReportMetric> Metrics { get; } = new();
    public List<GraphicalReportSection> Sections { get; } = new();
}

public sealed record GraphicalReportMetric(string Label, string Value);
public sealed record GraphicalReportSection(string Title, IReadOnlyList<GraphicalReportBar> Bars, double? MaxValue = null);
public sealed record GraphicalReportBar(string Label, double Value, string ValueText);
