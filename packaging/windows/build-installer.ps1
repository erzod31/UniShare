param(
    [string]$Version = "0.6.26",
    [switch]$SmokeOnly
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'La versión debe tener el formato X.Y.Z.'
}

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..\..")).Path
$project = Join-Path $repositoryRoot "src\UniShare.Desktop\UniShare.Desktop.csproj"
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
if ($projectXml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText -ne $Version) {
    throw 'La versión solicitada no coincide con el proyecto Windows.'
}
$publishDirectory = Join-Path $repositoryRoot "artifacts\windows-$Version-release"
$installerScript = Join-Path $scriptDirectory "UniShare.iss"
$cleanExitScript = Join-Path $scriptDirectory "test-clean-exit.ps1"
$signScript = Join-Path $scriptDirectory "sign-artifact.ps1"
$requireSigning = $env:UNISHARE_WINDOWS_REQUIRE_SIGNING
if ($requireSigning -and $requireSigning -notin @('0', '1')) {
    throw 'UNISHARE_WINDOWS_REQUIRE_SIGNING must be 0 or 1.'
}
$signingRequested = $requireSigning -eq '1' -or
    -not [string]::IsNullOrWhiteSpace($env:UNISHARE_WINDOWS_CERTIFICATE_THUMBPRINT) -or
    -not [string]::IsNullOrWhiteSpace($env:UNISHARE_WINDOWS_TIMESTAMP_URL)
$signing = if ($signingRequested) { & $signScript -ValidateOnly } else { $null }
$requiredSdk = (Get-Content (Join-Path $repositoryRoot "global.json") -Raw | ConvertFrom-Json).sdk.version
$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$versionedCheckout = $false
if ($gitCommand) {
    $gitRoots = @(& $gitCommand.Source -C $repositoryRoot rev-parse --show-toplevel 2>$null)
    $versionedCheckout = $LASTEXITCODE -eq 0 -and $gitRoots.Count -eq 1 -and
        [string]::Equals([IO.Path]::GetFullPath($gitRoots[0].Trim()), $repositoryRoot, [StringComparison]::OrdinalIgnoreCase)
}
if ($versionedCheckout) {
    $sourceRevisionOutput = @(& $gitCommand.Source -C $repositoryRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $sourceRevisionOutput.Count -ne 1) {
        throw "No se pudo determinar la revisión Git del código fuente."
    }
    $sourceRevision = $sourceRevisionOutput[0].Trim()
    $sourceStatus = @(& $gitCommand.Source -C $repositoryRoot status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo comprobar si el árbol Git está limpio."
    }
    if ($sourceStatus.Count -gt 0) { $sourceRevision += ".dirty" }
}
else {
    $revisionFile = Join-Path $repositoryRoot 'SOURCE_REVISION'
    $archiveRevision = if (Test-Path -LiteralPath $revisionFile) {
        (Get-Content -LiteralPath $revisionFile -Raw).Trim()
    } else { '' }
    if ($archiveRevision -notmatch '^[0-9a-f]{40}$') {
        throw 'No se pudo determinar la revisión: usa un checkout Git o un ZIP fuente oficial con SOURCE_REVISION.'
    }
    # La procedencia es verificable, pero sin Git no se pueden detectar ediciones posteriores del ZIP.
    $sourceRevision = "$archiveRevision.archive"
}

$dotnetCandidates = @(
    Get-Command dotnet -All -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Source
)
$dotnetCandidates += $env:DOTNET_HOST_PATH
$dotnetCandidates += Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$dotnetCandidates = $dotnetCandidates | Where-Object { $_ } | Select-Object -Unique
$dotnet = $null
foreach ($candidate in $dotnetCandidates) {
    if (-not (Test-Path -LiteralPath $candidate)) {
        continue
    }

    $versionOutput = @(& $candidate --version 2>$null)
    $versionExitCode = $LASTEXITCODE
    $resolvedVersion = $versionOutput | Select-Object -First 1
    if ($versionExitCode -eq 0 -and $resolvedVersion -and $resolvedVersion.Trim() -eq $requiredSdk) {
        $dotnet = $candidate
        break
    }
}
if (-not $dotnet) {
    throw "No se encontró un dotnet capaz de resolver el SDK $requiredSdk fijado en global.json."
}
Write-Output "Usando .NET SDK $requiredSdk desde $dotnet"

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "No se encontró Inno Setup 6. Instálalo con: winget install --id JRSoftware.InnoSetup -e"
}

if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedPublish = (Resolve-Path -LiteralPath $publishDirectory).Path
    if (-not $resolvedPublish.StartsWith($repositoryRoot + '\artifacts\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publish directory must remain inside repository artifacts.'
    }
    Move-Item -LiteralPath $resolvedPublish -Destination ($resolvedPublish + '.previous-' + [Guid]::NewGuid().ToString('N'))
}
$existingInstaller = Join-Path $repositoryRoot "artifacts\UniShare-Windows-Setup-$Version.exe"
if (Test-Path -LiteralPath $existingInstaller) {
    Move-Item -LiteralPath $existingInstaller -Destination ($existingInstaller + '.previous-' + [Guid]::NewGuid().ToString('N'))
}

Push-Location $repositoryRoot
try {
    & $dotnet restore $project -r win-x64 --locked-mode -p:NuGetLockFilePath=packages.win-x64.lock.json
    if ($LASTEXITCODE -ne 0) { throw "Falló la restauración win-x64." }

    & $dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $publishDirectory `
        -p:SatelliteResourceLanguages=es -p:DebugSymbols=false -p:DebugType=None `
        "-p:SourceRevisionId=$sourceRevision"
    if ($LASTEXITCODE -ne 0) { throw "Falló la publicación autocontenida." }

    if ($signing) {
        & $signScript -Path (Join-Path $publishDirectory "UniShare.Desktop.exe")
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot "README.md") -Destination $publishDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs\INSTALLATION.md") -Destination $publishDirectory
    & (Join-Path $scriptDirectory "copy-notices.ps1") -PublishDirectory $publishDirectory

    & $cleanExitScript -Executable (Join-Path $publishDirectory "UniShare.Desktop.exe") -SmokeOnly:$SmokeOnly
    if ($LASTEXITCODE -ne 0) { throw "Falló la prueba de cierre limpio." }

    $installerArguments = @("/DMyAppVersion=$Version", "/DPublishDir=$publishDirectory")
    if ($signing) {
        $powerShell = (Get-Process -Id $PID).Path
        $signCommand = '$q' + $powerShell + '$q -NoProfile -NonInteractive -File $q' +
            $signScript + '$q -Path $f'
        $installerArguments += '/DEnableSigning'
        $installerArguments += "/Sunishare=$signCommand"
    }
    & $iscc @installerArguments $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Falló la compilación del instalador." }
    if ($signing) {
        & $signing.SignTool verify /pa /all /tw (Join-Path $repositoryRoot "artifacts\UniShare-Windows-Setup-$Version.exe")
        if ($LASTEXITCODE -ne 0) { throw 'Installer signature verification failed.' }
    }
}
finally {
    Pop-Location
}

Write-Output "Instalador creado en artifacts\UniShare-Windows-Setup-$Version.exe"
Write-Output "Revisión de origen: $sourceRevision"
