[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateRange(250, 30000)]
    [int] $StartupBudgetMilliseconds = 5000,

    [ValidateRange(250, 30000)]
    [int] $StartupTargetMilliseconds = 3000,

    [ValidateRange(32, 4096)]
    [int] $WorkingSetBudgetMiB = 250
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($StartupTargetMilliseconds -gt $StartupBudgetMilliseconds) {
    throw 'StartupTargetMilliseconds cannot exceed StartupBudgetMilliseconds.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appPath = Join-Path $repositoryRoot "src\LexVerse.App\bin\$Configuration\net10.0-windows10.0.19041.0\LexVerse.App.exe"
if (-not (Test-Path -LiteralPath $appPath)) {
    throw "LexVerse.App executable was not found at $appPath. Build the $Configuration configuration first."
}

$timer = [System.Diagnostics.Stopwatch]::StartNew()
$appProcess = Start-Process -FilePath $appPath -PassThru
try {
    $ready = $false
    while ($timer.ElapsedMilliseconds -le $StartupBudgetMilliseconds) {
        $appProcess.Refresh()
        if ($appProcess.HasExited) {
            throw "LexVerse.App exited before its main window was ready (exit code $($appProcess.ExitCode))."
        }

        if ($appProcess.MainWindowHandle -ne [IntPtr]::Zero -and
            $appProcess.MainWindowTitle -eq 'LexVerse Control Center' -and
            $appProcess.Responding) {
            $ready = $true
            break
        }

        Start-Sleep -Milliseconds 25
    }

    if (-not $ready) {
        throw "LexVerse.App did not present a responsive Control Center within $StartupBudgetMilliseconds ms."
    }

    $startupMilliseconds = $timer.Elapsed.TotalMilliseconds
    Start-Sleep -Milliseconds 250
    $appProcess.Refresh()
    $workingSetMiB = $appProcess.WorkingSet64 / 1MB
    if ($workingSetMiB -gt $WorkingSetBudgetMiB) {
        throw "LexVerse.App working set was $([math]::Round($workingSetMiB, 1)) MiB; budget is $WorkingSetBudgetMiB MiB."
    }

    [pscustomobject]@{
        StartupMilliseconds = [math]::Round($startupMilliseconds, 1)
        StartupTargetMilliseconds = $StartupTargetMilliseconds
        StartupTargetMet = $startupMilliseconds -le $StartupTargetMilliseconds
        StartupBudgetMilliseconds = $StartupBudgetMilliseconds
        WorkingSetMiB = [math]::Round($workingSetMiB, 1)
        WorkingSetBudgetMiB = $WorkingSetBudgetMiB
    } | Format-List
}
finally {
    if (-not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(5000)) {
            Stop-Process -Id $appProcess.Id
            $appProcess.WaitForExit()
        }
    }
}
