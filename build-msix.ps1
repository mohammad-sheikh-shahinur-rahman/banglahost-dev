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

# Locate mspdbcmf.exe so the build emits a .msixsym symbols package alongside the .msix.
# The Windows App SDK targets only probe $(VCToolsInstallDir)/$(VsInstallRoot), neither of
# which is set when `dotnet publish` runs from a plain shell instead of a Developer Command
# Prompt — so the symbols package was silently skipped (the build logs it as a *warning*).
# Without symbols the Store cannot resolve a crash dump into a stack, and every failure is
# bucketed as "Uncategorized ... detailed diagnostic data is not currently available", which
# is exactly what 1.6.5.0's Partner Center dashboard reported for all 4 of its hits.
$symArgs = @()
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsRoot = & $vswhere -latest -products * -property installationPath 2>$null | Select-Object -First 1
    if ($vsRoot) {
        $vcRoot = Join-Path $vsRoot "VC\Tools\MSVC"
        if (Test-Path $vcRoot) {
            $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'x86') { 'x86' } else { 'x64' }
            $mspdbcmf = Get-ChildItem $vcRoot -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending |
                ForEach-Object { Join-Path $_.FullName "bin\Host$hostArch\x64\mspdbcmf.exe" } |
                Where-Object { Test-Path $_ } |
                Select-Object -First 1
            if ($mspdbcmf) {
                Write-Host "Symbols: $mspdbcmf" -ForegroundColor DarkGray
                $symArgs = @("-p:MsPdbCmfExeFullpath=$mspdbcmf")
            }
        }
    }
}
if ($symArgs.Count -eq 0) {
    Write-Warning "mspdbcmf.exe not found - NO symbols package will be generated, so Store crash reports will stay 'Uncategorized'. Install the Visual Studio 'Desktop development with C++' workload."
}

# Publish the app with BuildMsix=true and GenerateAppxPackageOnBuild=true
dotnet publish src/BanglaHost.App/BanglaHost.App.csproj -c $Configuration -r $Rid --self-contained true -p:WindowsAppSDKSelfContained=true -p:BuildMsix=true -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false @symArgs -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# The MSIX package should be generated in the AppPackages directory of the project, let's find it and copy it to publish-msix
$appPackagesDir = Join-Path $PSScriptRoot "src\BanglaHost.App\bin\$Configuration\net8.0-windows10.0.19041.0\$Rid\AppPackages"
if (Test-Path $appPackagesDir) {
    Write-Host "Copying MSIX package..." -ForegroundColor Cyan
    Copy-Item -Path "$appPackagesDir\*" -Destination $publish -Recurse -Force
    Write-Host "MSIX Package built successfully in 'publish-msix' directory." -ForegroundColor Green
} else {
    Write-Warning "AppPackages directory not found. MSIX generation might have failed."
}
