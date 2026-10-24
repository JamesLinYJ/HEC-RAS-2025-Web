using System;
using System.Runtime.InteropServices;

namespace Utility.Math.x86;

public static class NativeUtilities
{
	public static GCHandle[] PinArrays(params object[] parameters)
	{
		GCHandle[] gcHandles = new GCHandle[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			if (parameters[i] is Array localValue)
			{
				gcHandles[i] = GCHandle.Alloc(localValue, GCHandleType.Pinned);
			}
		}
		return gcHandles;
	}

	public static void UnpinArrays(GCHandle[] handles)
	{
		for (int i = 0; i < handles.Length; i++)
		{
			GCHandle gCHandle = handles[i];
			if (gCHandle.IsAllocated)
			{
				gCHandle.Free();
			}
		}
	}
}
