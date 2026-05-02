using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Models;
using Planner.App.Services;

namespace Planner.App.ViewModels;

public partial class FinanceViewModel : ObservableObject
{
    private readonly PlannerService _service = new();
    private readonly ExchangeRateService _rates = new();
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private bool _loadStarted;

    [ObservableProperty] private string _loadError = "";

    [ObservableProperty] private DateTime _selectedMonth = DateTime.Today;
    [ObservableProperty] private string _selectedCurrency = CurrencyInfo.UAH;
    [ObservableProperty] private int _selectedStatsFilterIndex;
    [ObservableProperty] private decimal _monthIncome;
    [ObservableProperty] private decimal _monthExpenses;
    [ObservableProperty] private decimal _monthMargin;
    [ObservableProperty] private string _monthCaption = "";

    [ObservableProperty] private ObservableCollection<CategorySumItem> _incomeByCategory = new();
    [ObservableProperty] private ObservableCollection<CategorySumItem> _expensesByCategory = new();
    [ObservableProperty] private ObservableCollection<TransactionItemViewModel> _transactions = new();
    [ObservableProperty] private ObservableCollection<FinanceCategory> _incomeCategories = new();
    [ObservableProperty] private ObservableCollection<FinanceCategory> _expenseCategories = new();

    public string CurrencySymbol => CurrencyInfo.Symbol(SelectedCurrency);
    public string SavingsPanelCurrencySymbol => CurrencyInfo.Symbol(SavingsPanelCurrency);

    public IEnumerable<FinanceCategory> TransactionCategories => NewTransactionType == TransactionType.Income ? IncomeCategories : ExpenseCategories;

    public bool CanSaveTransaction => (NewTransactionType == TransactionType.Income ? IncomeCategories : ExpenseCategories).Any();

    [ObservableProperty] private bool _isAddTransactionOpen;
    [ObservableProperty] private bool _isAddCategoryOpen;
    [ObservableProperty] private OperationKind _newOperationKind = OperationKind.Expense;
    [ObservableProperty] private TransactionType _newTransactionType = TransactionType.Expense;
    [ObservableProperty] private FinanceCategory? _newTransactionCategory;
    [ObservableProperty] private string _newTransactionCurrency = CurrencyInfo.SEK;
    [ObservableProperty] private string _newTransactionAmount = "";
    [ObservableProperty] private DateTime _newTransactionDate = DateTime.Today;
    [ObservableProperty] private string _newTransactionNote = "";
    [ObservableProperty] private SavingsItemViewModel? _selectedSavingsForTransaction;
    [ObservableProperty] private bool _isTransferBetweenSavings;
    [ObservableProperty] private SavingsItemViewModel? _transferFromSavings;
    [ObservableProperty] private SavingsItemViewModel? _transferToSavings;
    [ObservableProperty] private string _newCategoryName = "";
    [ObservableProperty] private TransactionType _newCategoryType = TransactionType.Expense;

    [ObservableProperty] private string _rateSekToUahText = "";
    [ObservableProperty] private string _rateUsdToUahText = "";
    [ObservableProperty] private string _rateUsdToSekText = "";
    [ObservableProperty] private string _rateEurToSekText = "";
    [ObservableProperty] private string _ratesDateText = "";
    [ObservableProperty] private bool _isLoadingRates;

    public string RefreshRatesButtonText => IsLoadingRates ? "Загрузка..." : "Обновить курс";

    [ObservableProperty] private ObservableCollection<SavingsItemViewModel> _savingsEntries = new();
    [ObservableProperty] private decimal _totalSavingsDisplay;
    [ObservableProperty] private bool _isAddSavingsOpen;
    [ObservableProperty] private bool _isEditSavingsOpen;
    [ObservableProperty] private SavingsCategory? _newSavingsCategory;
    [ObservableProperty] private string _newSavingsName = "";
    [ObservableProperty] private string _newSavingsCurrency = CurrencyInfo.UAH;
    [ObservableProperty] private string _newSavingsBalance = "";
    [ObservableProperty] private SavingsItemViewModel? _editingSavingsItem;
    [ObservableProperty] private string _editSavingsItemName = "";
    [ObservableProperty] private string _editSavingsBalance = "";

    [ObservableProperty] private ObservableCollection<SavingsCategory> _savingsCategories = new();
    [ObservableProperty] private bool _isAddSavingsCategoryOpen;
    [ObservableProperty] private bool _isEditSavingsCategoryOpen;
    [ObservableProperty] private string _newSavingsCategoryName = "";
    [ObservableProperty] private SavingsCategory? _editingSavingsCategory;
    [ObservableProperty] private string _editSavingsCategoryName = "";
    [ObservableProperty] private string _savingsCategoryError = "";

