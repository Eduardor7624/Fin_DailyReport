using System.Data;
using FinzatiDailyReport.Models;
using Microsoft.Data.SqlClient;

namespace FinzatiDailyReport.Data;

public sealed class DailyReportRepository
{
    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;

    public DailyReportRepository(string connectionString, int commandTimeoutSeconds)
    {
        _connectionString = connectionString;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public async Task<DailyReportData> GetDailyReportAsync(DateTime reportDate, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return new DailyReportData
        {
            ReportDate = reportDate,
            ProcessSummary = await GetProcessSummaryAsync(connection, reportDate, cancellationToken),
            ProcessGroups = await GetProcessGroupsAsync(connection, reportDate, cancellationToken),
            OperationErrors = await GetOperationErrorsAsync(connection, reportDate, cancellationToken),
            ProcessErrors = await GetProcessErrorsAsync(connection, reportDate, cancellationToken),
            VisitSummary = await GetVisitSummaryAsync(connection, reportDate, cancellationToken),
            PageVisits = await GetPageVisitsAsync(connection, reportDate, cancellationToken),
            CompanyVisits = await GetCompanyVisitsAsync(connection, reportDate, cancellationToken),
            PageTypeVisits = await GetPageTypeVisitsAsync(connection, reportDate, cancellationToken),
            ReferrerVisits = await GetReferrerVisitsAsync(connection, reportDate, cancellationToken)
        };
    }

    private SqlCommand CreateCommand(SqlConnection connection, string sql, DateTime date)
    {
        var command = new SqlCommand(sql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = _commandTimeoutSeconds
        };
        command.Parameters.Add("@Fecha", SqlDbType.Date).Value = date.Date;
        return command;
    }

    private async Task<ProcessGeneralSummary> GetProcessSummaryAsync(SqlConnection c, DateTime d, CancellationToken ct)
    {
        const string sql = """
SELECT COUNT(*) TotalEjecuciones,
       SUM(CASE WHEN UPPER(ISNULL(Status,'')) = 'SUCCESS' THEN 1 ELSE 0 END) EjecucionesExitosas,
       SUM(CASE WHEN UPPER(ISNULL(Status,'')) IN ('ERROR','FAILED','FAILURE') THEN 1 ELSE 0 END) EjecucionesConError,
       SUM(CASE WHEN UPPER(ISNULL(Status,'')) NOT IN ('SUCCESS','ERROR','FAILED','FAILURE') THEN 1 ELSE 0 END) OtrosEstados,
       COUNT(DISTINCT ApplicationName) CantidadAplicaciones,
       SUM(ISNULL(TotalItems,0)) TotalItems, SUM(ISNULL(SuccessCount,0)) TotalExitosos,
       SUM(ISNULL(ErrorCount,0)) TotalErrores, SUM(ISNULL(WarningCount,0)) TotalWarnings,
       MIN(StartedDate) PrimeraEjecucion, MAX(FinishedDate) UltimaFinalizacion,
       AVG(CAST(DurationSeconds AS DECIMAL(18,2))) DuracionPromedioSegundos
FROM dbo.AmericaMarketProcessRuns
WHERE CreatedDate >= @Fecha AND CreatedDate < DATEADD(DAY,1,@Fecha);
""";
        await using var cmd = CreateCommand(c, sql, d);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return new();
        return new ProcessGeneralSummary
        {
            TotalExecutions = Db.Int(r,"TotalEjecuciones"), SuccessfulExecutions = Db.Int(r,"EjecucionesExitosas"),
            ErrorExecutions = Db.Int(r,"EjecucionesConError"), OtherStatuses = Db.Int(r,"OtrosEstados"),
            ApplicationCount = Db.Int(r,"CantidadAplicaciones"), TotalItems = Db.Long(r,"TotalItems"),
            TotalSuccess = Db.Long(r,"TotalExitosos"), TotalErrors = Db.Long(r,"TotalErrores"),
            TotalWarnings = Db.Long(r,"TotalWarnings"), FirstExecution = Db.Date(r,"PrimeraEjecucion"),
            LastCompletion = Db.Date(r,"UltimaFinalizacion"), AverageDurationSeconds = Db.Decimal(r,"DuracionPromedioSegundos")
        };
    }

    private async Task<List<ProcessGroupSummary>> GetProcessGroupsAsync(SqlConnection c, DateTime d, CancellationToken ct)
    {
        const string sql = """
SELECT ApplicationName, RunMode, Status, COUNT(*) CantidadEjecuciones,
       MIN(StartedDate) PrimeraEjecucion, MAX(StartedDate) UltimaEjecucion,
       AVG(CAST(DurationSeconds AS DECIMAL(18,2))) DuracionPromedioSegundos,
       MAX(DurationSeconds) DuracionMaximaSegundos,
       SUM(ISNULL(TotalItems,0)) TotalItems, SUM(ISNULL(SuccessCount,0)) TotalExitosos,
       SUM(ISNULL(ErrorCount,0)) TotalErrores, SUM(ISNULL(WarningCount,0)) TotalWarnings
FROM dbo.AmericaMarketProcessRuns
WHERE CreatedDate >= @Fecha AND CreatedDate < DATEADD(DAY,1,@Fecha)
GROUP BY ApplicationName,RunMode,Status
ORDER BY ApplicationName,RunMode,Status;
""";
        var list = new List<ProcessGroupSummary>();
        await using var cmd = CreateCommand(c, sql, d); await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new ProcessGroupSummary {
            ApplicationName=Db.String(r,"ApplicationName"), RunMode=Db.String(r,"RunMode"), Status=Db.String(r,"Status"),
            ExecutionCount=Db.Int(r,"CantidadEjecuciones"), FirstExecution=Db.Date(r,"PrimeraEjecucion"), LastExecution=Db.Date(r,"UltimaEjecucion"),
            AverageDurationSeconds=Db.Decimal(r,"DuracionPromedioSegundos"), MaximumDurationSeconds=Db.Decimal(r,"DuracionMaximaSegundos"),
            TotalItems=Db.Long(r,"TotalItems"), TotalSuccess=Db.Long(r,"TotalExitosos"), TotalErrors=Db.Long(r,"TotalErrores"), TotalWarnings=Db.Long(r,"TotalWarnings") });
        return list;
    }

