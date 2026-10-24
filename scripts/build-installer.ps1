param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'

if (-not $CompilerPath) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) { $CompilerPath = $compilerCommand.Source }
    else {
        foreach ($programDirectory in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
            if (-not $programDirectory) { continue }
            $candidate = Join-Path $programDirectory 'Inno Setup 6\ISCC.exe'
            if (Test-Path -LiteralPath $candidate) { $CompilerPath = $candidate; break }
        }
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Install Inno Setup 6.3 or later, or pass -CompilerPath with the path to ISCC.exe.'
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src\TimeDeck.Windows\TimeDeck.Windows.csproj') -Raw
$appVersion = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($appVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'The Windows project must define a numeric installer version.' }

& (Join-Path $PSScriptRoot 'publish.ps1')
& $CompilerPath /Qp "/DAppVersion=$appVersion" (Join-Path $projectRoot 'installer\TimeDeck.iss')
if ($LASTEXITCODE -ne 0) { throw 'Time Deck installer compilation failed.' }

$installer = Join-Path $projectRoot "artifacts\installer\TimeDeck-Setup-$appVersion-win-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'The compiler did not produce the expected installer.' }
Write-Output "Installer: $installer"
