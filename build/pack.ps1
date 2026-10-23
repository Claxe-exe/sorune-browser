# Builds the Sorune installer (Setup.exe) and update packages with Velopack.
#   Requires: .NET 10 SDK, vpk tool (dotnet tool install -g vpk)
#   Usage:    ./build/pack.ps1 -Version 0.9.2
#   Output:   artifacts/releases (SoruneBrowser-win-Setup.exe, portable zip, update packages)
#   Publish:  vpk upload github --repoUrl https://github.com/Claxe-exe/sorune-browser --publish --releaseName "Sorune <version>" --tag v<version> -o artifacts/releases --token <token>
param([string]$Version = "0.9.2")
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot
$publish = Join-Path $root "artifacts\publish"
$releases = Join-Path $root "artifacts\releases"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

# Self-contained: the installed app doesn't need .NET on the user's PC.
dotnet publish "$root\src\Claxe\Claxe.csproj" -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none -o $publish
Remove-Item "$publish\*.xml" -ErrorAction SilentlyContinue
# Preset wallpapers are local extras (not in the repo); a public installer ships only the theme wallpapers.
Remove-Item "$publish\Assets\Wallpapers" -Recurse -ErrorAction SilentlyContinue

# The previous release next to the new one lets vpk build a small delta update package.
vpk download github --repoUrl https://github.com/Claxe-exe/sorune-browser --outputDir $releases
if ($LASTEXITCODE -ne 0) { Write-Warning "Önceki sürüm indirilemedi; yalnızca tam paket üretilecek." }

# The pack id decides the install folder (%LOCALAPPDATA%\SoruneBrowser). It must NOT be "Sorune":
# that is the user data folder, and uninstalling removes the install folder.
vpk pack --packId SoruneBrowser --packVersion $Version --packDir $publish --mainExe Sorune.exe `
    --packTitle Sorune --packAuthors Claxe --icon "$root\src\Claxe\Assets\claxe.ico" --outputDir $releases

Write-Host "Kurulum dosyası: $releases"
