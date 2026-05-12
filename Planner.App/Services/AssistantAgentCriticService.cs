namespace Planner.App.Services;

public sealed class AssistantAgentCriticService
{
    public AssistantCriticResult Review(string userText, string replyText, IReadOnlyList<string> actionLines)
    {
        var normalizedUser = Normalize(userText);
        var normalizedReply = Normalize(replyText);

        if (LooksLikeCompletionQuestion(normalizedUser) &&
            normalizedReply.Contains("какую цель отметить", StringComparison.Ordinal))
        {
            return new AssistantCriticResult(
                false,
                "Вы спрашивали статус выполнения, а не просили что-то отмечать. Я не буду менять цели без явной команды. Уточните период или название цели, если нужно проверить статус.",
                "completion-question-misread-as-command");
        }

        if (actionLines.Count == 0 && LooksLikeCompletedActionClaim(normalizedReply))
        {
            return new AssistantCriticResult(
                false,
                "Я не выполнил действие, потому что не получил корректную команду инструмента. Сформулируйте действие еще раз или укажите id/название объекта.",
                "action-claim-without-tool-result");
        }

        if (actionLines.Count == 0 && LooksLikeFutureActionPromise(normalizedReply))
        {
            return new AssistantCriticResult(
                false,
                "Не смог выполнить действие — модель пообещала, но команда не отправилась. Попробуйте ещё раз тем же сообщением или уточните название/период.",
                "future-action-promise-without-tool-call");
        }

        if (LooksLikeFinanceReportRequest(normalizedUser) &&
            !ContainsFinanceReportEvidence(normalizedReply) &&
            !actionLines.Any(line => ContainsFinanceReportEvidence(Normalize(line))))
        {
            return new AssistantCriticResult(
                false,
                "Для финансового отчета мне нужны данные по доходам, расходам, операциям и счетам. Я не буду подменять его отчетом по целям.",
                "finance-report-missing-finance-data");
        }

        if (LooksLikeMarginQuestion(normalizedUser) &&
            TryExtractMultiCurrencyMargins(actionLines, out var marginLines) &&
            ClaimsSingleCurrencyAgnosticMargin(normalizedReply))
        {
            return new AssistantCriticResult(
                false,
                "Итоговую маржу одним числом считать нельзя без конвертации валют.\n\nМаржа по валютам:\n" + marginLines,
                "mixed-currency-margin");
        }

        return new AssistantCriticResult(true, replyText, "ok");
    }

    private static bool LooksLikeCompletionQuestion(string normalized)
    {
        return normalized.Contains("выполн", StringComparison.Ordinal) &&
               (normalized.Contains("?", StringComparison.Ordinal) ||
                normalized.Contains(" ли ", StringComparison.Ordinal) ||
                normalized.Contains(" я их выполнил ", StringComparison.Ordinal));
    }

    private static bool LooksLikeCompletedActionClaim(string normalized)
    {
        var claims = new[]
        {
            "создал", "создала", "обновил", "обновила", "удалил", "удалила",
            "отметил", "отметила", "сохранил", "сохранила", "сформировал", "сформировала",
            "добавил", "добавила", "записал", "записала", "выполнил", "выполнила"
        };
        return claims.Any(claim => normalized.Contains($" {claim} ", StringComparison.Ordinal));
    }

    private static bool LooksLikeFutureActionPromise(string normalized)
    {
        var verbs = new[]
        {
            "добавлю", "создам", "сохраню", "обновлю", "удалю", "отмечу",
            "сформирую", "запишу", "выполню", "сделаю", "поставлю", "запланирую"
        };
        if (verbs.Any(verb => normalized.Contains($" {verb} ", StringComparison.Ordinal) ||
                              normalized.EndsWith($" {verb}", StringComparison.Ordinal)))
            return true;

        var phrases = new[]
        {
            " подождите ", " минутку ", " сейчас выполню ", " сейчас сделаю ",
            " сейчас добавлю ", " сейчас создам ", " сейчас сохраню ", " сейчас обновлю ",
            " сейчас удалю ", " сейчас отмечу ", " секунду ", " момент ",
            " just a moment ", " one moment ", " i will ", " i ll ", " let me "
        };
        return phrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal));
    }

    private static bool LooksLikeFinanceReportRequest(string normalized)
    {
        return (normalized.Contains(" отчет ", StringComparison.Ordinal) ||
                normalized.Contains(" отчёт ", StringComparison.Ordinal)) &&
               (normalized.Contains(" финанс", StringComparison.Ordinal) ||
                normalized.Contains(" деньг", StringComparison.Ordinal) ||
                normalized.Contains(" доход", StringComparison.Ordinal) ||
                normalized.Contains(" расход", StringComparison.Ordinal) ||
                normalized.Contains(" трат", StringComparison.Ordinal));
    }

    private static bool ContainsFinanceReportEvidence(string normalized)
    {
        return normalized.Contains(" доход", StringComparison.Ordinal) ||
               normalized.Contains(" расход", StringComparison.Ordinal) ||
               normalized.Contains(" маржа", StringComparison.Ordinal) ||
               normalized.Contains(" операци", StringComparison.Ordinal) ||
               normalized.Contains(" финансов", StringComparison.Ordinal);
    }

    private static bool LooksLikeMarginQuestion(string normalized)
    {
        return normalized.Contains(" марж", StringComparison.Ordinal);
    }

    private static bool ClaimsSingleCurrencyAgnosticMargin(string normalized)
    {
        return normalized.Contains(" марж", StringComparison.Ordinal) &&
               !normalized.Contains(" валют", StringComparison.Ordinal) &&
               !normalized.Contains(" конверт", StringComparison.Ordinal);
    }

    private static bool TryExtractMultiCurrencyMargins(IReadOnlyList<string> actionLines, out string marginLines)
    {
        var rows = new List<string>();
        foreach (var raw in actionLines)
        {
            foreach (var line in (raw ?? "").Split('\n'))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"-\s*(?<currency>[A-Z]{3}):\s*доход\s*(?<income>[-\d\s,.]+),\s*расход\s*(?<expense>[-\d\s,.]+),\s*маржа\s*(?<margin>[-\d\s,.]+)");
                if (!match.Success)
                    continue;

                rows.Add($"- {match.Groups["currency"].Value}: {match.Groups["margin"].Value.Trim()}");
            }
        }

        marginLines = string.Join("\n", rows.Distinct());
        return rows.Select(x => x[..Math.Min(5, x.Length)]).Distinct().Count() > 1;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return " ";

        var chars = value.Trim().ToLowerInvariant().Replace('ё', 'е')
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '?' ? ch : ' ')
            .ToArray();
        return " " + string.Join("", chars).Replace("  ", " ") + " ";
    }
}

public sealed record AssistantCriticResult(
    bool Approved,
    string RevisedReply,
    string Reason);
