[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Sign')]
    [string]$Path,
    [Parameter(Mandatory = $true, ParameterSetName = 'Validate')]
    [switch]$ValidateOnly,
    [string]$CertificateThumbprint = $env:UNISHARE_WINDOWS_CERTIFICATE_THUMBPRINT,
    [string]$TimestampUrl = $env:UNISHARE_WINDOWS_TIMESTAMP_URL
)

$ErrorActionPreference = 'Stop'
if ($CertificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$') {
    throw 'Configure UNISHARE_WINDOWS_CERTIFICATE_THUMBPRINT with a code-signing certificate in CurrentUser\My.'
}
if ([string]::IsNullOrWhiteSpace($TimestampUrl)) {
    throw 'Configure UNISHARE_WINDOWS_TIMESTAMP_URL with the timestamp authority supplied by your certificate provider.'
}
$timestamp = $null
if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestamp) -or
    $timestamp.Scheme -notin @('https', 'http') -or $timestamp.UserInfo -or $timestamp.Fragment) {
    throw 'The timestamp authority must be an absolute HTTP(S) URL without credentials or a fragment.'
}
$certificate = Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' -CodeSigningCert |
    Where-Object { $_.Thumbprint -eq $CertificateThumbprint } | Select-Object -First 1
if (-not $certificate -or -not $certificate.HasPrivateKey -or
    $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
    throw 'No valid code-signing certificate with a private key matches the configured thumbprint.'
}
$signTool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
if (-not $signTool) {
    $signTool = Get-ChildItem -Path 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signTool) { throw 'Install the Windows SDK Signing Tools before requesting a signed build.' }

if ($ValidateOnly) {
    [pscustomobject]@{ SignTool = $signTool; TimestampUrl = $TimestampUrl; Thumbprint = $CertificateThumbprint }
    return
}
$artifact = (Resolve-Path -LiteralPath $Path).Path
& $signTool sign /s My /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $artifact
if ($LASTEXITCODE -ne 0) { throw 'Authenticode signing failed; the artifact must not be distributed as signed.' }
& $signTool verify /pa /all /tw $artifact
if ($LASTEXITCODE -ne 0) { throw 'Authenticode or timestamp verification failed.' }
$signature = Get-AuthenticodeSignature -LiteralPath $artifact
if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
    throw 'The artifact does not have a valid trusted signature and timestamp.'
}
Write-Output "Verified Authenticode signature: $([IO.Path]::GetFileName($artifact))"
