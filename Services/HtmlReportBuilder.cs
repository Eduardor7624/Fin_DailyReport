using System.Net;
using System.Text;
using FinzatiDailyReport.Configuration;
using FinzatiDailyReport.Models;

namespace FinzatiDailyReport.Services;

public sealed class HtmlReportBuilder
{
    private readonly ReportSettings _s;
    public HtmlReportBuilder(ReportSettings settings) => _s = settings;

    public string Build(DailyReportData d, ReportAnalysis a)
    {
        var accent = a.Health switch
        {
            SystemHealthLevel.Stable => "#15803d",
            SystemHealthLevel.Incidents => "#b45309",
            _ => "#b91c1c"
        };

        var bg = a.Health switch
        {
            SystemHealthLevel.Stable => "#f0fdf4",
            SystemHealthLevel.Incidents => "#fffbeb",
            _ => "#fef2f2"
        };

        var sb = new StringBuilder();
        sb.Append($$"""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{{E(_s.Title)}}</title></head>
<body style="margin:0;background:#f3f4f6;font-family:Arial,Helvetica,sans-serif;color:#111827;">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f3f4f6"><tr><td align="center" style="padding:24px 10px">
<table role="presentation" width="760" cellspacing="0" cellpadding="0" style="width:100%;max-width:760px;background:#fff;border-radius:14px;overflow:hidden;box-shadow:0 4px 18px rgba(15,23,42,.08)">
<tr><td style="background:#111827;padding:28px 32px;color:#fff"><div style="font-size:13px;letter-spacing:1.6px;text-transform:uppercase;color:#cbd5e1">{{E(_s.BrandName)}} Monitoring</div><h1 style="margin:7px 0 5px;font-size:26px;line-height:1.2">{{E(_s.Title)}}</h1><div style="color:#cbd5e1;font-size:14px">{{d.ReportDate:dddd, MMMM d, yyyy}} · {{E(_s.TimeZoneDisplayName)}}</div></td></tr>
<tr><td style="padding:28px 32px 8px"><p style="margin:0 0 18px;line-height:1.6">Good morning,</p><p style="margin:0 0 10px;line-height:1.6">Please find below the Finzati daily summary of system processes, errors, user registrations, and website activity generated on <strong>{{d.ReportDate:yyyy-MM-dd}}</strong>.</p><p style="margin:0 0 22px;color:#64748b;font-size:13px;line-height:1.5">Reporting window: <strong>{{FmtPeriod(d.PeriodStartEastern, d.PeriodEndEastern)}} Eastern Time</strong>.</p>
<div style="border:1px solid {{accent}};background:{{bg}};border-radius:10px;padding:18px 20px"><div style="font-size:12px;text-transform:uppercase;letter-spacing:1px;color:{{accent}};font-weight:bold">Overall Status</div><div style="font-size:23px;font-weight:bold;color:{{accent}};margin:4px 0">{{HealthLabel(a.Health)}}</div><div style="font-size:14px;line-height:1.5;color:#374151">{{E(HealthExplanation(a))}}</div></div></td></tr>
{{Section("Process Metrics", ProcessCards(d.ProcessSummary))}}
{{Section("Application Summary", ProcessDecisionSummary(d.ProcessSummary) + ProcessTable(d.ProcessGroups))}}
{{Section("Daily Errors", ErrorIntro(a) + OperationErrorsTable(a.PriorityOperationErrors))}}
{{Section("Processes Requiring Attention", ProcessErrorsTable(a.PriorityProcessErrors))}}
{{Section("User Activity", UserActivityDecisionSummary(d.UserActivity) + UserActivityCards(d.UserActivity))}}
{{Section("Website Activity", VisitDecisionSummary(d.VisitSummary, d.CountryVisits) + VisitCards(d.VisitSummary) + CountryAndDeviceTables(d) + PageTypeTable(d.PageTypeVisits) + TwoColumnLists(d))}}
{{Section("Conclusion and Recommended Actions", Conclusion(a))}}
<tr><td style="padding:8px 32px 30px"><p style="margin:20px 0 4px;line-height:1.5">Best regards,</p><p style="margin:0;font-weight:bold">{{E(_s.PreparedBy)}}</p></td></tr>
<tr><td style="padding:16px 32px;background:#f8fafc;color:#64748b;font-size:11px;line-height:1.5;border-top:1px solid #e5e7eb">This report was generated automatically by FinzatiDailyReport. Totals reflect records found between {{E(FmtPeriod(d.PeriodStartEastern, d.PeriodEndEastern))}} Eastern Time.</td></tr>
</table></td></tr></table></body></html>
""");

        return sb.ToString();
    }

