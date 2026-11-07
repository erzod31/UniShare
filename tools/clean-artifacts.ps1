param(
    [string]$KeepVersion = "0.6.25"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot "artifacts"

function Remove-PathSafely([string]$TargetPath) {
    if (-not (Test-Path -LiteralPath $TargetPath)) { return }
    $item = Get-Item -LiteralPath $TargetPath -Force
    if ($item.PSIsContainer) {
        cmd /c "rmdir /s /q `"$TargetPath`"" | Out-Null
    } else {
        Remove-Item -LiteralPath $TargetPath -Force
    }
}

$worktreeDir = Join-Path $artifactsRoot "DG-0018-source"
if (Test-Path -LiteralPath $worktreeDir) {
    Remove-PathSafely $worktreeDir
    git -C $repoRoot worktree prune
}

if (Test-Path -LiteralPath $artifactsRoot) {
    Get-ChildItem -LiteralPath $artifactsRoot -Force | ForEach-Object {
        $name = $_.Name
        $keep = ($name -like "*$KeepVersion*") -and ($name -notlike "*.previous-*")
        if (-not $keep) {
            Remove-PathSafely $_.FullName
        }
    }
}

Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs") -Filter "cleanup-DG-*-manifest.json" -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-PathSafely $_.FullName }
