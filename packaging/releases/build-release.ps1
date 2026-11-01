#requires -Version 7.0
param(
    [string]$Version = '0.6.25',
    [string]$EdgeExtensionId = '',
    [string]$ChromeExtensionId = ''
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifacts = Join-Path $root 'artifacts'
$logs = Join-Path $artifacts "test-results/release-$Version"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use X.Y.Z.' }
foreach ($id in @($EdgeExtensionId, $ChromeExtensionId)) {
    if ($id -and $id -cnotmatch '^[a-p]{32}$') { throw 'Only an actual store-assigned extension ID is accepted.' }
}
[xml]$windows = Get-Content -LiteralPath (Join-Path $root 'src/UniShare.Desktop/UniShare.Desktop.csproj') -Raw
$android = Get-Content -LiteralPath (Join-Path $root 'platforms/android/app/build.gradle.kts') -Raw
$extension = Get-Content -LiteralPath (Join-Path $root 'integrations/chromium-extension/manifest.json') -Raw | ConvertFrom-Json
if ($windows.SelectSingleNode('/Project/PropertyGroup/Version').InnerText -ne $Version -or
    $android -notmatch ('versionName\s*=\s*"' + [regex]::Escape($Version) + '"') -or $extension.version -ne $Version) {
    throw 'Windows, Android and extension source versions must match the requested release.'
}
[IO.Directory]::CreateDirectory($logs) | Out-Null
Push-Location $root
try {
    $revision = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or @(git status --porcelain --untracked-files=all).Count -ne 0) { throw 'Commit source changes before building a release.' }
    $publicCommit = (git rev-parse refs/heads/public).Trim()
    if ($LASTEXITCODE -ne 0 -or (git show public:SOURCE_REVISION).Trim() -ne $revision) {
        throw 'Prepare the sanitized public branch from this revision before building.'
    }
    & (Join-Path $root 'packaging/windows/build-installer.ps1') -Version $Version -SmokeOnly *> (Join-Path $logs 'windows-package.log')
    if (-not $?) { throw 'Windows packaging failed; inspect windows-package.log.' }
    Push-Location (Join-Path $root 'platforms/android')
    try {
        & .\gradlew.bat --no-daemon test lintRelease assembleRelease assembleDebugAndroidTest *> (Join-Path $logs 'android-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Android validation failed; inspect android-build.log.' }
    } finally { Pop-Location }
    foreach ($channel in @('development', 'edge', 'chrome')) {
        $id = switch ($channel) { 'edge' { $EdgeExtensionId }; 'chrome' { $ChromeExtensionId }; default { '' } }
        & (Join-Path $root 'packaging/chromium/build-store-package.ps1') -Version $Version -Channel $channel -ExtensionId $id *> (Join-Path $logs "$channel-package.log")
        if (-not $?) { throw "Extension packaging failed: $channel" }
    }
    $apk = Join-Path $artifacts "UniShare-Android-$Version.apk"
    $androidZip = Join-Path $artifacts "UniShare-Android-$Version.zip"
    $windowsZip = Join-Path $artifacts "UniShare-Windows-$Version.zip"
    foreach ($existing in @($apk, $androidZip, $windowsZip)) {
        if (Test-Path -LiteralPath $existing) { throw 'Release output already exists; preserve/review it before rerunning.' }
    }
    Copy-Item -LiteralPath (Join-Path $root 'platforms/android/app/build/outputs/apk/release/app-release.apk') -Destination $apk
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $artifacts "windows-$Version-release"), $windowsZip)
    $archive = [IO.Compression.ZipFile]::Open($androidZip, [IO.Compression.ZipArchiveMode]::Create)
    try { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $apk, [IO.Path]::GetFileName($apk)) | Out-Null }
    finally { $archive.Dispose() }
    & (Join-Path $root 'packaging/public/build-source-package.ps1') -Version $Version *> (Join-Path $logs 'source-package.log')
    if (-not $?) { throw 'Source privacy validation failed.' }

    $roles = [ordered]@{
        "UniShare-Windows-Setup-$Version.exe" = 'Windows per-user x64 installer; not installed during QA'
        "UniShare-Windows-$Version.zip" = 'Windows self-contained portable runtime'
        "UniShare-Android-$Version.apk" = 'Android sideload update package'
        "UniShare-Android-$Version.zip" = 'Android APK download wrapper'
        "UniShare-Chromium-$Version.zip" = 'Unpacked development extension; no native update channel'
        "UniShare-Edge-Store-$Version.zip" = 'Edge Add-ons submission payload; not a store installation'
        "UniShare-Edge-Submission-$Version.zip" = 'Edge listing, privacy, screenshots and payload'
        "UniShare-Chrome-Store-$Version.zip" = 'Chrome Web Store submission payload; not a store installation'
        "UniShare-Chrome-Submission-$Version.zip" = 'Chrome listing, privacy, screenshots and payload'
        "UniShare-Source-$Version.zip" = 'Sanitized source snapshot without private history'
    }
    $files = @($roles.GetEnumerator() | ForEach-Object {
        $file = Get-Item -LiteralPath (Join-Path $artifacts $_.Key)
        [ordered]@{ path = $file.FullName; version = $Version; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash; role = $_.Value }
    })
    $sums = Join-Path $artifacts "SHA256SUMS-$Version.txt"
    [IO.File]::WriteAllLines($sums, @($files | ForEach-Object { $_.sha256 + '  ' + [IO.Path]::GetFileName($_.path) }), [Text.UTF8Encoding]::new($false))
    $notes = Join-Path $artifacts "RELEASE-NOTES-$Version.md"
    @"
