param(
    [string]$Version = "0.6.16"
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..\..")).Path
$project = Join-Path $repositoryRoot "src\UniShare.Desktop\UniShare.Desktop.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\windows-$Version-release"
$installerScript = Join-Path $scriptDirectory "UniShare.iss"
$cleanExitScript = Join-Path $scriptDirectory "test-clean-exit.ps1"

$dotnetCandidates = @(
    $env:DOTNET_HOST_PATH,
    (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"),
    (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1)
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$dotnet = $dotnetCandidates | Select-Object -First 1
if (-not $dotnet) {
    throw "No se encontró dotnet. Instala el SDK fijado en global.json."
}

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
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

Push-Location $repositoryRoot
try {
    & $dotnet restore $project -r win-x64 --locked-mode -p:NuGetLockFilePath=packages.win-x64.lock.json
    if ($LASTEXITCODE -ne 0) { throw "Falló la restauración win-x64." }

    & $dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $publishDirectory `
        -p:SatelliteResourceLanguages=es -p:DebugSymbols=false -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw "Falló la publicación autocontenida." }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot "README.md") -Destination $publishDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs\INSTALLATION.md") -Destination $publishDirectory

    & $cleanExitScript -Executable (Join-Path $publishDirectory "UniShare.Desktop.exe")
    if ($LASTEXITCODE -ne 0) { throw "Falló la prueba de cierre limpio." }

    & $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publishDirectory" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Falló la compilación del instalador." }
}
finally {
    Pop-Location
}

Write-Output "Instalador creado en artifacts\UniShare-Windows-Setup-$Version.exe"
