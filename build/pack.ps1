# Builds the Claxe installer (Setup.exe) and update packages with Velopack.
#   Requires: .NET 10 SDK, vpk tool (dotnet tool install -g vpk)
#   Usage:    ./build/pack.ps1 -Version 0.2.0
#   Output:   artifacts/releases (Claxe-win-Setup.exe, portable zip, update packages)
#   Publish:  vpk upload github --repoUrl https://github.com/<owner>/claxe-browser --publish --releaseName "Claxe <version>" --tag v<version> -o artifacts/releases --token <token>
param([string]$Version = "0.2.0")
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot
$publish = Join-Path $root "artifacts\publish"
$releases = Join-Path $root "artifacts\releases"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

# Self-contained: the installed app doesn't need .NET on the user's PC.
dotnet publish "$root\src\Claxe\Claxe.csproj" -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none -o $publish
Remove-Item "$publish\*.xml" -ErrorAction SilentlyContinue

# The pack id decides the install folder (%LOCALAPPDATA%\ClaxeBrowser). It must NOT be "Claxe":
# that is the user data folder, and uninstalling removes the install folder.
vpk pack --packId ClaxeBrowser --packVersion $Version --packDir $publish --mainExe Claxe.exe `
    --packTitle Claxe --packAuthors Claxe --icon "$root\src\Claxe\Assets\claxe.ico" --outputDir $releases

Write-Host "Kurulum dosyası: $releases"
