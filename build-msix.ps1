# Build MSIX Package for BanglaHost

param(
    [string]$Rid = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$publish = Join-Path $PSScriptRoot "publish-msix"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "Building MSIX package for $Rid..." -ForegroundColor Cyan

# Publish the app with BuildMsix=true and GenerateAppxPackageOnBuild=true
dotnet publish src/BanglaHost.App/BanglaHost.App.csproj -c $Configuration -r $Rid --self-contained true -p:WindowsAppSDKSelfContained=true -p:BuildMsix=true -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -o $publish

# The MSIX package should be generated in the AppPackages directory of the project, let's find it and copy it to publish-msix
$appPackagesDir = Join-Path $PSScriptRoot "src\BanglaHost.App\bin\$Configuration\net8.0-windows10.0.19041.0\$Rid\AppPackages"
if (Test-Path $appPackagesDir) {
    Write-Host "Copying MSIX package..." -ForegroundColor Cyan
    Copy-Item -Path "$appPackagesDir\*" -Destination $publish -Recurse -Force
    Write-Host "MSIX Package built successfully in 'publish-msix' directory." -ForegroundColor Green
} else {
    Write-Warning "AppPackages directory not found. MSIX generation might have failed."
}
