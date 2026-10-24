# HEC-DSS browser-WASM checkpoint

This directory pins the official HEC-DSS source and the repaired macOS build patch while the React frontend takes priority. It does **not** contain a usable browser-WASM DSS library or claim that the C# DSS path works in browser WASM yet.

## Source and managed contract

`hecdss-wasm-source-lock.json` records the upstream HEC-DSS v7-IT-2 commit and archive hash, the existing v7it2.2 patch and package hashes, and every managed `hecdss` import declared by `DssNative.cs`. The managed wrapper declares 31 imports: 29 required functions and two unused deprecated `ifltab` functions. The existing NuGet package is an `osx-arm64` dylib package and cannot serve the browser runtime.

The existing patch applies to the pinned official source and adds `zlegacyrawbuffers6.f90`, a 239-line Fortran 90 adapter set using `ISO_C_BINDING`, `C_LOC`, and `C_F_POINTER` for legacy DSS 6 typed-buffer calls. Preserve this repair and verify its callers and exported behavior against the DSS 6 reader/converter paths. The app must not silently lose DSS 6 support.

## Build constraint

The pinned HEC-DSS archive contains 357 `.f` sources; the existing repair patch adds the `.f90` adapters above, for 358 Fortran files in the repaired source tree. The installed host `gfortran` targets macOS ARM64; it cannot produce Emscripten WebAssembly objects. A macOS dylib also cannot be dynamically loaded by the browser runtime.

Netlib `f2c` plus `libf2c` is only a candidate for the upstream Fortran 77 sources. The added DSS 6 adapters use Fortran 90 `ISO_C_BINDING`, `C_LOC`, and `C_F_POINTER`; do not assume f2c can translate them. The host translator builds, and the pinned HEC-DSS patch applies, but full source coverage, compilation of the F90 adapters, Emscripten linking, all import resolutions, and DSS round-trip compatibility remain unverified. This is a feasibility checkpoint, not yet a build recipe ready for release.

The managed `DssRuntimeService` currently resolves `hecdss` with `NativeLibrary.Load` from a platform shared library. Browser WASM needs the statically linked native archive and matching P/Invoke table, so the runtime resolver must gain a browser-WASM path as part of integration.

## Next DSS gate

After the React shell is in place, complete the dependency build recipe in this directory. Convert the upstream Fortran 77 sources, compile the patch-added F90 adapters with a WASM-capable Fortran compiler or a behavior-equivalent C bridge, statically link the DSS C and Fortran code for `browser-wasm`, and verify that all 29 required imports resolve. Then run a real browser-WASM fixture that writes a DSS time-series record with Chinese text in its path/metadata, closes it, reopens it, and checks timestamps, values, units, type, catalog entry, and DSS 6 read/convert behavior against the native reference. Electron must run the same generated WASM module through the same frontend path.

Temporary upstream sources, the host-built `f2c`, downloaded Netlib archives, generated code, and logs stay under a separate DSS build workspace; none belong in product compile inputs.
