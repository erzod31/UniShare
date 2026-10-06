param(
    [string]$Version = "0.6.16"
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "La versión debe tener el formato X.Y.Z."
}

$repositoryRoot = [IO.Path]::GetFullPath((git rev-parse --show-toplevel).Trim())
if (-not $repositoryRoot -or -not (Test-Path -LiteralPath (Join-Path $repositoryRoot ".git"))) {
    throw "Ejecuta el script desde el repositorio Git de UniShare."
}

$status = @(git -C $repositoryRoot status --porcelain --untracked-files=all)
if ($status.Count -ne 0) {
    throw "El árbol Git debe estar limpio antes de generar la instantánea pública."
}

$artifactRoot = Join-Path $repositoryRoot "artifacts"
[IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
$output = Join-Path $artifactRoot "UniShare-Source-$Version.zip"
if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Force
}

& git -C $repositoryRoot archive `
    --format=zip `
    "--prefix=UniShare-$Version/" `
    "--output=$output" `
    HEAD
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) {
    throw "git archive no pudo crear la instantánea pública."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($output)
try {
    $forbidden = @(
        '(^|/)\.git(/|$)',
        '(^|/)AGENTS\.md$',
        '(^|/)artifacts(/|$)',
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
            foreach ($rule in $privateContent.GetEnumerator()) {
                if ($content -match $rule.Value) {
                    throw "La instantánea contiene $($rule.Key) en $name"
                }
            }
        }
    }
}
finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $output).Length
Write-Output "Instantánea pública: $output"
Write-Output "Tamaño: $size bytes"
Write-Output "SHA-256: $hash"
