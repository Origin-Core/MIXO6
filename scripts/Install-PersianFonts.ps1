# MIXO6 Persian font installer (current-user scope only)
# Downloads font files on demand from the official GitHub repositories listed below.
# No fonts are bundled in the MIXO6 release archive.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$fontRepos = @(
    @{ Name = 'Vazirmatn'; Repo = 'rastikerdar/vazirmatn'; Branches = @('master','main') },
    @{ Name = 'Estedad';   Repo = 'aminabedi68/Estedad';      Branches = @('main','master') },
    @{ Name = 'Shabnam';   Repo = 'rastikerdar/shabnam';      Branches = @('master','main') },
    @{ Name = 'Sahel';     Repo = 'rastikerdar/sahel';        Branches = @('master','main') },
    @{ Name = 'Samim';     Repo = 'rastikerdar/samim';        Branches = @('master','main') }
)

Write-Host ''
Write-Host 'MIXO6 · نصب فونت‌های فارسی' -ForegroundColor Cyan
Write-Host 'منابع: مخازن رسمی GitHub برای Vazirmatn، Estedad، Shabnam، Sahel و Samim.'
Write-Host 'فونت‌ها فقط برای کاربر فعلی ویندوز نصب می‌شوند و دسترسی Administrator لازم نیست.'
Write-Host 'برای رعایت مجوز، فایل‌های مجوز همراه هر مخزن در پوشه MIXO6 نگه داشته می‌شوند.'
Write-Host 'این اسکریپت پیش از دانلود/نصب از تو تأیید می‌گیرد.' -ForegroundColor Yellow
$answer = Read-Host 'برای ادامه، Y را وارد کن'
if ($answer -notmatch '^(?i)y(es)?$') {
    Write-Host 'لغو شد؛ تغییری در ویندوز ایجاد نشد.' -ForegroundColor Yellow
    exit 0
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('MIXO6-fonts-' + [Guid]::NewGuid().ToString('N'))
$userFontRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts'
$licenseRoot = Join-Path $env:LOCALAPPDATA 'MIXO6\FontLicenses'
$registryRoot = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'
New-Item -ItemType Directory -Force -Path $tempRoot, $userFontRoot, $licenseRoot | Out-Null
New-Item -Path $registryRoot -Force | Out-Null
$installed = 0
$failedRepos = [System.Collections.Generic.List[string]]::new()

try {
    foreach ($fontRepo in $fontRepos) {
        $archive = Join-Path $tempRoot ($fontRepo.Name + '.zip')
        $extractDir = Join-Path $tempRoot ($fontRepo.Name)
        $downloaded = $false
        foreach ($branch in $fontRepo.Branches) {
            try {
                if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
                $url = 'https://codeload.github.com/' + $fontRepo.Repo + '/zip/refs/heads/' + $branch
                Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing -TimeoutSec 90 -MaximumRedirection 5
                if ((Get-Item -LiteralPath $archive).Length -lt 4096) { throw 'GitHub returned an unexpectedly small archive.' }
                $downloaded = $true
                break
            }
            catch {
                Write-Host ('  دریافت ' + $fontRepo.Name + ' از شاخهٔ ' + $branch + ' ناموفق بود: ' + $_.Exception.Message) -ForegroundColor DarkYellow
            }
        }
        if (-not $downloaded) {
            $failedRepos.Add($fontRepo.Name)
            continue
        }

        if (Test-Path $extractDir) { Remove-Item -LiteralPath $extractDir -Recurse -Force }
        Expand-Archive -LiteralPath $archive -DestinationPath $extractDir -Force
        $license = Get-ChildItem -LiteralPath $extractDir -Recurse -File | Where-Object {
            $_.Name -match '^(OFL(\.txt)?|LICENSE(\.txt|\.md)?|COPYING(\.txt)?)$'
        } | Select-Object -First 1
        if (-not $license) {
            Write-Host ('  مجوز متنی در آرشیو ' + $fontRepo.Name + ' پیدا نشد؛ برای رعایت مجوز، فونت‌هایش نصب نشدند.') -ForegroundColor Yellow
            $failedRepos.Add($fontRepo.Name + ' (license missing)')
            continue
        }
        Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $licenseRoot ($fontRepo.Name + '-' + $license.Name)) -Force

        $fontFiles = @(Get-ChildItem -LiteralPath $extractDir -Recurse -File | Where-Object {
            $_.Extension -in @('.ttf', '.otf') -and
            $_.FullName -notmatch '[\\/](test|tests|docs?|examples?|storybook|node_modules|\.git)[\\/]'
        } | Sort-Object FullName)
        if ($fontFiles.Count -eq 0) {
            Write-Host ('  فایل TTF/OTF در مخزن ' + $fontRepo.Name + ' پیدا نشد.') -ForegroundColor Yellow
            $failedRepos.Add($fontRepo.Name + ' (no TTF/OTF)')
            continue
        }

        # Avoid installing an enormous number of test/duplicate variants from a repository.
        if ($fontFiles.Count -gt 36) {
            $preferred = @($fontFiles | Where-Object { $_.BaseName -match '(Regular|Medium|Bold|SemiBold|Light|Variable|VF|Black|DemiBold)' })
            if ($preferred.Count -gt 0) { $fontFiles = @($preferred | Select-Object -First 36) }
            else { $fontFiles = @($fontFiles | Select-Object -First 36) }
        }

        $localIndex = 0
        foreach ($font in $fontFiles) {
            $localIndex++
            $safeBase = [System.IO.Path]::GetFileNameWithoutExtension($font.Name) -replace '[^A-Za-z0-9 _\-\[\]\(\)]', '_'
            $extension = $font.Extension.ToLowerInvariant()
            $destName = '{0}-{1:00}-{2}{3}' -f $fontRepo.Name, $localIndex, $safeBase, $extension
            $destPath = Join-Path $userFontRoot $destName
            Copy-Item -LiteralPath $font.FullName -Destination $destPath -Force
            $fontType = if ($extension -eq '.otf') { 'OpenType' } else { 'TrueType' }
            $entryName = [System.IO.Path]::GetFileNameWithoutExtension($destName) + ' (' + $fontType + ')'
            New-ItemProperty -Path $registryRoot -Name $entryName -Value $destPath -PropertyType String -Force | Out-Null
            $installed++
        }
        Write-Host ('  ' + $fontRepo.Name + ': ' + $fontFiles.Count + ' فایل نصب شد.') -ForegroundColor Green
    }

    # Notify Windows that current-user fonts changed. Restarting MIXO6 afterward is still recommended.
    try {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MIXO6FontRefresh {
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
'@ -ErrorAction SilentlyContinue
        $result = [IntPtr]::Zero
        [void][MIXO6FontRefresh]::SendMessageTimeout([IntPtr]0xffff, 0x001D, [UIntPtr]::Zero, [IntPtr]::Zero, 2, 2000, [ref]$result)
    } catch { }

    Write-Host ''
    Write-Host ('تعداد فایل‌های فونت نصب‌شده: ' + $installed) -ForegroundColor Cyan
    Write-Host ('مسیر فونت‌های کاربر: ' + $userFontRoot)
    Write-Host ('مجوزها: ' + $licenseRoot)
    if ($failedRepos.Count -gt 0) {
        Write-Host ('بعضی منابع در دسترس نبودند: ' + ($failedRepos -join ', ')) -ForegroundColor Yellow
        Write-Host 'اگر GitHub از شبکه‌ات در دسترس نیست، از دکمه‌های مخزن فونت در تنظیمات MIXO6 استفاده کن.' -ForegroundColor Yellow
    }
    Write-Host 'حالا MIXO6 را کامل ببند و دوباره باز کن، سپس تنظیمات ← فونت فارسی ← بازخوانی فونت‌ها را بزن.' -ForegroundColor Green
}
finally {
    if (Test-Path $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
