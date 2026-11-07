#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repositoryRoot ('artifacts/test-results/publication-' + [Guid]::NewGuid().ToString('N'))
$development = Join-Path $testRoot 'development'
$seed = Join-Path $testRoot 'seed'
$remote = Join-Path $testRoot 'remote.git'
$checks = [Collections.Generic.List[string]]::new()

function Invoke-TestGit([string]$Path, [string[]]$Arguments) {
    $result = & git -C $Path @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git fixture failed: $($Arguments -join ' ')" }
    return $result
}
function Assert([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $checks.Add($Name)
}
function Must-Fail([scriptblock]$Action, [string]$Name) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert $failed $Name
}
function Check-Hook([string]$Line, [bool]$ShouldPass, [string]$Name) {
    $psExe = (Get-Process -Id $PID).Path; $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'; try { $output = $Line | & $psExe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $development 'packaging/public/check-public-push.ps1') 2>&1 } finally { $ErrorActionPreference = $prev }
    Assert (($LASTEXITCODE -eq 0) -eq $ShouldPass) $Name
}

try {
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    & git init --quiet --bare $remote
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create local test remote.' }
    foreach ($entry in @(@{ Path = $seed; Branch = 'main' }, @{ Path = $development; Branch = 'master' })) {
        & git init --quiet --initial-branch=$($entry.Branch) $entry.Path
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create test repository.' }
        Invoke-TestGit $entry.Path -Arguments @('config', 'user.name', 'Publication test') | Out-Null
        Invoke-TestGit $entry.Path -Arguments @('config', 'user.email', 'publication@example.invalid') | Out-Null
    }
    Set-Content -LiteralPath (Join-Path $seed 'README.md') -Value 'Public initial source' -Encoding utf8
    Invoke-TestGit $seed -Arguments @('add', '.') | Out-Null
    Invoke-TestGit $seed -Arguments @('commit', '--quiet', '-m', 'Public seed') | Out-Null
    $seedCommit = Invoke-TestGit $seed -Arguments @('rev-parse', 'HEAD')
    Invoke-TestGit $seed -Arguments @('push', '--quiet', $remote, 'main') | Out-Null

    Set-Content -LiteralPath (Join-Path $development 'removed-private-note.txt') -Value '__PRIVATE_HISTORY_ONLY__' -Encoding utf8
    Invoke-TestGit $development -Arguments @('add', '.') | Out-Null
    Invoke-TestGit $development -Arguments @('commit', '--quiet', '-m', 'Private development seed') | Out-Null
    $privateRoot = Invoke-TestGit $development -Arguments @('rev-parse', 'HEAD')
    Remove-Item -LiteralPath (Join-Path $development 'removed-private-note.txt')
    [IO.Directory]::CreateDirectory((Join-Path $development 'packaging/public/hooks')) | Out-Null
    foreach ($file in @('build-source-package.ps1', 'prepare-publication.ps1', 'check-public-push.ps1', 'hooks/pre-push')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $development "packaging/public/$file")
    }
    Set-Content -LiteralPath (Join-Path $development '.gitignore') -Value 'artifacts/' -Encoding utf8
    [IO.File]::WriteAllLines((Join-Path $development '.gitattributes'), @('AGENTS.md export-ignore', 'SOURCE_REVISION export-subst', 'packaging/public/hooks/* text eol=lf'), [Text.UTF8Encoding]::new($false))
    Set-Content -LiteralPath (Join-Path $development 'AGENTS.md') -Value '__PRIVATE_LOCAL_INSTRUCTIONS__' -Encoding utf8
    [IO.File]::WriteAllLines((Join-Path $development 'SOURCE_REVISION'), @('$Format:%H$'), [Text.UTF8Encoding]::new($false))
    Set-Content -LiteralPath (Join-Path $development 'README.md') -Value 'Current safe source' -Encoding utf8
    Invoke-TestGit $development -Arguments @('add', '--all') | Out-Null
    Invoke-TestGit $development -Arguments @('update-index', '--chmod=+x', 'packaging/public/hooks/pre-push') | Out-Null
    Invoke-TestGit $development -Arguments @('commit', '--quiet', '-m', 'Current source') | Out-Null
    Invoke-TestGit $development -Arguments @('config', 'unishare.privateRoot', $privateRoot) | Out-Null
    Invoke-TestGit $development -Arguments @('config', 'core.hooksPath', 'packaging/public/hooks') | Out-Null
    Invoke-TestGit $development -Arguments @('remote', 'add', 'origin', $remote) | Out-Null
    Invoke-TestGit $development -Arguments @('fetch', '--quiet', '--no-tags', 'origin', 'refs/heads/main:refs/remotes/origin/main') | Out-Null
    $sourceHead = Invoke-TestGit $development -Arguments @('rev-parse', 'HEAD')
    $indexBefore = (Get-FileHash -LiteralPath (Join-Path $development '.git/index')).Hash

    Push-Location $development
    try {
        & (Join-Path $development 'packaging/public/prepare-publication.ps1') | Out-Null
        $approved = Invoke-TestGit $development -Arguments @('rev-parse', 'public')
        Assert ((Invoke-TestGit $development -Arguments @('symbolic-ref', '--short', 'HEAD')) -eq 'master') 'Development branch stays selected'
        Assert ((Invoke-TestGit $development -Arguments @('rev-parse', 'HEAD')) -eq $sourceHead) 'Development HEAD is unchanged'
        Assert ((Get-FileHash -LiteralPath (Join-Path $development '.git/index')).Hash -eq $indexBefore) 'Development index is unchanged'
        Invoke-TestGit $development -Arguments @('merge-base', '--is-ancestor', $seedCommit, 'public') | Out-Null
        Assert ($LASTEXITCODE -eq 0) 'Existing public ancestry is preserved'
        & git merge-base --is-ancestor $privateRoot public
        Assert ($LASTEXITCODE -eq 1) 'Private history is not public ancestry'
        $publicPaths = @(Invoke-TestGit $development -Arguments @('ls-tree', '-r', '--name-only', 'public'))
        Assert ('AGENTS.md' -notin $publicPaths) 'Local instructions are excluded'
        Assert ('removed-private-note.txt' -notin $publicPaths) 'Removed private file is excluded'
        Assert ((Invoke-TestGit $development -Arguments @('ls-tree', 'public', 'packaging/public/hooks/pre-push')) -match '^100755 ') 'Executable hook mode is preserved'
        & git grep --quiet '__PRIVATE_HISTORY_ONLY__' public --
        Assert ($LASTEXITCODE -eq 1) 'Private historical content is absent'
        Assert ((Invoke-TestGit $development -Arguments @('show', 'public:SOURCE_REVISION')).Trim() -eq $sourceHead) 'Source revision is substituted'
        & (Join-Path $development 'packaging/public/prepare-publication.ps1') | Out-Null
        Assert ((Invoke-TestGit $development -Arguments @('rev-parse', 'public')) -eq $approved) 'Repeated preparation is idempotent'
        Assert (@(Get-ChildItem -LiteralPath (Join-Path $development 'artifacts') -Force).Count -eq 0) 'Staging is removed after success'

        Add-Content -LiteralPath (Join-Path $development 'README.md') -Value 'Uncommitted edit'
        Must-Fail { & (Join-Path $development 'packaging/public/prepare-publication.ps1') } 'Dirty source cannot be prepared'
        Assert ((Invoke-TestGit $development -Arguments @('rev-parse', 'public')) -eq $approved) 'Failure leaves public reference unchanged'
        Invoke-TestGit $development -Arguments @('restore', '--', 'README.md') | Out-Null
        Must-Fail { & (Join-Path $development 'packaging/public/build-source-package.ps1') -OutputPath (Join-Path $testRoot 'outside.zip') } 'Source output cannot escape artifacts'
        Assert (-not (Test-Path -LiteralPath (Join-Path $testRoot 'outside.zip'))) 'Rejected output creates no file'

        $privatePath = 'C:' + [char]92 + 'Users' + [char]92 + 'PrivateFixture' + [char]92 + 'notes'
        Set-Content -LiteralPath (Join-Path $development 'leak.md') -Value $privatePath -Encoding utf8
        Invoke-TestGit $development -Arguments @('add', 'leak.md') | Out-Null
        Invoke-TestGit $development -Arguments @('commit', '--quiet', '-m', 'Unsafe source fixture') | Out-Null
        Must-Fail { & (Join-Path $development 'packaging/public/prepare-publication.ps1') } 'Private profile path blocks preparation'
        Assert ((Invoke-TestGit $development -Arguments @('rev-parse', 'public')) -eq $approved) 'Privacy failure preserves approved revision'
        Assert (@(Get-ChildItem -LiteralPath (Join-Path $development 'artifacts') -Force).Count -eq 0) 'Staging is removed after failure'

        $zero = '0' * 40
        Check-Hook "refs/heads/public $approved refs/heads/main $seedCommit" $true 'Hook accepts approved public snapshot'
        Check-Hook "refs/heads/master $sourceHead refs/heads/main $seedCommit" $false 'Hook rejects development branch'
        Check-Hook "(delete) $zero refs/heads/main $approved" $false 'Hook rejects deletion'
        Check-Hook "refs/heads/public $sourceHead refs/heads/main $seedCommit" $false 'Hook rejects private ancestry'
        $tree = Invoke-TestGit $development -Arguments @('rev-parse', 'public^{tree}')
        $unapproved = Invoke-TestGit $development -Arguments @('commit-tree', $tree, '-p', $approved, '-m', 'Unapproved public edit')
        Check-Hook "refs/heads/public $unapproved refs/heads/main $seedCommit" $false 'Hook rejects unscanned public commit'
        Check-Hook "refs/heads/public $approved refs/heads/main $privateRoot" $false 'Hook rejects rewritten main history'
        Invoke-TestGit $development -Arguments @('tag', 'v1.2.3', $approved) | Out-Null
        Check-Hook "refs/tags/v1.2.3 $approved refs/tags/v1.2.3 $zero" $true 'Hook accepts public release tag'
        Check-Hook "refs/tags/v1.2.3 $approved refs/tags/v1.2.3 $seedCommit" $false 'Hook rejects replacement release tag'
        Invoke-TestGit $development -Arguments @('push', '--quiet', 'origin', 'public:main') | Out-Null
        Assert ((Invoke-TestGit $remote -Arguments @('rev-parse', 'main')) -eq $approved) 'Actual Git pre-push hook accepts local fixture publication'
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'; try { $blockedOutput = & git push origin master:unsafe 2>&1 } finally { $ErrorActionPreference = $prev }
        Assert ($LASTEXITCODE -ne 0) 'Actual Git hook blocks private branch publication'
        & git --git-dir=$remote show-ref --verify --quiet refs/heads/unsafe
        Assert ($LASTEXITCODE -eq 1) 'Rejected private branch does not reach remote'
        Assert ((Invoke-TestGit $development -Arguments @('symbolic-ref', '--short', 'HEAD')) -eq 'master') 'All checks leave development branch selected'
    }
    finally { Pop-Location }
    $checks | ForEach-Object { Write-Output "PASS: $_" }
    Write-Output "Publication checks passed: $($checks.Count). Only isolated local test repositories were used."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolved = (Resolve-Path -LiteralPath $testRoot).Path
        $expectedRoot = Join-Path $repositoryRoot 'artifacts/test-results'
        if (-not $resolved.StartsWith($expectedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            @(Get-ChildItem -LiteralPath $testRoot -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0 -or
            ((Get-Item -LiteralPath $testRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Unsafe test cleanup path; inspect it manually.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
