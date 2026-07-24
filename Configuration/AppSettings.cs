namespace FinzatiDailyReport.Configuration;

public sealed class AppSettings
{
    public ConnectionStringsSettings ConnectionStrings { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public ReportSettings Report { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
}

public sealed class ConnectionStringsSettings
{
    public string AmericaMarketFMPLogs { get; set; } = string.Empty;
}

public sealed class DatabaseSettings
{
    public int CommandTimeoutSeconds { get; set; } = 180;
}

public sealed class EmailSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Finzati Monitoring";
    public List<string> To { get; set; } = [];
    public List<string> Cc { get; set; } = [];
    public string SubjectTemplate { get; set; } = "Finzati | Daily summary of processes and activity – {date}";
}

public sealed class ReportSettings
{
    public string Title { get; set; } = "Daily summary of processes and activity";
    public string BrandName { get; set; } = "Finzati";
    public string PreparedBy { get; set; } = "Finzati Support Team";
    public string TimeZoneDisplayName { get; set; } = "America/New_York";
    public int DefaultDaysOffset { get; set; } = -1;
    public int TopErrors { get; set; } = 5;
    public int TopPages { get; set; } = 10;
    public int TopCompanies { get; set; } = 10;
    public int TopReferrers { get; set; } = 10;
    public int CriticalErrorThreshold { get; set; } = 25;
    public int IncidentErrorThreshold { get; set; } = 1;
    public string OutputDirectory { get; set; } = "output";
    public List<string> IgnorePathPrefixes { get; set; } = ["/wp-admin", "/wp-login", "/.env", "/xmlrpc.php"];
}

public sealed class LoggingSettings
{
    public string Directory { get; set; } = "logs";
    public int RetentionDays { get; set; } = 30;
}