    [ObservableProperty] private bool _isCategoriesPanelOpen;
    [ObservableProperty] private bool _isSavingsPanelOpen;
    [ObservableProperty] private FinanceCategory? _editingFinanceCategory;
    [ObservableProperty] private string _editFinanceCategoryName = "";
    [ObservableProperty] private bool _isEditFinanceCategoryOpen;
    [ObservableProperty] private string _savingsPanelCurrency = CurrencyInfo.UAH;
    [ObservableProperty] private decimal _panelTotalSavings;
    [ObservableProperty] private ObservableCollection<SavingsMonthChartItem> _savingsMonthlyChart = new();
    [ObservableProperty] private bool _isSavingsChartsOpen;

    public FinanceViewModel()
    {
        var raw = SelectedMonth.ToString("MMMM yyyy", Ru);
        _monthCaption = raw.Length > 0 ? char.ToUpper(raw[0], Ru) + raw[1..] : raw;
    }

    public void StartLoad()
    {
        if (_loadStarted) return;
        _loadStarted = true;
        _ = LoadAsync();
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Task.Run(() => LoadRatesInBackground(this, dispatcher));
    }

    private static void LoadRatesInBackground(FinanceViewModel vm, Dispatcher dispatcher)
    {
        try
        {
            if (!string.IsNullOrEmpty(vm.RateSekToUahText) && vm.RateSekToUahText != "Не удалось загрузить курсы.") return;
            var rates = vm._rates.GetDailyRatesAsync().GetAwaiter().GetResult();
            if (rates == null) return;
            dispatcher.Invoke(() =>
            {
                vm.RateSekToUahText = $"1 SEK = {rates.SekToUah:N4} UAH";
                vm.RateUsdToUahText = rates.UsdToUah.HasValue ? $"1 USD = {rates.UsdToUah.Value:N4} UAH" : "—";
                vm.RateUsdToSekText = rates.UsdToSek.HasValue ? $"1 USD = {rates.UsdToSek.Value:N4} SEK" : "—";
                vm.RateEurToSekText = rates.EurToSek.HasValue ? $"1 EUR = {rates.EurToSek.Value:N4} SEK" : "—";
                vm.RatesDateText = $"Курсы НБУ на {rates.Date}";
            });
        }
        catch { }
    }

    public async Task LoadAsync()
    {
        LoadError = "";
        try
        {
            var loadService = new PlannerService();
            await LoadCategoriesAsync(loadService);
            await LoadSavingsCategoriesAsync(loadService);
            await LoadMonthStatsAsync(loadService);
            await LoadTransactionsAsync(loadService);
            await LoadSavingsAsync(loadService);
        }
        catch (Exception ex)
        {
            LoadError = "Ошибка загрузки: " + ex.Message;
            var raw = SelectedMonth.ToString("MMMM yyyy", Ru);
            MonthCaption = raw.Length > 0 ? char.ToUpper(raw[0], Ru) + raw[1..] : raw;
        }
    }

    /// <summary>Статистика и список операций за выбранный месяц — только в UI-потоке (нельзя цепочкой ContinueWith с пула потоков).</summary>
    private async Task ReloadMonthStatsAndTransactionsAsync()
    {
        try
        {
            await LoadMonthStatsAsync(_service);
            await LoadTransactionsAsync(_service);
        }
        catch (Exception ex)
        {
            LoadError = "Ошибка загрузки: " + ex.Message;
            var raw = SelectedMonth.ToString("MMMM yyyy", Ru);
            MonthCaption = raw.Length > 0 ? char.ToUpper(raw[0], Ru) + raw[1..] : raw;
        }
    }

    private async Task LoadSavingsCategoriesAsync(PlannerService loadService)
    {
        var list = await loadService.GetSavingsCategoriesAsync();
        SavingsCategories.Clear();
        foreach (var c in list) SavingsCategories.Add(c);
    }

    private Task LoadSavingsCategoriesAsync() => LoadSavingsCategoriesAsync(_service);
    private Task LoadCategoriesAsync() => LoadCategoriesAsync(_service);

    private async Task LoadCategoriesAsync(PlannerService loadService)
    {
        var income = await loadService.GetFinanceCategoriesAsync(TransactionType.Income);
        var expense = await loadService.GetFinanceCategoriesAsync(TransactionType.Expense);
        IncomeCategories.Clear();
        ExpenseCategories.Clear();
        foreach (var c in income) IncomeCategories.Add(c);
        foreach (var c in expense) ExpenseCategories.Add(c);
        OnPropertyChanged(nameof(TransactionCategories));
        OnPropertyChanged(nameof(CanSaveTransaction));
    }

