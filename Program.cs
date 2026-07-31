using System.Diagnostics;
using FinzatiDailyReport.Configuration;
using FinzatiDailyReport.Data;
using FinzatiDailyReport.Services;
using FinzatiDailyReport.Utilities;
using Microsoft.Extensions.Configuration;

const string applicationName = "FinzatiDailyReport";
var stopwatch = Stopwatch.StartNew();
using var cancellationSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
    Console.WriteLine("Cancellation requested...");
};

try
{
    var arguments = CommandLineOptions.Parse(args);
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    var settings = configuration.Get<AppSettings>()
        ?? throw new InvalidOperationException("Could not load appsettings.json.");

    SettingsValidator.Validate(settings, arguments.NoSend);

    var reportDate = arguments.ReportDate ?? DateTime.Today.AddDays(settings.Report.DefaultDaysOffset);
    reportDate = reportDate.Date;

    Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, settings.Report.OutputDirectory));
    Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, settings.Logging.Directory));

    var logger = new FileLogger(
        Path.Combine(AppContext.BaseDirectory, settings.Logging.Directory),
        settings.Logging.RetentionDays);

    logger.Info($"Starting {applicationName}. Period start date: {reportDate:yyyy-MM-dd}. NoSend: {arguments.NoSend}.");
    Console.WriteLine($"Finzati Daily Report - period starts {reportDate:yyyy-MM-dd} at 09:25 Eastern");
    Console.WriteLine(new string('-', 48));

    var repository = new DailyReportRepository(
        settings.ConnectionStrings.AmericaMarketFMPLogs,
        settings.Database.CommandTimeoutSeconds);

    Console.WriteLine("Reading daily process, error, and traffic data...");
    var data = await repository.GetDailyReportAsync(reportDate, cancellationSource.Token);

    var analyzer = new ReportAnalyzer(settings.Report);
    var analysis = analyzer.Analyze(data);

    var reportBuilder = new HtmlReportBuilder(settings.Report);
    var html = reportBuilder.Build(data, analysis);

    var outputPath = Path.Combine(
        AppContext.BaseDirectory,
        settings.Report.OutputDirectory,
        $"Finzati-Daily-Report-{data.ReportDate:yyyy-MM-dd}.html");

    await File.WriteAllTextAsync(outputPath, html, cancellationSource.Token);
    logger.Info($"HTML report saved to {outputPath}.");
    Console.WriteLine($"HTML report created: {outputPath}");

    if (!arguments.NoSend)
    {
        Console.WriteLine("Sending email...");
        var sender = new SmtpEmailSender(settings.Email);
        var emailSubject = TemplateHelper.ReplaceDate(settings.Email.SubjectTemplate, data.ReportDate);
        await sender.SendHtmlAsync(emailSubject, html, cancellationSource.Token);
        logger.Info($"Email sent successfully to: {string.Join(", ", settings.Email.To)}.");
        Console.WriteLine("Email sent successfully.");
    }
    else
    {
        logger.Info("Email sending skipped because --no-send was specified.");
        Console.WriteLine("Email was not sent (--no-send). Open the HTML file to review it.");
    }

    stopwatch.Stop();
    logger.Info($"Completed successfully in {stopwatch.Elapsed}.");
    Console.WriteLine($"Completed in {stopwatch.Elapsed}.");
}
catch (OperationCanceledException)
{
    stopwatch.Stop();
    Console.Error.WriteLine($"Process cancelled after {stopwatch.Elapsed}.");
    Environment.ExitCode = 2;
}
catch (Exception ex)
{
    stopwatch.Stop();
    Console.Error.WriteLine("FATAL ERROR:");
    Console.Error.WriteLine(ex);

    try
    {
        var fallbackDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(fallbackDir);
        await File.AppendAllTextAsync(
            Path.Combine(fallbackDir, $"fatal-{DateTime.Today:yyyy-MM-dd}.log"),
            $"[{DateTimeOffset.Now:O}] {ex}{Environment.NewLine}");
    }
    catch { }

    Environment.ExitCode = 1;
}
