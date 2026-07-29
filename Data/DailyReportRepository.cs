using System.Data;
using FinzatiDailyReport.Models;
using Microsoft.Data.SqlClient;

namespace FinzatiDailyReport.Data;

public sealed class DailyReportRepository
{
    private const string WindowsEasternTimeZoneId = "Eastern Standard Time";
    private const string IanaEasternTimeZoneId = "America/New_York";

    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;
    private readonly TimeZoneInfo _easternTimeZone;

    public DailyReportRepository(string connectionString, int commandTimeoutSeconds)
    {
        _connectionString = connectionString;
        _commandTimeoutSeconds = commandTimeoutSeconds;
        _easternTimeZone = ResolveEasternTimeZone();
    }

    public async Task<DailyReportData> GetDailyReportAsync(
        DateTime reportDate,
        CancellationToken cancellationToken)
    {
        var period = CreateReportPeriod(reportDate);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return new DailyReportData
        {
            ReportDate = period.ReportDate,
            ProcessSummary = await GetProcessSummaryAsync(connection, period, cancellationToken),
            ProcessGroups = await GetProcessGroupsAsync(connection, period, cancellationToken),
            OperationErrors = await GetOperationErrorsAsync(connection, period, cancellationToken),
            ProcessErrors = await GetProcessErrorsAsync(connection, period, cancellationToken),
            VisitSummary = await GetVisitSummaryAsync(connection, period, cancellationToken),
            PageVisits = await GetPageVisitsAsync(connection, period, cancellationToken),
            CompanyVisits = await GetCompanyVisitsAsync(connection, period, cancellationToken),
            PageTypeVisits = await GetPageTypeVisitsAsync(connection, period, cancellationToken),
            ReferrerVisits = await GetReferrerVisitsAsync(connection, period, cancellationToken)
        };
    }

    /// <summary>
    /// Creates the UTC interval corresponding to the requested calendar day in
    /// Miami/New York time.
    ///
    /// For today's report, the interval ends at the current UTC time so the
    /// report covers local midnight through the moment it is generated.
    /// For a past date, the interval covers the complete local calendar day.
    /// Daylight-saving changes are handled automatically by TimeZoneInfo.
    /// </summary>
    private ReportPeriod CreateReportPeriod(DateTime reportDate)
    {
        var localReportDate = reportDate.Date;
        var localStart = DateTime.SpecifyKind(localReportDate, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, _easternTimeZone);
        var nextDayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, _easternTimeZone);

        var nowUtc = DateTime.UtcNow;
        var todayEastern = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _easternTimeZone).Date;

        DateTime endUtc;

        if (localReportDate == todayEastern)
        {
            endUtc = nowUtc < nextDayStartUtc ? nowUtc : nextDayStartUtc;
        }
        else if (localReportDate < todayEastern)
        {
            endUtc = nextDayStartUtc;
        }
        else
        {
            // A future report date has no elapsed reporting window yet.
            endUtc = startUtc;
        }

        return new ReportPeriod(localReportDate, startUtc, endUtc);
    }

    private SqlCommand CreateCommand(
        SqlConnection connection,
        string sql,
        ReportPeriod period)
    {
        var command = new SqlCommand(sql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = _commandTimeoutSeconds
        };

        command.Parameters.Add("@StartUtc", SqlDbType.DateTime2).Value = period.StartUtc;
        command.Parameters.Add("@EndUtc", SqlDbType.DateTime2).Value = period.EndUtc;

        return command;
    }

    private async Task<ProcessGeneralSummary> GetProcessSummaryAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT COUNT(*) TotalEjecuciones,
       SUM(CASE WHEN UPPER(ISNULL(Status,'')) = 'SUCCESS' THEN 1 ELSE 0 END) EjecucionesExitosas,
       SUM(CASE WHEN UPPER(ISNULL(Status,'')) IN ('ERROR','FAILED','FAILURE') THEN 1 ELSE 0 END) EjecucionesConError,
       SUM(CASE WHEN UPPER(ISNULL(Status,'')) NOT IN ('SUCCESS','ERROR','FAILED','FAILURE') THEN 1 ELSE 0 END) OtrosEstados,
       COUNT(DISTINCT ApplicationName) CantidadAplicaciones,
       SUM(ISNULL(TotalItems,0)) TotalItems,
       SUM(ISNULL(SuccessCount,0)) TotalExitosos,
       SUM(ISNULL(ErrorCount,0)) TotalErrores,
       SUM(ISNULL(WarningCount,0)) TotalWarnings,
       MIN(StartedDate) PrimeraEjecucion,
       MAX(FinishedDate) UltimaFinalizacion,
       AVG(CAST(DurationSeconds AS DECIMAL(18,2))) DuracionPromedioSegundos
