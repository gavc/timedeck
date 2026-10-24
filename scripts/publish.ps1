param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\TimeDeck'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\TimeDeck.Windows\TimeDeck.Windows.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Time Deck publish failed.' }
Write-Output "Run $(Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'TimeDeck.exe')"
