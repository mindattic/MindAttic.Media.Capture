# Rebuilds the netstandard2.1 core and copies it into the Unity package (unity/com.mindattic.media.capture/Runtime/Plugins).
# Run after changing src/MindAttic.Media.Capture; every Unity project references the package by path and picks it up on focus.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'src\MindAttic.Media.Capture\MindAttic.Media.Capture.csproj') -c Release -f netstandard2.1 --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
$out = Join-Path $root 'src\MindAttic.Media.Capture\bin\Release\netstandard2.1'
$plugins = Join-Path $root 'unity\com.mindattic.media.capture\Runtime\Plugins'
Copy-Item (Join-Path $out 'MindAttic.Media.Capture.dll'), (Join-Path $out 'MindAttic.Media.Capture.xml') $plugins -Force
Write-Host "Updated $plugins"
