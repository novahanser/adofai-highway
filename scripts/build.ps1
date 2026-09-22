param(
    [string]$GameDir = (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\A Dance of Fire and Ice'),
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
Push-Location $taskRepo
try {
    if (-not $SkipTests) {
        foreach ($taskProject in @('LaneAllocator.Tests', 'Settings.Tests', 'ChartEventBuilder.Tests', 'HighwayGeometry.Tests')) {
            dotnet run --project "tests/$taskProject/$taskProject.csproj" --configuration Release
            if ($LASTEXITCODE -ne 0) { throw "Tests failed: $taskProject" }
        }
    }
    dotnet build adofai-highway/adofai-highway.csproj --configuration Release "-p:GameDir=$GameDir" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

    $taskInfo = Get-Content -LiteralPath 'adofai-highway/Info.json' -Raw | ConvertFrom-Json
    $taskStage = Join-Path $taskRepo 'dist/AdofaiHighway'
    New-Item -ItemType Directory -Path $taskStage -Force | Out-Null
    Copy-Item -LiteralPath 'adofai-highway/bin/Release/AdofaiHighway.dll' -Destination $taskStage
    Copy-Item -LiteralPath 'adofai-highway/Info.json' -Destination $taskStage
    $taskReadme = (Get-Content -LiteralPath 'README.md' -Raw).Replace('(docs/分轨说明.md)', '(分轨说明.md)')
    $taskReadme | Set-Content -LiteralPath (Join-Path $taskStage 'README.md') -Encoding utf8
    Copy-Item -LiteralPath 'docs/分轨说明.md' -Destination $taskStage

    # Explicit input list: never include game/Unity/UMM assemblies or user settings.
    $taskZip = Join-Path $taskRepo "dist/AdofaiHighway-$($taskInfo.Version)-multilane.zip"
    Compress-Archive -LiteralPath @(
        (Join-Path $taskStage 'AdofaiHighway.dll'),
        (Join-Path $taskStage 'Info.json'),
        (Join-Path $taskStage 'README.md'),
        (Join-Path $taskStage '分轨说明.md')
    ) -DestinationPath $taskZip -Force
    $taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$taskHash  $(Split-Path -Leaf $taskZip)" | Set-Content -LiteralPath "$taskZip.sha256" -Encoding utf8
    Write-Output "Package: $taskZip"
    Write-Output "SHA256: $taskHash"
}
finally {
    Pop-Location
}