# UniShare $Version — candidate, not published

Windows, Android and browser extension candidates use version $Version. The extension now checks
its installed identity, official browser channel and pending browser update before offering Apply.
Separate Edge/Chrome submissions include English/Spanish listing, privacy and actual QA screenshots.

Windows and Android update from Settings using GitHub Releases, exact package names, size/hash and
app identity/signature checks. Versions up to 0.6.23 need one initial manual update to gain this UI.
Do not uninstall: retain the library, pairing and existing application identity.

Windows remains historically unsigned (unless production signing was explicitly configured).
Android retains its existing signing certificate; changing keys breaks updates. Development ZIPs
are not store installations. Store publication/account approval and physical/native end-to-end
installation tests are pending; candidate tests use isolated profiles and fake installers.
"@ | Set-Content -LiteralPath $notes -Encoding utf8NoBOM
    $plan = [ordered]@{
        schemaVersion = 1; productId = 'unishare'; version = $Version
        generatedAt = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'Europe/Brussels').ToString('o')
        sourceRevision = $revision; publicRevision = $publicCommit; repository = 'erzod31/UniShare'
        extensionIdentities = @{ edge = $(if ($EdgeExtensionId) { $EdgeExtensionId } else { $null }); chrome = $(if ($ChromeExtensionId) { $ChromeExtensionId } else { $null }) }
        prepared = $true; performed = $false; nativeInstallersExecuted = 0; artifacts = $files
        checksumFile = $sums; releaseNotes = $notes
        publication = @(
            'Review this plan, source diff, verification report and hashes; authorize publication separately.'
            'git push origin public:main'
            "git tag v$Version $publicCommit; git push origin refs/tags/v$Version"
            "gh release create v$Version --draft --title 'UniShare $Version' --notes-file '$notes'"
            'Upload exactly the ten listed artifacts and SHA256SUMS; compare every GitHub asset size/digest with this plan.'
            "gh release edit v$Version --draft=false"
            'Use each publisher account to upload the exact store ZIP and complete certification; record assigned IDs before subsequent updates.'
        )
        impact = 'Publishing stable exposes these packages to compatible installed updaters; older clients need manual bootstrap. Store review is separate.'
        rollback = 'Before activation delete the draft, preserving existing releases; after activation withdraw the defective release and publish a higher fixed version. Never downgrade data or uninstall.'
        dependencies = @('Explicit remote-publication approval', 'Verified store publisher accounts/contact details and store-assigned IDs', 'Independent Windows/Android native-upgrade QA before claiming it verified')
    }
    $plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifacts "release-$Version.json") -Encoding utf8NoBOM
    Write-Output "Prepared $($files.Count) candidate artifacts. No remote publication or native installer was executed."
}
finally { Pop-Location }
