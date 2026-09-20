<#
.SYNOPSIS
  Read, set, or bump the Gitland release version across all the places it lives,
  then verify those places agree.

.DESCRIPTION
  src/Gitland.App/Gitland.App.csproj <Version> is the source of truth: it stamps
  gitland.exe, so the installer can check the published payload against it.

  Two build inputs cannot read an assembly and are synchronized here instead: the
  forge.toml [app] version (every wizard string and the registry Version entry
  interpolate the Forge app.version variable, so they follow it automatically)
  and the release filenames in README.md. This script keeps them in lockstep so a
  release cannot ship mismatched numbers.

  docs/releases/<version>.md and docs/design.md record what shipped in a given
  release and are deliberately left alone; only the README's pointer moves.

.EXAMPLE
  .\version.ps1                  # report current version + consistency
  .\version.ps1 -Set 0.10.0      # set everywhere, then verify
  .\version.ps1 -Bump patch      # 0.9.0 -> 0.9.1 everywhere, then verify
  .\version.ps1 -Bump minor -DryRun
#>
param(
    [string] $Set,
    [ValidateSet('patch', 'minor', 'major')]
    [string] $Bump,
    [switch] $DryRun,
    [string] $RepoRoot = $PSScriptRoot
)

# ---- pure version helpers ---------------------------------------------------

function Parse-SemVer([string] $text) {
    if ($text -notmatch '^\s*(\d+)\.(\d+)\.(\d+)\s*$') {
        throw "Not a valid x.y.z version: '$text'"
    }
    [pscustomobject]@{
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
    }
}

function Format-SemVer($v) { "{0}.{1}.{2}" -f $v.Major, $v.Minor, $v.Patch }

function Step-SemVer([string] $current, [string] $kind) {
    $v = Parse-SemVer $current
    switch ($kind) {
        'patch' { $v.Patch++ }
        'minor' { $v.Minor++; $v.Patch = 0 }
        'major' { $v.Major++; $v.Minor = 0; $v.Patch = 0 }
        default { throw "Unknown bump kind: '$kind'" }
    }
    Format-SemVer $v
}

function Normalize-Ver([string] $s) {
    # Collapse a 3- or 4-part version to canonical x.y.z (drops a trailing .0).
    $s.Trim() -replace '^(\d+\.\d+\.\d+)\.0$', '$1'
}

# ---- file locations ---------------------------------------------------------

function Get-CsprojPath([string] $root) { Join-Path $root 'src/Gitland.App/Gitland.App.csproj' }
function Get-ForgePath ([string] $root) { Join-Path $root 'forge.toml' }
function Get-ReadmePath([string] $root) { Join-Path $root 'README.md' }

# Every place a version lives by hand, as { Where = <label>; Value = <x.y.z> }.
function Get-VersionItems([string] $root) {
    $cs = [System.IO.File]::ReadAllText((Get-CsprojPath $root))
    $fg = [System.IO.File]::ReadAllText((Get-ForgePath  $root))
    $rm = [System.IO.File]::ReadAllText((Get-ReadmePath $root))
    $items = New-Object System.Collections.Generic.List[object]

    if ($cs -match '<Version>\s*(\d+(?:\.\d+)+)\s*</Version>') {
        $items.Add([pscustomobject]@{ Where = 'csproj <Version>'; Value = (Normalize-Ver $Matches[1]) })
    }
    else { throw 'src/Gitland.App/Gitland.App.csproj has no <Version>; restore it before building or releasing.' }

    if ($fg -match '(?m)^version\s*=\s*"(\d+(?:\.\d+)+)"') {
        $items.Add([pscustomobject]@{ Where = 'forge [app] version'; Value = (Normalize-Ver $Matches[1]) })
    }
    else { throw 'forge.toml has no [app] version; restore it before building or releasing.' }

    # Forge interpolates app.version into the wizard strings and the registry
    # entry. A literal number there would silently freeze while [app] version
    # moves, so fail instead of shipping a wizard that lies about its version.
    $literal = [regex]::Matches($fg, 'Gitland[- ](?:Setup-)?\d+\.\d+\.\d+')
    if ($literal.Count) {
        throw "forge.toml hardcodes a version ($($literal[0].Value)); use the app.version variable so the wizard follows [app] version."
    }

    $release = [regex]::Matches($rm, 'docs/releases/(\d+\.\d+\.\d+)\.md')
    if ($release.Count -eq 0) { throw 'README.md has no docs/releases/x.y.z.md link; restore it before releasing.' }
    for ($i = 0; $i -lt $release.Count; $i++) {
        $label = if ($release.Count -eq 1) { 'README release notes' } else { "README release notes #$($i + 1)" }
        $items.Add([pscustomobject]@{ Where = $label; Value = (Normalize-Ver $release[$i].Groups[1].Value) })
    }

    $setup = [regex]::Matches($rm, 'Gitland-Setup-(\d+\.\d+\.\d+)\.exe')
    if ($setup.Count -eq 0) { throw 'README.md has no Gitland-Setup-x.y.z.exe reference; restore it before releasing.' }
    for ($i = 0; $i -lt $setup.Count; $i++) {
        $label = if ($setup.Count -eq 1) { 'README installer' } else { "README installer #$($i + 1)" }
        $items.Add([pscustomobject]@{ Where = $label; Value = (Normalize-Ver $setup[$i].Groups[1].Value) })
    }

    # The dist/ build paths in the build section. Gitland-Setup- is excluded by the
    # digit that has to follow the dash, so it is only counted once, above.
    $dist = [regex]::Matches($rm, 'Gitland-(\d+\.\d+\.\d+)')
    if ($dist.Count -eq 0) { throw 'README.md has no dist/Gitland-x.y.z reference; restore it before releasing.' }
    for ($i = 0; $i -lt $dist.Count; $i++) {
        $label = if ($dist.Count -eq 1) { 'README dist path' } else { "README dist path #$($i + 1)" }
        $items.Add([pscustomobject]@{ Where = $label; Value = (Normalize-Ver $dist[$i].Groups[1].Value) })
    }

    , $items.ToArray()
}