FROM dbo.AmericaMarketProcessRuns
WHERE CreatedDate >= @StartUtc
  AND CreatedDate < @EndUtc;
""";

        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return new ProcessGeneralSummary();

        return new ProcessGeneralSummary
        {
            TotalExecutions = Db.Int(reader, "TotalEjecuciones"),
            SuccessfulExecutions = Db.Int(reader, "EjecucionesExitosas"),
            ErrorExecutions = Db.Int(reader, "EjecucionesConError"),
            OtherStatuses = Db.Int(reader, "OtrosEstados"),
            ApplicationCount = Db.Int(reader, "CantidadAplicaciones"),
            TotalItems = Db.Long(reader, "TotalItems"),
            TotalSuccess = Db.Long(reader, "TotalExitosos"),
            TotalErrors = Db.Long(reader, "TotalErrores"),
            TotalWarnings = Db.Long(reader, "TotalWarnings"),
            FirstExecution = Db.UtcDateToTimeZone(reader, "PrimeraEjecucion", _easternTimeZone),
            LastCompletion = Db.UtcDateToTimeZone(reader, "UltimaFinalizacion", _easternTimeZone),
            AverageDurationSeconds = Db.Decimal(reader, "DuracionPromedioSegundos")
        };
    }

    private async Task<List<ProcessGroupSummary>> GetProcessGroupsAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT ApplicationName,
       RunMode,
       Status,
       COUNT(*) CantidadEjecuciones,
       MIN(StartedDate) PrimeraEjecucion,
       MAX(StartedDate) UltimaEjecucion,
       AVG(CAST(DurationSeconds AS DECIMAL(18,2))) DuracionPromedioSegundos,
       MAX(DurationSeconds) DuracionMaximaSegundos,
       SUM(ISNULL(TotalItems,0)) TotalItems,
       SUM(ISNULL(SuccessCount,0)) TotalExitosos,
       SUM(ISNULL(ErrorCount,0)) TotalErrores,
       SUM(ISNULL(WarningCount,0)) TotalWarnings
FROM dbo.AmericaMarketProcessRuns
WHERE CreatedDate >= @StartUtc
  AND CreatedDate < @EndUtc
GROUP BY ApplicationName, RunMode, Status
ORDER BY ApplicationName, RunMode, Status;
""";

        var list = new List<ProcessGroupSummary>();

        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ProcessGroupSummary
            {
                ApplicationName = Db.String(reader, "ApplicationName"),
                RunMode = Db.String(reader, "RunMode"),
                Status = Db.String(reader, "Status"),
                ExecutionCount = Db.Int(reader, "CantidadEjecuciones"),
                FirstExecution = Db.UtcDateToTimeZone(reader, "PrimeraEjecucion", _easternTimeZone),
                LastExecution = Db.UtcDateToTimeZone(reader, "UltimaEjecucion", _easternTimeZone),
                AverageDurationSeconds = Db.Decimal(reader, "DuracionPromedioSegundos"),
                MaximumDurationSeconds = Db.Decimal(reader, "DuracionMaximaSegundos"),
                TotalItems = Db.Long(reader, "TotalItems"),
                TotalSuccess = Db.Long(reader, "TotalExitosos"),
                TotalErrors = Db.Long(reader, "TotalErrores"),
                TotalWarnings = Db.Long(reader, "TotalWarnings")
            });
        }

        return list;
    }

    private async Task<List<OperationErrorSummary>> GetOperationErrorsAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT ISNULL([Path], '(No path)') [Path],
       ISNULL(ProcessException, '(No exception detail)') Error,
       COUNT(*) CantidadRegistros,
       MIN(CreatedDate) PrimeraOcurrencia,
       MAX(CreatedDate) UltimaOcurrencia