    private static string Section(string title, string content) =>
        $"<tr><td style='padding:20px 32px 4px'><h2 style='font-size:18px;margin:0 0 14px;color:#111827;border-bottom:2px solid #e5e7eb;padding-bottom:8px'>{E(title)}</h2>{content}</td></tr>";

    private static string ProcessCards(ProcessGeneralSummary x) => Cards([
        ("Executions", x.TotalExecutions.ToString("N0")),
        ("Successful", x.SuccessfulExecutions.ToString("N0")),
        ("With Errors", x.ErrorExecutions.ToString("N0")),
        ("Warnings", x.TotalWarnings.ToString("N0")),
        ("Applications", x.ApplicationCount.ToString("N0")),
        ("Items Processed", x.TotalItems.ToString("N0")),
        ("Successful Items", x.TotalSuccess.ToString("N0")),
        ("Average Duration", FmtDuration(x.AverageDurationSeconds))
    ]);

    private static string UserActivityCards(UserActivitySummary x) => Cards([
        ("New Registrations", x.RegisteredUsers.ToString("N0")),
        ("New Active Users", x.RegisteredAndActiveUsers.ToString("N0")),
        ("New Inactive Users", x.RegisteredButInactiveUsers.ToString("N0")),
        ("Deactivated Users", x.DeactivatedUsers?.ToString("N0") ?? "Not tracked")
    ]);

    private static string VisitCards(VisitGeneralSummary x) => Cards([
        ("All Requests", x.TotalVisits.ToString("N0")),
        ("Real Visits", x.HumanVisits.ToString("N0")),
        ("Bot Visits", x.BotVisits.ToString("N0")),
        ("Real Traffic", $"{x.HumanVisitPercent:N1}%"),
        ("Countries", x.Countries.ToString("N0")),
        ("United States", x.UnitedStatesVisits.ToString("N0")),
        ("Unique Human Pages", x.DifferentPages.ToString("N0")),
        ("Human Activity Window", $"{FmtTime(x.FirstVisit)} – {FmtTime(x.LastVisit)}")
    ]);


    private static string ProcessDecisionSummary(ProcessGeneralSummary x)
    {
        var message = x.TotalExecutions == 0
            ? "No scheduled-process executions were recorded in this reporting window. Confirm that the nightly jobs ran."
            : x.ErrorExecutions == 0
                ? $"All {x.TotalExecutions:N0} recorded process executions completed without detected errors."
                : $"{x.ErrorExecutions:N0} of {x.TotalExecutions:N0} process executions require attention. Review the affected applications below.";
        return DecisionBox(message, x.ErrorExecutions == 0 && x.TotalExecutions > 0);
    }

    private static string UserActivityDecisionSummary(UserActivitySummary x)
    {
        var message = x.RegisteredUsers == 0
            ? "No new Finzati users registered during this reporting window."
            : $"{x.RegisteredUsers:N0} new user(s) registered: {x.RegisteredAndActiveUsers:N0} active and {x.RegisteredButInactiveUsers:N0} inactive.";
        return DecisionBox(message, x.RegisteredUsers > 0);
    }

