using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using Utility;

namespace Geospatial.Rendering;

public class ColorMap<T> where T : unmanaged, IEquatable<T>, IBinaryInteger<T>
{
	private const float DefaultSaturation = 0.75f;

	private const float DefaultLuminance = 0.5f;

	private byte _alpha = byte.MaxValue;

	private readonly Dictionary<T, Color> _colorMap;

	private static readonly Func<T, int> ConvertToInt = GetConverter();

	private readonly float _saturation = 0.75f;

	private readonly float _luminance = 0.5f;

	private readonly float _p;

	private readonly float _q;

	private readonly bool _isDefault;

	private readonly T _noDataValue;

	public const int NoDataColorInt = 0;

	public static readonly Color NoDataColor = Color.FromArgb(0, 0, 0, 0);

	public int Count => _colorMap.Count;

	public byte Alpha
	{
		get
		{
			return _alpha;
		}
		set
		{
			_alpha = value;
		}
	}

	public ColorMap(T noDataValue)
	{
		_noDataValue = noDataValue;
		_colorMap = new Dictionary<T, Color>();
		_colorMap[noDataValue] = NoDataColor;
		_isDefault = true;
	}

	public ColorMap(T noDataValue, float defaultSat = -1f, float defaultLum = -1f, byte defaultOpacity = byte.MaxValue)
	{
		_noDataValue = noDataValue;
		_colorMap = new Dictionary<T, Color>();
		_colorMap[noDataValue] = NoDataColor;
		if (defaultLum < 0f)
		{
			defaultLum = 0.5f;
		}
		if (defaultSat < 0f)
		{
			defaultSat = 1f;
		}
		Alpha = defaultOpacity;
		_saturation = defaultSat;
		_luminance = defaultLum;
		ColorMath.ComputePQ(_saturation, _luminance, out _p, out _q);
		_isDefault = false;
	}

	private Color GetColorInternal(T key)
	{
		if (!_colorMap.TryGetValue(key, out var colorInternal))
		{
			colorInternal = GetDeterministicColor(key);
		}
		if (_alpha != byte.MaxValue && key != _noDataValue)
		{
			colorInternal = Color.FromArgb(_alpha, colorInternal);
		}
		return colorInternal;
	}

	public void ParallelPaint(Memory<T> values, Memory<int> outputColorBuf, bool setNoDataPixels = true)
	{
		if (setNoDataPixels)
		{
			Parallel.SmartFor(values.Length, (int start, int finish) =>
			{
				Span<T> buffer = values.Span;
				Span<int> outputColorBufSpan = outputColorBuf.Span;
				T localValue = buffer[start];
				int toArgb = GetColorInternal(localValue).ToArgb();
				for (int i = start; i <= finish; i++)
				{
					T localValue2 = buffer[i];
					if (localValue2.Equals(localValue))
					{
						outputColorBufSpan[i] = toArgb;
					}
					else
					{
						int toArgb2 = GetColorInternal(localValue2).ToArgb();
						outputColorBufSpan[i] = toArgb2;
						localValue = localValue2;
						toArgb = toArgb2;
					}
				}
			});
			return;
		}
		Parallel.SmartFor(values.Length, (int start, int finish) =>
		{
			Span<T> bufferSpan = values.Span;
			Span<int> outputColorBuf2 = outputColorBuf.Span;
			T localValue3 = bufferSpan[start];
			int toArgb3 = GetColorInternal(localValue3).ToArgb();
			for (int i = start; i <= finish; i++)
			{
				T localValue4 = bufferSpan[i];
				if (!localValue4.Equals(_noDataValue))
				{
					if (localValue4.Equals(localValue3))
					{
						outputColorBuf2[i] = toArgb3;
					}
					else
					{
						int toArgb4 = GetColorInternal(localValue4).ToArgb();
						outputColorBuf2[i] = toArgb4;
						localValue3 = localValue4;
						toArgb3 = toArgb4;
					}
				}
			}
		});
	}

	public void SetColor(T color2, Color color)
	{
		_colorMap[color2] = color;
	}

