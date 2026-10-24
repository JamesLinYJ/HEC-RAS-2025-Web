using System.Runtime.InteropServices;
using System.Security;

namespace Utility.Math.x86;

[SuppressUnmanagedCodeSecurity]
internal sealed class DCSRILUTNative
{
	private DCSRILUTNative()
	{
	}

	static DCSRILUTNative()
	{
		NativeLoader.EnsureLoaded();
	}

	[DllImport("RasNativeParallel.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
	internal static extern void DCSRILUT(ref int n, double[] a, int[] ia, int[] ja, double[] bilut, int[] ibilut, int[] jbilut, ref double fillInTol, ref int maxFill, int[] iparm, double[] dparm, ref int error);
}
