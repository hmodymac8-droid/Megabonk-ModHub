# Runs every installer test on throwaway fake game folders in %TEMP% (never touches the real game).
#   .\test-installer.ps1 "path\to\Mod Hub Installer x.y.z.exe"
param([string]$Exe)
$ErrorActionPreference = "Stop"
$T = "$env:TEMP\mhi-tests"; if (Test-Path $T) { Remove-Item -Recurse -Force $T }; New-Item -ItemType Directory $T | Out-Null
$fails = 0
function Run($name, $dir) {
    $log = "$T\$name.log"
    $p = Start-Process -FilePath $Exe -ArgumentList ("--silent --target `"$dir`" --log `"$log`"") -Wait -PassThru
    $script:last = (Get-Content $log -ErrorAction SilentlyContinue) -join " | "
    return $p.ExitCode
}
function Expect($name, $ok) { if ($ok) { "PASS  $name" } else { "FAIL  $name :: $script:last"; $script:fails++ } }
function NewGame($n) { $d = "$T\$n"; New-Item -ItemType Directory -Force $d | Out-Null; Set-Content "$d\Megabonk.exe" "fake"; return $d }

$g = NewGame "fresh"
Expect "fresh install" ((Run "fresh" $g) -eq 0 -and (Test-Path "$g\winhttp.dll") -and (Test-Path "$g\BepInEx\plugins\ModHub\ModHub.dll") -and (($Exe -like "*-noMelonBridge*") -or (Test-Path "$g\BepInEx\plugins\MelonBridge\MelonBridge.dll")) -and -not (Test-Path "$g\INSTALL - READ ME.txt"))
Expect "run again changes nothing" ((Run "again" $g) -eq 0 -and $script:last -match "installed=0 ")

$u = NewGame "update"
New-Item -ItemType Directory -Force "$u\BepInEx\plugins\ModHub", "$u\BepInEx\plugins\MyMod", "$u\BepInEx\config" | Out-Null
Set-Content "$u\BepInEx\plugins\ModHub\ModHub.dll" "old hub"; Set-Content "$u\BepInEx\plugins\MyMod\MyMod.dll" "my mod"; Set-Content "$u\BepInEx\config\BepInEx.cfg" "my settings"
Expect "update keeps own mods/settings, backs up replaced files" ((Run "update" $u) -eq 0 -and (Get-Content "$u\BepInEx\plugins\MyMod\MyMod.dll") -eq "my mod" -and (Get-Content "$u\BepInEx\config\BepInEx.cfg") -eq "my settings" -and ((Get-ChildItem "$u\BepInEx\_installer_backup" -Recurse -Filter ModHub.dll | Get-Content) -eq "old hub"))

$l = NewGame "leftover"; New-Item -ItemType Directory -Force "$l\ModHub-1.1.0-AllInOne-with-BepInEx\BepInEx" | Out-Null; Set-Content "$l\ModHub-1.1.0-AllInOne-with-BepInEx\winhttp.dll" "x"
Expect "wrong earlier extraction moved to backup" ((Run "leftover" $l) -eq 0 -and -not (Test-Path "$l\ModHub-1.1.0-AllInOne-with-BepInEx"))

New-Item -ItemType Directory -Force "$T\notgame" | Out-Null
Expect "refuses a folder without Megabonk.exe" ((Run "notgame" "$T\notgame") -eq 2)

$r = NewGame "readonly"; icacls $r /deny "${env:USERNAME}:(W,AD,WD)" | Out-Null
Expect "protected folder detected" ((Run "readonly" $r) -eq 3 -and $script:last -match "canWrite=False")
icacls $r /remove:d $env:USERNAME | Out-Null

New-Item -ItemType Directory -Force "$T\fakeproc" | Out-Null; Copy-Item "$PSHOME\powershell.exe" "$T\fakeproc\Megabonk.exe"
$fake = Start-Process "$T\fakeproc\Megabonk.exe" -ArgumentList "-NoProfile -Command Start-Sleep 60" -WindowStyle Hidden -PassThru; Start-Sleep 2
Expect "open game detected" ((Run "gameopen" (NewGame "running")) -eq 3 -and $script:last -match "running=True")
Stop-Process -Id $fake.Id; Start-Sleep 1

$k = NewGame "locked"; New-Item -ItemType Directory -Force "$k\BepInEx\core" | Out-Null; Set-Content "$k\BepInEx\core\0Harmony.dll" "old"
$fs = [IO.File]::Open("$k\BepInEx\core\0Harmony.dll", 'Open', 'ReadWrite', 'None')
$code = Run "locked" $k; $fs.Close()
Expect "locked file: clear message, nothing changed" ($code -eq 1 -and $script:last -match "Nothing was changed" -and (Get-ChildItem $k -Recurse -File).Count -eq 2)
Expect "locked file: retry works" ((Run "locked-retry" $k) -eq 0)

$m = NewGame "melon"; New-Item -ItemType Directory -Force "$m\MelonLoader" | Out-Null; Set-Content "$m\version.dll" "ml"
Expect "real MelonLoader: warned, still installs" ((Run "melon" $m) -eq 0 -and $script:last -match "otherMelonLoader=True")

# "Did it work": the check reads the game's BepInEx\LogOutput.log (fake folders, no real game is started).
function Verify($name, $dir, $log, $seconds) {
    $out = "$T\$name.log"
    $job = if ($log) { Start-Job { param($d, $text) Start-Sleep 3; Set-Content "$d\BepInEx\LogOutput.log" $text } -ArgumentList $dir, $log }
    $p = Start-Process -FilePath $Exe -ArgumentList ("--verify --target `"$dir`" --seconds $seconds --log `"$out`"") -Wait -PassThru
    if ($job) { $job | Wait-Job | Remove-Job }
    $script:last = Get-Content $out -ErrorAction SilentlyContinue
    return $p.ExitCode
}
$v = NewGame "verify-ok"; New-Item -ItemType Directory -Force "$v\BepInEx" | Out-Null
Expect "check: Mod Hub loaded" ((Verify "verify-ok" $v "[Message: Preloader] BepInEx`n[Info   :   Mod Hub] Mod Hub 9.9.9 loaded. Press F4 to open it." 20) -eq 0 -and $script:last -match "Loaded version=9.9.9")
$v = NewGame "verify-nohub"; New-Item -ItemType Directory -Force "$v\BepInEx" | Out-Null
Expect "check: BepInEx runs, Mod Hub missing" ((Verify "verify-nohub" $v "[Error  :   BepInEx] Could not load [Mod Hub]`n[Message:   BepInEx] Chainloader startup complete" 20) -eq 4 -and $script:last -match "NoModHub" -and $script:last -match "Could not load")
$v = NewGame "verify-nolog"; New-Item -ItemType Directory -Force "$v\BepInEx" | Out-Null; Set-Content "$v\BepInEx\LogOutput.log" "Mod Hub 1.0.0 loaded"; (Get-Item "$v\BepInEx\LogOutput.log").LastWriteTime = (Get-Date).AddDays(-1)
Expect "check: an old log doesn't count" ((Verify "verify-nolog" $v $null 5) -eq 4 -and $script:last -match "NoBepInEx")

$p = Start-Process -FilePath $Exe -ArgumentList ("--find --log `"$T\find.log`"") -Wait -PassThru
# Where Steam says the game is (its appmanifest), not a fixed folder name.
$steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
$acf = @($steam) + ([regex]::Matches((Get-Content "$steam\steamapps\libraryfolders.vdf" -Raw -ErrorAction SilentlyContinue), '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' }) |
       ForEach-Object { "$_\steamapps\appmanifest_3405340.acf" } | Where-Object { Test-Path $_ } | Select-Object -First 1
$installdir = if ($acf) { [regex]::Match((Get-Content $acf -Raw), '"installdir"\s+"([^"]+)"').Groups[1].Value } else { "Megabonk" }
$script:last = Get-Content "$T\find.log"
Expect "finds the game through Steam ($installdir)" ($script:last -like "*\$installdir")

if ($fails -eq 0) { "ALL PASSED" } else { "$fails FAILED" }