    private StatsFilterType CurrentStatsFilter =>
        SelectedStatsFilterIndex == 1 ? StatsFilterType.IncomeOnly
        : SelectedStatsFilterIndex == 2 ? StatsFilterType.ExpenseOnly
        : StatsFilterType.All;

    private async Task LoadMonthStatsAsync(PlannerService loadService)
    {
        var from = new DateTime(SelectedMonth.Year, SelectedMonth.Month, 1);
        var toExclusive = from.AddMonths(1);
        var list = await loadService.GetTransactionsAsync(from, toExclusive, null, CurrentStatsFilter);
        var rates = await _rates.GetDailyRatesAsync();
        var symbol = CurrencyInfo.Symbol(SelectedCurrency);
        decimal income = 0, expenses = 0;
        var incomeByCat = new Dictionary<string, decimal>();
        var expensesByCat = new Dictionary<string, decimal>();
        foreach (var t in list)
        {
            var converted = t.Currency == SelectedCurrency
                ? t.Amount
                : (rates != null && ExchangeRateService.ConvertWithRates(t.Amount, t.Currency, SelectedCurrency, rates) is { } c ? c : 0);
            if (t.Category.Type == TransactionType.Income)
            {
                income += converted;
                incomeByCat.TryGetValue(t.Category.Name, out var v);
                incomeByCat[t.Category.Name] = v + converted;
            }
            else
            {
                expenses += converted;
                expensesByCat.TryGetValue(t.Category.Name, out var v);
                expensesByCat[t.Category.Name] = v + converted;
            }
        }
        var incomeItems = incomeByCat.Select(kv => new CategorySumItem(kv.Key, kv.Value, symbol)).OrderByDescending(x => x.Sum).ToList();
        var expenseItems = expensesByCat.Select(kv => new CategorySumItem(kv.Key, kv.Value, symbol)).OrderByDescending(x => x.Sum).ToList();
        var raw = SelectedMonth.ToString("MMMM yyyy", Ru);
        MonthCaption = raw.Length > 0 ? char.ToUpper(raw[0], Ru) + raw[1..] : raw;
        MonthIncome = income;
        MonthExpenses = expenses;
        MonthMargin = income - expenses;
        IncomeByCategory.Clear();
        ExpensesByCategory.Clear();
        foreach (var x in incomeItems) IncomeByCategory.Add(x);
        foreach (var x in expenseItems) ExpensesByCategory.Add(x);
        OnPropertyChanged(nameof(CurrencySymbol));
    }

    private Task LoadMonthStatsAsync() => LoadMonthStatsAsync(_service);

    private async Task LoadTransactionsAsync(PlannerService loadService)
    {
        var from = new DateTime(SelectedMonth.Year, SelectedMonth.Month, 1);
        var toExclusive = from.AddMonths(1);
        var list = await loadService.GetTransactionsAsync(from, toExclusive, null, CurrentStatsFilter);
        var rates = await _rates.GetDailyRatesAsync();
        var symbol = CurrencyInfo.Symbol(SelectedCurrency);
        var items = list.Select(t =>
        {
            decimal displayAmount;
            string displaySymbol;
            if (rates != null && ExchangeRateService.ConvertWithRates(t.Amount, t.Currency, SelectedCurrency, rates) is { } c)
            {
                displayAmount = c;
                displaySymbol = symbol;
            }
            else
            {
                displayAmount = t.Amount;
                displaySymbol = CurrencyInfo.Symbol(t.Currency);
            }
            return new TransactionItemViewModel(t, displayAmount, displaySymbol, _service, () => _ = LoadAsync());
        }).ToList();
        Transactions.Clear();
        foreach (var i in items) Transactions.Add(i);
    }

    private Task LoadTransactionsAsync() => LoadTransactionsAsync(_service);

    partial void OnSelectedMonthChanged(DateTime value) => _ = ReloadMonthStatsAndTransactionsAsync();
    partial void OnSelectedCurrencyChanged(string value) => _ = LoadAsync();
    partial void OnSelectedStatsFilterIndexChanged(int value) => _ = ReloadMonthStatsAndTransactionsAsync();

    partial void OnNewTransactionTypeChanged(TransactionType value)
    {
        NewTransactionCategory = value == TransactionType.Income
            ? IncomeCategories.FirstOrDefault()
            : ExpenseCategories.FirstOrDefault();
        OnPropertyChanged(nameof(TransactionCategories));
        OnPropertyChanged(nameof(CanSaveTransaction));
    }

    [RelayCommand]
    private void PrevMonth() => SelectedMonth = SelectedMonth.AddMonths(-1);

