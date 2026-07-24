namespace FinzatiDailyReport.Utilities;

public sealed class FileLogger
{
    private readonly string _directory;
    public FileLogger(string directory,int retentionDays)
    {
        _directory=directory; Directory.CreateDirectory(directory);
        if(retentionDays>0) foreach(var f in Directory.EnumerateFiles(directory,"*.log"))
            try { if(File.GetLastWriteTime(f)<DateTime.Now.AddDays(-retentionDays)) File.Delete(f); } catch { }
    }
    public void Info(string message)=>Write("INFO",message);
    private void Write(string level,string message)=>File.AppendAllText(Path.Combine(_directory,$"FinzatiDailyReport-{DateTime.Today:yyyy-MM-dd}.log"),$"[{DateTimeOffset.Now:O}] [{level}] {message}{Environment.NewLine}");
}