	private static Func<T, int> GetConverter()
	{
		if (typeof(T) == typeof(int))
		{
			return (T el) => Unsafe.As<T, int>(ref el);
		}
		if (typeof(T) == typeof(byte))
		{
			return (T el) => Unsafe.As<T, byte>(ref el);
		}
		if (typeof(T) == typeof(short))
		{
			return (T el) => Unsafe.As<T, short>(ref el);
		}
		if (typeof(T) == typeof(uint))
		{
			return (T el) => unchecked((int)Unsafe.As<T, uint>(ref el));
		}
		if (typeof(T) == typeof(long))
		{
			return (T el) => (int)Unsafe.As<T, long>(ref el);
		}
		throw new NotImplementedException();
	}

	private Color GetDeterministicColor(T localValue)
	{
		float h = (float)(((long)ConvertToInt(localValue) * 59L % 360 + 360) % 360) / 360f;
		if (_isDefault)
		{
			return HueToColor_FixedSL(h);
		}
		return ColorMath.PQHtoRGB(_p, _q, h);
	}

	public ColorMap<T> CloneSetSaturation(float newSaturation, float newLuminance)
	{
		ColorMap<T> colorMap = new ColorMap<T>(_noDataValue, newSaturation, newLuminance, Alpha);
		foreach (KeyValuePair<T, Color> colorMap2 in _colorMap)
		{
			T key = colorMap2.Key;
			Color color2 = colorMap2.Value;
			if (key == _noDataValue)
			{
				colorMap.SetColor(key, color2);
				continue;
			}
			ColorMath.RGBtoHSL(color2.R, color2.G, color2.B, out var h, out var _, out var l);
			Color color = ColorMath.HSLtoRGB(h, newSaturation, (newLuminance < 0f) ? l : newLuminance);
			colorMap.SetColor(key, color);
		}
		return colorMap;
	}

	public ColorMap<T> CloneSetOpacity(byte newOpacity)
	{
		ColorMap<T> colorMap = new ColorMap<T>(_noDataValue, -1f, -1f, newOpacity);
		foreach (KeyValuePair<T, Color> colorMap2 in _colorMap)
		{
			T key = colorMap2.Key;
			Color color = colorMap2.Value;
			if (key == _noDataValue)
			{
				colorMap.SetColor(key, color);
			}
			else
			{
				colorMap.SetColor(key, Color.FromArgb(newOpacity, color));
			}
		}
		return colorMap;
	}

	public ColorMap<T> CloneRatioSaturation(float saturationRatio, float luminanceRatio)
	{
		ColorMap<T> colorMap = new ColorMap<T>(_noDataValue, _saturation * saturationRatio, _luminance * luminanceRatio);
		foreach (KeyValuePair<T, Color> colorMap2 in _colorMap)
		{
			T key = colorMap2.Key;
			Color color2 = colorMap2.Value;
			if (key == _noDataValue)
			{
				colorMap.SetColor(key, color2);
				continue;
			}
			ColorMath.RGBtoHSL(color2.R, color2.G, color2.B, out var h, out var s, out var l);
			Color color = ColorMath.HSLtoRGB(h, saturationRatio * s, luminanceRatio * l);
			colorMap.SetColor(key, color);
		}
		return colorMap;
	}

	private static Color HueToColor_FixedSL(float h)
	{
		float hueToRGB_FixedSL = HueToRGB_FixedSL(h + 1f / 3f);
		float hueToRGB_FixedSL2 = HueToRGB_FixedSL(h);
		float hueToRGB_FixedSL3 = HueToRGB_FixedSL(h - 1f / 3f);
		return Color.FromArgb((int)(hueToRGB_FixedSL * 255f), (int)(hueToRGB_FixedSL2 * 255f), (int)(hueToRGB_FixedSL3 * 255f));
	}

	private static float HueToRGB_FixedSL(float t)
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
			return 0.125f + 4.5f * t;
		}
		if (t < 0.5f)
		{
			return 0.875f;
		}
		if (t < 2f / 3f)
		{
			return 0.125f + 0.75f * (2f / 3f - t) * 6f;
		}
		return 0.125f;
	}
}
