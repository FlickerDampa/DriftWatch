#!/usr/bin/env bash
# Builds DriftWatch.exe with Mono on Linux or macOS (C# 5, .NET Framework 4.x). On Windows use build.ps1.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$here/build"
# Compile against the .NET Framework 4.8 reference assemblies, not Mono's own libraries, so the build can only use
# methods that exist in the .NET Framework that ships with Windows.
api=/usr/lib/mono/4.8-api
mcs -langversion:5 -nologo -optimize+ -target:winexe -out:"$here/build/DriftWatch.exe" \
  -win32icon:"$here/src/DriftWatch/Assets/DriftWatch.ico" \
  -nostdlib -r:$api/mscorlib.dll -r:$api/System.dll -r:$api/System.Core.dll \
  -r:$api/System.Xml.Linq.dll -r:$api/System.Xml.dll -r:$api/System.Windows.Forms.dll -r:$api/System.Drawing.dll \
  "$here"/src/DriftWatch/Core/*.cs "$here"/src/DriftWatch/Export/*.cs "$here"/src/DriftWatch/UI/*.cs "$here"/src/DriftWatch/UI/Pages/*.cs \
  "$here"/src/DriftWatch/Cli.cs "$here"/src/DriftWatch/Program.cs "$here"/src/DriftWatch/Properties/AssemblyInfo.cs
echo "Built $here/build/DriftWatch.exe"
