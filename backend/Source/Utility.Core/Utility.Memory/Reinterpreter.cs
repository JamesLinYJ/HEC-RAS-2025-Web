using System;
using System.Runtime.InteropServices;

namespace Utility.Memory;

public class Reinterpreter : IDisposable
{
	[StructLayout(LayoutKind.Explicit)]
	private struct Union
	{
		[FieldOffset(0)]
		public byte[] ByteArray;

		[FieldOffset(0)]
		public float[] FloatArray;
	}

	private GCHandle _handle;

	private Union _union;

	private static readonly nuint BYTE_ARRAY;

	private static readonly nuint FLOAT_ARRAY;

	private static readonly int PTR_SIZE;

	public unsafe byte[] ByteArray
	{
		get
		{
			float* addrOfPinnedObject = (float*)_handle.AddrOfPinnedObject();
			byte* addrOfPinnedObject2 = (byte*)addrOfPinnedObject;
			nuint* addrOfPinnedObject2AbovePtR_SIZE = (nuint*)(addrOfPinnedObject2 - PTR_SIZE);
			nuint* nuintAddrOfPinnedObject2Term = (nuint*)(addrOfPinnedObject2 - 2 * PTR_SIZE);
			*addrOfPinnedObject2AbovePtR_SIZE = (nuint)(_union.FloatArray.Length * 4);
			*nuintAddrOfPinnedObject2Term = BYTE_ARRAY;
			return _union.ByteArray;
		}
	}

	unsafe static Reinterpreter()
	{
		PTR_SIZE = sizeof(nuint);
		byte[] byteValues = new byte[1];
		float[] floatValues = new float[1];
		fixed (byte* localValue = byteValues)
		{
			fixed (float* localValue2 = floatValues)
			{
				BYTE_ARRAY = *(nuint*)(localValue - 2 * PTR_SIZE);
				FLOAT_ARRAY = *(nuint*)((byte*)localValue2 - 2 * PTR_SIZE);
			}
		}
	}

	public Reinterpreter(float[] buf)
	{
		_union = default;
		_union.FloatArray = buf;
		_handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
	}

	public void Dispose()
	{
		_handle.Free();
	}
}
