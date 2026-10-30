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
Expect "fresh install" ((Run "fresh" $g) -eq 0 -and (Test-Path "$g\winhttp.dll") -and (Test-Path "$g\BepInEx\plugins\ModHub\ModHub.dll") -and (Test-Path "$g\BepInEx\plugins\MelonBridge\MelonBridge.dll") -and -not (Test-Path "$g\INSTALL - READ ME.txt"))
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

$p = Start-Process -FilePath $Exe -ArgumentList ("--find --log `"$T\find.log`"") -Wait -PassThru
Expect "finds the game through Steam" ((Get-Content "$T\find.log") -match "Megabonk$")

if ($fails -eq 0) { "ALL PASSED" } else { "$fails FAILED" }
