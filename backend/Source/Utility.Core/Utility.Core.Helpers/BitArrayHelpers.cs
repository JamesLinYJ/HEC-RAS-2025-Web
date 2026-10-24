using System.Collections;

namespace Utility.Core.Helpers;

public static class BitArrayHelpers
{
	public static int GetCardinality(this BitArray bitArray)
	{
		int[] bitArrayArray = new int[(bitArray.Count >> 5) + 1];
		bitArray.CopyTo(bitArrayArray, 0);
		int cardinality = 0;
		bitArrayArray[^1] &= ~(-1 << bitArray.Count % 32);
		for (int i = 0; i < bitArrayArray.Length; i++)
		{
			int bitArrayArray2 = bitArrayArray[i];
			bitArrayArray2 -= (bitArrayArray2 >> 1) & 0x55555555;
			bitArrayArray2 = (bitArrayArray2 & 0x33333333) + ((bitArrayArray2 >> 2) & 0x33333333);
			bitArrayArray2 = ((bitArrayArray2 + (bitArrayArray2 >> 4)) & 0xF0F0F0F) * 16843009 >> 24;
			cardinality += bitArrayArray2;
		}
		return cardinality;
	}
}
