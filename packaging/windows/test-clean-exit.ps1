param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"

if ($TimeoutSeconds -lt 1) {
    throw "TimeoutSeconds debe ser positivo."
}

$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$profileRoot = Join-Path $temporaryBase ("unishare-clean-exit-" + [Guid]::NewGuid().ToString("N"))
$configRoot = Join-Path $profileRoot "config"
$process = $null

try {
    New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }

    $configuration = @{
        port = $port
        pairingKey = "unishare-clean-exit-" + [Guid]::NewGuid().ToString("N")
    } | ConvertTo-Json
    [IO.File]::WriteAllText(
        (Join-Path $configRoot "direct-sync.json"),
        $configuration,
        [Text.UTF8Encoding]::new($false))

    $process = Start-Process -FilePath $executablePath -ArgumentList @(
        "--allow-multiple",
        "--skip-tailscale",
        "--exit-on-close",
        "--profile",
        $profileRoot) -PassThru

    $startupDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $startupDeadline) {
        if ($process.HasExited) {
            throw "UniShare terminó antes de mostrar la ventana (código $($process.ExitCode))."
        }
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    }
    if ($process.MainWindowHandle -eq 0) {
        throw "UniShare no mostró la ventana en $TimeoutSeconds segundos."
    }

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.CloseMainWindow()) {
        throw "Windows no aceptó la solicitud de cierre de UniShare."
    }
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "UniShare no terminó en $TimeoutSeconds segundos después de cerrar la ventana."
    }
    $stopwatch.Stop()
    if ($process.ExitCode -ne 0) {
        throw "UniShare terminó con código $($process.ExitCode)."
    }

    Write-Output "Cierre limpio verificado en $($stopwatch.ElapsedMilliseconds) ms."
}
finally {
    if ($process -is [Diagnostics.Process] -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    if (Test-Path -LiteralPath $profileRoot) {
        $resolvedProfile = [IO.Path]::GetFullPath($profileRoot)
        if (-not $resolvedProfile.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "El perfil temporal quedó fuera del directorio temporal esperado."
        }
        Remove-Item -LiteralPath $resolvedProfile -Recurse -Force
    }
}
