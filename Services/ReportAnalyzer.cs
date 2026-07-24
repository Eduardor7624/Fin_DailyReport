using FinzatiDailyReport.Configuration;
using FinzatiDailyReport.Models;

namespace FinzatiDailyReport.Services;

public sealed class ReportAnalyzer
{
    private readonly ReportSettings _settings;
    public ReportAnalyzer(ReportSettings settings) => _settings = settings;

    public ReportAnalysis Analyze(DailyReportData data)
    {
        var operationEvents = data.OperationErrors.Sum(x => x.Count);
        var processProblems = data.ProcessErrors.Sum(x => x.ErrorCount);
        var totalSignal = operationEvents + processProblems + (int)data.ProcessSummary.TotalErrors;

        var probeVisits = data.PageVisits
            .Where(p => _settings.IgnorePathPrefixes.Any(prefix => p.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Sum(p => p.VisitCount);

        var health = totalSignal >= _settings.CriticalErrorThreshold || data.ProcessSummary.ErrorExecutions >= 3
            ? SystemHealthLevel.Critical
            : totalSignal >= _settings.IncidentErrorThreshold || data.ProcessSummary.ErrorExecutions > 0
                ? SystemHealthLevel.Incidents
                : SystemHealthLevel.Stable;

        var (label, explanation) = health switch
        {
            SystemHealthLevel.Stable => ("ESTABLE", "No se detectaron errores operativos o procesos fallidos que requieran atención inmediata."),
            SystemHealthLevel.Incidents => ("CON INCIDENCIAS", "Se detectaron errores o procesos con problemas. Conviene revisar las incidencias priorizadas en este reporte."),
            _ => ("CRÍTICO", "El volumen o la concentración de errores supera el umbral configurado y requiere revisión prioritaria.")
        };

        var actions = new List<string>();
        if (data.ProcessSummary.ErrorExecutions > 0) actions.Add("Revisar las ejecuciones con estado ERROR, FAILED o FAILURE y confirmar si requieren reproceso.");
        if (operationEvents > 0) actions.Add("Atender primero los errores con mayor número de ocurrencias y validar su causa raíz.");
        if (probeVisits > 0) actions.Add($"Separar {probeVisits:N0} accesos automatizados o intentos de exploración de la actividad real de usuarios.");
        if (data.ProcessSummary.TotalExecutions == 0) actions.Add("Confirmar que los procesos programados esperados se ejecutaron; hoy no aparecen ejecuciones registradas.");
        if (actions.Count == 0) actions.Add("Mantener el monitoreo habitual; no se identificaron acciones correctivas inmediatas.");

        return new ReportAnalysis
        {
            Health = health, HealthLabel = label, HealthExplanation = explanation,
            TotalOperationErrorEvents = operationEvents, DifferentOperationErrors = data.OperationErrors.Count,
            AutomatedProbeVisits = probeVisits,
            PriorityOperationErrors = data.OperationErrors.Take(_settings.TopErrors).ToList(),
            PriorityProcessErrors = data.ProcessErrors.Take(_settings.TopErrors).ToList(),
            RecommendedActions = actions
        };
    }
}
