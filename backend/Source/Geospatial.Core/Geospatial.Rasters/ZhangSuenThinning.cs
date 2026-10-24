using System;
using System.Collections.Generic;

namespace Geospatial.Rasters;

public static class ZhangSuenThinning
{
	private static readonly bool[] Lut1;

	private static readonly bool[] Lut2;

	static ZhangSuenThinning()
	{
		Lut1 = new bool[256];
		Lut2 = new bool[256];
		for (int i = 0; i < 256; i++)
		{
			bool condition = (i & 1) != 0;
			bool condition2 = (i & 2) != 0;
			bool condition3 = (i & 4) != 0;
			bool condition4 = (i & 8) != 0;
			bool condition5 = (i & 0x10) != 0;
			bool condition6 = (i & 0x20) != 0;
			bool condition7 = (i & 0x40) != 0;
			bool condition8 = (i & 0x80) != 0;
			int ordinal = (condition ? 1 : 0) + (condition2 ? 1 : 0) + (condition3 ? 1 : 0) + (condition4 ? 1 : 0) + (condition5 ? 1 : 0) + (condition6 ? 1 : 0) + (condition7 ? 1 : 0) + (condition8 ? 1 : 0);
			int countTransitions = CountTransitions(condition, condition2, condition3, condition4, condition5, condition6, condition7, condition8);
			bool countTransitionsTerm = ordinal >= 2 && ordinal <= 6 && countTransitions == 1;
			Lut1[i] = countTransitionsTerm && !(condition & condition3 & condition5) && !(condition3 & condition5 & condition7);
			Lut2[i] = countTransitionsTerm && !(condition & condition3 & condition7) && !(condition & condition5 & condition7);
		}
	}

	private static int CountTransitions(bool p2, bool p3, bool p4, bool p5, bool p6, bool p7, bool p8, bool p9)
	{
		int ordinal = 0;
		if (!p2 & p3)
		{
			ordinal++;
		}
		if (!p3 & p4)
		{
			ordinal++;
		}
		if (!p4 & p5)
		{
			ordinal++;
		}
		if (!p5 & p6)
		{
			ordinal++;
		}
		if (!p6 & p7)
		{
			ordinal++;
		}
		if (!p7 & p8)
		{
			ordinal++;
		}
		if (!p8 & p9)
		{
			ordinal++;
		}
		if (!p9 & p2)
		{
			ordinal++;
		}
		return ordinal;
	}

	public static void Thin(Span<byte> data, int width, int height)
	{
		int ordinal = 0;
		for (int i = 1; i < height - 1; i++)
		{
			int widthTerm = i * width;
			for (int j = 1; j < width - 1; j++)
			{
				if (data[widthTerm + j] != 0)
				{
					ordinal++;
				}
			}
		}
		List<int> intList = new List<int>(ordinal);
		for (int k = 1; k < height - 1; k++)
		{
			int widthTerm2 = k * width;
			for (int l = 1; l < width - 1; l++)
			{
				if (data[widthTerm2 + l] != 0)
				{
					intList.Add(widthTerm2 + l);
				}
			}
		}
		List<int> flagged = new List<int>(ordinal);
		List<int> intListList = new List<int>(ordinal);
		bool condition = true;
		while (condition)
		{
			condition = false;
			bool subIteration = SubIteration(data, width, height, intList, Lut1, flagged, intListList);
			condition |= subIteration;
			if (subIteration)
			{
				List<int> intList2 = intListList;
				intListList = intList;
				intList = intList2;
				intListList.Clear();
			}
			bool subIteration2 = SubIteration(data, width, height, intList, Lut2, flagged, intListList);
			condition |= subIteration2;
			if (subIteration2)
			{
				List<int> intList3 = intListList;
				intListList = intList;
				intList = intList3;
				intListList.Clear();
			}
		}
	}

	private static bool SubIteration(Span<byte> data, int w, int h, List<int> pixels, bool[] lut, List<int> flagged, List<int> nextActive)
	{
		flagged.Clear();
		foreach (int pixel in pixels)
		{
			if (data[pixel] != 0)
			{
				byte b = (byte)(((data[pixel - w] != 0) ? 1u : 0u) | (uint)((data[pixel - w + 1] != 0) ? 2 : 0) | (uint)((data[pixel + 1] != 0) ? 4 : 0) | (uint)((data[pixel + w + 1] != 0) ? 8 : 0) | (uint)((data[pixel + w] != 0) ? 16 : 0) | (uint)((data[pixel + w - 1] != 0) ? 32 : 0) | (uint)((data[pixel - 1] != 0) ? 64 : 0) | (uint)((data[pixel - w - 1] != 0) ? 128 : 0));
				if (lut[b])
				{
					flagged.Add(pixel);
				}
			}
		}
		if (flagged.Count == 0)
		{
			return false;
		}
		foreach (int flagged2 in flagged)
		{
			data[flagged2] = 0;
		}
		nextActive.Clear();
		foreach (int pixel2 in pixels)
		{
			if (data[pixel2] != 0)
			{
				data[pixel2] = 2;
				nextActive.Add(pixel2);
			}
		}
		foreach (int flagged3 in flagged)
		{
			AddIfForeground(data, nextActive, flagged3 - w - 1, w, h);
			AddIfForeground(data, nextActive, flagged3 - w, w, h);
			AddIfForeground(data, nextActive, flagged3 - w + 1, w, h);
			AddIfForeground(data, nextActive, flagged3 - 1, w, h);
			AddIfForeground(data, nextActive, flagged3 + 1, w, h);
			AddIfForeground(data, nextActive, flagged3 + w - 1, w, h);
			AddIfForeground(data, nextActive, flagged3 + w, w, h);
			AddIfForeground(data, nextActive, flagged3 + w + 1, w, h);
		}
		foreach (int nextActive2 in nextActive)
		{
			data[nextActive2] = 1;
		}
		return true;
	}

	private static void AddIfForeground(Span<byte> data, List<int> intList, int idx, int w, int h)
	{
		int idxTerm = idx / w;
		int idxTerm2 = idx % w;
		if (idxTerm >= 1 && idxTerm < h - 1 && idxTerm2 >= 1 && idxTerm2 < w - 1 && data[idx] == 1)
		{
			data[idx] = 2;
			intList.Add(idx);
		}
	}
}
