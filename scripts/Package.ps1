[CmdletBinding()]
param(
    [string]$YakPath,
    [switch]$StageOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot 'HopperWire.csproj'
$manifestPath = Join-Path $repoRoot 'yak/manifest.yml'
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ })
$manifest = Get-Content -LiteralPath $manifestPath -Raw
$manifestVersion = [regex]::Match($manifest, '(?m)^version:\s*(\S+)\s*$').Groups[1].Value
if ($version -ne $manifestVersion) {
    throw "Project version '$version' does not match manifest version '$manifestVersion'."
}

if (!$StageOnly) {
    if (!$YakPath) {
        $yakCommand = Get-Command 'yak' -ErrorAction SilentlyContinue
        if ($yakCommand) { $YakPath = $yakCommand.Source }
        elseif ($env:ProgramFiles) { $YakPath = Join-Path $env:ProgramFiles 'Rhino 8/System/Yak.exe' }
    }
    if (!$YakPath -or !(Test-Path -LiteralPath $YakPath -PathType Leaf)) {
        throw 'Yak was not found. Pass -YakPath to the Rhino Yak executable, or use -StageOnly.'
    }
    $YakPath = (Resolve-Path -LiteralPath $YakPath).Path
}

& dotnet build $projectPath --configuration Release --framework net7.0
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed with exit code $LASTEXITCODE." }

# A fresh directory ensures that stale binaries never enter a package.
$outputPath = Join-Path $repoRoot 'artifacts/yak'
$stagingPath = Join-Path $outputPath ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingPath 'manifest.yml')
Copy-Item -LiteralPath (Join-Path $repoRoot 'icon.png') -Destination (Join-Path $stagingPath 'icon.png')
Copy-Item -LiteralPath (Join-Path $repoRoot 'bin/Release/net7.0/HopperWire.gha') -Destination $stagingPath
if ($StageOnly) {
    Write-Output "Staged package sources at $stagingPath"
    return
}

Push-Location -LiteralPath $stagingPath
try {
    & $YakPath build --platform any
    if ($LASTEXITCODE -ne 0) { throw "Yak build failed with exit code $LASTEXITCODE." }
    $packages = @(Get-ChildItem -LiteralPath $stagingPath -Filter '*.yak')
    if ($packages.Count -ne 1) { throw 'Expected exactly one generated Yak package.' }
    Copy-Item -LiteralPath $packages[0].FullName -Destination $outputPath -Force
    Write-Output "Built package: $(Join-Path $outputPath $packages[0].Name)"
}
finally {
    Pop-Location
}
