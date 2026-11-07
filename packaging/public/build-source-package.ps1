#requires -Version 5.1
param(
    [string]$Version = "0.6.25",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "La versión debe tener el formato X.Y.Z."
}

$repositoryRoot = [IO.Path]::GetFullPath((git -C $PSScriptRoot rev-parse --show-toplevel).Trim())
if (-not $repositoryRoot -or -not (Test-Path -LiteralPath (Join-Path $repositoryRoot ".git"))) {
    throw "Ejecuta el script desde el repositorio Git de UniShare."
}

$status = @(git -C $repositoryRoot status --porcelain --untracked-files=all)
if ($status.Count -ne 0) {
    throw "El árbol Git debe estar limpio antes de generar la instantánea pública."
}

$artifactRoot = Join-Path $repositoryRoot "artifacts"
[IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
$output = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $artifactRoot "UniShare-Source-$Version.zip"
} elseif ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}
if (-not $output.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetExtension($output) -ne '.zip') {
    throw "El ZIP fuente debe guardarse dentro de artifacts."
}
$outputParent = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($output))
while ($null -ne $outputParent) {
    if ($outputParent.Exists -and ($outputParent.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "El destino del ZIP no puede atravesar enlaces de directorio."
    }
    $outputParent = $outputParent.Parent
}
if ((Test-Path -LiteralPath $output) -and ((Get-Item -LiteralPath $output -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "El destino del ZIP no puede ser un enlace."
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$temporaryOutput = Join-Path $artifactRoot ".UniShare-Source-$Version-$([Guid]::NewGuid().ToString('N')).zip"

try {
    & git -C $repositoryRoot archive `
        --format=zip `
        "--prefix=UniShare-$Version/" `
        "--output=$temporaryOutput" `
        HEAD
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temporaryOutput)) {
        throw "git archive no pudo crear la instantánea pública."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($temporaryOutput)
    try {
        $forbidden = @(
            '(^|/)\.git(/|$)',
            '(^|/)AGENTS\.md$',
            '(^|/)artifacts(/|$)',
            '(^|/)(?:bin|obj|build|\.gradle|\.kotlin|\.vs|\.idea|TestResults)(/|$)',
            '(^|/)(?:blobs|staging|recovery|backups)(/|$)',
            '\.unishare\.zip$',
            '(^|/)(?:\.env(?:\..*)?|local\.properties|direct-sync\.json|secrets\.json|credentials\.json)$',
            '\.(?:db|db-shm|db-wal|jks|keystore|p12|pfx|pem|key)$'
        ) -join '|'

        $textExtensions = @(
            '.cs', '.csproj', '.css', '.gradle', '.html', '.iss', '.js', '.json', '.kt', '.kts',
            '.md', '.properties', '.props', '.ps1', '.slnx', '.targets', '.toml', '.txt', '.xml',
            '.yml', '.yaml'
        )
        $privateContent = [ordered]@{
            'ruta de perfil Windows' = '[A-Za-z]:\\' + 'Users\\[^\\\r\n]+'
            'ruta de perfil macOS' = '/' + 'Users/[^/\s]+'
            'ruta de perfil Linux' = '/' + 'home/[^/\s]+'
            'nombre MagicDNS real' = '[A-Za-z0-9-]+\.tailc[0-9]+\.ts\.net'
            'IP privada Tailscale' = '(?<![0-9])100\.(?:6[4-9]|[7-9][0-9]|1[01][0-9]|12[0-7])\.(?:[0-9]{1,3}\.)[0-9]{1,3}(?![0-9])'
            'clave privada' = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
            'token GitHub' = 'gh[pousr]_[A-Za-z0-9_]{20,}'
            'clave AWS' = 'AKIA[0-9A-Z]{16}'
        }

        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ([IO.Path]::IsPathRooted($name) -or $name.Split('/') -contains '..') {
                throw "La instantánea contiene una ruta insegura: $name"
            }
            if ($name -match $forbidden) {
                throw "La instantánea contiene un archivo privado o generado: $name"
            }
            if ($entry.Length -gt 0 -and $textExtensions -contains [IO.Path]::GetExtension($name).ToLowerInvariant()) {
                $stream = $entry.Open()
                try {
                    $reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false), $true)
                    try {
                        $content = $reader.ReadToEnd()
                    }
                    finally {
                        $reader.Dispose()
                    }
                }
                finally {
                    $stream.Dispose()
                }
                # Emscripten creates a public virtual home inside WASM. Accept that literal only
                # in the exact reviewed upstream file; modified bytes still receive every check.
                $sha256 = [Security.Cryptography.SHA256]::Create()
                try {
                    $contentHash = [BitConverter]::ToString($sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($content))).Replace('-', '')
                }
                finally {
                    $sha256.Dispose()
                }
                $knownVirtualHome = $name -eq "UniShare-$Version/shared/translation/vendor/bergamot-translator.js" -and
                    $contentHash -eq '972FA0450146FE06F4006A2BA56E8C4834403E27873CFE47FB3BA85AA7B9B082'
                foreach ($rule in $privateContent.GetEnumerator()) {
                    $scanContent = if ($knownVirtualHome -and $rule.Key -eq 'ruta de perfil Linux') {
                        $content.Replace(('/' + 'home/web_user'), '/wasm-virtual-user')
                    } else { $content }
                    if ($scanContent -match $rule.Value) {
                        throw "La instantánea contiene $($rule.Key) en $name"
                    }
                }
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    if (Test-Path -LiteralPath $output) {
        Move-Item -LiteralPath $output -Destination ($output + '.previous-' + [Guid]::NewGuid().ToString('N'))
    }
    [IO.File]::Move($temporaryOutput, $output)
}
finally {
    if (Test-Path -LiteralPath $temporaryOutput) {
        Remove-Item -LiteralPath $temporaryOutput -Force
    }
}

$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $output).Length
Write-Output "Instantánea pública: $output"
Write-Output "Tamaño: $size bytes"
Write-Output "SHA-256: $hash"
