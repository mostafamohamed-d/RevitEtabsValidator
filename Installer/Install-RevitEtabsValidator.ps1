param(
  [string]$Configuration = "Release",
  [string]$RevitVersion = "2025"
)
$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $projectRoot 'RevitEtabsValidator.csproj'
if (!(Test-Path $project)) { throw "Project file not found: $project" }

Write-Host "Building RevitEtabsValidator ($Configuration)..." -ForegroundColor Cyan
dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

$dll = Join-Path $projectRoot ("bin\{0}\RevitEtabsValidator.dll" -f $Configuration)
if (!(Test-Path $dll)) {
    throw "Build succeeded but DLL was not found at: $dll"
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

Write-Host "" 
Write-Host "Installation complete." -ForegroundColor Green
Write-Host "DLL:      $destDll"
Write-Host "Manifest: $destManifest"
Write-Host "" 
Write-Host "Verify BOTH files exist in the Revit $RevitVersion Addins folder, then restart Revit $RevitVersion." -ForegroundColor Yellow
