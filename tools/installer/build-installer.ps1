param(
    [string]$InnoSetupPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"

# Resolve project root
$rootDir = Join-Path $PSScriptRoot "..\.."
$rootDir = [System.IO.Path]::GetFullPath($rootDir)

# Extract version from Directory.Build.props
$propsPath = Join-Path $rootDir "Directory.Build.props"
if (-not (Test-Path $propsPath)) {
    throw "Directory.Build.props not found at $propsPath"
}
[xml]$props = Get-Content $propsPath
$version = $props.Project.PropertyGroup.Version
if (-not $version) {
    throw "Version not found in Directory.Build.props"
}

Write-Host "Building DesktopFences installer v$version" -ForegroundColor Cyan

# Step 1: dotnet publish
Write-Host "Publishing..." -ForegroundColor Yellow
$publishArgs = @(
    (Join-Path $rootDir "src\DesktopFences.App"),
    "-c", "Release",
    "-p:PublishProfile=win-x64-self-contained"
)
& dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Step 2: Create output directory
$artifactsDir = Join-Path $rootDir "artifacts\installer"
if (-not (Test-Path $artifactsDir)) {
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
}

# Step 3: Build Inno Setup installer
Write-Host "Building installer..." -ForegroundColor Yellow
$issFile = Join-Path $PSScriptRoot "DesktopFences.iss"
if (-not (Test-Path $InnoSetupPath)) {
    throw "Inno Setup compiler not found at $InnoSetupPath. Install Inno Setup 6 or pass -InnoSetupPath."
}
& $InnoSetupPath $issFile "/DMyAppVersion=$version" "/Q"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$outputFile = Join-Path $artifactsDir "DesktopFences-Setup-$version.exe"
Write-Host "Installer built: $outputFile" -ForegroundColor Green
