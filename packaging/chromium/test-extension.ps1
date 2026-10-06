param(
    [string]$Source = ""
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..\..")).Path
$sourceRoot = if ([string]::IsNullOrWhiteSpace($Source)) {
    Join-Path $repositoryRoot "integrations\chromium-extension"
} else {
    (Resolve-Path $Source).Path
}

$node = Get-Command node -ErrorAction Stop
$javascriptFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Filter "*.js" -File)
if ($javascriptFiles.Count -eq 0) {
    throw "La extensión no contiene JavaScript."
}
foreach ($file in $javascriptFiles) {
    & $node.Source --check $file.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "JavaScript no válido: $($file.Name)"
    }
}

$jsonFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Filter "*.json" -File -Recurse)
foreach ($file in $jsonFiles) {
    try {
        Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -ErrorAction Stop | Out-Null
    } catch {
        throw "JSON no válido: $($file.FullName): $($_.Exception.Message)"
    }
}

$manifestPath = Join-Path $sourceRoot "manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.manifest_version -ne 3) {
    throw "La extensión debe usar Manifest V3."
}

$requiredFiles = @(
    $manifest.background.service_worker,
    $manifest.action.default_popup,
    $manifest.options_page
) + @($manifest.icons.PSObject.Properties.Value) + @($manifest.action.default_icon.PSObject.Properties.Value)
foreach ($relativePath in $requiredFiles | Where-Object { $_ } | Select-Object -Unique) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $relativePath) -PathType Leaf)) {
        throw "El manifiesto referencia un archivo inexistente: $relativePath"
    }
}

$manifestMessages = @($manifest.name, $manifest.description, $manifest.action.default_title) |
    Where-Object { $_ -match '^__MSG_[A-Za-z0-9_]+__$' } |
    ForEach-Object { $_.Substring(6, $_.Length - 8) }
$localeRoot = Join-Path $sourceRoot "_locales"
$locales = @(Get-ChildItem -LiteralPath $localeRoot -Directory)
if ($locales.Count -lt 2) {
    throw "Deben existir al menos los idiomas español e inglés."
}
$referenceKeys = $null
foreach ($locale in $locales) {
    $messagesPath = Join-Path $locale.FullName "messages.json"
    if (-not (Test-Path -LiteralPath $messagesPath -PathType Leaf)) {
        throw "Falta messages.json para $($locale.Name)."
    }
    $messages = Get-Content -LiteralPath $messagesPath -Raw | ConvertFrom-Json
    $keys = @($messages.PSObject.Properties.Name | Sort-Object)
    if ($null -eq $referenceKeys) {
        $referenceKeys = $keys
    } elseif (Compare-Object $referenceKeys $keys) {
        throw "Los idiomas de la extensión no contienen las mismas claves."
    }
    foreach ($key in $manifestMessages) {
        $property = $messages.PSObject.Properties[$key]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace($property.Value.message)) {
            throw "Falta la traducción '$key' en $($locale.Name)."
        }
    }
}

foreach ($html in Get-ChildItem -LiteralPath $sourceRoot -Filter "*.html" -File) {
    $content = Get-Content -LiteralPath $html.FullName -Raw
    foreach ($match in [regex]::Matches($content, '(?:src|href)="([^"#]+\.(?:js|css))"')) {
        $relativePath = $match.Groups[1].Value
        if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $relativePath) -PathType Leaf)) {
            throw "$($html.Name) referencia un recurso inexistente: $relativePath"
        }
    }
}

Write-Output "Extensión MV3 válida: $($javascriptFiles.Count) JavaScript, $($jsonFiles.Count) JSON y $($locales.Count) idiomas."
