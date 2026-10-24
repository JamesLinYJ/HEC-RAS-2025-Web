using System.Runtime.InteropServices;
using System.Security;

namespace Utility.Math.x86;

[SuppressUnmanagedCodeSecurity]
internal sealed class PardisoNative
{
	private PardisoNative()
	{
	}

	static PardisoNative()
	{
		NativeLoader.EnsureLoaded();
	}

	[DllImport("RasNativeParallel.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
	internal static extern int PARDISO(long[] handle, ref int maxfct, ref int mnum, ref int mtype, ref int phase, ref int n, double[] a, int[] ia, int[] ja, int[] perm, ref int nrhs, int[] iparm, ref int msglvl, double[] b, double[] x, ref int error);
}
