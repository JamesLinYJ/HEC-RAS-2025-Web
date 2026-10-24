using System;
using System.IO;
using System.Runtime.InteropServices;
using OSGeo.GDAL;
using Utility.Memory;

namespace Geospatial.GDALAssist;

public class VirtualFile : IDisposable
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private unsafe delegate nuint WriteNative(void* bytes, nuint size, nuint count, nint file);

	private static readonly Lazy<WriteNative> _writeNative = new Lazy<WriteNative>(LoadWriter);

	private static WriteNative LoadWriter()
	{
		string nativeDirectory = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
		string pattern = OperatingSystem.IsMacOS() ? "libgdal.*.dylib" : OperatingSystem.IsWindows() ? "gdal*.dll" : "libgdal.so*";
		foreach (string directory in new[] { nativeDirectory, AppContext.BaseDirectory })
		{
			if (!Directory.Exists(directory)) continue;
			foreach (string filename in Directory.EnumerateFiles(directory, pattern))
			{
				if (!NativeLibrary.TryLoad(filename, out nint library)) continue;
				if (NativeLibrary.TryGetExport(library, "VSIFWriteL", out nint writer))
				{
					return Marshal.GetDelegateForFunctionPointer<WriteNative>(writer);
				}
				NativeLibrary.Free(library);
			}
		}
		throw new DllNotFoundException("The GDAL runtime does not provide VSIFWriteL for binary virtual-file writes.");
	}
	public static NoHandlePool<string> VirtualFileNamePool = new NoHandlePool<string>(() => "/vsimem/" + Guid.NewGuid());

	private bool _isDisposed;

	public nint NativeHandle;

	public string VirtualFilename;

	private VirtualFile(string vfn, nint handle)
	{
		VirtualFilename = vfn;
		NativeHandle = handle;
	}

	public static VirtualFile CreateInMemory()
	{
		string newGuidGuidTerm = "/vsimem/" + Guid.NewGuid();
		nint vsifOpenExL = Gdal.VSIFOpenExL(newGuidGuidTerm, "w", 0, null);
		if (vsifOpenExL == 0)
		{
			throw new Exception();
		}
		return new VirtualFile(newGuidGuidTerm, vsifOpenExL);
	}

	public unsafe void WriteBytes(Memory<byte> data)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (data.IsEmpty) return;
		using var memoryHandle = data.Pin();
		if (_writeNative.Value(memoryHandle.Pointer, 1, (nuint)data.Length, NativeHandle) != (nuint)data.Length)
		{
			throw new IOException("GDAL could not write the complete virtual-file buffer.");
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			Gdal.VSIFCloseL(NativeHandle);
			Gdal.Unlink(VirtualFilename);
			VirtualFileNamePool.Return(VirtualFilename);
			_isDisposed = true;
		}
	}

	~VirtualFile()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
