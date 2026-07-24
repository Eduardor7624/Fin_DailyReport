namespace FinzatiDailyReport.Utilities;

public sealed record CommandLineOptions(DateTime? ReportDate, bool NoSend)
{
    public static CommandLineOptions Parse(string[] args)
    {
        DateTime? date = null;
        var noSend = false;
        for (var i=0;i<args.Length;i++)
        {
            if (args[i].Equals("--no-send",StringComparison.OrdinalIgnoreCase)) noSend=true;
            else if (args[i].Equals("--date",StringComparison.OrdinalIgnoreCase))
            {
                if (i+1>=args.Length || !DateTime.TryParse(args[++i],out var parsed))
                    throw new ArgumentException("Use --date yyyy-MM-dd.");
                date=parsed.Date;
            }
            else if (args[i].StartsWith("--date=",StringComparison.OrdinalIgnoreCase))
            {
                if (!DateTime.TryParse(args[i][7..],out var parsed)) throw new ArgumentException("Use --date=yyyy-MM-dd.");
                date=parsed.Date;
            }
        }
        return new(date,noSend);
    }
}
