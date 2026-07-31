namespace FinzatiDailyReport.Models;

public sealed class DailyReportData
{
    // Date shown in the email subject, file name, and report header.
    public DateTime ReportDate { get; init; }

    // Actual reporting window in Eastern Time.
    public DateTime PeriodStartEastern { get; init; }
    public DateTime PeriodEndEastern { get; init; }

    public ProcessGeneralSummary ProcessSummary { get; init; } = new();
    public List<ProcessGroupSummary> ProcessGroups { get; init; } = [];
    public List<OperationErrorSummary> OperationErrors { get; init; } = [];
    public List<ProcessErrorSummary> ProcessErrors { get; init; } = [];
    public UserActivitySummary UserActivity { get; init; } = new();
    public VisitGeneralSummary VisitSummary { get; init; } = new();
    public List<PageVisitSummary> PageVisits { get; init; } = [];
    public List<CompanyVisitSummary> CompanyVisits { get; init; } = [];
    public List<PageTypeVisitSummary> PageTypeVisits { get; init; } = [];
    public List<ReferrerVisitSummary> ReferrerVisits { get; init; } = [];
}

public sealed class ProcessGeneralSummary
{
    public int TotalExecutions { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int ErrorExecutions { get; set; }
    public int OtherStatuses { get; set; }
    public int ApplicationCount { get; set; }
    public long TotalItems { get; set; }
    public long TotalSuccess { get; set; }
    public long TotalErrors { get; set; }
    public long TotalWarnings { get; set; }
    public DateTime? FirstExecution { get; set; }
    public DateTime? LastCompletion { get; set; }
    public decimal? AverageDurationSeconds { get; set; }
}

public sealed class ProcessGroupSummary
{
    public string ApplicationName { get; set; } = string.Empty;
    public string RunMode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ExecutionCount { get; set; }
    public DateTime? FirstExecution { get; set; }
    public DateTime? LastExecution { get; set; }
    public decimal? AverageDurationSeconds { get; set; }
    public decimal? MaximumDurationSeconds { get; set; }
    public long TotalItems { get; set; }
    public long TotalSuccess { get; set; }
    public long TotalErrors { get; set; }
    public long TotalWarnings { get; set; }
}

public sealed class OperationErrorSummary
{
    public string Path { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime? FirstOccurrence { get; set; }
    public DateTime? LastOccurrence { get; set; }
}

public sealed class ProcessErrorSummary
{
    public string ApplicationName { get; set; } = string.Empty;
    public string RunMode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
    public string ProcessException { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public DateTime? FirstOccurrence { get; set; }
    public DateTime? LastOccurrence { get; set; }
}

public sealed class UserActivitySummary
{
    public int RegisteredUsers { get; set; }
    public int RegisteredAndActiveUsers { get; set; }
    public int RegisteredButInactiveUsers { get; set; }

    // Null means the Users table does not currently contain DeactivatedAt.
    public int? DeactivatedUsers { get; set; }
    public bool HasDeactivationTracking => DeactivatedUsers.HasValue;
}

public sealed class VisitGeneralSummary
{
    public int TotalVisits { get; set; }
    public int DifferentPages { get; set; }
    public int DifferentReferrers { get; set; }
    public DateTime? FirstVisit { get; set; }
    public DateTime? LastVisit { get; set; }
}

public sealed class PageVisitSummary
{
    public string Path { get; set; } = string.Empty;
    public int VisitCount { get; set; }
    public DateTime? FirstVisit { get; set; }
    public DateTime? LastVisit { get; set; }
}

public sealed class CompanyVisitSummary
{
    public string Symbol { get; set; } = string.Empty;
    public int VisitCount { get; set; }
    public DateTime? FirstVisit { get; set; }
    public DateTime? LastVisit { get; set; }
}

public sealed class PageTypeVisitSummary
{
    public string PageType { get; set; } = string.Empty;
    public int VisitCount { get; set; }
    public int DifferentPages { get; set; }
    public DateTime? FirstVisit { get; set; }
    public DateTime? LastVisit { get; set; }
}

public sealed class ReferrerVisitSummary
{
    public string Referrer { get; set; } = string.Empty;
    public int VisitCount { get; set; }
    public DateTime? FirstVisit { get; set; }
    public DateTime? LastVisit { get; set; }
}

public enum SystemHealthLevel { Stable, Incidents, Critical }

public sealed class ReportAnalysis
{
    public SystemHealthLevel Health { get; init; }
    public string HealthLabel { get; init; } = string.Empty;
    public string HealthExplanation { get; init; } = string.Empty;
    public int TotalOperationErrorEvents { get; init; }
    public int DifferentOperationErrors { get; init; }
    public int AutomatedProbeVisits { get; init; }
    public List<OperationErrorSummary> PriorityOperationErrors { get; init; } = [];
    public List<ProcessErrorSummary> PriorityProcessErrors { get; init; } = [];
    public List<string> RecommendedActions { get; init; } = [];
}
