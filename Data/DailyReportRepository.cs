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
            ReportDate = period.DisplayDate,
            PeriodStartEastern = period.StartEastern,
            PeriodEndEastern = period.EndEastern,
            ProcessSummary = await GetProcessSummaryAsync(connection, period, cancellationToken),
            ProcessGroups = await GetProcessGroupsAsync(connection, period, cancellationToken),
            OperationErrors = await GetOperationErrorsAsync(connection, period, cancellationToken),
            ProcessErrors = await GetProcessErrorsAsync(connection, period, cancellationToken),
            UserActivity = await GetUserActivityAsync(connection, period, cancellationToken),
            VisitSummary = await GetVisitSummaryAsync(connection, period, cancellationToken),
            PageVisits = await GetPageVisitsAsync(connection, period, cancellationToken),
            CompanyVisits = await GetCompanyVisitsAsync(connection, period, cancellationToken),
            PageTypeVisits = await GetPageTypeVisitsAsync(connection, period, cancellationToken),
            ReferrerVisits = await GetReferrerVisitsAsync(connection, period, cancellationToken),
            CountryVisits = await GetCountryVisitsAsync(connection, period, cancellationToken),
            ClientTypeVisits = await GetClientTypeVisitsAsync(connection, period, cancellationToken)
        };
    }

    /// <summary>
    /// Builds the interval from 9:26 AM Eastern on the requested start date
    /// through the current moment. With DefaultDaysOffset = -1, a report run
    /// today reads everything from yesterday at 9:26 AM Eastern until now.
    ///
    /// The displayed report date is today's date in Eastern Time, not the
    /// requested start date.
    /// </summary>
    private ReportPeriod CreateReportPeriod(DateTime reportDate)
    {
        var nowUtc = DateTime.UtcNow;
        var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _easternTimeZone);

        var startEastern = DateTime.SpecifyKind(
            reportDate.Date.AddHours(9).AddMinutes(26),
            DateTimeKind.Unspecified);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startEastern, _easternTimeZone);

        // Protect against a future/invalid start date.
        var endUtc = nowUtc >= startUtc ? nowUtc : startUtc;
        var endEastern = TimeZoneInfo.ConvertTimeFromUtc(endUtc, _easternTimeZone);

        return new ReportPeriod(
            DisplayDate: nowEastern.Date,
            StartEastern: startEastern,
            EndEastern: endEastern,
            StartUtc: startUtc,
            EndUtc: endUtc);
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
       SUM(CASE
               WHEN UPPER(ISNULL(Status,'')) = 'SUCCESS'
                    AND ISNULL(ErrorCount,0) = 0
                    AND ProcessException IS NULL
               THEN 1 ELSE 0
           END) EjecucionesExitosas,
       SUM(CASE
               WHEN UPPER(ISNULL(Status,'')) IN ('ERROR','FAILED','FAILURE','SUCCESS_WITH_ERRORS')
                    OR ISNULL(ErrorCount,0) > 0
                    OR ProcessException IS NOT NULL
               THEN 1 ELSE 0
           END) EjecucionesConError,
       SUM(CASE
               WHEN UPPER(ISNULL(Status,'')) NOT IN ('SUCCESS','ERROR','FAILED','FAILURE','SUCCESS_WITH_ERRORS')
                    AND ISNULL(ErrorCount,0) = 0
                    AND ProcessException IS NULL
               THEN 1 ELSE 0
           END) OtrosEstados,
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
       CASE
           WHEN SUM(CASE WHEN UPPER(ISNULL(Status,'')) IN ('ERROR','FAILED','FAILURE','SUCCESS_WITH_ERRORS')
                              OR ISNULL(ErrorCount,0) > 0 OR ProcessException IS NOT NULL THEN 1 ELSE 0 END) > 0
               THEN 'ATTENTION'
           WHEN SUM(CASE WHEN ISNULL(WarningCount,0) > 0 THEN 1 ELSE 0 END) > 0
               THEN 'WARNING'
           ELSE 'SUCCESS'
       END Status,
       COUNT(*) CantidadEjecuciones,
       SUM(CASE WHEN UPPER(ISNULL(Status,'')) = 'SUCCESS' AND ISNULL(ErrorCount,0) = 0 AND ProcessException IS NULL THEN 1 ELSE 0 END) EjecucionesExitosas,
       SUM(CASE WHEN UPPER(ISNULL(Status,'')) IN ('ERROR','FAILED','FAILURE','SUCCESS_WITH_ERRORS') OR ISNULL(ErrorCount,0) > 0 OR ProcessException IS NOT NULL THEN 1 ELSE 0 END) EjecucionesConError,
       SUM(CASE WHEN ISNULL(WarningCount,0) > 0 THEN 1 ELSE 0 END) EjecucionesConWarnings,
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
GROUP BY ApplicationName, RunMode
ORDER BY CASE WHEN SUM(CASE WHEN UPPER(ISNULL(Status,'')) IN ('ERROR','FAILED','FAILURE','SUCCESS_WITH_ERRORS') OR ISNULL(ErrorCount,0) > 0 OR ProcessException IS NOT NULL THEN 1 ELSE 0 END) > 0 THEN 0 ELSE 1 END,
         ApplicationName, RunMode;
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
                SuccessfulExecutions = Db.Int(reader, "EjecucionesExitosas"),
                ErrorExecutions = Db.Int(reader, "EjecucionesConError"),
                WarningExecutions = Db.Int(reader, "EjecucionesConWarnings"),
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
        UPPER(ISNULL(Status,'')) IN ('ERROR','FAILED','FAILURE','SUCCESS_WITH_ERRORS')
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

    private async Task<UserActivitySummary> GetUserActivityAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string registrationSql = """
SELECT COUNT(*) RegisteredUsers,
       SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) RegisteredAndActiveUsers,
       SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) RegisteredButInactiveUsers
FROM [AmericaMarket].[dbo].[Users]
WHERE CreatedAt >= @StartUtc
  AND CreatedAt < @EndUtc;
""";

        var result = new UserActivitySummary();

        await using (var command = CreateCommand(connection, registrationSql, period))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                result.RegisteredUsers = Db.Int(reader, "RegisteredUsers");
                result.RegisteredAndActiveUsers = Db.Int(reader, "RegisteredAndActiveUsers");
                result.RegisteredButInactiveUsers = Db.Int(reader, "RegisteredButInactiveUsers");
            }
        }

        const string deactivationColumnSql = """
SELECT CASE
           WHEN COL_LENGTH('AmericaMarket.dbo.Users', 'DeactivatedAt') IS NULL THEN 0
           ELSE 1
       END;
""";

        await using (var columnCommand = new SqlCommand(deactivationColumnSql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = _commandTimeoutSeconds
        })
        {
            var hasColumnValue = await columnCommand.ExecuteScalarAsync(cancellationToken);
            var hasDeactivatedAt = Convert.ToInt32(hasColumnValue) == 1;

            if (!hasDeactivatedAt)
            {
                result.DeactivatedUsers = null;
                return result;
            }
        }

        const string deactivationSql = """
SELECT COUNT(*)
FROM [AmericaMarket].[dbo].[Users]
WHERE DeactivatedAt >= @StartUtc
  AND DeactivatedAt < @EndUtc;
""";

        await using (var command = CreateCommand(connection, deactivationSql, period))
        {
            var value = await command.ExecuteScalarAsync(cancellationToken);
            result.DeactivatedUsers = value is null or DBNull ? 0 : Convert.ToInt32(value);
        }

        return result;
    }

    private async Task<VisitGeneralSummary> GetVisitSummaryAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT COUNT(*) TotalVisitas,
       SUM(CASE WHEN ISNULL(IsBot,0) = 0 THEN 1 ELSE 0 END) VisitasHumanas,
       SUM(CASE WHEN ISNULL(IsBot,0) = 1 THEN 1 ELSE 0 END) VisitasBots,
       COUNT(DISTINCT CASE WHEN ISNULL(IsBot,0) = 0 THEN NULLIF(CountryCode,'') END) Paises,
       SUM(CASE WHEN ISNULL(IsBot,0) = 0 AND CountryCode = 'US' THEN 1 ELSE 0 END) VisitasEstadosUnidos,
       COUNT(DISTINCT CASE WHEN ISNULL(IsBot,0) = 0 THEN [Path] END) PaginasDiferentes,
       COUNT(DISTINCT CASE WHEN ISNULL(IsBot,0) = 0 THEN NULLIF(Referrer,'') END) ReferrersDiferentes,
       MIN(CASE WHEN ISNULL(IsBot,0) = 0 THEN CreatedAt END) PrimeraVisita,
       MAX(CASE WHEN ISNULL(IsBot,0) = 0 THEN CreatedAt END) UltimaVisita
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
            HumanVisits = Db.Int(reader, "VisitasHumanas"),
            BotVisits = Db.Int(reader, "VisitasBots"),
            HumanVisitPercent = Db.Int(reader, "TotalVisitas") == 0 ? 0 : Math.Round(Db.Int(reader, "VisitasHumanas") * 100m / Db.Int(reader, "TotalVisitas"), 1),
            Countries = Db.Int(reader, "Paises"),
            UnitedStatesVisits = Db.Int(reader, "VisitasEstadosUnidos"),
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
  AND ISNULL(IsBot,0) = 0
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
      AND ISNULL(IsBot,0) = 0
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
      AND ISNULL(IsBot,0) = 0
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
  AND ISNULL(IsBot,0) = 0
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

    private async Task<List<CountryVisitSummary>> GetCountryVisitsAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
