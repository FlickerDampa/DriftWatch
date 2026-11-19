# Builds DriftWatch.exe on Windows with the C# compiler that ships with .NET Framework 4.x.
# Nothing to install: run this from the repository folder in PowerShell.
#   powershell -ExecutionPolicy Bypass -File .\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '.NET Framework 4.x C# compiler not found. Install .NET Framework 4.8.' }

$src = Join-Path $root 'src\DriftWatch'
$out = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$cscArgs = @(
  '/nologo', '/optimize+', '/target:winexe', '/platform:anycpu',
  "/out:$out\DriftWatch.exe",
  "/win32icon:$src\Assets\DriftWatch.ico",
  '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll', '/r:System.Xml.Linq.dll',
  '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
  "$src\Core\*.cs", "$src\Export\*.cs", "$src\UI\*.cs", "$src\UI\Pages\*.cs",
  "$src\Cli.cs", "$src\Program.cs", "$src\Properties\AssemblyInfo.cs"
)
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit code $LASTEXITCODE)." }
Write-Host "Built $out\DriftWatch.exe"
