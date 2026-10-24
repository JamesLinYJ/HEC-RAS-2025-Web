using System.Runtime.InteropServices;
using System.Security;

namespace Utility.Math.x86;

[SuppressUnmanagedCodeSecurity]
internal sealed class PardisoInitNative
{
	private PardisoInitNative()
	{
	}

	static PardisoInitNative()
	{
		NativeLoader.EnsureLoaded();
	}

	[DllImport("RasNativeParallel.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
	internal static extern void PARDISOINIT(long[] handle, ref int mtype, int[] iparm);
}
