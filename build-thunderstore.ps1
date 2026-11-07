# Builds the Thunderstore edition of Mod Hub and its Thunderstore package in .\dist
# (Thunderstore only: no Nexus, no drop folder or Downloads, no self-update - r2modman / Gale update it).
#   .\build-thunderstore.ps1
param([string]$GameDir = "")   # empty: the folder Steam uses for Megabonk (see game-dir.ps1)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = $PSScriptRoot
. "$root\game-dir.ps1"
$GameDir = Resolve-GameDir $GameDir
[xml]$proj = Get-Content "$root\ModHub.csproj"
$version = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]

dotnet build "$root\ModHub.csproj" -c Release -nologo -p:GameDir="$GameDir" -p:Edition=Thunderstore
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$dll = "$root\bin\Thunderstore\net6.0\ModHub.dll"

$bridgeRoot = Join-Path (Split-Path $root -Parent) "MelonBridge"
dotnet build "$bridgeRoot\Plugin\MelonBridge.csproj" -c Release -nologo -p:GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { throw "Melon Bridge build failed" }
$bridgeBin = "$bridgeRoot\Plugin\bin\Release\net6.0"
$bridgeFiles = @("$bridgeBin\MelonBridge.dll", "$bridgeBin\MelonLoader.dll", "$bridgeRoot\ThirdParty\MelonBridge.Newtonsoft.Json.dll",
                 "$bridgeRoot\ThirdParty\Newtonsoft.Json-LICENSE.md", "$bridgeRoot\package\README.txt")

$src = "$root\thunderstore"
$stage = Join-Path $env:TEMP "mh-ts-stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force "$stage\plugins\ModHub", "$stage\plugins\MelonBridge" | Out-Null
Copy-Item $dll "$stage\plugins\ModHub\"
Copy-Item $bridgeFiles "$stage\plugins\MelonBridge\"
Copy-Item "$src\icon.png", "$src\README.md", "$src\CHANGELOG.md" $stage

# manifest.json without a BOM (Thunderstore rejects one)
$manifest = [ordered]@{
    name           = "ModHub"
    version_number = $version
    website_url    = "https://github.com/hmodymac8-droid/Megabonk-ModHub"
    description    = "In-game mod manager (F4): switch mods on/off, profiles, one-click installs and updates from Thunderstore, What's new notes, crash safety. English and Arabic."
    dependencies   = @("BepInEx-BepInExPack_IL2CPP-6.0.755")
}
[IO.File]::WriteAllText("$stage\manifest.json", ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$zipPath = "$dist\AhmedHus21-ModHub-$version-Thunderstore.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    $base = (Resolve-Path $stage).Path.TrimEnd('\') + '\'
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $_.FullName.Substring($base.Length).Replace('\', '/'), 'Optimal') | Out-Null
    }
} finally { $zip.Dispose() }
"{0}  ({1:N1} MB)" -f (Split-Path $zipPath -Leaf), ((Get-Item $zipPath).Length / 1MB)
"ModHub.dll SHA-256: " + (Get-FileHash $dll -Algorithm SHA256).Hash