FROM dbo.AmericaMarketOperationLogs
WHERE CreatedDate >= @StartUtc
  AND CreatedDate < @EndUtc
  AND ProcessException IS NOT NULL
GROUP BY [Path], ProcessException
ORDER BY CantidadRegistros DESC, [Path];
""";

        var list = new List<OperationErrorSummary>();

        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new OperationErrorSummary
            {
                Path = Db.String(reader, "Path"),
                Error = Db.String(reader, "Error"),
                Count = Db.Int(reader, "CantidadRegistros"),
                FirstOccurrence = Db.UtcDateToTimeZone(reader, "PrimeraOcurrencia", _easternTimeZone),
                LastOccurrence = Db.UtcDateToTimeZone(reader, "UltimaOcurrencia", _easternTimeZone)
            });
        }

        return list;
    }

    private async Task<List<ProcessErrorSummary>> GetProcessErrorsAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT ISNULL(ApplicationName, '(Unknown)') ApplicationName,
       ISNULL(RunMode, '(Not specified)') RunMode,
       ISNULL(Status, '(Unknown)') Status,
       ISNULL(ExceptionType, '') ExceptionType,
       ISNULL(ProcessException, '(No exception detail)') ProcessException,
       COUNT(*) CantidadErrores,
       MIN(CreatedDate) PrimeraOcurrencia,
       MAX(CreatedDate) UltimaOcurrencia
FROM dbo.AmericaMarketProcessRuns
WHERE CreatedDate >= @StartUtc
  AND CreatedDate < @EndUtc
  AND (
        UPPER(ISNULL(Status,'')) IN ('ERROR','FAILED','FAILURE')
        OR ISNULL(ErrorCount,0) > 0
        OR ProcessException IS NOT NULL
      )
GROUP BY ApplicationName, RunMode, Status, ExceptionType, ProcessException
ORDER BY CantidadErrores DESC, UltimaOcurrencia DESC;
""";

        var list = new List<ProcessErrorSummary>();

        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ProcessErrorSummary
            {
                ApplicationName = Db.String(reader, "ApplicationName"),
                RunMode = Db.String(reader, "RunMode"),
                Status = Db.String(reader, "Status"),
                ExceptionType = Db.String(reader, "ExceptionType"),
                ProcessException = Db.String(reader, "ProcessException"),
                ErrorCount = Db.Int(reader, "CantidadErrores"),
                FirstOccurrence = Db.UtcDateToTimeZone(reader, "PrimeraOcurrencia", _easternTimeZone),
                LastOccurrence = Db.UtcDateToTimeZone(reader, "UltimaOcurrencia", _easternTimeZone)
            });
        }

        return list;
    }

    private async Task<VisitGeneralSummary> GetVisitSummaryAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT COUNT(*) TotalVisitas,
       COUNT(DISTINCT [Path]) PaginasDiferentes,
       COUNT(DISTINCT NULLIF(Referrer,'')) ReferrersDiferentes,
       MIN(CreatedAt) PrimeraVisita,
       MAX(CreatedAt) UltimaVisita
FROM dbo.AppPageVisitLog
WHERE CreatedAt >= @StartUtc
  AND CreatedAt < @EndUtc;
