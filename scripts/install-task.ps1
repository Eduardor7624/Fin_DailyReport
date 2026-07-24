param(
    [string]$TaskName = "Finzati Daily Report",
    [string]$RunAt = "07:30",
    [string]$ExecutableDirectory = "$PSScriptRoot\..\publish"
)

$exe = Join-Path $ExecutableDirectory "FinzatiDailyReport.exe"
if (-not (Test-Path $exe)) { throw "Executable not found: $exe. Run publish-win-x64.cmd first." }

$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $ExecutableDirectory
$trigger = New-ScheduledTaskTrigger -Daily -At $RunAt
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 1)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType S4U -RunLevel Highest
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description "Generates and emails the Finzati daily process and activity report." -Force
Write-Host "Task '$TaskName' installed to run daily at $RunAt."
