# Finds the Megabonk folder Steam actually uses (app 3405340), so the build scripts compile against the game you play
# and -Install puts the files there. Steam can move the game (another library, or a renamed folder): a fixed path
# then points at an old copy. Used by build.ps1 and build-thunderstore.ps1.
function Find-GameDir {
    $libs = @()
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $libs += $steam
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $libs += [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' }
        }
    }
    foreach ($lib in $libs | Select-Object -Unique) {
        $acf = Join-Path $lib "steamapps\appmanifest_3405340.acf"
        if (-not (Test-Path $acf)) { continue }
        $m = [regex]::Match((Get-Content $acf -Raw), '"installdir"\s+"([^"]+)"')
        if ($m.Success) { return (Join-Path $lib "steamapps\common\$($m.Groups[1].Value)") }
    }
    return "C:\Program Files (x86)\Steam\steamapps\common\Megabonk"
}

# The game folder to use: the one given, else the one Steam uses. It must have BepInEx (the build references its files).
function Resolve-GameDir([string]$GameDir) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET SDK isn't installed (the 'dotnet' command is missing). Install the .NET 6 SDK or newer from https://dotnet.microsoft.com/download and run this again."
    }
    if (-not $GameDir) { $GameDir = Find-GameDir }
    if (-not (Test-Path "$GameDir\BepInEx\core\BepInEx.Core.dll")) {
        throw "BepInEx 6 (IL2CPP) isn't installed in $GameDir. Install it there and start the game once, or pass -GameDir with the folder that has BepInEx (with r2modman or Gale: the profile folder)."
    }
    # BepInEx makes BepInEx\interop on the first start with the game: the build compiles against those files.
    if (-not (Get-ChildItem "$GameDir\BepInEx\interop" -Filter *.dll -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        throw "$GameDir\BepInEx\interop is empty. Start Megabonk once with BepInEx (wait for the main menu), close it, then run this again."
    }
    Write-Host "Game folder: $GameDir"
    return $GameDir
}
