param(
  [string]$Configuration = "Release",
  [string]$RevitVersion = "2025"
)
$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $projectRoot 'RevitEtabsValidator.csproj'
if (!(Test-Path -LiteralPath $project)) { throw "Project file not found: $project" }

$sourceApp = Join-Path $projectRoot 'Revit\App.cs'
if (!(Test-Path -LiteralPath $sourceApp)) { throw "Expected Revit application entry point was not found: $sourceApp" }
$sourceText = Get-Content -LiteralPath $sourceApp -Raw
if ($sourceText -notmatch 'class\s+App\s*:\s*IExternalApplication') {
    throw "Revit\App.cs does not contain the expected IExternalApplication entry point. Pull the latest repository revision before building."
}

Write-Host "Cleaning previous build output..." -ForegroundColor Cyan
dotnet clean $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed with exit code $LASTEXITCODE." }

$binRoot = Join-Path $projectRoot "bin\$Configuration"
if (Test-Path -LiteralPath $binRoot) {
    Remove-Item -LiteralPath $binRoot -Recurse -Force -ErrorAction Stop
}

Write-Host "Building RevitEtabsValidator ($Configuration)..." -ForegroundColor Cyan
dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

$dll = Join-Path $projectRoot ("bin\{0}\RevitEtabsValidator.dll" -f $Configuration)
if (!(Test-Path -LiteralPath $dll)) {
    throw "Build succeeded but DLL was not found at: $dll"
}

# Verify the compiled artifact, not only the source file. This catches stale or wrong DLLs before Revit sees them.
try {
    $assembly = [System.Reflection.Assembly]::LoadFrom($dll)
    $appType = $assembly.GetType('RevitEtabsValidator.App', $false, $false)
    $commandType = $assembly.GetType('RevitEtabsValidator.Revit.Commands.ShowValidatorCommand', $false, $false)
    if ($null -eq $appType) {
        throw "Compiled DLL does not contain RevitEtabsValidator.App. The local source/build is not the expected revision."
    }
    if ($null -eq $commandType) {
        throw "Compiled DLL does not contain RevitEtabsValidator.Revit.Commands.ShowValidatorCommand."
    }
    Write-Host "Artifact verified: RevitEtabsValidator.App and ShowValidatorCommand are present." -ForegroundColor Green
    Write-Host "Assembly identity: $($assembly.FullName)"
}
catch {
    throw "Compiled artifact verification failed: $($_.Exception.Message)"
}

$destRoot = Join-Path $env:APPDATA ("Autodesk\Revit\Addins\{0}" -f $RevitVersion)
New-Item -ItemType Directory -Force -Path $destRoot | Out-Null

$destDll = Join-Path $destRoot 'RevitEtabsValidator.dll'
$destManifest = Join-Path $destRoot 'RevitEtabsValidator.addin'

Copy-Item -LiteralPath $dll -Destination $destDll -Force

$escapedDll = [System.Security.SecurityElement]::Escape($destDll)
$manifest = @"
<?xml version=""1.0"" encoding=""utf-8"" standalone=""no""?>
<RevitAddIns>
  <AddIn Type=""Application"">
    <Name>Revit ↔ ETABS Structural Model Validator</Name>
    <Assembly>$escapedDll</Assembly>
    <AddInId>8D3F9B8A-FA8C-4C4A-9A1D-0B1D5D8F6E73</AddInId>
    <FullClassName>RevitEtabsValidator.App</FullClassName>
    <VendorId>STRUCT-AUTO</VendorId>
    <VendorDescription>Structural model coordination validator</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $destManifest -Value $manifest -Encoding UTF8

# Final deployment verification.
$writtenManifest = Get-Content -LiteralPath $destManifest -Raw
if ($writtenManifest -notmatch [regex]::Escape($escapedDll)) {
    throw "Manifest verification failed. Assembly path in $destManifest does not match $destDll"
}
if ($writtenManifest -match 'CouplingBeamVerifier') {
    throw "Manifest verification failed: CouplingBeamVerifier reference detected in $destManifest"
}
if (!(Test-Path -LiteralPath $destDll)) {
    throw "DLL verification failed: $destDll"
}

$installedAssembly = [System.Reflection.Assembly]::LoadFrom($destDll)
if ($null -eq $installedAssembly.GetType('RevitEtabsValidator.App', $false, $false)) {
    throw "Installed DLL verification failed: RevitEtabsValidator.App is missing from $destDll"
}

Write-Host ""
Write-Host "Installation complete." -ForegroundColor Green
Write-Host "DLL:      $destDll"
Write-Host "Manifest: $destManifest"
Write-Host "Assembly: $($installedAssembly.FullName)"
Write-Host ""
Write-Host "The installer will now STOP before deployment if the DLL does not contain RevitEtabsValidator.App." -ForegroundColor Yellow
Write-Host "Close Revit 2025 completely before running this script again." -ForegroundColor Yellow