""";

        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return new VisitGeneralSummary();

        return new VisitGeneralSummary
        {
            TotalVisits = Db.Int(reader, "TotalVisitas"),
            DifferentPages = Db.Int(reader, "PaginasDiferentes"),
            DifferentReferrers = Db.Int(reader, "ReferrersDiferentes"),
            FirstVisit = Db.UtcDateToTimeZone(reader, "PrimeraVisita", _easternTimeZone),
            LastVisit = Db.UtcDateToTimeZone(reader, "UltimaVisita", _easternTimeZone)
        };
    }

    private async Task<List<PageVisitSummary>> GetPageVisitsAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT ISNULL([Path], '(No path)') [Path],
       COUNT(*) CantidadVisitas,
       MIN(CreatedAt) PrimeraVisita,
       MAX(CreatedAt) UltimaVisita
FROM dbo.AppPageVisitLog
WHERE CreatedAt >= @StartUtc
  AND CreatedAt < @EndUtc
GROUP BY [Path]
ORDER BY CantidadVisitas DESC, [Path];
""";

        var list = new List<PageVisitSummary>();

        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new PageVisitSummary
            {
                Path = Db.String(reader, "Path"),
                VisitCount = Db.Int(reader, "CantidadVisitas"),
                FirstVisit = Db.UtcDateToTimeZone(reader, "PrimeraVisita", _easternTimeZone),
                LastVisit = Db.UtcDateToTimeZone(reader, "UltimaVisita", _easternTimeZone)
            });
        }

        return list;
    }

    private async Task<List<CompanyVisitSummary>> GetCompanyVisitsAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
WITH CompanyPaths AS
(
    SELECT CreatedAt,
           UPPER(
               LEFT(
                   SUBSTRING([Path], CHARINDEX('/company/', [Path]) + 9, 4000),
                   CHARINDEX(
                       '/',
                       SUBSTRING([Path], CHARINDEX('/company/', [Path]) + 9, 4000) + '/'
                   ) - 1
               )
           ) Symbol
    FROM dbo.AppPageVisitLog
    WHERE CreatedAt >= @StartUtc
      AND CreatedAt < @EndUtc
      AND ([Path] LIKE '/en/company/%' OR [Path] LIKE '/es/company/%')
)
SELECT Symbol,
       COUNT(*) CantidadVisitas,
       MIN(CreatedAt) PrimeraVisita,
       MAX(CreatedAt) UltimaVisita