    private async Task<List<OperationErrorSummary>> GetOperationErrorsAsync(SqlConnection c, DateTime d, CancellationToken ct)
    {
        const string sql = """
SELECT ISNULL([Path],'(No path)') [Path], ISNULL(ProcessException,'(No exception detail)') Error,
       COUNT(*) CantidadRegistros, MIN(CreatedDate) PrimeraOcurrencia, MAX(CreatedDate) UltimaOcurrencia
FROM dbo.AmericaMarketOperationLogs
WHERE CreatedDate >= @Fecha AND CreatedDate < DATEADD(DAY,1,@Fecha)
  AND ProcessException IS NOT NULL
GROUP BY [Path],ProcessException
ORDER BY CantidadRegistros DESC,[Path];
""";
        var list = new List<OperationErrorSummary>();
        await using var cmd = CreateCommand(c, sql, d); await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new OperationErrorSummary { Path=Db.String(r,"Path"), Error=Db.String(r,"Error"), Count=Db.Int(r,"CantidadRegistros"), FirstOccurrence=Db.Date(r,"PrimeraOcurrencia"), LastOccurrence=Db.Date(r,"UltimaOcurrencia") });
        return list;
    }

    private async Task<List<ProcessErrorSummary>> GetProcessErrorsAsync(SqlConnection c, DateTime d, CancellationToken ct)
    {
        const string sql = """
SELECT ISNULL(ApplicationName,'(Unknown)') ApplicationName, ISNULL(RunMode,'(Not specified)') RunMode,
       ISNULL(Status,'(Unknown)') Status, ISNULL(ExceptionType,'') ExceptionType,
       ISNULL(ProcessException,'(No exception detail)') ProcessException,
       COUNT(*) CantidadErrores, MIN(CreatedDate) PrimeraOcurrencia, MAX(CreatedDate) UltimaOcurrencia
FROM dbo.AmericaMarketProcessRuns
WHERE CreatedDate >= @Fecha AND CreatedDate < DATEADD(DAY,1,@Fecha)
  AND (UPPER(ISNULL(Status,'')) IN ('ERROR','FAILED','FAILURE') OR ISNULL(ErrorCount,0)>0 OR ProcessException IS NOT NULL)
GROUP BY ApplicationName,RunMode,Status,ExceptionType,ProcessException
ORDER BY CantidadErrores DESC,UltimaOcurrencia DESC;
""";
        var list = new List<ProcessErrorSummary>();
        await using var cmd = CreateCommand(c, sql, d); await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new ProcessErrorSummary { ApplicationName=Db.String(r,"ApplicationName"), RunMode=Db.String(r,"RunMode"), Status=Db.String(r,"Status"), ExceptionType=Db.String(r,"ExceptionType"), ProcessException=Db.String(r,"ProcessException"), ErrorCount=Db.Int(r,"CantidadErrores"), FirstOccurrence=Db.Date(r,"PrimeraOcurrencia"), LastOccurrence=Db.Date(r,"UltimaOcurrencia") });
        return list;
    }

    private async Task<VisitGeneralSummary> GetVisitSummaryAsync(SqlConnection c, DateTime d, CancellationToken ct)
    {
        const string sql = """
SELECT COUNT(*) TotalVisitas, COUNT(DISTINCT [Path]) PaginasDiferentes,
       COUNT(DISTINCT NULLIF(Referrer,'')) ReferrersDiferentes,
       MIN(CreatedAt) PrimeraVisita, MAX(CreatedAt) UltimaVisita
FROM dbo.AppPageVisitLog WHERE CreatedAt >= @Fecha AND CreatedAt < DATEADD(DAY,1,@Fecha);
""";
        await using var cmd=CreateCommand(c,sql,d); await using var r=await cmd.ExecuteReaderAsync(ct); if(!await r.ReadAsync(ct)) return new();
        return new VisitGeneralSummary { TotalVisits=Db.Int(r,"TotalVisitas"), DifferentPages=Db.Int(r,"PaginasDiferentes"), DifferentReferrers=Db.Int(r,"ReferrersDiferentes"), FirstVisit=Db.Date(r,"PrimeraVisita"), LastVisit=Db.Date(r,"UltimaVisita") };
    }

    private async Task<List<PageVisitSummary>> GetPageVisitsAsync(SqlConnection c, DateTime d, CancellationToken ct)
    {
        const string sql="""
SELECT ISNULL([Path],'(No path)') [Path], COUNT(*) CantidadVisitas, MIN(CreatedAt) PrimeraVisita, MAX(CreatedAt) UltimaVisita
FROM dbo.AppPageVisitLog WHERE CreatedAt >= @Fecha AND CreatedAt < DATEADD(DAY,1,@Fecha)
GROUP BY [Path] ORDER BY CantidadVisitas DESC,[Path];
""";
        var list=new List<PageVisitSummary>(); await using var cmd=CreateCommand(c,sql,d); await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new PageVisitSummary { Path=Db.String(r,"Path"), VisitCount=Db.Int(r,"CantidadVisitas"), FirstVisit=Db.Date(r,"PrimeraVisita"), LastVisit=Db.Date(r,"UltimaVisita") }); return list;
    }

    private async Task<List<CompanyVisitSummary>> GetCompanyVisitsAsync(SqlConnection c, DateTime d, CancellationToken ct)
    {
        const string sql="""
WITH CompanyPaths AS (
 SELECT CreatedAt,
   UPPER(LEFT(SUBSTRING([Path], CHARINDEX('/company/',[Path])+9, 4000),
     CHARINDEX('/', SUBSTRING([Path],CHARINDEX('/company/',[Path])+9,4000) + '/')-1)) Symbol
 FROM dbo.AppPageVisitLog
 WHERE CreatedAt >= @Fecha AND CreatedAt < DATEADD(DAY,1,@Fecha)
   AND ([Path] LIKE '/en/company/%' OR [Path] LIKE '/es/company/%')
)
SELECT Symbol,COUNT(*) CantidadVisitas,MIN(CreatedAt) PrimeraVisita,MAX(CreatedAt) UltimaVisita
FROM CompanyPaths WHERE NULLIF(Symbol,'') IS NOT NULL
GROUP BY Symbol ORDER BY CantidadVisitas DESC,Symbol;
""";
        var list=new List<CompanyVisitSummary>(); await using var cmd=CreateCommand(c,sql,d); await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new CompanyVisitSummary { Symbol=Db.String(r,"Symbol"), VisitCount=Db.Int(r,"CantidadVisitas"), FirstVisit=Db.Date(r,"PrimeraVisita"), LastVisit=Db.Date(r,"UltimaVisita") }); return list;
    }

    private async Task<List<PageTypeVisitSummary>> GetPageTypeVisitsAsync(SqlConnection c, DateTime d, CancellationToken ct)
    {
        const string sql="""
WITH Classified AS (
 SELECT [Path],CreatedAt,CASE WHEN [Path] IN ('/en','/es','/en/','/es/') THEN 'Home'
 WHEN [Path] LIKE '/en/company/%' OR [Path] LIKE '/es/company/%' THEN 'Company'
 WHEN [Path] LIKE '%/screener%' THEN 'Screener' WHEN [Path] LIKE '%/watchlist%' THEN 'Watchlist'
 WHEN [Path] LIKE '%/news%' THEN 'News' WHEN [Path] LIKE '%/calendar%' THEN 'Calendar'
 WHEN [Path] LIKE '%/billing%' THEN 'Billing' ELSE 'Other' END TipoPagina
 FROM dbo.AppPageVisitLog WHERE CreatedAt >= @Fecha AND CreatedAt < DATEADD(DAY,1,@Fecha)
)
SELECT TipoPagina,COUNT(*) CantidadVisitas,COUNT(DISTINCT [Path]) PaginasDiferentes,
 MIN(CreatedAt) PrimeraVisita,MAX(CreatedAt) UltimaVisita
FROM Classified GROUP BY TipoPagina ORDER BY CantidadVisitas DESC;
""";
        var list=new List<PageTypeVisitSummary>(); await using var cmd=CreateCommand(c,sql,d); await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new PageTypeVisitSummary { PageType=Db.String(r,"TipoPagina"), VisitCount=Db.Int(r,"CantidadVisitas"), DifferentPages=Db.Int(r,"PaginasDiferentes"), FirstVisit=Db.Date(r,"PrimeraVisita"), LastVisit=Db.Date(r,"UltimaVisita") }); return list;
    }

    private async Task<List<ReferrerVisitSummary>> GetReferrerVisitsAsync(SqlConnection c, DateTime d, CancellationToken ct)
    {
        const string sql="""
SELECT COALESCE(NULLIF(Referrer,''),'(Direct / No referrer)') Referrer,COUNT(*) CantidadVisitas,
 MIN(CreatedAt) PrimeraVisita,MAX(CreatedAt) UltimaVisita
FROM dbo.AppPageVisitLog WHERE CreatedAt >= @Fecha AND CreatedAt < DATEADD(DAY,1,@Fecha)
GROUP BY COALESCE(NULLIF(Referrer,''),'(Direct / No referrer)') ORDER BY CantidadVisitas DESC;
""";
        var list=new List<ReferrerVisitSummary>(); await using var cmd=CreateCommand(c,sql,d); await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new ReferrerVisitSummary { Referrer=Db.String(r,"Referrer"), VisitCount=Db.Int(r,"CantidadVisitas"), FirstVisit=Db.Date(r,"PrimeraVisita"), LastVisit=Db.Date(r,"UltimaVisita") }); return list;
    }

    private static class Db
    {
        public static string String(SqlDataReader r,string n)=>r[n] is DBNull ? string.Empty : Convert.ToString(r[n])?.Trim() ?? string.Empty;
        public static int Int(SqlDataReader r,string n)=>r[n] is DBNull ? 0 : Convert.ToInt32(r[n]);
        public static long Long(SqlDataReader r,string n)=>r[n] is DBNull ? 0 : Convert.ToInt64(r[n]);
        public static decimal? Decimal(SqlDataReader r,string n)=>r[n] is DBNull ? null : Convert.ToDecimal(r[n]);
        public static DateTime? Date(SqlDataReader r,string n)=>r[n] is DBNull ? null : Convert.ToDateTime(r[n]);
    }
}
