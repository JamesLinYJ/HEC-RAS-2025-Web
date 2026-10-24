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
source_root="$work_root/source/official"
build_root="$work_root/build/repro"
install_root="$work_root/install/repro"
log_root="$work_root/logs/repro"

hdf5_sha="096bf04114fc539138a663cb16892e694741cdb678cf3e59655ea11831fc19a4"
zlib_sha="9a93b2b7dfdac77ceba5a558a580e74667dd6fede4585b91eefb60f03b72df23"
libaec_sha="cc7b93be9002e25a88b45d6b8d5f6120756cb5e1000613f91d803ce0beba24d9"
hdf5_commit="4fde4fd787744b6a19b12032f4dad0383fbe6fbd"

if [[ ! -x "$emscripten_root/emcc" || ! -x "$node_path" ]]; then
  echo "Missing the task-local .NET 10.0.12 / Emscripten 3.1.56 toolchain:" >&2
  echo "  $emscripten_root/emcc" >&2
  echo "  $node_path" >&2
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

mkdir -p "$work_root/source" "$source_root" "$build_root" "$install_root" "$log_root" "$EM_CACHE"

fetch_verified() {
  local filename="$1" url="$2" expected_sha="$3"
  local archive="$work_root/source/$filename"
  if [[ ! -f "$archive" ]]; then
    curl --fail --location --retry 3 --output "$archive.partial" "$url"
    mv "$archive.partial" "$archive"
  fi
  local actual_sha
  actual_sha="$(shasum -a 256 "$archive" | awk '{print $1}')"
  if [[ "$actual_sha" != "$expected_sha" ]]; then
    echo "SHA-256 mismatch for $archive" >&2
    echo "  expected: $expected_sha" >&2
    echo "  actual:   $actual_sha" >&2
    exit 3
  fi
  printf '%s\n' "$archive"
}

extract_once() {
  local archive="$1" directory="$2" expected_sha="$3"
  local marker="$directory/.verified-source-archive-sha256"
  if [[ -f "$marker" ]]; then
    if [[ "$(cat "$marker")" != "$expected_sha" ]]; then
      echo "Source marker does not match the pinned archive: $directory" >&2
      exit 4
    fi
    return
  fi
  if [[ -e "$directory" ]]; then
    echo "Refusing to reuse an unverified source directory: $directory" >&2
    exit 4
  fi
  mkdir -p "$directory"
  tar -xzf "$archive" --strip-components=1 -C "$directory"
  printf '%s\n' "$expected_sha" > "$marker"
}

run_logged() {
  local logfile="$1"
  shift
  printf 'Running: %s\n' "$*"
  if ! "$@" > "$logfile" 2>&1; then
    tail -n 100 "$logfile" >&2
    return 1
  fi
  tail -n 2 "$logfile"
}

HDF5_ARCHIVE="$(fetch_verified hdf5-2.1.1-4fde4fd.tar.gz \
  "https://github.com/HDFGroup/hdf5/archive/$hdf5_commit.tar.gz" "$hdf5_sha")"
ZLIB_ARCHIVE="$(fetch_verified zlib-1.3.1.tar.gz \
  "https://zlib.net/fossils/zlib-1.3.1.tar.gz" "$zlib_sha")"
LIBAEC_ARCHIVE="$(fetch_verified libaec-1.1.7.tar.gz \
  "https://github.com/MathisRosenhauer/libaec/releases/download/v1.1.7/libaec-1.1.7.tar.gz" "$libaec_sha")"

HDF5_SOURCE="$source_root/hdf5-2.1.1-$hdf5_commit"
ZLIB_SOURCE="$source_root/zlib-1.3.1"
LIBAEC_SOURCE="$source_root/libaec-1.1.7"
extract_once "$HDF5_ARCHIVE" "$HDF5_SOURCE" "$hdf5_sha"
extract_once "$ZLIB_ARCHIVE" "$ZLIB_SOURCE" "$zlib_sha"
extract_once "$LIBAEC_ARCHIVE" "$LIBAEC_SOURCE" "$libaec_sha"

ZLIB_PREFIX="$install_root/zlib"
LIBAEC_PREFIX="$install_root/libaec"
HDF5_PREFIX="$install_root/hdf5"

run_logged "$log_root/zlib-configure.log" "$emscripten_root/emcmake" cmake \
  -S "$ZLIB_SOURCE" -B "$build_root/zlib" \
  -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=OFF \
  -DZLIB_BUILD_EXAMPLES=OFF \
  -DCMAKE_VERBOSE_MAKEFILE=ON \
  -DCMAKE_INSTALL_PREFIX="$ZLIB_PREFIX"
