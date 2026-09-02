param(
  [string]$BuildOutput = "$(Join-Path $PSScriptRoot '..\bin\Release')",
  [switch]$BuildFirst
)
$ErrorActionPreference='Stop'
$project=Resolve-Path (Join-Path $PSScriptRoot '..\RevitEtabsValidator.csproj')
if($BuildFirst){ dotnet build $project -c Release }
$dll=Join-Path $BuildOutput 'RevitEtabsValidator.dll'
if(!(Test-Path $dll)){ throw "Build output not found: $dll. Build Release first or pass -BuildFirst." }
$destRoot=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2025'
New-Item -ItemType Directory -Force -Path $destRoot | Out-Null
$destDll=Join-Path $destRoot 'RevitEtabsValidator.dll'
Copy-Item $dll $destDll -Force
$template=Get-Content (Join-Path $PSScriptRoot 'RevitEtabsValidator.addin.template') -Raw
$manifest=$template.Replace('__ASSEMBLY_PATH__',$destDll)
Set-Content (Join-Path $destRoot 'RevitEtabsValidator.addin') $manifest -Encoding UTF8
Write-Host "Installed to $destRoot" -ForegroundColor Green
Write-Host "Restart Revit 2025. Use Structural QA > Model Coordination > Revit ↔ ETABS Validator."
