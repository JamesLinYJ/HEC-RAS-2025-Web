using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Utility.Hashing;

public static class StableHash
{
	private const uint P1 = 2654435761u;

	private const uint P2 = 2246822519u;

	private const uint P3 = 3266489917u;

	private const uint P4 = 668265263u;

	private const uint P5 = 374761393u;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static uint RotL(uint v, int r)
	{
		return (v << r) | (v >> 32 - r);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int Round(int hash, int input)
	{
		return (int)RotL((uint)(hash + input * -2048144777), 13) * -1640531535;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int Avalanche(int hash)
	{
		int hashTerm = (hash ^ (hash >>> 15)) * -2048144777;
		int ordinal = (hashTerm ^ (hashTerm >>> 13)) * -1028477379;
		return ordinal ^ (ordinal >>> 16);
	}

	public static int Combine<T1>(T1 v1)
	{
		return CombineIntHashes(GetStableHash(v1));
	}

	public static int Combine<T1, T2>(T1 v1, T2 v2)
	{
		return CombineIntHashes(GetStableHash(v1), GetStableHash(v2));
	}

	public static int Combine<T1, T2, T3>(T1 v1, T2 v2, T3 v3)
	{
		return CombineIntHashes(GetStableHash(v1), GetStableHash(v2), GetStableHash(v3));
	}

	public static int Combine<T1, T2, T3, T4>(T1 v1, T2 v2, T3 v3, T4 v4)
	{
		return CombineIntHashes(GetStableHash(v1), GetStableHash(v2), GetStableHash(v3), GetStableHash(v4));
	}

	public static int Combine<T1, T2, T3, T4, T5>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5)
	{
		return CombineIntHashes(GetStableHash(v1), GetStableHash(v2), GetStableHash(v3), GetStableHash(v4), GetStableHash(v5));
	}

	public static int Combine<T1, T2, T3, T4, T5, T6>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6)
	{
		return CombineIntHashes(GetStableHash(v1), GetStableHash(v2), GetStableHash(v3), GetStableHash(v4), GetStableHash(v5), GetStableHash(v6));
	}

	public static int Combine<T1, T2, T3, T4, T5, T6, T7>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7)
	{
		return CombineIntHashes(GetStableHash(v1), GetStableHash(v2), GetStableHash(v3), GetStableHash(v4), GetStableHash(v5), GetStableHash(v6), GetStableHash(v7));
	}

	public static int Combine<T1, T2, T3, T4, T5, T6, T7, T8>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7, T8 v8)
	{
		return CombineIntHashes(GetStableHash(v1), GetStableHash(v2), GetStableHash(v3), GetStableHash(v4), GetStableHash(v5), GetStableHash(v6), GetStableHash(v7), GetStableHash(v8));
	}

	public static int CombineIntHashes(int h1)
	{
		return Avalanche(Round(374761397, h1));
	}

	public static int CombineIntHashes(int h1, int h2)
	{
		return Avalanche(Round(Round(374761401, h1), h2));
	}

	public static int CombineIntHashes(int h1, int h2, int h3)
	{
		return Avalanche(Round(Round(Round(374761405, h1), h2), h3));
	}

	public static int CombineIntHashes(int h1, int h2, int h3, int h4)
	{
		return Avalanche(Round(Round(Round(Round(374761409, h1), h2), h3), h4));
	}

	public static int CombineIntHashes(params int[] hashes)
	{
		if (hashes == null)
		{
			return 0;
		}
		uint hash = (uint)(374761393 + hashes.Length * 4);
		for (int i = 0; i < hashes.Length; i++)
		{
			hash = (uint)Round((int)hash, hashes[i]);
		}
		return Avalanche((int)hash);
	}

	private static int GetStableHash<T>(T localValue)
	{
		if (localValue == null)
		{
			return 0;
		}
		if (localValue is string s)
		{
			return HashStringOrdinal(s);
		}
		if (typeof(T).IsEnum)
		{
			return Convert.ToInt32(localValue);
		}
		return EqualityComparer<T>.Default.GetHashCode(localValue);
	}

	public static int HashStringOrdinal(string s)
	{
		uint hash = (uint)(374761393 + s.Length * 2);
		for (int i = 0; i < s.Length; i++)
		{
			hash = (uint)Round((int)hash, s[i]);
		}
		return Avalanche((int)hash);
	}
}
