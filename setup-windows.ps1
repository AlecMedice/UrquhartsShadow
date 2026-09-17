<#
  Urquhart's Shadow - one-shot Windows setup.
  Right-click > "Run with PowerShell" (or: powershell -ExecutionPolicy Bypass -File .\setup-windows.ps1)

  What it does:
    1. Makes sure the project code is present (checks out the development branch if this is a git clone).
    2. Finds an installed Unity 6 editor via Unity Hub and pins ProjectVersion.txt to it.
    3. Pass 1 (batch mode): imports TextMeshPro resources, sets Input Handling, creates config assets.
    4. Pass 2 (batch mode): builds the greybox scenes, prefabs, water and UI (GreyboxBuilder.BuildAll).
    5. Opens the Unity editor on the project. Open Assets/Scenes/Bootstrap.unity and press Play.
  Each batch pass can take a few minutes the first time (package download + import). Logs go to .\Logs\.
#>
param(
    [string]$UnityPath = "",
    [string]$Branch = "claude/youthful-hawking-37d4py"
)
$ErrorActionPreference = "Stop"
$proj = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $proj
New-Item -ItemType Directory -Force -Path "$proj\Logs" | Out-Null

function Say($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }
function Fail($m) { Write-Host "`nERROR: $m" -ForegroundColor Red; Read-Host "Press Enter to exit"; exit 1 }

# ---------- 1. Code present? ----------
if (-not (Test-Path "$proj\Assets\Scripts\Core\GameBootstrap.cs")) {
    if (Test-Path "$proj\.git") {
        Say "Project code missing; checking out branch $Branch"
        $git = Get-Command git -ErrorAction SilentlyContinue
        if (-not $git) { Fail "git is not installed. Install Git for Windows, or download the branch zip from GitHub and unzip it here." }
        git fetch origin $Branch
        git checkout $Branch
    } else {
        Fail "This folder does not contain the project code (Assets\Scripts missing) and is not a git clone. Clone the repo, or download the '$Branch' branch as a zip from GitHub and unzip it here."
    }
}

# ---------- 2. Find Unity ----------
function Find-Unity {
    if ($UnityPath -and (Test-Path $UnityPath)) { return $UnityPath }
    $roots = @("C:\Program Files\Unity\Hub\Editor")
    $secondary = "$env:APPDATA\UnityHub\secondaryInstallPath.json"
    if (Test-Path $secondary) {
        $p = (Get-Content $secondary -Raw).Trim('"', ' ', "`r", "`n")
        if ($p -and (Test-Path $p)) { $roots += $p }
    }
    $candidates = @()
    foreach ($r in $roots) {
        if (Test-Path $r) {
            Get-ChildItem $r -Directory | ForEach-Object {
                $exe = Join-Path $_.FullName "Editor\Unity.exe"
                if (Test-Path $exe) { $candidates += [pscustomobject]@{ Version = $_.Name; Exe = $exe } }
            }
        }
    }
    $six = $candidates | Where-Object { $_.Version -like "6000.*" } | Sort-Object Version -Descending
    if ($six) { return $six[0] }
    if ($candidates) { return ($candidates | Sort-Object Version -Descending)[0] }
    return $null
}

$unity = Find-Unity
if (-not $unity) {
    Fail "No Unity editor found under Unity Hub. In Unity Hub > Installs, install a Unity 6 editor (6000.0.x LTS), then run this script again. Or pass -UnityPath 'C:\...\Editor\Unity.exe'."
}
if ($unity -is [string]) { $exe = $unity; $ver = "" } else { $exe = $unity.Exe; $ver = $unity.Version }
Say "Using Unity $ver at $exe"
if ($ver -and $ver -notlike "6000.*") { Write-Host "WARNING: this project targets Unity 6 (6000.x). $ver may not compile." -ForegroundColor Yellow }
if ($ver) { "m_EditorVersion: $ver`n" | Set-Content "$proj\ProjectSettings\ProjectVersion.txt" -NoNewline }

