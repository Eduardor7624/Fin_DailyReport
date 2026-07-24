namespace FinzatiDailyReport.Utilities;
public static class TemplateHelper { public static string ReplaceDate(string template,DateTime date)=>(template??string.Empty).Replace("{date}",date.ToString("yyyy-MM-dd"),StringComparison.OrdinalIgnoreCase); }
