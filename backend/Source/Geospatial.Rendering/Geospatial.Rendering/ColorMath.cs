using System;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace Geospatial.Rendering;

public static class ColorMath
{
	public static Color HSLtoRGB(float h, float s, float l)
	{
		float computed;
		float computed2;
		float computed3;
		if (s == 0f)
		{
			computed = (computed2 = (computed3 = l));
		}
		else
		{
			ComputePQ(s, l, out var p, out var q);
			computed = PQTtoRGB(p, q, h + 1f / 3f);
			computed2 = PQTtoRGB(p, q, h);
			computed3 = PQTtoRGB(p, q, h - 1f / 3f);
		}
		return Color.FromArgb((int)(computed * 255f), (int)(computed2 * 255f), (int)(computed3 * 255f));
	}

	public static Color PQHtoRGB(float p, float q, float h)
	{
		float pqTtoRGB = PQTtoRGB(p, q, h + 1f / 3f);
		float pqTtoRGB2 = PQTtoRGB(p, q, h);
		float pqTtoRGB3 = PQTtoRGB(p, q, h - 1f / 3f);
		return Color.FromArgb((int)(pqTtoRGB * 255f), (int)(pqTtoRGB2 * 255f), (int)(pqTtoRGB3 * 255f));
	}

	public static float PQTtoRGB(float p, float q, float t)
	{
		if (t < 0f)
		{
			t++;
		}
		if (t > 1f)
		{
			t--;
		}
		if (t < 1f / 6f)
		{
			return p + (q - p) * 6f * t;
		}
		if (t < 0.5f)
		{
			return q;
		}
		if (t < 2f / 3f)
		{
			return p + (q - p) * (2f / 3f - t) * 6f;
		}
		return p;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ComputePQ(float s, float l, out float p, out float q)
	{
		q = ((l < 0.5f) ? (l * (1f + s)) : (l + s - l * s));
		p = 2f * l - q;
	}

	public static void RGBtoHSL(byte r, byte g, byte b, out float h, out float s, out float l)
	{
		float computed = (float)(int)r / 255f;
		float computed2 = (float)(int)g / 255f;
		float computed3 = (float)(int)b / 255f;
		float computed4 = Math.Max(computed3, Math.Max(computed, computed2));
		float computed5 = Math.Min(computed3, Math.Min(computed, computed2));
		float computed6 = computed4 - computed5;
		if (computed6 == 0f)
		{
			h = 0f;
		}
		else if (computed4 == computed)
		{
			h = (computed2 - computed3) / computed6 % 6f;
		}
		else if (computed4 == computed2)
		{
			h = (computed3 - computed) / computed6 + 2f;
		}
		else
		{
			h = (computed - computed2) / computed6 + 4f;
		}
		h = (float)Math.Round(h * 60f);
		if (h < 0f)
		{
			h += 360f;
		}
		h /= 360f;
		l = (computed4 + computed5) / 2f;
		if (computed6 == 0f)
		{
			s = 0f;
		}
		else
		{
			s = computed6 / (1f - Math.Abs(2f * l - 1f));
		}
	}

	public static Color DesaturateColor(Color clr, float ratio)
	{
		float clrTerm = (float)(int)clr.R / 255f;
		float clrTerm2 = (float)(int)clr.G / 255f;
		float clrTerm3 = (float)(int)clr.B / 255f;
		float ratioOffset = 1f - ratio;
		float computed = 0.3f * clrTerm + 0.6f * clrTerm2 + 0.1f * clrTerm3;
		float computed2 = clrTerm + ratioOffset * (computed - clrTerm);
		float computed3 = clrTerm2 + ratioOffset * (computed - clrTerm2);
		float computed4 = clrTerm3 + ratioOffset * (computed - clrTerm3);
		return Color.FromArgb((byte)(computed2 * 255f), (byte)(computed3 * 255f), (byte)(computed4 * 255f));
	}

	public static Color GetDeterministicColor(int seed, float sat = 0.75f, float lum = 0.5f)
	{
		return HueToColor((float)((long)seed * 59L % 360) / 360f, sat, lum);
	}

	private static Color HueToColor(float h, float s = 0.75f, float l = 0.5f)
	{
		float hueToRGB = HueToRGB(h + 1f / 3f, s, l);
		float hueToRGB2 = HueToRGB(h, s, l);
		float hueToRGB3 = HueToRGB(h - 1f / 3f, s, l);
		return Color.FromArgb((int)(hueToRGB * 255f), (int)(hueToRGB2 * 255f), (int)(hueToRGB3 * 255f));
	}

	private static float HueToRGB(float t, float s = 0.75f, float l = 0.5f)
	{
		float computed = l + s - l * s;
		float computed2 = 2f * l - computed;
		if (t < 0f)
		{
			t++;
		}
		if (t > 1f)
		{
			t--;
		}
		if (t < 1f / 6f)
		{
			return computed2 + (computed - computed2) * 6f * t;
		}
		if (t < 0.5f)
		{
			return computed;
		}
		if (t < 2f / 3f)
		{
			return computed2 + (computed - computed2) * (2f / 3f - t) * 6f;
		}
		return computed2;
	}

	private static byte LerpByte(byte a, byte b, double t)
	{
		double computed = b - a;
		return (byte)((double)(int)a + computed * t);
	}

	public static Color Lerp(Color colorA, Color colorB, double t)
	{
		byte red = LerpByte(colorA.R, colorB.R, t);
		byte green = LerpByte(colorA.G, colorB.G, t);
		byte blue = LerpByte(colorA.B, colorB.B, t);
		return Color.FromArgb(LerpByte(colorA.A, colorB.A, t), red, green, blue);
	}

	public static double Luminance(Color clr)
	{
		return 0.299 * (double)(int)clr.R + 0.587 * (double)(int)clr.G + 0.114 * (double)(int)clr.B;
	}

	public static int Chroma(Color clr)
	{
		return Math.Max(clr.R, Math.Max(clr.G, clr.B)) - Math.Min(clr.R, Math.Min(clr.G, clr.B));
	}
}
