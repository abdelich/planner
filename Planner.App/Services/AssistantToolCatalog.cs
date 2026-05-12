using System.Globalization;
using System.Text;

namespace Planner.App.Services;

public static class AssistantToolCatalog
{
    private static readonly IReadOnlyDictionary<string, AssistantToolSpec> Specs =
        BuildSpecs().ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string name) => Specs.ContainsKey(NormalizeName(name));

    public static bool RequiresConfirmation(string name)
    {
        return Specs.TryGetValue(NormalizeName(name), out var spec) && spec.RequiresConfirmation;
    }

    public static AssistantToolValidationResult Validate(AssistantToolCommand command)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.Name))
            return new AssistantToolValidationResult(false, "Команда пустая.");

        var name = NormalizeName(command.Name);
        if (!Specs.TryGetValue(name, out var spec))
            return new AssistantToolValidationResult(false, $"Неизвестный инструмент: {command.Name}.");

        command.Name = name;
        foreach (var group in spec.RequiredAny)
        {
            if (!group.Any(key => HasValue(command.Args, key)))
            {
                var required = string.Join(" или ", group);
                return new AssistantToolValidationResult(false, $"Для {spec.Name} нужен аргумент: {required}.");
            }
        }

        var knownArgs = spec.Args.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in command.Args)
        {
            if (!knownArgs.Contains(arg.Key))
                return new AssistantToolValidationResult(false, $"У {spec.Name} нет аргумента {arg.Key}. Используйте только аргументы из схемы инструмента.");
        }

        foreach (var arg in spec.Args)
        {
            if (!command.Args.TryGetValue(arg.Name, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            if (!ValidateValue(arg, value, out var error))
                return new AssistantToolValidationResult(false, $"{spec.Name}.{arg.Name}: {error}");
        }

        return new AssistantToolValidationResult(true, "");
    }

    public static string RenderForPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tool catalog. Return tool calls only as JSON commands with these exact names and args.");
        foreach (var spec in Specs.Values.OrderBy(x => x.Name))
        {
            var confirmation = spec.RequiresConfirmation ? " Requires UI confirmation." : "";
            sb.AppendLine($"- {spec.Name}: {spec.Description}.{confirmation}");
            var argText = string.Join("; ", spec.Args.Select(FormatArg));
            var required = spec.RequiredAny.Count == 0
                ? ""
                : $" required={string.Join(", ", spec.RequiredAny.Select(g => g.Count == 1 ? g[0] : "(" + string.Join("|", g) + ")"))}";
            sb.AppendLine($"  args: {argText}{required}");
        }
        return sb.ToString().Trim();
    }

    private static string FormatArg(AssistantToolArgSpec arg)
    {
        var required = arg.Required ? " required" : "";
        var values = arg.Values.Count > 0 ? $" values={string.Join("|", arg.Values)}" : "";
        return $"{arg.Name}:{arg.Kind}{required}{values}";
    }

    private static bool ValidateValue(AssistantToolArgSpec arg, string raw, out string error)
    {
        error = "";
        var value = raw.Trim();
        switch (arg.Kind)
        {
            case "int":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    error = "ожидалось целое число.";
                break;
            case "decimal":
                if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue) || decimalValue < 0)
                    error = "ожидалось неотрицательное число в invariant формате, например 12.50.";
                break;
            case "date":
                if (!DateTime.TryParse(value, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.AssumeLocal, out _) &&
                    !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
                    error = "ожидалась дата, лучше yyyy-MM-dd.";
                break;
            case "time":
                if (!TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
                    !TimeOnly.TryParse(value, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out _))
                    error = "ожидалось время HH:mm.";
                break;
            case "bool":
                if (!bool.TryParse(value, out _) && value is not "0" and not "1")
                    error = "ожидалось true/false.";
                break;
            case "enum":
                if (arg.Values.Count > 0 && !arg.Values.Contains(value, StringComparer.OrdinalIgnoreCase))
                    error = $"ожидалось одно из: {string.Join(", ", arg.Values)}.";
                break;
            case "string":
                break;
            default:
                error = $"неизвестный тип аргумента {arg.Kind}.";
                break;
        }

        return string.IsNullOrWhiteSpace(error);
    }

    private static bool HasValue(Dictionary<string, string> args, string key)
    {
        return args.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
    }

    private static string NormalizeName(string name) => (name ?? "").Trim().ToLowerInvariant();

    private static IEnumerable<AssistantToolSpec> BuildSpecs()
    {
        yield return Spec("create_goal", "Create a day/week/month or recurring goal",
            Args(
                Req("title", "string"),
                Opt("description", "string"),
                Opt("category", "enum", "period", "recurring"),
                Opt("type", "enum", "daily", "weekly", "monthly", "day", "week", "month"),
                Opt("targetCount", "int"),
                Opt("startDate", "date"),
                Opt("recurrenceKind", "enum", "everyday", "daily", "everyndays", "every_n_days", "interval", "specificdaysofweek", "weekdays", "days_of_week"),
                Opt("intervalDays", "int"),
                Opt("weekdays", "string"),
                Opt("recurrenceDays", "int")),
            Required("title"));

        yield return Spec("update_goal", "Update an existing goal",
            Args(IdArgs("goalId"), Opt("title", "string"), Opt("description", "string"), Opt("targetCount", "int"), Opt("type", "enum", "daily", "weekly", "monthly", "day", "week", "month"), Opt("category", "enum", "period", "recurring"), Opt("startDate", "date"), Opt("recurrenceKind", "enum", "everyday", "daily", "everyndays", "every_n_days", "interval", "specificdaysofweek", "weekdays", "days_of_week"), Opt("intervalDays", "int"), Opt("weekdays", "string"), Opt("recurrenceDays", "int"), Opt("isArchived", "bool")),
            RequiredAny("goalId", "id"));

        yield return Spec("delete_goal", "Delete a goal and its completions", Args(IdArgs("goalId")), RequiredAny("goalId", "id"));
        yield return Spec("mark_goal_completed", "Mark a goal completed for a date", Args(IdArgs("goalId"), Opt("date", "date"), Opt("count", "int")), RequiredAny("goalId", "id"));
        yield return Spec("unmark_goal_completed", "Remove goal completion for a date", Args(IdArgs("goalId"), Opt("date", "date")), RequiredAny("goalId", "id"));

        yield return Spec("create_reminder", "Create an interval reminder", Args(Req("title", "string"), Opt("intervalMinutes", "int"), Opt("activeFrom", "time"), Opt("activeTo", "time")), Required("title"));
        yield return Spec("update_reminder", "Update an existing reminder", Args(IdArgs("reminderId"), Opt("title", "string"), Opt("intervalMinutes", "int"), Opt("activeFrom", "time"), Opt("activeTo", "time"), Opt("isEnabled", "bool")), RequiredAny("reminderId", "id"));
        yield return Spec("delete_reminder", "Delete a reminder", Args(IdArgs("reminderId")), RequiredAny("reminderId", "id"));
        yield return Spec("mark_reminder_completed", "Mark a reminder slot completed", Args(IdArgs("reminderId"), Opt("date", "date"), Opt("time", "time")), RequiredAny("reminderId", "id"));
        yield return Spec("unmark_reminder_completed", "Remove a reminder slot completion", Args(IdArgs("reminderId"), Opt("date", "date"), Opt("time", "time")), RequiredAny("reminderId", "id"));
        yield return Spec("archive_goal", "Archive a goal so it stops showing in active lists", Args(IdArgs("goalId")), RequiredAny("goalId", "id"));
        yield return Spec("unarchive_goal", "Restore an archived goal", Args(IdArgs("goalId")), RequiredAny("goalId", "id"));

        yield return Spec("create_transaction", "Create a finance transaction and update a savings account balance", Args(Req("amount", "decimal"), Opt("type", "enum", "income", "expense", "доход", "расход"), Req("categoryId", "int"), Req("savingsEntryId", "int"), Opt("currency", "string"), Opt("date", "date"), Opt("note", "string")), Required("amount", "categoryId", "savingsEntryId"), confirmation: true);
        yield return Spec("update_transaction", "Update a finance transaction", Args(IdArgs("transactionId"), Opt("amount", "decimal"), Opt("categoryId", "int"), Opt("currency", "string"), Opt("date", "date"), Opt("note", "string")), RequiredAny("transactionId", "id"), confirmation: true);
        yield return Spec("delete_transaction", "Delete a finance transaction", Args(IdArgs("transactionId")), RequiredAny("transactionId", "id"), confirmation: true);
        yield return Spec("transfer_between_savings", "Transfer money between savings accounts", Args(Req("fromSavingsEntryId", "int"), Req("toSavingsEntryId", "int"), Req("amount", "decimal")), Required("fromSavingsEntryId", "toSavingsEntryId", "amount"), confirmation: true);

        yield return Spec("create_finance_category", "Create an income or expense category", Args(Req("name", "string"), Opt("type", "enum", "income", "expense", "доход", "расход")), Required("name"));
        yield return Spec("update_finance_category", "Rename a finance category", Args(IdArgs("categoryId"), Req("name", "string")), Combine(RequiredAny("categoryId", "id"), Required("name")));
        yield return Spec("delete_finance_category", "Delete a finance category and its transactions", Args(IdArgs("categoryId")), RequiredAny("categoryId", "id"), confirmation: true);

        yield return Spec("create_savings_category", "Create a savings category", Args(Req("name", "string")), Required("name"));
        yield return Spec("update_savings_category", "Rename a savings category", Args(IdArgs("categoryId"), Req("name", "string")), Combine(RequiredAny("categoryId", "id"), Required("name")));
        yield return Spec("delete_savings_category", "Delete an empty savings category", Args(IdArgs("categoryId")), RequiredAny("categoryId", "id"), confirmation: true);
        yield return Spec("create_savings_account", "Create a savings account", Args(Req("name", "string"), Opt("categoryId", "int"), Opt("savingsCategoryId", "int"), Opt("balance", "decimal"), Opt("currency", "string")), Combine(Required("name"), RequiredAny("categoryId", "savingsCategoryId")));
        yield return Spec("create_savings_entry", "Alias for create_savings_account", Args(Req("name", "string"), Opt("categoryId", "int"), Opt("savingsCategoryId", "int"), Opt("balance", "decimal"), Opt("currency", "string")), Combine(Required("name"), RequiredAny("categoryId", "savingsCategoryId")));
        yield return Spec("update_savings_account", "Update savings account name or balance", Args(IdArgs("savingsEntryId"), Opt("name", "string"), Opt("balance", "decimal")), RequiredAny("savingsEntryId", "id"), confirmation: true);
        yield return Spec("update_savings_entry", "Alias for update_savings_account", Args(IdArgs("savingsEntryId"), Opt("name", "string"), Opt("balance", "decimal")), RequiredAny("savingsEntryId", "id"), confirmation: true);
        yield return Spec("delete_savings_account", "Delete a savings account", Args(IdArgs("savingsEntryId")), RequiredAny("savingsEntryId", "id"), confirmation: true);
        yield return Spec("delete_savings_entry", "Alias for delete_savings_account", Args(IdArgs("savingsEntryId")), RequiredAny("savingsEntryId", "id"), confirmation: true);
        yield return Spec("save_savings_snapshot", "Save the current total savings (in UAH) as a snapshot for a given year/month. If totalUah is omitted, the tool computes it from current account balances using NBU rates",
            Args(Opt("year", "int"), Opt("month", "int"), Opt("totalUah", "decimal")));
        yield return Spec("inspect_savings_snapshots", "List monthly savings snapshots (year, month, total in UAH)",
            Args(Opt("limit", "int")));

        yield return Spec("save_period_note", "Save a note for day/week/month", Args(Req("text", "string"), Opt("kind", "enum", "day", "week", "month", "daily", "weekly", "monthly"), Opt("date", "date")), Required("text"));
        yield return Spec("inspect_goals", "Read goal status from the database without changing anything. Use before answering goal status/history questions",
            Args(
                Opt("kind", "enum", "day", "week", "month", "all", "daily", "weekly", "monthly"),
                Opt("date", "date"),
                Opt("year", "int"),
                Opt("month", "int"),
                Opt("includeArchived", "bool")));
        yield return Spec("inspect_reminders", "Read reminder status from the database without changing anything. Use before answering reminder status/history questions",
            Args(
                Opt("kind", "enum", "month", "all", "monthly"),
                Opt("date", "date"),
                Opt("year", "int"),
                Opt("month", "int"),
                Opt("includeDisabled", "bool")));
        yield return Spec("inspect_finances", "Read finance data from the database without changing anything. Use before answering finance/account/transaction questions",
            Args(
                Opt("kind", "enum", "day", "week", "month", "all", "daily", "weekly", "monthly"),
                Opt("date", "date"),
                Opt("year", "int"),
                Opt("month", "int"),
                Opt("targetCurrency", "enum", "UAH", "SEK", "USD")));
        yield return Spec("inspect_exchange_rates", "Get current NBU exchange rates and optionally convert an amount",
            Args(
                Opt("amount", "decimal"),
                Opt("fromCurrency", "enum", "UAH", "SEK", "USD", "EUR"),
                Opt("toCurrency", "enum", "UAH", "SEK", "USD", "EUR")));
        yield return Spec("get_exchange_rates", "Alias for inspect_exchange_rates",
            Args(
                Opt("amount", "decimal"),
                Opt("fromCurrency", "enum", "UAH", "SEK", "USD", "EUR"),
                Opt("toCurrency", "enum", "UAH", "SEK", "USD", "EUR")));
        yield return Spec("inspect_reports", "Read saved assistant reports from the database without changing anything",
            Args(
                Opt("limit", "int")));
        yield return Spec("open_graphical_report", "Create and open a graphical WPF report window for goals, reminders, finance, or general dashboard data",
            Args(
                Opt("kind", "enum", "day", "week", "month", "daily", "weekly", "monthly"),
                Opt("domain", "enum", "general", "finance", "financial", "money", "goals", "goal", "reminders", "reminder", "общий", "финансы", "цели", "цель", "напоминания", "напоминание"),
                Opt("date", "date"),
                Opt("year", "int"),
                Opt("month", "int"),
                Opt("targetCurrency", "enum", "UAH", "SEK", "USD")));
        yield return Spec("create_graphical_report", "Alias for open_graphical_report",
            Args(
                Opt("kind", "enum", "day", "week", "month", "daily", "weekly", "monthly"),
                Opt("domain", "enum", "general", "finance", "financial", "money", "goals", "goal", "reminders", "reminder", "общий", "финансы", "цели", "цель", "напоминания", "напоминание"),
                Opt("date", "date"),
                Opt("year", "int"),
                Opt("month", "int"),
                Opt("targetCurrency", "enum", "UAH", "SEK", "USD")));
        yield return Spec("generate_report", "Generate and save a day/week/month report. Use domain=finance for financial reports, domain=goals for goal reports, domain=reminders for reminder reports, domain=general otherwise",
            Args(
                Opt("kind", "enum", "day", "week", "month", "daily", "weekly", "monthly"),
                Opt("domain", "enum", "general", "finance", "financial", "money", "goals", "goal", "reminders", "reminder", "общий", "финансы", "цели", "цель", "напоминания", "напоминание"),
                Opt("date", "date"),
                Opt("year", "int"),
                Opt("month", "int"),
                Opt("targetCurrency", "enum", "UAH", "SEK", "USD")));
    }

    private static AssistantToolSpec Spec(
        string name,
        string description,
        IReadOnlyList<AssistantToolArgSpec> args,
        IReadOnlyList<IReadOnlyList<string>> requiredAny,
        bool confirmation = false)
    {
        return new AssistantToolSpec(name, description, args, requiredAny, confirmation);
    }

    private static AssistantToolSpec Spec(
        string name,
        string description,
        IReadOnlyList<AssistantToolArgSpec> args)
    {
        return new AssistantToolSpec(name, description, args, Array.Empty<IReadOnlyList<string>>(), false);
    }

    private static AssistantToolArgSpec[] Args(params AssistantToolArgSpec[] args) => args;

    private static AssistantToolArgSpec[] Args(params object[] parts)
    {
        var list = new List<AssistantToolArgSpec>();
        foreach (var part in parts)
        {
            if (part is AssistantToolArgSpec arg)
                list.Add(arg);
            else if (part is IEnumerable<AssistantToolArgSpec> many)
                list.AddRange(many);
        }
        return list.ToArray();
    }

    private static AssistantToolArgSpec Req(string name, string kind, params string[] values) => new(name, kind, true, values);
    private static AssistantToolArgSpec Opt(string name, string kind, params string[] values) => new(name, kind, false, values);
    private static AssistantToolArgSpec[] IdArgs(string primary) => new[] { Req(primary, "int"), Opt("id", "int") };

    private static IReadOnlyList<IReadOnlyList<string>> Required(params string[] args)
    {
        return args.Select(x => (IReadOnlyList<string>)new[] { x }).ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<string>> RequiredAny(params string[] args)
    {
        return new[] { (IReadOnlyList<string>)args.ToArray() };
    }

    private static IReadOnlyList<IReadOnlyList<string>> Combine(params IReadOnlyList<IReadOnlyList<string>>[] groups)
    {
        return groups.SelectMany(x => x).ToArray();
    }
}

public sealed record AssistantToolSpec(
    string Name,
    string Description,
    IReadOnlyList<AssistantToolArgSpec> Args,
    IReadOnlyList<IReadOnlyList<string>> RequiredAny,
    bool RequiresConfirmation);

public sealed record AssistantToolArgSpec(
    string Name,
    string Kind,
    bool Required,
    IReadOnlyList<string> Values);

public sealed record AssistantToolValidationResult(
    bool Success,
    string Message);
