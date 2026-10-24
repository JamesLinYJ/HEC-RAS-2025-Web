using System;

namespace Utility.VisualElements;

public interface IBitmap
{
	bool HasData { get; set; }

	void RenderColorBufferToBitmap(Memory<int> colorBuffer);

	void RenderColorBufferToBitmap(byte[] colorBuffer);

	void GetUnmanagedPointer(Action<nint> action);
}
