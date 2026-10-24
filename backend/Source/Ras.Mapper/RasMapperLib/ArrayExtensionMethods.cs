using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class ArrayExtensionMethods
{
	[CompilerGenerated]
private sealed class _Closure_0024__2_002D0<_0024CLS0> where _0024CLS0 : struct
	{
		public _0024CLS0[] src;

		public int sz;

		public int tSize;

		public _Closure_0024__2_002D0(_Closure_0024__2_002D0<_0024CLS0> other)
		{
			if (other != null)
			{
				src = other.src;
				sz = other.sz;
				tSize = other.tSize;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int i)
		{
			checked
			{
				Buffer.BlockCopy(src, 0, src, i * sz * tSize, sz * tSize);
			}
		}
	}

	public static int IndexOf(this string[] src, string stringValue)
	{
		checked
		{
			int count = src.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(src[i], stringValue, TextCompare: false) == 0)
				{
					return i;
				}
			}
			return -1;
		}
	}

	public static void SetAll<T>(ref T[] src, T v, int count) where T : struct
	{
		if (src == null || src.Count() != count)
		{
			src = new T[checked(count - 1 + 1)];
		}
		SetAll(src, v);
	}

	public static void SetAll<T>(this T[] src, T v) where T : struct
	{
		_Closure_0024__2_002D0<T> capturedLocals = default;
		_Closure_0024__2_002D0<T> capturedLocals2 = new _Closure_0024__2_002D0<T>(capturedLocals);
		capturedLocals2.src = src;
		if (capturedLocals2.src == null)
		{
			return;
		}
		capturedLocals2.sz = 1024;
		checked
		{
			if (capturedLocals2.src.Length < capturedLocals2.sz)
			{
				int count = capturedLocals2.src.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					capturedLocals2.src[i] = v;
				}
				return;
			}
			int count2 = capturedLocals2.sz - 1;
			for (int j = 0; j <= count2; j++)
			{
				capturedLocals2.src[j] = v;
			}
			int lengthOverSz = unchecked(capturedLocals2.src.Length / capturedLocals2.sz);
			int capturedLocals3 = capturedLocals2.src.Length - lengthOverSz * capturedLocals2.sz;
			capturedLocals2.tSize = Marshal.SizeOf(capturedLocals2.src[0]);
			Parallel.For(0, lengthOverSz, [SpecialName] (int ordinal) =>
			{
				Buffer.BlockCopy(capturedLocals2.src, 0, capturedLocals2.src, ordinal * capturedLocals2.sz * capturedLocals2.tSize, capturedLocals2.sz * capturedLocals2.tSize);
			});
			if (capturedLocals3 > 0)
			{
				Buffer.BlockCopy(capturedLocals2.src, 0, capturedLocals2.src, lengthOverSz * capturedLocals2.sz * capturedLocals2.tSize, capturedLocals3 * capturedLocals2.tSize);
			}
		}
	}
}