    [RelayCommand]
    private void NextMonth() => SelectedMonth = SelectedMonth.AddMonths(1);

    [RelayCommand]
    private void OpenAddTransaction()
    {
        SaveTransactionError = "";
        NewOperationKind = OperationKind.Expense;
        NewTransactionType = TransactionType.Expense;
        NewTransactionCategory = ExpenseCategories.FirstOrDefault();
        NewTransactionCurrency = SelectedCurrency;
        NewTransactionAmount = "";
        NewTransactionDate = DateTime.Today;
        NewTransactionNote = "";
        SelectedSavingsForTransaction = SavingsEntries.FirstOrDefault();
        IsTransferBetweenSavings = false;
        TransferFromSavings = SavingsEntries.FirstOrDefault();
        TransferToSavings = SavingsEntries.Skip(1).FirstOrDefault() ?? TransferFromSavings;
        IsAddTransactionOpen = true;
    }

    partial void OnNewOperationKindChanged(OperationKind value)
    {
        IsTransferBetweenSavings = value == OperationKind.Transfer;
        if (value == OperationKind.Income) NewTransactionType = TransactionType.Income;
        else if (value == OperationKind.Expense) NewTransactionType = TransactionType.Expense;
    }

    [RelayCommand]
    private void CloseAddTransaction() => IsAddTransactionOpen = false;

    [ObservableProperty] private string _saveTransactionError = "";

