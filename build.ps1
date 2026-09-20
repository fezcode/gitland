param(
    [switch]$Test,
    [switch]$Payload,
    [ValidatePattern('^win-(x64|arm64)$')]
    [string]$Rid = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build Gitland.slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Test) {
        dotnet test tests/Gitland.Tests/Gitland.Tests.csproj -c Release --no-build --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
        dotnet tools/Gitland.Preview/bin/Release/net10.0/Gitland.Preview.dll dist/preview
        if ($LASTEXITCODE -ne 0) { throw 'Native UI checks failed.' }
    }
    [xml]$appProject = Get-Content -LiteralPath 'src/Gitland.App/Gitland.App.csproj'
    $version = $appProject.Project.PropertyGroup.Version
    $releaseDirectory = 'dist/Gitland-' + $version
    dotnet publish src/Gitland.App/Gitland.App.csproj -c Release --no-restore --no-build -o $releaseDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Write-Host "Ready: $releaseDirectory/gitland.exe"

    if ($Payload) {
        # The installer payload: self-contained, so it runs without a .NET install.
        # forge.toml reads it from the fixed path dist/<rid>.
        . (Join-Path $PSScriptRoot 'version.ps1')
        if (-not (Show-Report $PSScriptRoot $null)) { throw 'Version mismatch; run version.ps1 -Set x.y.z before packaging.' }

        $payloadDirectory = Join-Path $PSScriptRoot "dist/$Rid"
        $payloadExecutable = Join-Path $payloadDirectory 'gitland.exe'

        # Publish cannot overwrite a running executable; it fails part-way and
        # leaves a stale binary that still looks like a successful build. Only the
        # copy being replaced needs to exit - installed copies keep running.
        $running = @(Get-Process -Name gitland -ErrorAction SilentlyContinue |
                     Where-Object { $_.Path -eq $payloadExecutable })
        if ($running.Count) {
            Write-Host "Stopping $($running.Count) gitland process(es) running from $payloadDirectory." -ForegroundColor Yellow
            $running | Stop-Process -Force
            foreach ($process in $running) { $process.WaitForExit() }
        }

        # Clean only this payload, so other builds and dist/installer survive.
        $full = [IO.Path]::GetFullPath($payloadDirectory)
        $boundary = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist')) + [IO.Path]::DirectorySeparatorChar
        if (-not $full.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean outside dist: $full"
        }
        if (Test-Path -LiteralPath $full) {
            $items = @((Get-Item -LiteralPath $full)) + @(Get-ChildItem -LiteralPath $full -Recurse -Force)
            if ($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
                throw "Refusing to clean a payload directory containing links: $full"
            }
            Remove-Item -LiteralPath $full -Recurse -Force
        }

        Write-Host "`nPublishing the self-contained payload for $Rid..." -ForegroundColor Cyan
        dotnet publish src/Gitland.App/Gitland.App.csproj -c Release -r $Rid --self-contained `
            -p:PublishSingleFile=false -p:DebugType=embedded -o $payloadDirectory --nologo
        if ($LASTEXITCODE -ne 0) {
            Write-Host "PUBLISH FAILED - $payloadExecutable is STALE or missing." -ForegroundColor Red
            throw 'Payload publish failed.'
        }
        if (-not (Test-Path -LiteralPath $payloadExecutable)) { throw "Payload publish produced no $payloadExecutable." }
        Write-Host "Payload: $payloadDirectory (Gitland $version, $Rid)"
    }
} finally { Pop-Location }
