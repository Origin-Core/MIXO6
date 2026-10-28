$ErrorActionPreference = 'Stop'
$version = '2.6.0'
$expectedSha256 = 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
$toolDir = Join-Path $PSScriptRoot '..\src\MIXO6\tools'
$toolDir = [System.IO.Path]::GetFullPath($toolDir)
$exePath = Join-Path $toolDir 'PresentMon.exe'
$licensePath = Join-Path $toolDir 'PresentMon-LICENSE.txt'
$exeUrl = "https://github.com/GameTechDev/PresentMon/releases/download/v$version/PresentMon-$version-x64.exe"
$licenseUrl = "https://raw.githubusercontent.com/GameTechDev/PresentMon/v$version/LICENSE.txt"

New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
$validExisting = $false
if (Test-Path $exePath) {
    $existingHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $validExisting = $existingHash -eq $expectedSha256
    if (-not $validExisting) { Remove-Item -LiteralPath $exePath -Force }
}
if (-not $validExisting) {
    Write-Host "Downloading official PresentMon v$version..." -ForegroundColor Cyan
    $temporaryPath = "$exePath.download"
    if (Test-Path $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
    Invoke-WebRequest -Uri $exeUrl -OutFile $temporaryPath -MaximumRedirection 10
    $downloadHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($downloadHash -ne $expectedSha256) {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        throw "PresentMon SHA256 mismatch. Expected $expectedSha256; got $downloadHash. The build is stopped to avoid packaging an unexpected binary."
    }
    Move-Item -LiteralPath $temporaryPath -Destination $exePath -Force
}
if (-not (Test-Path $licensePath)) {
    Write-Host 'Downloading PresentMon license notice...' -ForegroundColor Cyan
    Invoke-WebRequest -Uri $licenseUrl -OutFile $licensePath -MaximumRedirection 10
}
if ((Get-Item -LiteralPath $licensePath).Length -lt 100) { throw 'PresentMon license text is missing or unexpectedly short.' }
Write-Host "PresentMon v$version verified: $exePath" -ForegroundColor Green
