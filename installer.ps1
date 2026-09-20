param(
    [ValidatePattern('^win-(x64|arm64)$')]
    [string]$Rid      = 'win-x64',
    [string]$ForgeDir = '..\Forge',
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    . (Join-Path $PSScriptRoot 'version.ps1')
    $versions = Test-Consistent $PSScriptRoot $null
    if (-not $versions.AllOk) {
        Show-Report $PSScriptRoot $null | Out-Null
        throw 'Version mismatch; run version.ps1 before packaging.'
    }
    $version = $versions.Target

    $forgeRoot    = Resolve-Path $ForgeDir
    $forgeGui     = Join-Path $forgeRoot 'build\forge.exe'
    $uninstaller  = Join-Path $forgeRoot 'build\uninstall.exe'
    $payloadDir   = Join-Path $PSScriptRoot "dist\$Rid"
    $outDir       = Join-Path $PSScriptRoot 'dist\installer'
    $setupPath    = Join-Path $outDir "Gitland-Setup-$version.exe"

    function Assert-GuiExecutable([string] $Path) {
        $stream = [IO.File]::OpenRead($Path)
        $reader = New-Object IO.BinaryReader($stream)
        try {
            if ($stream.Length -lt 128 -or $reader.ReadUInt16() -ne 0x5A4D) { throw "Not a Windows executable: $Path" }
            $stream.Position = 0x3C
            $peOffset = $reader.ReadInt32()
            if ($peOffset -lt 64 -or $peOffset -gt $stream.Length - 94) { throw "Invalid PE header: $Path" }
            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE signature: $Path" }
            $stream.Position = $peOffset + 24 + 68
            if ($reader.ReadUInt16() -ne 2) {
                throw "Expected a GUI executable: $Path. Build Forge with gobake build (windowsgui subsystem)."
            }
        } finally { $reader.Dispose(); $stream.Dispose() }
    }

    if (-not $SkipBuild) {
        Write-Host "[1/4] Building Gitland and staging the $Rid payload..." -ForegroundColor Cyan
        $buildArgs = @{ Payload = $true; Rid = $Rid }
        if (-not $SkipTests) { $buildArgs.Test = $true }
        # build.ps1 runs with ErrorActionPreference Stop and throws on any failure,
        # so a returning call means the payload was published.
        & (Join-Path $PSScriptRoot 'build.ps1') @buildArgs
    } else {
        Write-Host '[1/4] Skipping the rebuild (-SkipBuild)' -ForegroundColor DarkGray
    }

    Write-Host "`n[2/4] Checking the published payload..." -ForegroundColor Cyan
    foreach ($relative in @('gitland.exe', 'gitland.dll', 'Gitland.Core.dll', 'Avalonia.Base.dll',
                            'hostfxr.dll', 'coreclr.dll', 'LICENSES')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payloadDir $relative))) {
            throw "Missing payload: $relative. Run build.ps1 -Payload first."
        }
    }
    $payloadVersion = (Get-Item -LiteralPath (Join-Path $payloadDir 'gitland.dll')).VersionInfo.FileVersion
    if ($payloadVersion -ne "$version.0") {
        throw "The published Gitland is $payloadVersion but Forge expects $version. Run build.ps1 -Payload first."
    }
    # A framework-dependent publish would install an app that needs a .NET runtime
    # the user may not have; the self-contained one carries hostfxr + coreclr.
    if (-not (Test-Path -LiteralPath (Join-Path $payloadDir 'System.Private.CoreLib.dll'))) {
        throw 'The payload is not self-contained. Run build.ps1 -Payload first.'
    }

    Write-Host "`n[3/4] Checking Forge..." -ForegroundColor Cyan
    foreach ($required in @($forgeGui, $uninstaller)) {
        if (-not (Test-Path -LiteralPath $required)) {
            throw "Missing $required. Run 'gobake build' in $forgeRoot first."
        }
    }
    Assert-GuiExecutable $forgeGui
    Assert-GuiExecutable $uninstaller
    Write-Host "  using $forgeGui" -ForegroundColor DarkGray

    Write-Host "`n[4/4] Building Setup.exe..." -ForegroundColor Cyan

    # forge.exe is a GUI-subsystem binary, so $LASTEXITCODE is not propagated
    # through PowerShell's call operator. Use Start-Process -Wait -PassThru.
    function Invoke-Forge {
        param([string[]]$ForgeArgs)
        # Start-Process joins arguments into one Windows command line. Quote paths,
        # including trailing backslashes, so checkouts with spaces work in PS 5.1.
        $quoted = @($ForgeArgs | ForEach-Object {
            if ($_ -match '["\r\n]') { throw 'Invalid Forge argument.' }
            '"' + ($_ -replace '(\\+)$', '$1$1') + '"'
        })
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
        $stdout = Join-Path $outDir "forge-$($ForgeArgs[0]).stdout.log"
        $stderr = Join-Path $outDir "forge-$($ForgeArgs[0]).stderr.log"
        $process = Start-Process -FilePath $forgeGui -ArgumentList $quoted -WorkingDirectory $PSScriptRoot `
            -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout | Write-Host }
        if ($process.ExitCode -ne 0) {
            $detail = Get-Content -LiteralPath $stderr -Raw
            throw "forge $($ForgeArgs -join ' ') failed (exit $($process.ExitCode)): $detail"
        }
        if ((Get-Item -LiteralPath $stderr).Length) { Write-Warning (Get-Content -LiteralPath $stderr -Raw) }
    }

    Invoke-Forge @('validate')
    # Remove only this version's old installer, so a failed build cannot leave an
    # earlier Setup.exe behind to be reported as a new success.
    if (Test-Path -LiteralPath $setupPath) { Remove-Item -LiteralPath $setupPath -Force }
    Invoke-Forge @('build', '--out', $outDir)

    if (-not (Test-Path -LiteralPath $setupPath)) { throw "Expected installer missing after build: $setupPath" }
    $setup = Get-Item -LiteralPath $setupPath

    # Verify the PE subsystem is GUI (2) - guards the console-window regression.
    Assert-GuiExecutable $setup.FullName

    Write-Host ''
    Write-Host "Done. Output: $($setup.FullName)" -ForegroundColor Green
    Write-Host ('  size:      {0} MB' -f [math]::Round($setup.Length / 1MB, 1))
    Write-Host '  subsystem: GUI'
    Write-Host "  version:   $version"
    Write-Host "  SHA256:    $((Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash)"
} finally { Pop-Location }
