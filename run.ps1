param([string]$Repository)
$ErrorActionPreference = 'Stop'
[xml]$appProject = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/Gitland.App/Gitland.App.csproj')
$app = Join-Path $PSScriptRoot ('dist/Gitland-' + $appProject.Project.PropertyGroup.Version + '/gitland.exe')
if (!(Test-Path -LiteralPath $app)) { & (Join-Path $PSScriptRoot 'build.ps1') }
if ($Repository) { & $app $Repository } else { & $app }
