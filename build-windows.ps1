$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\MIXO6\MIXO6.csproj'
$artifactRoot = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifactRoot 'MIXO6-win-x64'
$zipPath = Join-Path $artifactRoot 'MIXO6-win-x64.zip'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'برنامه dotnet پیدا نشد. ابتدا .NET 10 SDK را نصب کن.'
}

Push-Location $root
try {
    & (Join-Path $root 'scripts\Download-PresentMon.ps1')
    New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Write-Host '==> Restoring MIXO6 for Windows x64...' -ForegroundColor Cyan
    dotnet restore $project -r win-x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    Write-Host '==> Publishing self-contained MIXO6.exe...' -ForegroundColor Cyan
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    if (-not (Test-Path (Join-Path $publishDir 'MIXO6.exe'))) { throw 'MIXO6.exe was not published.' }
    if (-not (Test-Path (Join-Path $publishDir 'tools\PresentMon.exe'))) { throw 'PresentMon.exe was not included in the publish folder; the FPS monitor would not work.' }
    if (-not (Test-Path (Join-Path $publishDir 'tools\PresentMon-LICENSE.txt'))) { throw 'PresentMon license notice is missing from the publish folder.' }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
    Write-Host "Build completed: $publishDir\MIXO6.exe" -ForegroundColor Green
    Write-Host "Release ZIP: $zipPath" -ForegroundColor Green
}
finally {
    Pop-Location
}