WITH HumanVisits AS
(
    SELECT COALESCE(NULLIF(CountryCode,''), 'UNKNOWN') CountryCode
    FROM dbo.AppPageVisitLog
    WHERE CreatedAt >= @StartUtc
      AND CreatedAt < @EndUtc
      AND ISNULL(IsBot,0) = 0
), Totals AS
(
    SELECT COUNT(*) TotalHumanVisits FROM HumanVisits
)
SELECT h.CountryCode,
       COUNT(*) CantidadVisitas,
       CAST(CASE WHEN t.TotalHumanVisits = 0 THEN 0 ELSE COUNT(*) * 100.0 / t.TotalHumanVisits END AS DECIMAL(6,2)) Porcentaje
FROM HumanVisits h
CROSS JOIN Totals t
GROUP BY h.CountryCode, t.TotalHumanVisits
ORDER BY CantidadVisitas DESC, h.CountryCode;
""";

        var list = new List<CountryVisitSummary>();
        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new CountryVisitSummary
            {
                CountryCode = Db.String(reader, "CountryCode"),
                VisitCount = Db.Int(reader, "CantidadVisitas"),
                PercentOfHumanVisits = Db.Decimal(reader, "Porcentaje") ?? 0
            });
        }
        return list;
    }

    private async Task<List<ClientTypeVisitSummary>> GetClientTypeVisitsAsync(
        SqlConnection connection,
        ReportPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
WITH HumanVisits AS
(
    SELECT COALESCE(NULLIF(ClientType,''), 'UNKNOWN') ClientType
    FROM dbo.AppPageVisitLog
    WHERE CreatedAt >= @StartUtc
      AND CreatedAt < @EndUtc
      AND ISNULL(IsBot,0) = 0
), Totals AS
(
    SELECT COUNT(*) TotalHumanVisits FROM HumanVisits
)
SELECT h.ClientType,
       COUNT(*) CantidadVisitas,
       CAST(CASE WHEN t.TotalHumanVisits = 0 THEN 0 ELSE COUNT(*) * 100.0 / t.TotalHumanVisits END AS DECIMAL(6,2)) Porcentaje
FROM HumanVisits h
CROSS JOIN Totals t
GROUP BY h.ClientType, t.TotalHumanVisits
ORDER BY CantidadVisitas DESC, h.ClientType;
""";

        var list = new List<ClientTypeVisitSummary>();
        await using var command = CreateCommand(connection, sql, period);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ClientTypeVisitSummary
            {
                ClientType = Db.String(reader, "ClientType"),
                VisitCount = Db.Int(reader, "CantidadVisitas"),
                PercentOfHumanVisits = Db.Decimal(reader, "Porcentaje") ?? 0
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
        DateTime DisplayDate,
        DateTime StartEastern,
        DateTime EndEastern,
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