    private static string VisitDecisionSummary(VisitGeneralSummary x, IEnumerable<CountryVisitSummary> countries)
    {
        var topCountry = countries.FirstOrDefault();
        var countryText = topCountry is null ? "No country data was available." : $"Top country: {topCountry.CountryCode} with {topCountry.VisitCount:N0} real visits ({topCountry.PercentOfHumanVisits:N1}%).";
        var message = $"Finzati received {x.TotalVisits:N0} requests: {x.HumanVisits:N0} real visits and {x.BotVisits:N0} bot visits. {countryText}";
        return DecisionBox(message, x.HumanVisits > 0);
    }

    private static string CountryAndDeviceTables(DailyReportData d) =>
        "<table width='100%' cellspacing='12' cellpadding='0' role='presentation'><tr>" +
        "<td width='50%' valign='top'><h3 style='font-size:14px;margin:20px 0 8px'>Real Traffic by Country</h3>" +
        Table(["Country", "Visits", "% Real Traffic"], d.CountryVisits.Take(10).Select(x => new[] { E(x.CountryCode), x.VisitCount.ToString("N0"), $"{x.PercentOfHumanVisits:N1}%" })) +
        "</td><td width='50%' valign='top'><h3 style='font-size:14px;margin:20px 0 8px'>Real Traffic by Device</h3>" +
        Table(["Client Type", "Visits", "% Real Traffic"], d.ClientTypeVisits.Select(x => new[] { E(x.ClientType), x.VisitCount.ToString("N0"), $"{x.PercentOfHumanVisits:N1}%" })) +
        "</td></tr></table>";

    private static string DecisionBox(string message, bool positive)
    {
        var border = positive ? "#86efac" : "#fcd34d";
        var background = positive ? "#f0fdf4" : "#fffbeb";
        return $"<div style='margin:0 0 14px;padding:13px 15px;border:1px solid {border};background:{background};border-radius:8px;font-size:14px;line-height:1.55'>{E(message)}</div>";
    }

    private static string Cards(IEnumerable<(string Label, string Value)> cards)
    {
        var s = new StringBuilder("<table width='100%' cellspacing='8' cellpadding='0' role='presentation'>");
        var i = 0;

        foreach (var c in cards)
        {
            if (i % 4 == 0)
                s.Append("<tr>");

            s.Append($"<td width='25%' valign='top' style='padding:12px;background:#f8fafc;border:1px solid #e5e7eb;border-radius:8px'><div style='font-size:11px;color:#64748b;text-transform:uppercase'>{E(c.Label)}</div><div style='font-size:18px;font-weight:bold;margin-top:5px'>{E(c.Value)}</div></td>");

            if (i % 4 == 3)
                s.Append("</tr>");

            i++;
        }

        if (i % 4 != 0)
            s.Append("</tr>");

        return s.Append("</table>").ToString();
    }

    private static string ProcessTable(IEnumerable<ProcessGroupSummary> rows) => Table(
        ["Application / Mode", "Decision Status", "Runs", "Successful", "Problem Runs", "Items / Errors", "Avg. Duration"],
        rows.Select(x => new[]
        {
            E(x.ApplicationName) + "<br><span style='color:#64748b;font-size:11px'>" + E(x.RunMode) + "</span>",
            Status(x.Status),
            x.ExecutionCount.ToString("N0"),
            x.SuccessfulExecutions.ToString("N0"),
            x.ErrorExecutions.ToString("N0"),
            $"{x.TotalItems:N0} / {x.TotalErrors:N0}",
            FmtDuration(x.AverageDurationSeconds)
        }));

    private static string OperationErrorsTable(IEnumerable<OperationErrorSummary> rows) => Table(
        ["Path / Process", "Error Summary", "Count", "First Occurrence", "Last Occurrence"],
        rows.Select(x => new[]
        {
            E(x.Path),
            E(Short(x.Error, 260)),
            x.Count.ToString("N0"),
            FmtDateTime(x.FirstOccurrence),
            FmtDateTime(x.LastOccurrence)
        }),
        "No operational errors were recorded.");

