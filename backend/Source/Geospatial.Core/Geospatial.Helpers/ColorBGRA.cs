using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Geospatial.Helpers;

[StructLayout(LayoutKind.Explicit)]
public struct ColorBGRA
{
	[FieldOffset(0)]
	public int BGRA;

	[FieldOffset(0)]
	public byte B;

	[FieldOffset(1)]
	public byte G;

	[FieldOffset(2)]
	public byte R;

	[FieldOffset(3)]
	public byte A;

	public ColorBGRA BlendOver(ColorBGRA belowColor, double thisColorPercent)
	{
		return new ColorBGRA
		{
			A = (byte)((double)(int)A * thisColorPercent + (double)(int)belowColor.A * (1.0 - thisColorPercent)),
			R = (byte)((double)(int)R * thisColorPercent + (double)(int)belowColor.R * (1.0 - thisColorPercent)),
			G = (byte)((double)(int)G * thisColorPercent + (double)(int)belowColor.G * (1.0 - thisColorPercent)),
			B = (byte)((double)(int)B * thisColorPercent + (double)(int)belowColor.B * (1.0 - thisColorPercent))
		};
	}

	public ColorBGRA BlendOver(ColorBGRA b)
	{
		float computed = (float)(int)A / 255f;
		float computed2 = (float)(int)R / 255f;
		float computed3 = (float)(int)G / 255f;
		float computed4 = (float)(int)B / 255f;
		float computed5 = (float)(int)b.A / 255f;
		float computed6 = (float)(int)b.R / 255f;
		float computed7 = (float)(int)b.G / 255f;
		float computed8 = (float)(int)b.B / 255f;
		float computed9 = 1f - (1f - computed) * (1f - computed5);
		float computed10 = computed2 * computed / computed9 + computed6 * computed5 * (1f - computed) / computed9;
		float computed11 = computed3 * computed / computed9 + computed7 * computed5 * (1f - computed) / computed9;
		float computed12 = computed4 * computed / computed9 + computed8 * computed5 * (1f - computed) / computed9;
		return new ColorBGRA
		{
			A = (byte)(computed9 * 255f),
			R = (byte)(computed10 * 255f),
			G = (byte)(computed11 * 255f),
			B = (byte)(computed12 * 255f)
		};
	}

	public ColorBGRA MultiplyAlpha()
	{
		return new ColorBGRA
		{
			A = A,
			R = (byte)(R * A / 255),
			G = (byte)(G * A / 255),
			B = (byte)(B * A / 255)
		};
	}

	public ColorBGRA MultiplyAlpha_Fast()
	{
		uint a = A;
		switch (a)
		{
		case 255u:
			return this;
		case 0u:
			return new ColorBGRA
			{
				A = 0,
				R = 0,
				G = 0,
				B = 0
			};
		default:
		{
			uint ordinal = R * a;
			uint ordinal2 = G * a;
			uint ordinal3 = B * a;
			return new ColorBGRA
			{
				A = (byte)a,
				R = (byte)(ordinal + 1 + (ordinal >> 8) >> 8),
				G = (byte)(ordinal2 + 1 + (ordinal2 >> 8) >> 8),
				B = (byte)(ordinal3 + 1 + (ordinal3 >> 8) >> 8)
			};
		}
		}
	}

	public static ColorBGRA FromInt(int argb)
	{
		return new ColorBGRA
		{
			A = (byte)(argb >> 24),
			R = (byte)(argb >> 16),
			G = (byte)(argb >> 8),
			B = (byte)argb
		};
	}

	public override bool Equals([NotNullWhen(true)] object obj)
	{
		if (!(obj is ColorBGRA c))
		{
			return false;
		}
		return Equals(c);
	}

	public bool Equals(ColorBGRA c)
	{
		return BGRA == c.BGRA;
	}

	public override int GetHashCode()
	{
		return BGRA;
	}

	public static Span<ColorBGRA> From(Span<int> argb)
	{
		return MemoryMarshal.Cast<int, ColorBGRA>(argb);
	}

	public static Span<int> To(Span<ColorBGRA> argb)
	{
		return MemoryMarshal.Cast<ColorBGRA, int>(argb);
	}

	public static implicit operator int(ColorBGRA color)
	{
		return color.BGRA;
	}

	public static implicit operator ColorBGRA(int color)
	{
		return FromInt(color);
	}
}