# ---------- 2b. Align package versions with this editor ----------
# Unity 6 pins the render pipeline and several core packages to the editor version. Take those versions from the
# URP project template that ships inside this editor and merge our extra packages (Netcode, Services, etc.) on top.
function Sync-Manifest($editorExe) {
    $editorDir = Split-Path -Parent $editorExe
    $tplDir = Join-Path $editorDir "Data\Resources\PackageManager\ProjectTemplates"
    $tpl = $null
    if (Test-Path $tplDir) {
        $tpl = Get-ChildItem $tplDir -Filter "com.unity.template.universal-3d*.tgz" -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $tpl) { $tpl = Get-ChildItem $tplDir -Filter "com.unity.template.urp*.tgz" -ErrorAction SilentlyContinue | Select-Object -First 1 }
        if (-not $tpl) { $tpl = Get-ChildItem $tplDir -Filter "com.unity.template.universal*.tgz" -ErrorAction SilentlyContinue | Select-Object -First 1 }
    }
    if (-not $tpl) { Write-Host "  (no URP template found in this editor; keeping Packages\manifest.json as is)" -ForegroundColor Yellow; return }
    $tmp = Join-Path $env:TEMP "urquhart_template"
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $tmp | Out-Null
    & tar -xzf $tpl.FullName -C $tmp 2>$null
    $tplManifest = Get-ChildItem $tmp -Recurse -Filter manifest.json -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $tplManifest) { Write-Host "  (template manifest not found; keeping manifest as is)" -ForegroundColor Yellow; return }
    $tplJson = Get-Content $tplManifest.FullName -Raw | ConvertFrom-Json
    $ours = Get-Content "$proj\Packages\manifest.json" -Raw | ConvertFrom-Json
    $merged = [ordered]@{}
    foreach ($d in $tplJson.dependencies.PSObject.Properties) { $merged[$d.Name] = $d.Value }
    foreach ($d in $ours.dependencies.PSObject.Properties) { if (-not $merged.Contains($d.Name)) { $merged[$d.Name] = $d.Value } }
    $out = [ordered]@{ dependencies = $merged }
    ($out | ConvertTo-Json -Depth 5) | Set-Content "$proj\Packages\manifest.json" -Encoding UTF8
    Remove-Item "$proj\Packages\packages-lock.json" -Force -ErrorAction SilentlyContinue
    Write-Host "  Package versions aligned with $($tpl.Name)."
}
Say "Aligning package versions with Unity $ver"
Sync-Manifest $exe

# ---------- 3/4. Batch passes ----------
function Run-Batch($method, $log) {
    Say "Running $method (log: Logs\$log). This can take a few minutes..."
    $args = @("-batchmode", "-accept-apiupdate", "-projectPath", "`"$proj`"", "-executeMethod", $method, "-logFile", "`"$proj\Logs\$log`"", "-quit")
    $logPath = "$proj\Logs\$log"
    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    $p = Start-Process -FilePath $exe -ArgumentList $args -PassThru
    $started = Get-Date
    while (-not $p.HasExited) {
        Start-Sleep -Seconds 15
        $elapsed = [int]((Get-Date) - $started).TotalSeconds
        if (Test-Path $logPath) {
            $size = [int]((Get-Item $logPath).Length / 1KB)
            $last = (Get-Content $logPath -Tail 1 -ErrorAction SilentlyContinue)
            if ($last) { $last = $last.Substring(0, [Math]::Min(110, $last.Length)) }
            Write-Host ("    [{0,4}s] log {1,6} KB | {2}" -f $elapsed, $size, $last)
        } else {
            Write-Host ("    [{0,4}s] Unity running (pid {1}), no log file yet..." -f $elapsed, $p.Id)
        }
    }
    $text = ""
    if (Test-Path "$proj\Logs\$log") { $text = Get-Content "$proj\Logs\$log" -Raw }
    $errors = ($text -split "`n") | Where-Object { $_ -match "error CS\d+" } | Select-Object -Unique
    if ($errors) {
        Write-Host "`nC# compile errors found:" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host "  $_" }
        Write-Host "`nThe project code needs a fix before setup can continue. Give the lines above to Claude." -ForegroundColor Yellow
        Read-Host "Press Enter to exit"; exit 1
    }
    if ($text -match "No valid Unity Editor license|License is not valid|Failed to activate") {
        Fail "Unity has no active license for batch mode. Open Unity Hub, sign in, and make sure a Personal license is active (Hub > Preferences > Licenses), then run this again."
    }
    if ($p.ExitCode -ne 0) {
        Write-Host "Unity exited with code $($p.ExitCode). Check Logs\$log." -ForegroundColor Yellow
    }
}

Run-Batch "UrquhartsShadow.Editor.ProjectSetupMenu.FirstRunPrepare" "setup-pass1.log"
Run-Batch "UrquhartsShadow.Editor.GreyboxBuilder.BuildAll" "setup-pass2.log"

if (-not (Test-Path "$proj\Assets\Scenes\Bootstrap.unity")) {
    Write-Host "Greybox scenes were not created. Open the project in Unity and run the menu Urquhart's Shadow > Setup > Build Greybox (All), or check Logs\setup-pass2.log." -ForegroundColor Yellow
}

# ---------- 5. Open the editor ----------
Say "Opening Unity. When it loads: open Assets/Scenes/Bootstrap.unity (if not already open) and press Play, then choose Solo Expedition."
Start-Process -FilePath $exe -ArgumentList @("-projectPath", "`"$proj`"")
Read-Host "Press Enter to close this window"
