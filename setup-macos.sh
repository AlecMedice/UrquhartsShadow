#!/bin/bash
# Urquhart's Shadow - one-shot macOS setup.
#   chmod +x setup-macos.sh && ./setup-macos.sh            (or: bash setup-macos.sh)
#   Options: --unity "/Applications/Unity/Hub/Editor/6000.3.5f1/Unity.app"   --branch <git branch>
#
# What it does (same as setup-windows.ps1):
#   1. Makes sure the project code is present (checks out the development branch if this is a git clone).
#   2. Finds an installed Unity 6 editor via Unity Hub (prefers 6000.3 LTS, then 6000.0) and pins ProjectVersion.txt.
#   3. Aligns Packages/manifest.json with this editor: core packages from the editor's bundled copies and
#      project template, network packages as the newest stable release from Unity's registry.
#   4. Pass 1 (batch): TextMeshPro resources, Input System handling, config assets.
#   5. Pass 2 (batch): builds the greybox scenes, prefabs, water and UI (GreyboxBuilder.BuildAll).
#   6. Opens the Unity editor. Open Assets/Scenes/Bootstrap.unity and press Play, choose Solo Expedition.
set -u
PROJ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJ"
mkdir -p "$PROJ/Logs"
UNITY_APP=""
BRANCH="claude/youthful-hawking-37d4py"
while [ $# -gt 0 ]; do
  case "$1" in
    --unity) UNITY_APP="$2"; shift 2 ;;
    --branch) BRANCH="$2"; shift 2 ;;
    *) echo "Unknown option $1"; exit 1 ;;
  esac
done

say()  { printf "\n\033[36m==> %s\033[0m\n" "$1"; }
warn() { printf "\033[33m%s\033[0m\n" "$1"; }
fail() { printf "\n\033[31mERROR: %s\033[0m\n" "$1"; exit 1; }

# ---------- 1. Code present? ----------
if [ ! -f "$PROJ/Assets/Scripts/Core/GameBootstrap.cs" ]; then
  if [ -d "$PROJ/.git" ]; then
    say "Project code missing; checking out branch $BRANCH"
    command -v git >/dev/null || fail "git is not installed. Install Xcode Command Line Tools (xcode-select --install) or download the branch zip from GitHub."
    git fetch origin "$BRANCH" && git checkout "$BRANCH" || fail "Could not check out $BRANCH."
  else
    fail "This folder has no project code (Assets/Scripts missing) and is not a git clone. Clone the repo or unzip the '$BRANCH' branch here."
  fi
fi
command -v python3 >/dev/null || fail "python3 is required (it ships with Xcode Command Line Tools: xcode-select --install)."

