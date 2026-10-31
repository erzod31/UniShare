param(
    [Parameter(Mandatory = $true)] [string]$Serial,
    [string]$SdkRoot = $env:ANDROID_HOME,
    [switch]$SkipBuild,
    [switch]$LargeText
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $SdkRoot) { $SdkRoot = $env:ANDROID_SDK_ROOT }
$adb = Join-Path $SdkRoot 'platform-tools\adb.exe'
if (-not (Test-Path -LiteralPath $adb)) { throw 'Set ANDROID_HOME to an installed Android SDK.' }
$state = @(& $adb -s $Serial get-state 2>&1)
if ($LASTEXITCODE -ne 0 -or $state -notcontains 'device') { throw 'The explicitly selected Android device is not available.' }

Push-Location $PSScriptRoot
try {
    if (-not $SkipBuild) {
        & .\gradlew.bat --no-daemon assembleDebug assembleDebugAndroidTest
        if ($LASTEXITCODE -ne 0) { throw 'QA compilation failed.' }
    }
    $apk = Join-Path $PSScriptRoot 'app\build\outputs\apk\debug\app-debug.apk'
    $tests = Join-Path $PSScriptRoot 'app\build\outputs\apk\androidTest\debug\app-debug-androidTest.apk'
    $aapt = Get-ChildItem -Path (Join-Path $SdkRoot 'build-tools\*\aapt2.exe') |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    $badging = @(& $aapt dump badging $apk)
    if ($LASTEXITCODE -ne 0 -or -not ($badging -match "^package: name='org\.unishare\.app\.qa' ")) {
        throw 'Device tests only accept the isolated org.unishare.app.qa APK.'
    }
    & $adb -s $Serial install -r $apk
    if ($LASTEXITCODE -ne 0) { throw 'Installing the QA app failed.' }
    & $adb -s $Serial install -r $tests
    if ($LASTEXITCODE -ne 0) { throw 'Installing the test APK failed.' }
    & $adb -s $Serial shell input keyevent KEYCODE_WAKEUP
    $sdk = [int](@(& $adb -s $Serial shell getprop ro.build.version.sdk)[0])
    if ($sdk -ge 37) {
        # Reset only the QA permission, before starting instrumentation (revocation kills its UID).
        & $adb -s $Serial shell pm revoke org.unishare.app.qa android.permission.ACCESS_LOCAL_NETWORK
        & $adb -s $Serial shell pm clear-permission-flags org.unishare.app.qa android.permission.ACCESS_LOCAL_NETWORK user-set user-fixed
    }
    $originalScale = (@(& $adb -s $Serial shell settings get system font_scale)[0]).Trim()
    try {
        if ($LargeText) { & $adb -s $Serial shell settings put system font_scale 2.0 }
        $classes = @(
            'org.unishare.app.AndroidPersistenceTest', 'org.unishare.app.DirectSyncRuntimeTest',
            'org.unishare.app.AndroidUiWorkflowTest', 'org.unishare.app.AndroidShareAndPermissionTest',
            'org.unishare.app.OfflineDownloadRuntimeTest', 'org.unishare.app.OriginMetadataDeviceTest'
        ) -join ','
        $report = @(& $adb -s $Serial shell am instrument -w -r -e class $classes `
            org.unishare.app.qa.test/androidx.test.runner.AndroidJUnitRunner)
        $reports = Join-Path $repositoryRoot 'artifacts\test-results'
        New-Item -ItemType Directory -Path $reports -Force | Out-Null
        $label = if ($LargeText) { 'large-text' } else { 'normal' }
        $reportPath = Join-Path $reports "android-api-$sdk-$label.txt"
        $report | Out-File -LiteralPath $reportPath -Encoding utf8
        $text = $report -join "`n"
        $success = [regex]::Match($text, 'OK \((\d+) tests?\)')
        if (-not $success.Success -or $text -match 'FAILURES!!!|INSTRUMENTATION_FAILED|Process crashed') {
            $report | Select-String -Pattern 'Error in |AssertionError|IllegalStateException|Tests run:|FAILURES|Process crashed'
            throw "Android workflows failed. See $reportPath"
        }
        Write-Output "Android API $sdk ($label): $($success.Groups[1].Value) instrumented tests passed. Report: $reportPath"
    }
    finally {
        if ($LargeText) {
            if ($originalScale -eq 'null') { & $adb -s $Serial shell settings delete system font_scale }
            else { & $adb -s $Serial shell settings put system font_scale $originalScale }
        }
    }
}
finally { Pop-Location }
