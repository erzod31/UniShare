#Requires -Version 5.1
param([Parameter(Mandatory = $true)] [string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$assets = Get-Content -LiteralPath (Join-Path $root 'src\UniShare.Desktop\obj\project.assets.json') -Raw | ConvertFrom-Json
$noticeRoot = Join-Path $publish 'licenses'
New-Item -ItemType Directory -Path $noticeRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $noticeRoot
Get-ChildItem -LiteralPath (Join-Path $root 'licenses') -File | Copy-Item -Destination $noticeRoot
$packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name })
$runtime = Get-Content -LiteralPath (Join-Path $publish 'UniShare.Desktop.runtimeconfig.json') -Raw | ConvertFrom-Json
$packages += @($runtime.runtimeOptions.includedFrameworks | ForEach-Object { "$($_.name.ToLowerInvariant()).runtime.win-x64/$($_.version)" })
$packageFolders = @($assets.packageFolders.PSObject.Properties | ForEach-Object { $_.Name })
$inventory = foreach ($package in ($packages | Sort-Object -Unique)) {
    $parts = $package.Split('/')
    $folder = $null
    foreach ($cache in $packageFolders) {
        $candidate = Join-Path $cache $package.ToLowerInvariant()
        if (Test-Path -LiteralPath $candidate -PathType Container) { $folder = [IO.Path]::GetFullPath($candidate); break }
    }
    if (-not $folder) { throw "Cannot locate the license metadata for $package" }
    $nuspec = Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' -File | Select-Object -First 1
    [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
    $license = $metadata.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="license"]')
    $copyright = $metadata.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="copyright"]')
    $authors = $metadata.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="authors"]')
    $texts = @(Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY.NOTICES)' })
    if (-not $license -or ($license.type -eq 'expression' -and $license.InnerText -notin @('MIT','Apache-2.0','BSD-3-Clause','Unlicense'))) {
        throw "Review the distribution license for $package before packaging."
    }
    if ($license.type -eq 'file') {
        $licensePath = [IO.Path]::GetFullPath((Join-Path $folder $license.InnerText))
        if (-not $licensePath.StartsWith($folder + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Invalid license path for $package"
        }
        $texts += Get-Item -LiteralPath $licensePath
    }
    if ($texts.Count) {
        $destination = Join-Path $noticeRoot ($parts -join '-')
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $texts | Sort-Object FullName -Unique | Copy-Item -Destination $destination
    }
    [ordered]@{ package = $parts[0]; version = $parts[1]; license = $license.InnerText; authors = $authors.InnerText; copyright = $copyright.InnerText }
}
$inventory | ConvertTo-Json -Depth 4 | Out-File -LiteralPath (Join-Path $noticeRoot 'inventory-windows.json') -Encoding utf8
Write-Output "Third-party notices: $($inventory.Count) Windows packages/frameworks inventoried."
