# Builds Mod Hub, optionally installs it into your game, and makes the Nexus zips in .\dist
#   .\build.ps1             build + package
#   .\build.ps1 -Install    also copy the DLL into your Megabonk BepInEx\plugins folder
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Megabonk",
    [switch]$Install
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Zips with forward-slash paths so Vortex, 7-Zip and Steam Deck/Linux all extract them correctly.
function New-Zip([string]$SourceDir, [string]$ZipPath) {
    if (Test-Path $ZipPath) { Remove-Item $ZipPath }
    $zip = [IO.Compression.ZipFile]::Open($ZipPath, 'Create')
    try {
        $base = (Resolve-Path $SourceDir).Path.TrimEnd('\') + '\'
        # Mod Hub's own files first: Mod Hub 1.1.0-1.2.0 read the first plugin in a zip as "the mod", and this zip
        # also holds Melon Bridge (sorted before ModHub). With ModHub first they recognize it as a Mod Hub update.
        Get-ChildItem $SourceDir -Recurse -File -Force | Sort-Object { if ($_.FullName -like "*\plugins\ModHub\*") { 0 } else { 1 } }, FullName | ForEach-Object {
            $rel = $_.FullName.Substring($base.Length).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $rel, 'Optimal') | Out-Null
        }
    } finally { $zip.Dispose() }
}
$root = $PSScriptRoot
[xml]$proj = Get-Content "$root\ModHub.csproj"
$version = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]

dotnet build "$root\ModHub.csproj" -c Release -nologo -p:GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$dll = "$root\bin\Release\net6.0\ModHub.dll"

# Melon Bridge ships inside Mod Hub as its own plugin (runs MelonLoader mods; can be switched off in the Hub).
# It lives in a separate project next to this one. Without it Mod Hub still builds, but the zips cannot run
# MelonLoader mods, so they are named -noMelonBridge: a publishable zip must never come from a build without it.
$bridgeRoot = Join-Path (Split-Path $root -Parent) "MelonBridge"
$haveBridge = Test-Path "$bridgeRoot\Plugin\MelonBridge.csproj"
$bridgeFiles = @()
$melonReadme = $null
$noBridgeTag = if ($haveBridge) { "" } else { "-noMelonBridge" }
if ($haveBridge) {
    dotnet build "$bridgeRoot\Plugin\MelonBridge.csproj" -c Release -nologo -p:GameDir="$GameDir"
    if ($LASTEXITCODE -ne 0) { throw "Melon Bridge build failed" }
    $bridgeBin = "$bridgeRoot\Plugin\bin\Release\net6.0"
    $bridgeFiles = @("$bridgeBin\MelonBridge.dll", "$bridgeBin\MelonLoader.dll", "$bridgeRoot\ThirdParty\MelonBridge.Newtonsoft.Json.dll",
                     "$bridgeRoot\ThirdParty\Newtonsoft.Json-LICENSE.md", "$bridgeRoot\package\README.txt")
    $melonReadme = "$bridgeRoot\package\melon-mods README.txt"
}
else {
    Write-Warning "Melon Bridge not found at $bridgeRoot - building Mod Hub without it."
    Write-Warning "These zips will NOT run MelonLoader mods. Do not publish them; restore the MelonBridge project first."
}

if ($Install) {
    $dest = "$GameDir\BepInEx\plugins\ModHub"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item $dll $dest -Force
        if ($haveBridge) {
        New-Item -ItemType Directory -Force "$GameDir\BepInEx\plugins\MelonBridge", "$GameDir\BepInEx\melon-mods" | Out-Null
        Copy-Item $bridgeFiles "$GameDir\BepInEx\plugins\MelonBridge" -Force
        Copy-Item $melonReadme "$GameDir\BepInEx\melon-mods\README.txt" -Force
        Write-Host "Installed to $dest (+ Melon Bridge)"
    } else { Write-Host "Installed to $dest (no Melon Bridge)" }
}

$dist = "$root\dist"
$stage = Join-Path $env:TEMP "mh-stage"   # short path: the game's dotnet files are too deep for MAX_PATH under the workspace
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $dist | Out-Null

# 1) Mod only: for players who already have BepInEx (and for Vortex)
$modOnly = "$stage\mod"
New-Item -ItemType Directory -Force "$modOnly\BepInEx\plugins\ModHub" | Out-Null
Copy-Item $dll "$modOnly\BepInEx\plugins\ModHub\"
Copy-Item "$root\package\README.txt" "$modOnly\BepInEx\plugins\ModHub\"
if ($haveBridge) {
    New-Item -ItemType Directory -Force "$modOnly\BepInEx\plugins\MelonBridge", "$modOnly\BepInEx\melon-mods" | Out-Null
    Copy-Item $bridgeFiles "$modOnly\BepInEx\plugins\MelonBridge\"
    Copy-Item $melonReadme "$modOnly\BepInEx\melon-mods\README.txt"
}
New-Zip $modOnly "$dist\ModHub-$version$noBridgeTag.zip"

# 2) All-in-one: BepInEx + the mod, for first-time modders (one extract and done)
$aio = "$stage\aio"
Copy-Item -Recurse $modOnly $aio
foreach ($f in "winhttp.dll", "doorstop_config.ini", ".doorstop_version") { Copy-Item "$GameDir\$f" "$aio\" }
Copy-Item -Recurse "$GameDir\dotnet" "$aio\dotnet"
Copy-Item -Recurse "$GameDir\BepInEx\core" "$aio\BepInEx\core"
# Unity's base libraries are NOT shipped: BepInEx needs them as a .zip, and Nexus quarantines a zip inside a zip
# ("nested archives"). So the first start downloads them (needs internet once); INSTALL - READ ME.txt says so.
Copy-Item "$root\package\INSTALL - READ ME.txt" "$aio\"
New-Zip $aio "$dist\ModHub-$version$noBridgeTag-AllInOne-with-BepInEx.zip"

# 3) Installer: one exe with exactly the All-in-One inside (finds the game through Steam, checks, installs, verifies)
if ($haveBridge) {
    dotnet build "$root\Installer\Installer.csproj" -c Release -nologo -p:Payload="$dist\ModHub-$version$noBridgeTag-AllInOne-with-BepInEx.zip" -p:Version=$version
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    Copy-Item "$root\Installer\bin\Release\net48\Mod Hub Installer.exe" "$dist\Mod Hub Installer $version.exe" -Force
} else {
    Write-Warning "Skipped the installer: it embeds the All-in-One zip, which has no Melon Bridge here."
}

Get-ChildItem $dist | ForEach-Object { "{0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB) }