# Rewrite every location to $target. Targeted replacements only, so the historical
# numbers in docs/releases/*.md and docs/design.md are left untouched.
function Set-Versions([string] $root, [string] $target, [switch] $DryRun) {
    $target = Format-SemVer (Parse-SemVer $target)   # validates the input

    $csprojPath = Get-CsprojPath $root
    $forgePath  = Get-ForgePath  $root
    $readmePath = Get-ReadmePath $root
    $cs = [System.IO.File]::ReadAllText($csprojPath)
    $fg = [System.IO.File]::ReadAllText($forgePath)
    $rm = [System.IO.File]::ReadAllText($readmePath)

    $cs = [regex]::Replace($cs, '(<Version>)\s*\d+(?:\.\d+)+\s*(</Version>)', '${1}' + $target + '${2}')
    $fg = [regex]::Replace($fg, '(?m)^(version\s*=\s*")\d+(?:\.\d+)+(")',     '${1}' + $target + '${2}')
    $rm = [regex]::Replace($rm, '(Gitland-Setup-)\d+\.\d+\.\d+(\.exe)',       '${1}' + $target + '${2}')
    $rm = [regex]::Replace($rm, '(Gitland-)\d+\.\d+\.\d+',                    '${1}' + $target)
    $rm = [regex]::Replace($rm, '(docs/releases/)\d+\.\d+\.\d+(\.md)',        '${1}' + $target + '${2}')

    if (-not $DryRun) {
        $enc = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($csprojPath, $cs, $enc)
        [System.IO.File]::WriteAllText($forgePath,  $fg, $enc)
        [System.IO.File]::WriteAllText($readmePath, $rm, $enc)
    }
}

# Compare every location against $target (defaults to csproj <Version>).
function Test-Consistent([string] $root, [string] $target) {
    $items = Get-VersionItems $root
    if (-not $target) {
        $target = ($items | Where-Object { $_.Where -eq 'csproj <Version>' } | Select-Object -First 1).Value
    }
    $results = foreach ($it in $items) {
        [pscustomobject]@{ Where = $it.Where; Value = $it.Value; Ok = ($it.Value -eq $target) }
    }
    [pscustomobject]@{
        Target = $target
        Items  = $results
        AllOk  = (@($results | Where-Object { -not $_.Ok }).Count -eq 0)
    }
}

# ---- CLI --------------------------------------------------------------------

function Show-Report([string] $root, [string] $target) {
    $c = Test-Consistent $root $target
    Write-Host "Version locations (target $($c.Target)):"
    foreach ($it in $c.Items) {
        $tag   = if ($it.Ok) { 'ok ' } else { 'BAD' }
        $color = if ($it.Ok) { 'DarkGreen' } else { 'Red' }
        Write-Host ("  [{0}] {1,-26} {2}" -f $tag, $it.Where, $it.Value) -ForegroundColor $color
    }
    Write-Host '  [src] forge wizard strings and the registry Version entry interpolate app.version.' -ForegroundColor DarkGray
    if ($c.AllOk) { Write-Host 'All locations agree.' -ForegroundColor Green }
    else          { Write-Host 'MISMATCH: not all locations agree.' -ForegroundColor Red }

    # Release notes are written per release, so a fresh bump legitimately has none
    # yet. Warn rather than fail: a missing file must not block a bump.
    $notes = Join-Path $root "docs/releases/$($c.Target).md"
    if (-not (Test-Path -LiteralPath $notes)) {
        Write-Warning "docs/releases/$($c.Target).md does not exist yet; the README links to it. Write it before releasing."
    }
    $c.AllOk
}

function Main {
    if ($Set -and $Bump) { Write-Error 'Specify only one of -Set or -Bump.'; exit 2 }

    if ($Set) {
        $target = Format-SemVer (Parse-SemVer $Set)
    }
    elseif ($Bump) {
        $cur = ((Get-VersionItems $RepoRoot) | Where-Object { $_.Where -eq 'csproj <Version>' } | Select-Object -First 1).Value
        if (-not $cur) { Write-Error 'Could not read current version from src/Gitland.App/Gitland.App.csproj.'; exit 2 }
        $target = Step-SemVer $cur $Bump
        Write-Host "Bumping $cur -> $target ($Bump)" -ForegroundColor Cyan
    }
    else {
        # Report-only mode.
        $ok = Show-Report $RepoRoot $null
        exit ([int](-not $ok))
    }

    if ($DryRun) {
        Write-Host "DryRun: would set version to $target everywhere (no files written)." -ForegroundColor Yellow
        exit 0
    }

    Set-Versions $RepoRoot $target
    Write-Host "Set version to $target. Verifying..." -ForegroundColor Cyan
    $ok = Show-Report $RepoRoot $target
    exit ([int](-not $ok))
}

# Run Main only when executed directly; dot-sourcing (e.g. from installer.ps1)
# sets InvocationName to '.' and must not trigger the CLI.
if ($MyInvocation.InvocationName -ne '.') { Main }