    [RelayCommand]
    private async Task SaveTransaction()
    {
        SaveTransactionError = "";
        if (!decimal.TryParse(NewTransactionAmount?.Replace(",", ".").Trim(), NumberStyles.Any, Ru, out var amount) || amount <= 0)
        {
            SaveTransactionError = "Введите корректную сумму больше нуля.";
            return;
        }
        if (!IsTransferBetweenSavings && NewTransactionCategory == null)
        {
            SaveTransactionError = "Выберите категорию.";
            return;
        }
        if (!IsTransferBetweenSavings && SelectedSavingsForTransaction == null)
        {
            SaveTransactionError = "Выберите счёт сбережений.";
            return;
        }
        var selectedCategory = NewTransactionCategory!;
        var selectedAccount = SelectedSavingsForTransaction!;
        var currency = string.IsNullOrWhiteSpace(NewTransactionCurrency) ? CurrencyInfo.SEK : NewTransactionCurrency.Trim();
        if (IsTransferBetweenSavings)
        {
            if (TransferFromSavings == null || TransferToSavings == null)
            {
                SaveTransactionError = "Выберите счёт списания и счёт зачисления.";
                return;
            }
            if (TransferFromSavings.Entry.Id == TransferToSavings.Entry.Id)
            {
                SaveTransactionError = "Счёт списания и зачисления должны быть разными.";
                return;
            }

            try
            {
                var from = TransferFromSavings.Entry;
                var to = TransferToSavings.Entry;
                var rates = await _rates.GetDailyRatesAsync();
                var amountInFromCurrency = from.Currency == currency
                    ? amount
                    : (rates != null && ExchangeRateService.ConvertWithRates(amount, currency, from.Currency, rates) is { } fromConverted ? fromConverted : amount);
                var amountInToCurrency = to.Currency == currency
                    ? amount
                    : (rates != null && ExchangeRateService.ConvertWithRates(amount, currency, to.Currency, rates) is { } toConverted ? toConverted : amount);

                await _service.TransferBetweenSavingsAsync(from.Id, -amountInFromCurrency, to.Id, amountInToCurrency);
                IsAddTransactionOpen = false;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                SaveTransactionError = "Ошибка перевода: " + ex.Message;
            }
            return;
        }

        var t = new Transaction
        {
            Amount = amount,
            Currency = currency,
            Date = NewTransactionDate.Date,
            CategoryId = selectedCategory.Id,
            Note = string.IsNullOrWhiteSpace(NewTransactionNote) ? null : NewTransactionNote.Trim()
        };
        try
        {
            await _service.AddTransactionAsync(t);
            var account = selectedAccount.Entry;
            var rates = await _rates.GetDailyRatesAsync();
            decimal amountInAccountCurrency;
            if (account.Currency == currency)
                amountInAccountCurrency = amount;
            else if (rates != null && ExchangeRateService.ConvertWithRates(amount, currency, account.Currency, rates) is { } c)
                amountInAccountCurrency = c;
            else
                amountInAccountCurrency = amount;
            var delta = NewTransactionType == TransactionType.Income ? amountInAccountCurrency : -amountInAccountCurrency;
            await _service.AddDeltaToSavingsBalanceAsync(account.Id, delta);
            IsAddTransactionOpen = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SaveTransactionError = "Ошибка сохранения: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenCategoriesPanel()
    {
        IsCategoriesPanelOpen = true;
        _ = LoadCategoriesAsync();
    }

    [RelayCommand]
    private void CloseCategoriesPanel() => IsCategoriesPanelOpen = false;

    [RelayCommand]
    private void OpenAddCategory()
    {
        NewCategoryName = "";
        NewCategoryType = TransactionType.Expense;
        IsAddCategoryOpen = true;
    }

    [RelayCommand]
    private void CloseAddCategory() => IsAddCategoryOpen = false;

    [RelayCommand]
    private void OpenEditFinanceCategory(FinanceCategory cat)
    {
        EditingFinanceCategory = cat;
        EditFinanceCategoryName = cat.Name;
        IsEditFinanceCategoryOpen = true;
    }

    [RelayCommand]
    private void CloseEditFinanceCategory()
    {
        IsEditFinanceCategoryOpen = false;
        EditingFinanceCategory = null;
    }

    [RelayCommand]
    private async Task SaveEditFinanceCategory()
    {
        if (EditingFinanceCategory == null) return;
        if (string.IsNullOrWhiteSpace(EditFinanceCategoryName?.Trim())) return;
        await _service.UpdateFinanceCategoryAsync(EditingFinanceCategory.Id, EditFinanceCategoryName.Trim());
        IsEditFinanceCategoryOpen = false;
        EditingFinanceCategory = null;
        await LoadCategoriesAsync();
        await LoadMonthStatsAsync();
        await LoadTransactionsAsync();
    }

    [RelayCommand]
    private async Task DeleteFinanceCategory(FinanceCategory cat)
    {
        if (cat == null) return;
        var result = System.Windows.MessageBox.Show(
            $"Удалить категорию «{cat.Name}»? Операции с этой категорией останутся, но без привязки к категории.",
            "Удаление категории",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes) return;
        await _service.DeleteFinanceCategoryAsync(cat);
        await LoadCategoriesAsync();
        await LoadMonthStatsAsync();
        await LoadTransactionsAsync();
    }

    [RelayCommand]
    private async Task SaveCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName?.Trim())) return;
        var maxOrder = (NewCategoryType == TransactionType.Income ? IncomeCategories : ExpenseCategories).Count;
        var c = new FinanceCategory
        {
            Name = NewCategoryName.Trim(),
            Type = NewCategoryType,
            SortOrder = maxOrder
        };
        await _service.AddFinanceCategoryAsync(c);
        IsAddCategoryOpen = false;
        await LoadCategoriesAsync();
        if (SelectedMonth.Year == DateTime.Today.Year && SelectedMonth.Month == DateTime.Today.Month)
            await LoadMonthStatsAsync();
    }

    [RelayCommand]
    private async Task DeleteTransaction(TransactionItemViewModel item)
    {
        var id = item.Transaction.Id;
        await Task.Run(async () =>
        {
            var service = new PlannerService();
            await service.DeleteTransactionByIdAsync(id);
        });
        var toRemove = Transactions.FirstOrDefault(x => x.Transaction.Id == id);
        if (toRemove != null)
            Transactions.Remove(toRemove);
        await LoadMonthStatsAsync();
    }

    private async Task LoadRatesIfNeededAsync()
    {
        if (!string.IsNullOrEmpty(RateSekToUahText) && RateSekToUahText != "Не удалось загрузить курсы.") return;
        var rates = await _rates.GetDailyRatesAsync();
        if (rates == null) return;
        RateSekToUahText = $"1 SEK = {rates.SekToUah:N4} UAH";
        RateUsdToUahText = rates.UsdToUah.HasValue ? $"1 USD = {rates.UsdToUah.Value:N4} UAH" : "—";
        RateUsdToSekText = rates.UsdToSek.HasValue ? $"1 USD = {rates.UsdToSek.Value:N4} SEK" : "—";
        RateEurToSekText = rates.EurToSek.HasValue ? $"1 EUR = {rates.EurToSek.Value:N4} SEK" : "—";
        RatesDateText = $"Курсы НБУ на {rates.Date}";
    }

    [RelayCommand]
    private async Task FetchRatesAsync()
    {
        if (IsLoadingRates) return;
        IsLoadingRates = true;
        OnPropertyChanged(nameof(RefreshRatesButtonText));
        try
        {
            var rates = await _rates.GetDailyRatesAsync();
            if (rates != null)
            {
                RateSekToUahText = $"1 SEK = {rates.SekToUah:N4} UAH";
                RateUsdToUahText = rates.UsdToUah.HasValue ? $"1 USD = {rates.UsdToUah.Value:N4} UAH" : "—";
                RateUsdToSekText = rates.UsdToSek.HasValue ? $"1 USD = {rates.UsdToSek.Value:N4} SEK" : "—";
                RateEurToSekText = rates.EurToSek.HasValue ? $"1 EUR = {rates.EurToSek.Value:N4} SEK" : "—";
                RatesDateText = $"Курсы НБУ на {rates.Date}";
            }
            else
            {
                RateSekToUahText = "Не удалось загрузить курсы.";
                RateUsdToUahText = "";
                RateUsdToSekText = "";
                RateEurToSekText = "";
                RatesDateText = "";
            }
        }
        finally
        {
            IsLoadingRates = false;
            OnPropertyChanged(nameof(RefreshRatesButtonText));
        }
    }

    private async Task LoadSavingsAsync(PlannerService loadService)
    {
        var list = await loadService.GetSavingsEntriesAsync();
        var rates = await _rates.GetDailyRatesAsync();
        var symbol = CurrencyInfo.Symbol(SelectedCurrency);
        decimal total = 0;
        var items = new List<SavingsItemViewModel>();
        foreach (var e in list)
        {
            var converted = e.Currency == SelectedCurrency
                ? e.Balance
                : (rates != null && ExchangeRateService.ConvertWithRates(e.Balance, e.Currency, SelectedCurrency, rates) is { } c ? c : 0);
            total += converted;
            items.Add(new SavingsItemViewModel(e, converted, symbol, _service, () => _ = LoadSavingsAsync()));
        }
        TotalSavingsDisplay = total;
        SavingsEntries.Clear();
        foreach (var i in items) SavingsEntries.Add(i);
    }

    private Task LoadSavingsAsync() => LoadSavingsAsync(_service);

    [RelayCommand]
    private void OpenAddSavings()
    {
        NewSavingsCategory = SavingsCategories.FirstOrDefault();
        NewSavingsName = "";
        NewSavingsCurrency = SelectedCurrency;
        NewSavingsBalance = "";
        IsAddSavingsOpen = true;
    }

    [RelayCommand]
    private void CloseAddSavings() => IsAddSavingsOpen = false;

    [RelayCommand]
    private async Task SaveSavingsEntry()
    {
        if (string.IsNullOrWhiteSpace(NewSavingsName?.Trim())) return;
        if (NewSavingsCategory == null) return;
        if (!decimal.TryParse(NewSavingsBalance?.Replace(",", ".").Trim(), NumberStyles.Any, Ru, out var balance)) balance = 0;
        var entry = new SavingsEntry
        {
            SavingsCategoryId = NewSavingsCategory.Id,
            Name = NewSavingsName.Trim(),
            Currency = string.IsNullOrWhiteSpace(NewSavingsCurrency) ? CurrencyInfo.UAH : NewSavingsCurrency.Trim(),
            Balance = balance,
            SortOrder = SavingsEntries.Count
        };
        await _service.AddSavingsEntryAsync(entry);
        IsAddSavingsOpen = false;
        await LoadSavingsAsync();
        await OnSavingsChangedAsync();
    }

    [RelayCommand]
    private void OpenEditSavings(SavingsItemViewModel item)
    {
        EditingSavingsItem = item;
        EditSavingsItemName = item.Entry.Name;
        EditSavingsBalance = item.Entry.Balance.ToString("N2", Ru);
        IsEditSavingsOpen = true;
    }

    [RelayCommand]
    private void CloseEditSavings()
    {
        IsEditSavingsOpen = false;
        EditingSavingsItem = null;
        EditSavingsItemName = "";
    }

    [RelayCommand]
    private async Task SaveEditSavings()
    {
        if (EditingSavingsItem == null) return;
        if (!decimal.TryParse(EditSavingsBalance?.Replace(",", ".").Trim(), NumberStyles.Any, Ru, out var balance)) return;
        await _service.UpdateSavingsEntryAsync(EditingSavingsItem.Entry.Id, null, balance);
        IsEditSavingsOpen = false;
        EditingSavingsItem = null;
        await LoadSavingsAsync();
        await OnSavingsChangedAsync();
    }

    [RelayCommand]
    private async Task DeleteSavings(SavingsItemViewModel item)
    {
        await Task.Run(async () =>
        {
            var service = new PlannerService();
            await service.DeleteSavingsEntryByIdAsync(item.Entry.Id);
        });
        var toRemove = SavingsEntries.FirstOrDefault(x => x.Entry.Id == item.Entry.Id);
        if (toRemove != null) SavingsEntries.Remove(toRemove);
        await LoadSavingsAsync();
        await OnSavingsChangedAsync();
    }

    [RelayCommand]
    private void OpenAddSavingsCategory()
    {
        SavingsCategoryError = "";
        NewSavingsCategoryName = "";
        IsAddSavingsCategoryOpen = true;
    }

    [RelayCommand]
    private void CloseAddSavingsCategory() => IsAddSavingsCategoryOpen = false;

    [RelayCommand]
    private async Task SaveSavingsCategory()
    {
        SavingsCategoryError = "";
        if (string.IsNullOrWhiteSpace(NewSavingsCategoryName?.Trim())) return;
        var cat = new SavingsCategory { Name = NewSavingsCategoryName.Trim(), SortOrder = SavingsCategories.Count };
        await _service.AddSavingsCategoryAsync(cat);
        IsAddSavingsCategoryOpen = false;
        await LoadSavingsCategoriesAsync();
        await OnSavingsChangedAsync();
    }

    [RelayCommand]
    private void OpenEditSavingsCategory(SavingsCategory item)
    {
        SavingsCategoryError = "";
        EditingSavingsCategory = item;
        EditSavingsCategoryName = item.Name;
        IsEditSavingsCategoryOpen = true;
    }

    [RelayCommand]
    private void CloseEditSavingsCategory()
    {
        IsEditSavingsCategoryOpen = false;
        EditingSavingsCategory = null;
    }

    [RelayCommand]
    private async Task SaveEditSavingsCategory()
    {
        if (EditingSavingsCategory == null) return;
        SavingsCategoryError = "";
        if (string.IsNullOrWhiteSpace(EditSavingsCategoryName?.Trim())) return;
        await _service.UpdateSavingsCategoryAsync(EditingSavingsCategory.Id, EditSavingsCategoryName.Trim());
        IsEditSavingsCategoryOpen = false;
        EditingSavingsCategory = null;
        await LoadSavingsCategoriesAsync();
        await LoadSavingsAsync();
        await OnSavingsChangedAsync();
    }

    [RelayCommand]
    private async Task DeleteSavingsCategory(SavingsCategory item)
    {
        var (ok, error) = await _service.TryDeleteSavingsCategoryAsync(item.Id);
        if (!ok && error != null)
        {
            SavingsCategoryError = error;
            return;
        }
        await LoadSavingsCategoriesAsync();
        await LoadSavingsAsync();
        await OnSavingsChangedAsync();
    }

    [RelayCommand]
    private async Task OpenSavingsPanel()
    {
        SavingsPanelCurrency = SelectedCurrency;
        IsSavingsPanelOpen = true;
        await LoadSavingsCategoriesAsync();
        await LoadSavingsForPanelAsync();
        await LoadSavingsMonthlyChartAsync();
        await SaveCurrentMonthSnapshotAsync();
    }

    [RelayCommand]
    private async Task CloseSavingsPanel()
    {
        IsSavingsPanelOpen = false;
        await LoadSavingsAsync();
    }

    private async Task LoadSavingsForPanelAsync()
    {
        var list = await _service.GetSavingsEntriesAsync();
        var rates = await _rates.GetDailyRatesAsync();
        var symbol = CurrencyInfo.Symbol(SavingsPanelCurrency);
        decimal total = 0;
        var items = new List<SavingsItemViewModel>();
        foreach (var e in list)
        {
            var converted = e.Currency == SavingsPanelCurrency
                ? e.Balance
                : (rates != null && ExchangeRateService.ConvertWithRates(e.Balance, e.Currency, SavingsPanelCurrency, rates) is { } c ? c : 0);
            total += converted;
            items.Add(new SavingsItemViewModel(e, converted, symbol, _service, () => _ = LoadSavingsForPanelAsync()));
        }
        PanelTotalSavings = total;
        SavingsEntries.Clear();
        foreach (var i in items) SavingsEntries.Add(i);
    }

    private async Task LoadSavingsMonthlyChartAsync()
    {
        var snapshots = await _service.GetSavingsMonthlySnapshotsAsync();
        var rates = await _rates.GetDailyRatesAsync();
        var symbol = CurrencyInfo.Symbol(SavingsPanelCurrency);
        var list = new List<SavingsMonthChartItem>();
        foreach (var s in snapshots)
        {
            decimal amount = SavingsPanelCurrency == CurrencyInfo.UAH
                ? s.TotalAmountUah
                : (rates != null && ExchangeRateService.ConvertWithRates(s.TotalAmountUah, CurrencyInfo.UAH, SavingsPanelCurrency, rates) is { } c ? c : s.TotalAmountUah);
            var label = new DateTime(s.Year, s.Month, 1).ToString("MMM yyyy", Ru);
            list.Add(new SavingsMonthChartItem(s.Year, s.Month, label, amount, symbol));
        }
        var maxAmount = list.Count > 0 ? list.Max(x => x.Amount) : 0;
        if (maxAmount > 0)
            foreach (var i in list)
                i.BarWidthPercent = (double)(i.Amount / maxAmount * 100);
        SavingsMonthlyChart.Clear();
        foreach (var i in list) SavingsMonthlyChart.Add(i);
    }

    private async Task SaveCurrentMonthSnapshotAsync()
    {
        var list = await _service.GetSavingsEntriesAsync();
        var rates = await _rates.GetDailyRatesAsync();
        if (rates == null) return;
        decimal totalUah = 0;
        foreach (var e in list)
        {
            var inUah = e.Currency == CurrencyInfo.UAH ? e.Balance : (ExchangeRateService.ConvertWithRates(e.Balance, e.Currency, CurrencyInfo.UAH, rates) ?? 0);
            totalUah += inUah;
        }
        await _service.SaveSavingsSnapshotAsync(DateTime.Today.Year, DateTime.Today.Month, totalUah);
    }

    partial void OnSavingsPanelCurrencyChanged(string value)
    {
        OnPropertyChanged(nameof(SavingsPanelCurrencySymbol));
        _ = LoadSavingsForPanelAsync();
        _ = LoadSavingsMonthlyChartAsync();
    }

    [RelayCommand]
    private void OpenSavingsCharts()
    {
        IsSavingsChartsOpen = true;
    }

    [RelayCommand]
    private void CloseSavingsCharts() => IsSavingsChartsOpen = false;

    private async Task OnSavingsChangedAsync()
    {
        await SaveCurrentMonthSnapshotAsync();
        await LoadSavingsMonthlyChartAsync();
        if (IsSavingsPanelOpen) await LoadSavingsForPanelAsync();
    }

    partial void OnIsTransferBetweenSavingsChanged(bool value)
    {
        if (!value) return;
        TransferFromSavings ??= SavingsEntries.FirstOrDefault();
        if (TransferToSavings == null || TransferFromSavings?.Entry.Id == TransferToSavings.Entry.Id)
            TransferToSavings = SavingsEntries.FirstOrDefault(x => TransferFromSavings == null || x.Entry.Id != TransferFromSavings.Entry.Id)
                ?? TransferFromSavings;
    }
}

