using System.Runtime.InteropServices;
using System.Security;

namespace Utility.Math.x86;

[SuppressUnmanagedCodeSecurity]
internal sealed class PCGGetNative
{
	private PCGGetNative()
	{
	}

	static PCGGetNative()
	{
		NativeLoader.EnsureLoaded();
	}

	[DllImport("RasNativeParallel.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
	internal static extern void DCG_GET(ref int n, double[] x, double[] b, ref int RCI_request, int[] iparm, double[] dparm, double[] tmp, ref int iterCount);
}
