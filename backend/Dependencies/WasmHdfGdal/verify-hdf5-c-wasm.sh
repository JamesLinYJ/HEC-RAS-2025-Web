#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../../.." && pwd)"
task_root="$repo_root/work/wasm-chain-2026-10-22"
work_root="${WASM_HDF_GDAL_WORK_ROOT:-$task_root/hdf-gdal}"
toolchain_root="${WASM_HDF_GDAL_TOOLCHAIN_ROOT:-$task_root/toolchains}"
pack_root="$toolchain_root/cli-home/.dotnet/packs"
emsdk_root="$pack_root/Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.osx-arm64/10.0.12/tools"
emscripten_root="$emsdk_root/emscripten"
node_root="$pack_root/Microsoft.NET.Runtime.Emscripten.3.1.56.Node.osx-arm64/10.0.12/tools/bin"
node_path="$node_root/node"
install_root="$work_root/install/repro"
build_root="$work_root/build/repro"
log_root="$work_root/logs/repro"
probe_root="$work_root/probe/repro"

if [[ ! -f "$install_root/hdf5/lib/libhdf5.a" ]]; then
  install_root="$work_root/install"
  build_root="$work_root/build"
  log_root="$work_root/logs"
fi

if [[ ! -x "$emscripten_root/emcc" || ! -x "$node_path" ]]; then
  echo "Missing the task-local .NET 10.0.12 / Emscripten 3.1.56 toolchain." >&2
  exit 2
fi

if [[ ! -f "$install_root/hdf5/include/hdf5.h" ]]; then
  echo "Pinned HDF5 install not found; run build-hdf5-wasm.sh first." >&2
  exit 2
fi

export DOTNET_ROOT="$toolchain_root/dotnet"
export DOTNET_CLI_HOME="$toolchain_root/cli-home"
export EMSDK_PATH="$emsdk_root"
export DOTNET_EMSCRIPTEN_LLVM_ROOT="$emsdk_root/bin"
export DOTNET_EMSCRIPTEN_NODE_JS="$node_path"
export DOTNET_EMSCRIPTEN_BINARYEN_ROOT="$emsdk_root"
export EM_CONFIG="$emscripten_root/.emscripten"
export EM_CACHE="$work_root/emcache"
export FROZEN_CACHE=
export PATH="$emscripten_root:$emsdk_root/bin:$node_root:$PATH"

mkdir -p "$probe_root" "$log_root"

if ! "$emscripten_root/emcc" -pthread -O2 \
  -I"$install_root/hdf5/include" \
  -I"$install_root/libaec/include" \
  -I"$install_root/zlib/include" \
  "$script_dir/test/hdf-smoke.c" \
  "$install_root/hdf5/lib/libhdf5_hl.a" \
  "$install_root/hdf5/lib/libhdf5.a" \
  "$install_root/libaec/lib/libsz.a" \
  "$install_root/libaec/lib/libaec.a" \
  "$install_root/zlib/lib/libz.a" \
  -lm -pthread \
  -sPTHREAD_POOL_SIZE=4 -sPTHREAD_POOL_SIZE_STRICT=2 \
  -sEXIT_RUNTIME=1 \
  -o "$probe_root/hdf-smoke.js" \
  > "$log_root/hdf5-c-smoke-build.log" 2>&1; then
  tail -n 100 "$log_root/hdf5-c-smoke-build.log" >&2
  exit 1
fi

if ! "$node_path" "$probe_root/hdf-smoke.js" > "$log_root/hdf5-c-smoke-run.log" 2>&1; then
  cat "$log_root/hdf5-c-smoke-run.log" >&2
  exit 1
fi
cat "$log_root/hdf5-c-smoke-run.log"
