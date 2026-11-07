#requires -Version 5.1
param(
    [string]$Version = "0.6.25",
    [ValidateSet('development', 'edge', 'chrome')]
    [string]$Channel = 'edge',
    [string]$ExtensionId = ''
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use X.Y.Z.' }
if ($ExtensionId -and ($Channel -eq 'development' -or $ExtensionId -cnotmatch '^[a-p]{32}$')) {
    throw 'Use only the real 32-character store ID, or omit it before the first store upload.'
}

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..\..")).Path
$sourceRoot = Join-Path $repositoryRoot "integrations\chromium-extension"
$artifactRoot = Join-Path $repositoryRoot "artifacts"
$label = switch ($Channel) { 'edge' { 'Edge' }; 'chrome' { 'Chrome' }; 'development' { 'Chromium' } }
$packageRoot = Join-Path $artifactRoot "$Channel-$Version-package"
$packageName = if ($Channel -eq 'development') { "UniShare-Chromium-$Version.zip" } else { "UniShare-$label-Store-$Version.zip" }
$destination = Join-Path $artifactRoot $packageName
$submissionRoot = Join-Path $artifactRoot "$Channel-$Version-submission"
$submissionDestination = Join-Path $artifactRoot "UniShare-$label-Submission-$Version.zip"

& (Join-Path $scriptDirectory "test-extension.ps1") -Source $sourceRoot
if ($LASTEXITCODE -ne 0) {
    throw "La validación de la extensión falló."
}

$manifest = Get-Content (Join-Path $sourceRoot "manifest.json") -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version) {
    throw "La versión del manifiesto ($($manifest.version)) no coincide con $Version."
}

if (-not $packageRoot.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "La carpeta temporal debe permanecer dentro de artifacts."
}

if (Test-Path -LiteralPath $packageRoot) {
    Move-Item -LiteralPath $packageRoot -Destination ($packageRoot + '.previous-' + [Guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $destination) {
    Move-Item -LiteralPath $destination -Destination ($destination + '.previous-' + [Guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $submissionRoot) {
    Move-Item -LiteralPath $submissionRoot -Destination ($submissionRoot + '.previous-' + [Guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $submissionDestination) {
    Move-Item -LiteralPath $submissionDestination -Destination ($submissionDestination + '.previous-' + [Guid]::NewGuid().ToString('N'))
}

New-Item -ItemType Directory -Path (Join-Path $packageRoot "icons") -Force | Out-Null
foreach ($file in @("manifest.json", "background.js", "i18n.js", "updates.js", "popup.html", "popup.js", "options.html", "options.js", "style.css")) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $packageRoot
}
Copy-Item -Path (Join-Path $sourceRoot "icons\*.png") -Destination (Join-Path $packageRoot "icons")
Copy-Item -LiteralPath (Join-Path $sourceRoot "_locales") -Destination $packageRoot -Recurse
$distributionJson = [ordered]@{ schemaVersion = 1; productId = 'unishare'; store = $Channel; extensionId = $(if ($ExtensionId) { $ExtensionId } else { $null }) } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $packageRoot 'distribution.json'), $distributionJson, [Text.UTF8Encoding]::new($false))
& (Join-Path $scriptDirectory 'test-extension.ps1') -Source $packageRoot
if (-not $?) { throw 'La validación del paquete falló.' }

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $destination -CompressionLevel Optimal
Remove-Item -LiteralPath $packageRoot -Recurse -Force

if ($Channel -eq 'development') { Write-Output "Paquete de desarrollo: $destination"; return }

New-Item -ItemType Directory -Path $submissionRoot -Force | Out-Null
Copy-Item -LiteralPath $destination -Destination $submissionRoot
foreach ($file in @("PUBLISHING.md", "privacy-policy.md", "privacy-policy-en.md", "store-listing-es-ES.md", "store-listing-en-US.md")) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $submissionRoot
}
Copy-Item -LiteralPath (Join-Path $sourceRoot "store-assets\logo-300.png") -Destination $submissionRoot
Copy-Item -LiteralPath (Join-Path $sourceRoot "icons\icon-128.png") -Destination $submissionRoot
foreach ($asset in @('options-es-1280x800.png', 'options-en-1280x800.png', 'small-tile-440x280.png')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot "store-assets/$asset") -Destination $submissionRoot
}
Compress-Archive -Path (Join-Path $submissionRoot "*") -DestinationPath $submissionDestination -CompressionLevel Optimal
Remove-Item -LiteralPath $submissionRoot -Recurse -Force

Write-Output "Paquete $label creado en $destination"
Write-Output "Expediente de publicación creado en $submissionDestination"
Write-Output 'Pendiente: cuenta de editor, asignación del ID y aprobación de la tienda; este ZIP no es una instalación de tienda.'