run_logged "$log_root/zlib-build.log" cmake --build "$build_root/zlib" --parallel "${BUILD_JOBS:-2}" --verbose
run_logged "$log_root/zlib-install.log" cmake --install "$build_root/zlib"

run_logged "$log_root/libaec-configure.log" "$emscripten_root/emcmake" cmake \
  -S "$LIBAEC_SOURCE" -B "$build_root/libaec" \
  -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=OFF \
  -DBUILD_STATIC_LIBS=ON \
  -DBUILD_TESTING=OFF \
  -DAEC_FUZZING=OFF \
  -DCMAKE_VERBOSE_MAKEFILE=ON \
  -DCMAKE_INSTALL_PREFIX="$LIBAEC_PREFIX"
run_logged "$log_root/libaec-build.log" cmake --build "$build_root/libaec" --parallel "${BUILD_JOBS:-2}" --verbose
run_logged "$log_root/libaec-install.log" cmake --install "$build_root/libaec"

run_logged "$log_root/hdf5-configure.log" "$emscripten_root/emcmake" cmake \
  -S "$HDF5_SOURCE" -B "$build_root/hdf5" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_VERBOSE_MAKEFILE=ON \
  -DCMAKE_C_FLAGS=-pthread \
  -DCMAKE_CXX_FLAGS=-pthread \
  -DCMAKE_EXE_LINKER_FLAGS=-pthread \
  -DTHREADS_PREFER_PTHREAD_FLAG=ON \
  -DCMAKE_PREFIX_PATH="$ZLIB_PREFIX;$LIBAEC_PREFIX" \
  -DZLIB_ROOT="$ZLIB_PREFIX" \
  -DZLIB_LIBRARY="$ZLIB_PREFIX/lib/libz.a" \
  -DZLIB_INCLUDE_DIR="$ZLIB_PREFIX/include" \
  -DBUILD_SHARED_LIBS=OFF \
  -DBUILD_STATIC_LIBS=ON \
  -DBUILD_TESTING=OFF \
  -DHDF5_ALLOW_EXTERNAL_SUPPORT=NO \
  -DHDF5_ALLOW_UNSUPPORTED=ON \
  -DHDF5_BUILD_CPP_LIB=OFF \
  -DHDF5_BUILD_FORTRAN=OFF \
  -DHDF5_BUILD_HL_LIB=ON \
  -DHDF5_BUILD_TOOLS=OFF \
  -DHDF5_BUILD_UTILS=OFF \
  -DHDF5_BUILD_EXAMPLES=OFF \
  -DHDF5_ENABLE_CONCURRENCY=OFF \
  -DHDF5_ENABLE_PARALLEL=OFF \
  -DHDF5_ENABLE_SZIP_ENCODING=ON \
  -DHDF5_ENABLE_SZIP_SUPPORT=ON \
  -DHDF5_ENABLE_THREADSAFE=ON \
  -DHDF5_ENABLE_ZLIB_SUPPORT=ON \
  -DHDF5_USE_LIBAEC_STATIC=ON \
  -DHDF5_DEFAULT_API_VERSION=v200 \
  -DCMAKE_INSTALL_PREFIX="$HDF5_PREFIX"
run_logged "$log_root/hdf5-build.log" cmake --build "$build_root/hdf5" --parallel "${BUILD_JOBS:-2}" --verbose
run_logged "$log_root/hdf5-install.log" cmake --install "$build_root/hdf5"

if [[ ! -f "$HDF5_PREFIX/lib/libhdf5.a" || ! -f "$HDF5_PREFIX/lib/libhdf5_hl.a" ]]; then
  echo "HDF5 core/high-level static archives are missing after installation." >&2
  exit 5
fi
if ! rg -q '^# ?define H5_HAVE_THREADSAFE 1$' "$HDF5_PREFIX/include/H5pubconf.h"; then
  echo "The generated public HDF5 configuration does not report thread safety." >&2
  exit 5
fi
if ! rg -q '^#define H5_SIZEOF_SIZE_T 4$' "$HDF5_PREFIX/include/H5pubconf.h"; then
  echo "The generated wasm32 public configuration does not report 4-byte size_t." >&2
  exit 5
fi

rg 'warning:' "$log_root/hdf5-build.log" > "$log_root/hdf5-warnings.txt" || true
printf 'Pinned HDF5 thread-safe wasm archives installed under %s\n' "$HDF5_PREFIX"
printf 'Full compiler output: %s/hdf5-build.log\n' "$log_root"
printf 'Compiler warning lines captured: %s\n' "$(wc -l < "$log_root/hdf5-warnings.txt" | tr -d ' ')"
