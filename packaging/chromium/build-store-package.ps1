param(
    [string]$Version = "0.6.16"
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..\..")).Path
$sourceRoot = Join-Path $repositoryRoot "integrations\chromium-extension"
$artifactRoot = Join-Path $repositoryRoot "artifacts"
$packageRoot = Join-Path $artifactRoot "chromium-$Version-package"
$destination = Join-Path $artifactRoot "UniShare-Edge-Store-$Version.zip"
$submissionRoot = Join-Path $artifactRoot "edge-$Version-submission"
$submissionDestination = Join-Path $artifactRoot "UniShare-Edge-Submission-$Version.zip"

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
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Force
}
if (Test-Path -LiteralPath $submissionRoot) {
    Remove-Item -LiteralPath $submissionRoot -Recurse -Force
}
if (Test-Path -LiteralPath $submissionDestination) {
    Remove-Item -LiteralPath $submissionDestination -Force
}

New-Item -ItemType Directory -Path (Join-Path $packageRoot "icons") -Force | Out-Null
foreach ($file in @("manifest.json", "background.js", "i18n.js", "popup.html", "popup.js", "options.html", "options.js", "style.css")) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $packageRoot
}
Copy-Item -Path (Join-Path $sourceRoot "icons\*.png") -Destination (Join-Path $packageRoot "icons")
Copy-Item -LiteralPath (Join-Path $sourceRoot "_locales") -Destination $packageRoot -Recurse

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $destination -CompressionLevel Optimal
Remove-Item -LiteralPath $packageRoot -Recurse -Force

New-Item -ItemType Directory -Path $submissionRoot -Force | Out-Null
Copy-Item -LiteralPath $destination -Destination $submissionRoot
foreach ($file in @("PUBLISHING.md", "privacy-policy.md", "store-listing-es-ES.md")) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $submissionRoot
}
Copy-Item -LiteralPath (Join-Path $sourceRoot "store-assets\logo-300.png") -Destination $submissionRoot
Compress-Archive -Path (Join-Path $submissionRoot "*") -DestinationPath $submissionDestination -CompressionLevel Optimal
Remove-Item -LiteralPath $submissionRoot -Recurse -Force

Write-Output "Paquete Edge Add-ons creado en artifacts\UniShare-Edge-Store-$Version.zip"
Write-Output "Expediente de publicación creado en artifacts\UniShare-Edge-Submission-$Version.zip"