    private static string ProcessErrorsTable(IEnumerable<ProcessErrorSummary> rows) => Table(
        ["Application / Mode", "Status", "Error", "Count", "Last Occurrence"],
        rows.Select(x => new[]
        {
            E(x.ApplicationName) + "<br><span style='color:#64748b;font-size:11px'>" + E(x.RunMode) + "</span>",
            Status(x.Status),
            E(Short(string.IsNullOrWhiteSpace(x.ExceptionType)
                ? x.ProcessException
                : $"{x.ExceptionType}: {x.ProcessException}", 280)),
            x.ErrorCount.ToString("N0"),
            FmtDateTime(x.LastOccurrence)
        }),
        "No processes requiring attention were detected.");

    private static string PageTypeTable(IEnumerable<PageTypeVisitSummary> rows) =>
        "<h3 style='font-size:14px;margin:20px 0 8px'>Activity by Section</h3>" +
        Table(
            ["Section", "Visits", "Unique Pages", "First Visit", "Last Visit"],
            rows.Select(x => new[]
            {
                E(x.PageType),
                x.VisitCount.ToString("N0"),
                x.DifferentPages.ToString("N0"),
                FmtTime(x.FirstVisit),
                FmtTime(x.LastVisit)
            }));

    private string TwoColumnLists(DailyReportData d) =>
        $"<table width='100%' cellspacing='12' cellpadding='0' role='presentation'><tr><td width='50%' valign='top'>{Ranked("Most Visited Pages", d.PageVisits.Take(_s.TopPages).Select(x => (x.Path, x.VisitCount)))}</td><td width='50%' valign='top'>{Ranked("Most Viewed Companies", d.CompanyVisits.Take(_s.TopCompanies).Select(x => (x.Symbol, x.VisitCount)))}</td></tr><tr><td colspan='2'>{Ranked("Top Referrers", d.ReferrerVisits.Take(_s.TopReferrers).Select(x => (x.Referrer, x.VisitCount)))}</td></tr></table>";

    private static string Ranked(string title, IEnumerable<(string Name, int Count)> rows)
    {
        var list = rows.ToList();
        var s = new StringBuilder($"<h3 style='font-size:14px;margin:20px 0 8px'>{E(title)}</h3>");

        if (list.Count == 0)
            return s.Append("<div style='color:#64748b;font-size:13px'>No data available.</div>").ToString();

        s.Append("<ol style='padding-left:22px;margin:0;line-height:1.8;font-size:13px'>");

        foreach (var item in list)
            s.Append($"<li><span style='word-break:break-all'>{E(item.Name)}</span> — <strong>{item.Count:N0}</strong></li>");

        return s.Append("</ol>").ToString();
    }

    private static string ErrorIntro(ReportAnalysis a) =>
        $"<p style='font-size:14px;line-height:1.6'>A total of <strong>{a.TotalOperationErrorEvents:N0}</strong> operational error events were recorded, grouped into <strong>{a.DifferentOperationErrors:N0}</strong> distinct path-and-error combinations.</p>";

    private static string Conclusion(ReportAnalysis a)
    {
        var s = new StringBuilder(
            $"<p style='line-height:1.6'>The overall system status for the reporting period was <strong>{E(HealthLabel(a.Health))}</strong>.</p>" +
            "<h3 style='font-size:14px;margin:16px 0 7px'>Recommended Actions</h3>" +
            "<ul style='margin:0;padding-left:21px;line-height:1.7;font-size:14px'>");

        foreach (var action in RecommendedActions(a))
            s.Append($"<li>{E(action)}</li>");

        return s.Append("</ul>").ToString();
    }