public class CategorySumItem
{
    public string Name { get; }
    public decimal Sum { get; }
    public string SumText { get; }

    public CategorySumItem(string name, decimal sum, string currencySymbol = " ₽")
    {
        Name = name;
        Sum = sum;
        SumText = sum.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")) + currencySymbol;
    }
}

public class TransactionItemViewModel
{
    public Transaction Transaction { get; }
    private readonly PlannerService _service;
    private readonly Action _onChanged;

    public string AmountText { get; }
    public string DateText => Transaction.Date.ToString("d MMM", CultureInfo.GetCultureInfo("ru-RU"));
    public string CategoryName => Transaction.Category?.Name ?? "";
    public bool IsIncome => Transaction.Category?.Type == TransactionType.Income;

    public TransactionItemViewModel(Transaction transaction, decimal displayAmount, string displaySymbol, PlannerService service, Action onChanged)
    {
        Transaction = transaction;
        _service = service;
        _onChanged = onChanged;
        AmountText = displayAmount.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")) + displaySymbol;
    }

    public async Task DeleteAsync()
    {
        await _service.DeleteTransactionAsync(Transaction);
        _onChanged();
    }
}

public class SavingsItemViewModel
{
    public SavingsEntry Entry { get; }
    private readonly Action _onChanged;

    public string CategoryName => Entry.SavingsCategory?.Name ?? "";
    public string BalanceOriginalText => Entry.Balance.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")) + CurrencyInfo.Symbol(Entry.Currency);
    public string ConvertedBalanceText { get; }

    public SavingsItemViewModel(SavingsEntry entry, decimal convertedBalance, string displaySymbol, PlannerService service, Action onChanged)
    {
        Entry = entry;
        _onChanged = onChanged;
        ConvertedBalanceText = convertedBalance.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")) + displaySymbol;
    }
}

public class SavingsMonthChartItem
{
    public int Year { get; }
    public int Month { get; }
    public string Label { get; }
    public decimal Amount { get; }
    public string AmountText { get; }
    public double BarWidthPercent { get; set; }

    public SavingsMonthChartItem(int year, int month, string label, decimal amount, string symbol)
    {
        Year = year;
        Month = month;
        Label = label;
        Amount = amount;
        AmountText = amount.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")) + symbol;
    }
}
