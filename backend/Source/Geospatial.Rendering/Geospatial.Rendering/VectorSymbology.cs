using System;
using System.Drawing;
using System.Threading;

namespace Geospatial.Rendering;

public class VectorSymbology : ISpatialVersion
{
	private struct HsvColor(double h, double s, double v)
	{
		public double H = h;

		public double S = s;

		public double V = v;
	}

	public const double DefaultPenWidth = 1.0;

	public static readonly Color DefaultPenColor = Color.Black;

	public static readonly Color DefaultFillColor = Color.FromArgb(128, 0, 0, 255);

	private static ThreadLocal<Random> RNGG = new ThreadLocal<Random>(() => new Random());

	private Color _penColor;

	private double _penWidth;

	private Color _fillColor;

	private int _pointSize = 3;

	private bool _hasShadow;

	private bool _isDotted;

	private float[] _dottedStrokeStyle;

	private bool _isTranslated;

	private float[] _translation;

	private Color _haloColor;

	private float _haloThickness;

	private string _font;

	private int _textEmphasis;

	public int _textMaxWidth;

	private float _textKerning;

	private BlendMode _blendMode;

	private long _version;

	public Color PenColor
	{
		get
		{
			return _penColor;
		}
		set
		{
			if (!(_penColor == value))
			{
				_penColor = value;
				IncrementVersion();
			}
		}
	}

	public double PenWidth
	{
		get
		{
			return _penWidth;
		}
		set
		{
			if (_penWidth != value)
			{
				_penWidth = value;
				IncrementVersion();
			}
		}
	}

	public Color FillColor
	{
		get
		{
			return _fillColor;
		}
		set
		{
			if (!(_fillColor == value))
			{
				_fillColor = value;
				IncrementVersion();
			}
		}
	}

	public int PointSize
	{
		get
		{
			return _pointSize;
		}
		set
		{
			if (_pointSize != value)
			{
				_pointSize = value;
				IncrementVersion();
			}
		}
	}

	public bool HasShadow
	{
		get
		{
			return _hasShadow;
		}
		set
		{
			if (_hasShadow != value)
			{
				_hasShadow = value;
				IncrementVersion();
			}
		}
	}

	public bool IsDotted
	{
		get
		{
			return _isDotted;
		}
		set
		{
			if (_isDotted != value)
			{
				_isDotted = value;
				IncrementVersion();
			}
		}
	}

	public float[] DottedStrokeStyle
	{
		get
		{
			return _dottedStrokeStyle;
		}
		set
		{
			if (_dottedStrokeStyle != value)
			{
				_dottedStrokeStyle = value;
				IncrementVersion();
			}
		}
	}

	public bool IsTranslated
	{
		get
		{
			return _isTranslated;
		}
		set
		{
			if (_isTranslated != value)
			{
				_isTranslated = value;
				IncrementVersion();
			}
		}
	}

	public float[] Translation
	{
		get
		{
			return _translation;
		}
		set
		{
			if (_translation != value)
			{
				_translation = value;
				IncrementVersion();
			}
		}
	}

	public Color HaloColor
	{
		get
		{
			return _haloColor;
		}
		set
		{
			if (!(_haloColor == value))
			{
				_haloColor = value;
				IncrementVersion();
			}
		}
	}

	public float HaloThickness
	{
		get
		{
			return _haloThickness;
		}
		set
		{
			if (_haloThickness != value)
			{
				_haloThickness = value;
				IncrementVersion();
			}
		}
	}

	public string Font
	{
		get
		{
			return _font;
		}
		set
		{
			if (!(_font == value))
			{
				_font = value;
				IncrementVersion();
			}
		}
	}

	public int TextEmphasis
	{
		get
		{
			return _textEmphasis;
		}
		set
		{
			if (_textEmphasis != value)
			{
				_textEmphasis = value;
				IncrementVersion();
			}
		}
	}

	public int TextMaxWidth
	{
		get
		{
			return _textMaxWidth;
		}
		set
		{
			if (_textMaxWidth != value)
			{
				_textMaxWidth = value;
				IncrementVersion();
			}
		}
	}

	public float TextKerning
	{
		get
		{
			return _textKerning;
		}
		set
		{
			if (_textKerning != value)
			{
				_textKerning = value;
				IncrementVersion();
			}
		}
	}

	public BlendMode BlendMode
	{
		get
		{
			return _blendMode;
		}
		set
		{
			if (_blendMode != value)
			{
				_blendMode = value;
				IncrementVersion();
			}
		}
	}

	public long VersionXY => _version;

	public VectorSymbology(Color solidColor, double penWidth)
		: this(solidColor, penWidth, solidColor)
	{
	}

	public VectorSymbology(Color penColor, double penWidth, Color fillColor)
	{
		PenColor = penColor;
		PenWidth = penWidth;
		FillColor = fillColor;
	}

	public VectorSymbology Clone()
	{
		return new VectorSymbology(PenColor, PenWidth, FillColor)
		{
			PointSize = PointSize,
			HasShadow = HasShadow
		};
	}

	public VectorSymbology CloneDesaturated(float amnt)
	{
		return new VectorSymbology(ColorMath.DesaturateColor(PenColor, amnt), PenWidth, ColorMath.DesaturateColor(FillColor, amnt))
		{
			PointSize = PointSize,
			HasShadow = HasShadow
		};
	}