    private static string HealthLabel(SystemHealthLevel health) => health switch
    {
        SystemHealthLevel.Stable => "STABLE",
        SystemHealthLevel.Incidents => "INCIDENTS DETECTED",
        _ => "CRITICAL"
    };

    private static string HealthExplanation(ReportAnalysis a) => a.Health switch
    {
        SystemHealthLevel.Stable =>
            "All monitored processes completed without significant incidents, and no critical operational patterns were identified.",
        SystemHealthLevel.Incidents =>
            "One or more operational incidents were detected. Review the error and process sections below to determine whether corrective action is required.",
        _ =>
            "Critical failures or a significant concentration of errors were detected. Immediate review of the affected applications and processes is recommended."
    };

    private static IEnumerable<string> RecommendedActions(ReportAnalysis a)
    {
        if (a.TotalOperationErrorEvents > 0)
            yield return "Review the errors with the highest number of occurrences and confirm whether they share a common root cause.";

        if (a.PriorityProcessErrors.Any())
            yield return "Validate all processes that completed with ERROR, FAILED, FAILURE, or SUCCESS_WITH_ERRORS status, or reported one or more processing errors.";

        yield return "Separate automated probes and suspicious paths, such as /wp-login.php, from genuine user-facing application errors.";
        yield return "Confirm that all expected scheduled processes executed within their normal operating window.";

        if (a.Health == SystemHealthLevel.Stable)
            yield return "No immediate corrective action is required; continue routine monitoring.";
        else if (a.Health == SystemHealthLevel.Critical)
            yield return "Escalate unresolved critical failures and verify data completeness after remediation.";
    }

    private static string Table(
        string[] headers,
        IEnumerable<string[]> rows,
        string empty = "No data available for this section.")
    {
        var list = rows.ToList();

        if (list.Count == 0)
            return $"<div style='padding:14px;background:#f8fafc;border:1px solid #e5e7eb;border-radius:8px;color:#64748b;font-size:13px'>{E(empty)}</div>";

        var s = new StringBuilder("<table width='100%' cellspacing='0' cellpadding='0' style='border-collapse:collapse;font-size:12px'>");
        s.Append("<tr>");

        foreach (var header in headers)
            s.Append($"<th align='left' style='padding:9px 7px;background:#f1f5f9;border-bottom:1px solid #cbd5e1;color:#475569'>{E(header)}</th>");

        s.Append("</tr>");

        foreach (var row in list)
        {
            s.Append("<tr>");

            foreach (var cell in row)
                s.Append($"<td valign='top' style='padding:9px 7px;border-bottom:1px solid #e5e7eb;line-height:1.4'>{cell}</td>");

            s.Append("</tr>");
        }

        return s.Append("</table>").ToString();
    }

    private static string Status(string value)
    {
        var normalized = value.ToUpperInvariant();
        var color = normalized switch
        {
            "SUCCESS" => "#15803d",
            "ERROR" or "FAILED" or "FAILURE" => "#b91c1c",
            "SUCCESS_WITH_ERRORS" or "ATTENTION" or "WARNING" => "#b45309",
            _ => "#b45309"
        };

        return $"<span style='font-weight:bold;color:{color}'>{E(value)}</span>";
    }

    private static string FmtPeriod(DateTime start, DateTime end) =>
        $"{start:MM/dd/yyyy HH:mm:ss} – {end:MM/dd/yyyy HH:mm:ss}";

    private static string FmtDuration(decimal? value) =>
        value.HasValue ? $"{value.Value:N2} s" : "—";

    private static string FmtDateTime(DateTime? value) =>
        value?.ToString("MM/dd HH:mm:ss") ?? "—";

    private static string FmtTime(DateTime? value) =>
        value?.ToString("HH:mm:ss") ?? "—";

    private static string Short(string value, int length)
    {
        value = (value ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        return value.Length <= length ? value : value[..length] + "…";
    }

    private static string E(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);
}
