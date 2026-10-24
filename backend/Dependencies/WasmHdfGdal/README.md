# Browser WebAssembly HDF5 and GDAL checkpoint

This directory records the native geospatial dependency work for the shared
browser and Electron .NET WebAssembly backend. It deliberately contains only
the durable source lock and findings. Source archives, inspection programs,
and any future build outputs belong in the ignored task work directory.

## Current state

- The existing managed HDF binding remains `H5.Bindings 1.0.8-hecras.portable.3`.
  The project package manifest pins its local package SHA-256.
- HDF5 2.1.1 official source repository tag and immutable commit are pinned in
  `sources.lock.json`. Its GitHub codeload archive was downloaded and hashed.
- The matching HDF runtime NuGet currently supplies macOS dylibs only. No
  browser-WASM HDF static library or `NativeFileReference` package has been
  built in this checkpoint.
- The existing GDAL managed package is 3.13.3.557 and its project currently
  selects the macOS runtime and CLI packages. No GDAL WASM source dependency
  archive, static library, or runtime package has been built here.
- No C# HDF write/read round-trip or GDAL projection/GeoTIFF test has run under
  browser WebAssembly. Do not treat dependency restore or native macOS tests as
  WebAssembly evidence.

## Confirmed binding constraint

The managed HDF package is a P/Invoke binding, not an HDF implementation. A
metadata-only inspection of its shipped `H5.Bindings.dll` confirmed that
`HDF.PInvoke.H5T` has 89 static fields, including 47 `H5T_NATIVE_*_g` data
globals. Its type initializer makes 85 calls to `GetHid(name)`. That helper
uses `NativeLibrary.GetExport` to look up each HDF global in a dynamically
loaded library and reads the resulting address as an HDF identifier.

That dynamic data-symbol lookup cannot be assumed to work with a C static
archive linked into a browser WASM module. The next HDF step must provide and
verify a WASM-specific global-symbol bridge or equivalent static initialization
path while keeping the existing managed package and HDF identifiers. Merely
linking the HDF C functions into the module is insufficient.

The current .NET native package also enables HDF5 thread safety. A WASM build
must determine whether this configuration uses Emscripten pthreads and what
browser cross-origin-isolation headers it requires. Do not silently substitute
a non-thread-safe build without checking the existing backend's concurrency
contract.

## Next verification sequence

1. Configure the pinned HDF source with the exact Emscripten SDK shipped with
   .NET 10.0.12 and build the minimum compatible static HDF5 libraries,
   including the compression filters used by retained result files.
2. Implement the static H5T-global bridge and package the resulting `.a`
   files using `NativeFileReference`; keep C source out of product compile
   items.
3. Run the unchanged C# HDF code in a .NET browser-WASM test app: create a
   file, write representative typed/chunked datasets, reopen it, and compare
   dimensions and values. Include the solver's compression/filter usage.
4. Only after HDF has a real browser execution path, pin and build the official
   GDAL 3.13.3 source and its required dependencies. Then verify EPSG:4326 to
   EPSG:3857 and GeoTIFF create/reopen inside the same WASM runtime.

The checkpoint does not claim those steps have started or passed. Build sources
and logs remain under a separate HDF/GDAL build workspace outside this
product directory.
