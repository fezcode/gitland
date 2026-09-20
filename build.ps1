param([switch]$Test)
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
    $releaseDirectory = 'dist/Gitland-' + $appProject.Project.PropertyGroup.Version
    dotnet publish src/Gitland.App/Gitland.App.csproj -c Release --no-restore --no-build -o $releaseDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Write-Host "Ready: $releaseDirectory/gitland.exe"
} finally { Pop-Location }
