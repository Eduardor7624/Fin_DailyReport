namespace FinzatiDailyReport.Configuration;

public static class SettingsValidator
{
    public static void Validate(AppSettings settings, bool noSend)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionStrings.AmericaMarketFMPLogs))
            throw new InvalidOperationException("ConnectionStrings:AmericaMarketFMPLogs is required.");

        if (settings.Database.CommandTimeoutSeconds <= 0)
            settings.Database.CommandTimeoutSeconds = 180;

        if (settings.Report.DefaultDaysOffset > 0)
            throw new InvalidOperationException("Report:DefaultDaysOffset cannot point to a future day.");

        if (noSend) return;

        if (string.IsNullOrWhiteSpace(settings.Email.Host))
            throw new InvalidOperationException("Email:Host is required.");
        if (settings.Email.Port <= 0)
            throw new InvalidOperationException("Email:Port must be greater than zero.");
        if (string.IsNullOrWhiteSpace(settings.Email.FromAddress))
            throw new InvalidOperationException("Email:FromAddress is required.");
        if (settings.Email.To.Count == 0 || settings.Email.To.All(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("At least one Email:To recipient is required.");
    }
}