# ---------- 2. Find Unity ----------
find_unity() {
  if [ -n "$UNITY_APP" ]; then echo "$UNITY_APP"; return; fi
  local roots=("/Applications/Unity/Hub/Editor")
  local secondary="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"
  if [ -f "$secondary" ]; then
    local p; p="$(tr -d '"\n\r' < "$secondary")"
    [ -d "$p" ] && roots+=("$p")
  fi
  local all=()
  for r in "${roots[@]}"; do
    [ -d "$r" ] || continue
    for d in "$r"/*/; do
      [ -d "$d/Unity.app" ] && all+=("$(basename "$d")|$d/Unity.app")
    done
  done
  [ ${#all[@]} -eq 0 ] && return
  for stream in "6000.3." "6000.0." "6000."; do
    local best; best="$(printf "%s\n" "${all[@]}" | grep "^$stream" | sort -t'|' -k1,1 -V | tail -1)"
    if [ -n "$best" ]; then echo "${best#*|}"; return; fi
  done
  printf "%s\n" "${all[@]}" | sort -t'|' -k1,1 -V | tail -1 | sed 's/^[^|]*|//'
}

UNITY_APP="$(find_unity)"
[ -n "$UNITY_APP" ] && [ -d "$UNITY_APP" ] || fail "No Unity editor found. In Unity Hub > Installs, install Unity 6000.3 LTS, then run this again (or pass --unity /path/to/Unity.app)."
UNITY_APP="${UNITY_APP%/}"
UNITY_BIN="$UNITY_APP/Contents/MacOS/Unity"
[ -x "$UNITY_BIN" ] || fail "Unity binary not found at $UNITY_BIN"
VER="$(basename "$(dirname "$UNITY_APP")")"
say "Using Unity $VER at $UNITY_APP"
case "$VER" in 6000.*) ;; *) warn "WARNING: this project targets Unity 6 (6000.x). $VER may not compile." ;; esac
printf "m_EditorVersion: %s\n" "$VER" > "$PROJ/ProjectSettings/ProjectVersion.txt"

# ---------- 3. Align package versions ----------
say "Aligning package versions with Unity $VER"
RES="$UNITY_APP/Contents/Resources/PackageManager"
TPL_DIR="$RES/ProjectTemplates"
TPL_MANIFEST=""
if [ -d "$TPL_DIR" ]; then
  for pat in "com.unity.template.universal-3d" "com.unity.template.urp" "com.unity.template.universal" "com.unity.template.3d"; do
    TPL="$(ls "$TPL_DIR"/$pat*.tgz 2>/dev/null | head -1)"
    [ -n "$TPL" ] && break
  done
  if [ -n "${TPL:-}" ]; then
    TMPD="$(mktemp -d)"
    tar -xzf "$TPL" -C "$TMPD" 2>/dev/null
    TPL_MANIFEST="$(find "$TMPD" -name manifest.json | head -1)"
    echo "  Template: $(basename "$TPL")"
  fi
fi
[ -z "$TPL_MANIFEST" ] && warn "  (no project template found in this editor)"

python3 - "$PROJ/Packages/manifest.json" "$TPL_MANIFEST" "$RES/Editor" <<'PY'
import json, os, re, sys, urllib.request
ours_path, tpl_path, cache_dir = sys.argv[1], sys.argv[2], sys.argv[3]
with open(ours_path) as f: merged = dict(json.load(f)["dependencies"])
if tpl_path and os.path.isfile(tpl_path):
    with open(tpl_path) as f: merged.update(json.load(f)["dependencies"])
bundled = {}
if os.path.isdir(cache_dir):
    for n in os.listdir(cache_dir):
        m = re.match(r'^(com\.unity\.[a-z0-9.\-]+?)[@-](\d+\.\d+\.\d+[^/]*?)\.tgz$', n)
        if m: bundled[m.group(1)] = m.group(2)
applied = 0
for k in list(merged):
    if k in bundled: merged[k] = bundled[k]; applied += 1
print(f"  Bundled package versions applied: {applied}")
def latest(name):
    try:
        with urllib.request.urlopen(f"https://packages.unity.com/{name}", timeout=40) as r:
            data = json.load(r)
        vs = [v for v in data["versions"] if not re.search(r"-(pre|exp|preview|rc)", v)]
        key = lambda v: tuple(int(x) for x in re.sub(r'[^\d.].*$', '', v).split('.'))
        return sorted(vs, key=key)[-1] if vs else None
    except Exception:
        return None
for n in ["com.unity.netcode.gameobjects", "com.unity.services.multiplayer", "com.unity.inputsystem", "com.unity.ai.navigation", "com.unity.timeline"]:
    if n not in merged or n in bundled: continue
    v = latest(n)
    if v: merged[n] = v; print(f"  Registry: {n} -> {v}")
    else: print(f"  (could not query registry for {n}; keeping {merged[n]})")
for n in ["com.unity.transport", "com.unity.services.authentication"]: merged.pop(n, None)
with open(ours_path, "w") as f: json.dump({"dependencies": merged}, f, indent=2); f.write("\n")
lock = os.path.join(os.path.dirname(ours_path), "packages-lock.json")
if os.path.exists(lock): os.remove(lock)
print("  Final Packages/manifest.json:")
for k, v in merged.items(): print(f"    {k:<48} {v}")
PY

# ---------- 4/5. Batch passes ----------
run_batch() {
  local method="$1" log="$2" quit="${3:-yes}" logpath="$PROJ/Logs/$2"
  say "Running $method (log: Logs/$log). This can take a few minutes..."
  rm -f "$logpath"
  local extra=(); [ "$quit" = "yes" ] && extra=(-quit)
  "$UNITY_BIN" -batchmode -accept-apiupdate -projectPath "$PROJ" -executeMethod "$method" -logFile "$logpath" "${extra[@]}" &
  local pid=$! start=$(date +%s)
  while kill -0 "$pid" 2>/dev/null; do
    sleep 15
    local el=$(( $(date +%s) - start ))
    if [ -f "$logpath" ]; then
      printf "    [%4ss] log %6s KB | %s\n" "$el" "$(( $(stat -f%z "$logpath") / 1024 ))" "$(tail -1 "$logpath" | cut -c1-110)"
    else
      printf "    [%4ss] Unity running (pid %s), no log file yet...\n" "$el" "$pid"
    fi
  done
  wait "$pid"; local code=$?
  local text=""; [ -f "$logpath" ] && text="$(cat "$logpath")"
  if echo "$text" | grep -qE "No valid Unity Editor license|License is not valid|Failed to activate"; then
    fail "Unity has no active license for batch mode. Open Unity Hub, sign in, make sure a Personal license is active (Hub > Preferences > Licenses), then run this again."
  fi
  local errors; errors="$(echo "$text" | grep -E "error CS[0-9]+" | sort -u)"
  if [ -n "$errors" ]; then
    local own pkg
    own="$(echo "$errors" | grep -v PackageCache || true)"
    pkg="$(echo "$errors" | grep PackageCache || true)"
    printf "\n\033[31mC# compile errors found: %s in project code, %s in Unity packages.\033[0m\n" "$(echo "$own" | grep -c . )" "$(echo "$pkg" | grep -c .)"
    if [ -n "$own" ]; then warn "Project code errors (give these to Claude):"; echo "$own" | head -60 | sed 's/^/  /'; fi
    if [ -n "$pkg" ]; then
      warn "Package errors (first 5):"; echo "$pkg" | head -5 | sed 's/^/  /'
      warn "Unity's own packages do not compile on this editor version ($VER). Install Unity 6000.3 LTS in Unity Hub and run this again; it prefers LTS automatically."
    fi
    exit 1
  fi
  [ "$code" -ne 0 ] && warn "Unity exited with code $code. Check Logs/$log."
  return 0
}

run_batch "UrquhartsShadow.Editor.ProjectSetupMenu.FirstRunPrepare" "setup-pass1.log" no
run_batch "UrquhartsShadow.Editor.GreyboxBuilder.BuildAll" "setup-pass2.log"

[ -f "$PROJ/Assets/Scenes/Bootstrap.unity" ] || warn "Greybox scenes were not created. Open the project and run Urquhart's Shadow > Setup > Build Greybox (All), or check Logs/setup-pass2.log."

# ---------- 6. Open the editor ----------
say "Opening Unity. When it loads: open Assets/Scenes/Bootstrap.unity (if not already open), press Play, choose Solo Expedition."
open -n "$UNITY_APP" --args -projectPath "$PROJ"