	public static VectorSymbology DefaultSymbology()
	{
		return new VectorSymbology(DefaultPenColor, 1.0, DefaultFillColor);
	}

	public VectorSymbology GetSelectedSymbology()
	{
		return new VectorSymbology(Color.FromArgb(255, 41, 126, 255), PenWidth, Color.FromArgb(128, 41, 126, 255));
	}

	public VectorSymbology GetHighlightSymbology()
	{
		Color penColor = Color.FromArgb(128, PenColor);
		Color fillColor = Color.FromArgb(128, FillColor);
		double penWidth = ((!(PenWidth <= 1.0)) ? (3.0 + (PenWidth - 1.0) * 1.5) : (PenWidth * 3.0));
		return new VectorSymbology(penColor, penWidth, fillColor);
	}

	public static VectorSymbology RandomizeColors()
	{
		return RandomizeColorsHSV();
	}

	private static VectorSymbology RandomizeColorsHSV()
	{
		Random random = RNGG.Value;
		Color penColor = ConvertHsvToRgb((random.NextDouble() + 0.618033988749895) % 1.0 * 360.0, 0.7, 0.85);
		Color fillColor = ConvertHsvToRgb((random.NextDouble() + 0.618033988749895) % 1.0 * 360.0, 0.7, 0.85);
		return new VectorSymbology(penColor, 1.0, fillColor);
	}

	private static VectorSymbology RandomizeColorsRGB()
	{
		Random random = RNGG.Value;
		byte b = (byte)random.Next(0, 256);
		byte b2 = (byte)random.Next(0, 256);
		byte b3 = (byte)random.Next(0, 256);
		if (b < 150 && b2 < 150 && b3 < 150)
		{
			switch (random.Next(0, 3))
			{
			case 0:
				b = (byte)random.Next(150, 256);
				break;
			case 1:
				b2 = (byte)random.Next(150, 256);
				break;
			default:
				b3 = (byte)random.Next(150, 256);
				break;
			}
		}
		Color penColor = Color.FromArgb(b, b2, b3);
		Color fillColor = Color.FromArgb((byte)((double)(int)penColor.R * 0.5), (byte)((double)(int)penColor.G * 0.5), (byte)((double)(int)penColor.B * 0.5));
		return new VectorSymbology(penColor, 1.0, fillColor);
	}

	private static Color ConvertHsvToRgb(double h, double s, double v)
	{
		double computed;
		double computed2;
		double computed3;
		if (Math.Abs(s) < 1E-08)
		{
			computed = v;
			computed2 = v;
			computed3 = v;
		}
		else
		{
			h = ((!(Math.Abs(h - 360.0) < 1E-08)) ? (h / 60.0) : 0.0);
			int ordinal = (int)Math.Truncate(h);
			double computed4 = h - (double)ordinal;
			double computed5 = v * (1.0 - s);
			double computed6 = v * (1.0 - s * computed4);
			double computed7 = v * (1.0 - s * (1.0 - computed4));
			switch (ordinal)
			{
			case 0:
				computed = v;
				computed2 = computed7;
				computed3 = computed5;
				break;
			case 1:
				computed = computed6;
				computed2 = v;
				computed3 = computed5;
				break;
			case 2:
				computed = computed5;
				computed2 = v;
				computed3 = computed7;
				break;
			case 3:
				computed = computed5;
				computed2 = computed6;
				computed3 = v;
				break;
			case 4:
				computed = computed7;
				computed2 = computed5;
				computed3 = v;
				break;
			default:
				computed = v;
				computed2 = computed5;
				computed3 = computed6;
				break;
			}
		}
		return Color.FromArgb((byte)(computed * 255.0), (byte)(computed2 * 255.0), (byte)(computed3 * 255.0));
	}

	private static HsvColor ConvertRgbToHsv(int r, int b, int g)
	{
		double computed = Math.Min(Math.Min(r, g), b);
		double computed2 = Math.Max(Math.Max(r, g), b);
		double computed3 = computed2 - computed;
		double computed4 = ((!(Math.Abs(computed2) < 1E-08)) ? (computed3 / computed2) : 0.0);
		double computed5 = 0.0;
		if (Math.Abs(computed4 - 0.0) < 1E-08)
		{
			computed5 = 0.0;
		}
		else
		{
			if (Math.Abs((double)r - computed2) < 1E-08)
			{
				computed5 = (double)(g - b) / computed3;
			}
			else if (Math.Abs((double)g - computed2) < 1E-08)
			{
				computed5 = 2.0 + (double)(b - r) / computed3;
			}
			else if (Math.Abs((double)b - computed2) < 1E-08)
			{
				computed5 = 4.0 + (double)(r - g) / computed3;
			}
			computed5 *= 60.0;
			if (computed5 < 0.0)
			{
				computed5 += 360.0;
			}
		}
		return new HsvColor
		{
			H = computed5,
			S = computed4,
			V = computed2 / 255.0
		};
	}

	public void FreezeOne()
	{
		throw new NotImplementedException();
	}

	public void ThawOne()
	{
		throw new NotImplementedException();
	}

	protected void IncrementVersion()
	{
		Interlocked.Increment(ref _version);
	}
}
