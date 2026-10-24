using System.Runtime.InteropServices;
using System.Security;

namespace Utility.Math.x86;

[SuppressUnmanagedCodeSecurity]
internal sealed class DCSRILU0Native
{
	private DCSRILU0Native()
	{
	}

	static DCSRILU0Native()
	{
		NativeLoader.EnsureLoaded();
	}

	[DllImport("RasNativeParallel.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
	internal static extern void DCSRILU0(ref int n, double[] a, int[] ia, int[] ja, double[] bilu0, int[] iparm, double[] dparm, ref int error);
}
