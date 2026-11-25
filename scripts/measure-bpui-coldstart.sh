#!/usr/bin/env bash
# Time one fresh bpui process from start until the main window is mapped.
#
# bpui shell only. bpui is not wired to the emulator core, so this excludes
# machine construction and boot.
#
# Usage:
#   scripts/measure-bpui-coldstart.sh [publish-dir]
#
# publish-dir defaults to /tmp/bpui-publish.
# DISPLAY must already point at an X server. The baseline used Xvfb because
# the VM has no physical display:
#   Xvfb :99 -screen 0 1600x1200x24 -ac +extension RANDR +render -noreset
#   export DISPLAY=:99
#
# Framework-dependent publish (--self-contained false). The apphost needs the
# runtime selected by global.json. When DOTNET_ROOT is unset, it is set from
# the dotnet on PATH.
#
# xdotool 3.20160805 --sync sleeps about 500ms between tries, so a window
# mapped at 50ms and one mapped at 250ms both come back near 500ms. This
# script polls `xdotool search --onlyvisible --name` (about 2ms per try on
# the baseline machine) and then checks `xwininfo` Map State.
#
# Prints one line:
#   elapsed_ms=<n> polls=<n> pid=<pid> window=<id> map_state=<state> wm_name=<name> start_ns=<n> end_ns=<n> dotnet_root=<path>
#
# Repeat the script for more samples. The first sample after publish is the
# cold start. The window title searched is "BackPocket Emulator"
# (override with BPUI_WINDOW_NAME).

set -euo pipefail

publish_dir=${1:-/tmp/bpui-publish}
window_name=${BPUI_WINDOW_NAME:-BackPocket Emulator}
timeout_s=${BPUI_COLDSTART_TIMEOUT:-90}
log=${BPUI_COLDSTART_LOG:-/tmp/bpui-coldstart.log}

if [[ -z ${DISPLAY:-} ]]; then
  echo "DISPLAY is not set" >&2
  exit 2
fi

if [[ -z ${DOTNET_ROOT:-} ]]; then
  dotnet_bin=$(command -v dotnet || true)
  if [[ -z ${dotnet_bin} ]]; then
    echo "dotnet is not on PATH and DOTNET_ROOT is unset" >&2
    exit 2
  fi
  DOTNET_ROOT=$(cd "$(dirname "${dotnet_bin}")" && pwd)
  export DOTNET_ROOT
fi

app=${publish_dir}/bpui
dll=${publish_dir}/bpui.dll
if [[ ! -x ${app} ]]; then
  echo "apphost is missing or not executable: ${app}" >&2
  exit 2
fi
if [[ ! -f ${dll} ]]; then
  echo "bpui.dll is missing: ${dll}" >&2
  exit 2
fi

probe=$(date +%s%N)
if [[ ! ${probe} =~ ^[0-9]{19}$ ]]; then
  echo "date +%s%N did not return nanoseconds: ${probe}" >&2
  exit 2
fi

set +e
existing=$(xdotool search --onlyvisible --name "${window_name}" 2>/dev/null)
set -e
if [[ -n ${existing} ]]; then
  echo "a visible window named '${window_name}' already exists (${existing}); refusing to time a stale window" >&2
  exit 2
fi

: >"${log}"

start_ns=$(date +%s%N)
"${app}" >"${log}" 2>&1 &
pid=$!

cleanup() {
  if kill -0 "${pid}" 2>/dev/null; then
    kill "${pid}" 2>/dev/null || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do
      kill -0 "${pid}" 2>/dev/null || break
      sleep 0.1
    done
    if kill -0 "${pid}" 2>/dev/null; then
      kill -9 "${pid}" 2>/dev/null || true
    fi
    wait "${pid}" 2>/dev/null || true
  fi
}
trap cleanup EXIT

deadline=$((SECONDS + timeout_s))
polls=0
window_ids=
while (( SECONDS < deadline )); do
  polls=$((polls + 1))
  set +e
  window_ids=$(xdotool search --onlyvisible --name "${window_name}" 2>/dev/null)
  search_status=$?
  set -e
  if [[ ${search_status} -eq 0 && -n ${window_ids} ]]; then
    end_ns=$(date +%s%N)
    break
  fi
  if ! kill -0 "${pid}" 2>/dev/null; then
    echo "bpui exited before a visible window named '${window_name}' was mapped" >&2
    echo "--- bpui log ---" >&2
    cat "${log}" >&2 || true
    exit 1
  fi
  window_ids=
done

if [[ -z ${window_ids} ]]; then
  echo "no visible window named '${window_name}' within ${timeout_s}s (${polls} polls)" >&2
  echo "--- bpui log ---" >&2
  cat "${log}" >&2 || true
  exit 1
fi

window_id=${window_ids%%$'\n'*}
map_state=$(xwininfo -id "${window_id}" | sed -n 's/^.*Map State:[[:space:]]*//p')
if [[ ${map_state} != "IsViewable" ]]; then
  echo "window ${window_id} map state is '${map_state}', expected IsViewable" >&2
  exit 1
fi

wm_name=$(xprop -id "${window_id}" WM_NAME 2>/dev/null | sed -n 's/^WM_NAME([^)]*) = //p')
elapsed_ms=$(( (end_ns - start_ns) / 1000000 ))
echo "elapsed_ms=${elapsed_ms} polls=${polls} pid=${pid} window=${window_id} map_state=${map_state} wm_name=${wm_name} start_ns=${start_ns} end_ns=${end_ns} dotnet_root=${DOTNET_ROOT}"