FROM CompanyPaths
WHERE NULLIF(Symbol, '') IS NOT NULL
GROUP BY Symbol
ORDER BY CantidadVisitas DESC, Symbol;
""";

        var list = new List<CompanyVisitSummary>();

        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new CompanyVisitSummary
            {
                Symbol = Db.String(reader, "Symbol"),
                VisitCount = Db.Int(reader, "CantidadVisitas"),
                FirstVisit = Db.UtcDateToTimeZone(reader, "PrimeraVisita", _easternTimeZone),
                LastVisit = Db.UtcDateToTimeZone(reader, "UltimaVisita", _easternTimeZone)
            });
        }

        return list;
    }

    private async Task<List<PageTypeVisitSummary>> GetPageTypeVisitsAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
WITH Classified AS
(
    SELECT [Path],
           CreatedAt,
           CASE
               WHEN [Path] IN ('/en','/es','/en/','/es/') THEN 'Home'
               WHEN [Path] LIKE '/en/company/%' OR [Path] LIKE '/es/company/%' THEN 'Company'
               WHEN [Path] LIKE '%/screener%' THEN 'Screener'
               WHEN [Path] LIKE '%/watchlist%' THEN 'Watchlist'
               WHEN [Path] LIKE '%/news%' THEN 'News'
               WHEN [Path] LIKE '%/calendar%' THEN 'Calendar'
               WHEN [Path] LIKE '%/billing%' THEN 'Billing'
               ELSE 'Other'
           END TipoPagina
    FROM dbo.AppPageVisitLog
    WHERE CreatedAt >= @StartUtc
      AND CreatedAt < @EndUtc
)
SELECT TipoPagina,
       COUNT(*) CantidadVisitas,
       COUNT(DISTINCT [Path]) PaginasDiferentes,
       MIN(CreatedAt) PrimeraVisita,
       MAX(CreatedAt) UltimaVisita
FROM Classified
GROUP BY TipoPagina
ORDER BY CantidadVisitas DESC;
""";

        var list = new List<PageTypeVisitSummary>();

        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new PageTypeVisitSummary
            {
                PageType = Db.String(reader, "TipoPagina"),
                VisitCount = Db.Int(reader, "CantidadVisitas"),
                DifferentPages = Db.Int(reader, "PaginasDiferentes"),
                FirstVisit = Db.UtcDateToTimeZone(reader, "PrimeraVisita", _easternTimeZone),
                LastVisit = Db.UtcDateToTimeZone(reader, "UltimaVisita", _easternTimeZone)
            });
        }

        return list;
    }

    private async Task<List<ReferrerVisitSummary>> GetReferrerVisitsAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT COALESCE(NULLIF(Referrer,''), '(Direct / No referrer)') Referrer,
       COUNT(*) CantidadVisitas,
       MIN(CreatedAt) PrimeraVisita,
       MAX(CreatedAt) UltimaVisita
FROM dbo.AppPageVisitLog
WHERE CreatedAt >= @StartUtc
  AND CreatedAt < @EndUtc
GROUP BY COALESCE(NULLIF(Referrer,''), '(Direct / No referrer)')
ORDER BY CantidadVisitas DESC;
""";

        var list = new List<ReferrerVisitSummary>();

        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ReferrerVisitSummary
            {
                Referrer = Db.String(reader, "Referrer"),
                VisitCount = Db.Int(reader, "CantidadVisitas"),
                FirstVisit = Db.UtcDateToTimeZone(reader, "PrimeraVisita", _easternTimeZone),
                LastVisit = Db.UtcDateToTimeZone(reader, "UltimaVisita", _easternTimeZone)
            });
        }

        return list;
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsEasternTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IanaEasternTimeZoneId);
        }
    }

    private sealed record ReportPeriod(
        DateTime ReportDate,
        DateTime StartUtc,
        DateTime EndUtc);

    private static class Db
    {
        public static string String(SqlDataReader reader, string columnName) =>
            reader[columnName] is DBNull
                ? string.Empty
                : Convert.ToString(reader[columnName])?.Trim() ?? string.Empty;

        public static int Int(SqlDataReader reader, string columnName) =>
            reader[columnName] is DBNull
                ? 0
                : Convert.ToInt32(reader[columnName]);

        public static long Long(SqlDataReader reader, string columnName) =>
            reader[columnName] is DBNull
                ? 0
                : Convert.ToInt64(reader[columnName]);

        public static decimal? Decimal(SqlDataReader reader, string columnName) =>
            reader[columnName] is DBNull
                ? null
                : Convert.ToDecimal(reader[columnName]);

        public static DateTime? UtcDateToTimeZone(
            SqlDataReader reader,
            string columnName,
            TimeZoneInfo destinationTimeZone)
        {
            if (reader[columnName] is DBNull)
                return null;

            var databaseValue = Convert.ToDateTime(reader[columnName]);

            // SQL Server datetime/datetime2 values normally arrive with Kind=Unspecified.
            // The application stores these columns with SYSUTCDATETIME(), so explicitly
            // mark them as UTC before converting them to Miami/New York local time.
            var utcValue = DateTime.SpecifyKind(databaseValue, DateTimeKind.Utc);

            return TimeZoneInfo.ConvertTimeFromUtc(utcValue, destinationTimeZone);
        }
    }
}