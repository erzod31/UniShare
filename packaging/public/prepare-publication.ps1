#requires -Version 7.0
param(
    [string]$Version = "0.6.24"
)

$ErrorActionPreference = 'Stop'
foreach ($variable in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE')) {
    if ([Environment]::GetEnvironmentVariable($variable)) {
        throw "Retira $variable del entorno antes de preparar la publicación."
    }
}
$repositoryRoot = (git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Ejecuta el script dentro del repositorio.' }

function Invoke-PublicGit([string[]]$Arguments) {
    $result = & git -C $repositoryRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git falló: $($Arguments[0])" }
    return $result
}

$sourceCommit = Invoke-PublicGit -Arguments @('rev-parse', 'HEAD')
$privateRoots = @(Invoke-PublicGit -Arguments @('config', '--get-all', 'unishare.privateRoot'))
if ($privateRoots.Count -eq 0) { throw 'Falta configurar la protección del historial privado.' }
$baseCommit = Invoke-PublicGit -Arguments @('rev-parse', 'refs/remotes/origin/main^{commit}')
& git -C $repositoryRoot show-ref --verify --quiet refs/heads/public
$hasPublicBranch = $LASTEXITCODE -eq 0
$parentCommit = if ($hasPublicBranch) {
    Invoke-PublicGit -Arguments @('rev-parse', 'refs/heads/public^{commit}')
} else { $baseCommit }
& git -C $repositoryRoot merge-base --is-ancestor $baseCommit $parentCommit
if ($LASTEXITCODE -ne 0) { throw 'Actualiza la rama public con origin/main antes de continuar.' }
foreach ($privateRoot in $privateRoots) {
    & git -C $repositoryRoot merge-base --is-ancestor $privateRoot $parentCommit
    if ($LASTEXITCODE -ne 1) { throw 'La rama pública contiene historial privado o no se pudo verificar.' }
}

$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$stage = Join-Path $artifactRoot ('.publication-' + [Guid]::NewGuid().ToString('N'))
$sourceZip = $stage + '.zip'
try {
    & (Join-Path $PSScriptRoot 'build-source-package.ps1') -Version $Version -OutputPath $sourceZip | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($sourceZip, $stage)
    $sourceRoot = Join-Path $stage "UniShare-$Version"
    $env:GIT_INDEX_FILE = Join-Path $stage 'public.index'
    Invoke-PublicGit -Arguments @('read-tree', '--empty') | Out-Null
    Invoke-PublicGit -Arguments @('-c', 'core.autocrlf=false', "--work-tree=$sourceRoot", 'add', '--all', '--force', '--', '.') | Out-Null
    # ZIP extraction on Windows loses executable bits. Restore them from the source tree.
    foreach ($entry in @(Invoke-PublicGit -Arguments @('ls-tree', '-r', '--format=%(objectmode) %(path)', $sourceCommit))) {
        if ($entry -match '^100755 (.+)$' -and (Test-Path -LiteralPath (Join-Path $sourceRoot $Matches[1]) -PathType Leaf)) {
            Invoke-PublicGit -Arguments @('update-index', '--chmod=+x', '--', $Matches[1]) | Out-Null
        }
    }
    $tree = Invoke-PublicGit -Arguments @('write-tree')
    Remove-Item Env:\GIT_INDEX_FILE
    if ((Invoke-PublicGit -Arguments @('rev-parse', 'HEAD')) -ne $sourceCommit -or
        @(Invoke-PublicGit -Arguments @('status', '--porcelain', '--untracked-files=all')).Count -ne 0) {
        throw 'El árbol de desarrollo cambió durante la preparación; no se ha actualizado public.'
    }
    $parentTree = Invoke-PublicGit -Arguments @('rev-parse', "$parentCommit`^{tree}")
    $candidate = if ($tree -eq $parentTree) { $parentCommit } else {
        Invoke-PublicGit -Arguments @('commit-tree', $tree, '-p', $parentCommit, '-m', "Prepare public source $Version")
    }
    $expected = if ($hasPublicBranch) { $parentCommit } else { '0' * 40 }
    Invoke-PublicGit -Arguments @('update-ref', 'refs/heads/public', $candidate, $expected) | Out-Null
    Invoke-PublicGit -Arguments @('update-ref', 'refs/unishare/approved-public', $candidate) | Out-Null
    Write-Output "Instantánea pública preparada: $candidate"
    Write-Output 'No se ha publicado nada. Revisa: git diff origin/main public'
}
finally {
    Remove-Item Env:\GIT_INDEX_FILE -ErrorAction SilentlyContinue
    # Only this invocation's randomly named staging directory can be removed.
    if (Test-Path -LiteralPath $stage) {
        $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
        if (-not $resolvedStage.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            @(Get-ChildItem -LiteralPath $stage -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0 -or
            ((Get-Item -LiteralPath $stage -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'El temporal no se ha eliminado porque su ruta no es segura.'
        }
        Remove-Item -LiteralPath $resolvedStage -Force -Recurse
    }
    if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip -Force }
}
