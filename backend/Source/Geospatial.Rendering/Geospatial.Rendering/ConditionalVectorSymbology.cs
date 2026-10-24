using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Geospatial.Rendering;

public class ConditionalVectorSymbology<T> : VectorSymbology, IConditionalVectorSymbology
{
	protected Func<T, int, Color?> _getFillColorOverride;

	private Func<T, int, int?> _getPointSizeOverride;

	private Func<T, int, double?> _getPenWidthOverride;

	private Func<T, int, Color?> _getPenColorOverride;

	public Func<T, int, Color?> GetFillColorOverride
	{
		get
		{
			return _getFillColorOverride;
		}
		set
		{
			if (_getFillColorOverride != value)
			{
				_getFillColorOverride = value;
				IncrementVersion();
			}
		}
	}

	public Func<T, int, int?> GetPointSizeOverride
	{
		get
		{
			return _getPointSizeOverride;
		}
		set
		{
			if (_getPointSizeOverride != value)
			{
				_getPointSizeOverride = value;
				IncrementVersion();
			}
		}
	}

	public Func<T, int, double?> GetPenWidthOverride
	{
		get
		{
			return _getPenWidthOverride;
		}
		set
		{
			if (_getPenWidthOverride != value)
			{
				_getPenWidthOverride = value;
				IncrementVersion();
			}
		}
	}

	public Func<T, int, Color?> GetPenColorOverride
	{
		get
		{
			return _getPenColorOverride;
		}
		set
		{
			if (_getPenColorOverride != value)
			{
				_getPenColorOverride = value;
				IncrementVersion();
			}
		}
	}

	public bool HasAnyOverrides
	{
		get
		{
			if (_getFillColorOverride == null && _getPointSizeOverride == null && _getPenWidthOverride == null)
			{
				return _getPenColorOverride != null;
			}
			return true;
		}
	}

	public virtual Color GetFillColorDynamic(T model, int featureId)
	{
		if (GetFillColorOverride == null)
		{
			return FillColor;
		}
		return GetFillColorOverride(model, featureId) ?? FillColor;
	}

	public int GetPointSizeDynamic(T model, int featureId)
	{
		if (GetPointSizeOverride == null)
		{
			return PointSize;
		}
		return GetPointSizeOverride(model, featureId) ?? PointSize;
	}

	public double GetPenWidthDynamic(T model, int featureId)
	{
		if (GetPenWidthOverride == null)
		{
			return PenWidth;
		}
		return GetPenWidthOverride(model, featureId) ?? PenWidth;
	}

	public Color GetPenColorDynamic(T model, int featureId)
	{
		if (GetPenColorOverride == null)
		{
			return PenColor;
		}
		return GetPenColorOverride(model, featureId) ?? PenColor;
	}

	public ConditionalVectorSymbology(Color penColor, double penWidth, Color fillColor)
		: base(penColor, penWidth, fillColor)
	{
	}

	public static ConditionalVectorSymbology DefaultConditionalSymbology()
	{
		return new ConditionalVectorSymbology(VectorSymbology.DefaultPenColor, 1.0, VectorSymbology.DefaultFillColor);
	}

	public static ConditionalVectorSymbology FromBase(VectorSymbology symb)
	{
		return new ConditionalVectorSymbology(symb.PenColor, symb.PenWidth, symb.FillColor)
		{
			PointSize = symb.PointSize
		};
	}

	public static ConditionalVectorSymbology<T> FromBaseT(VectorSymbology symb)
	{
		return new ConditionalVectorSymbology<T>(symb.PenColor, symb.PenWidth, symb.FillColor)
		{
			PointSize = symb.PointSize
		};
	}
}
public class ConditionalVectorSymbology : ConditionalVectorSymbology<ConditionalVectorSymbology.Unit>
{
	[StructLayout(LayoutKind.Sequential, Size = 1)]
	public readonly struct Unit
	{
		public static readonly Unit Value;
	}

	public new Func<int, Color?> GetFillColorOverride
	{
		set
		{
			base.GetFillColorOverride = (Unit _, int fid) => value?.Invoke(fid);
		}
	}

	public new Func<int, int?> GetPointSizeOverride
	{
		set
		{
			base.GetPointSizeOverride = (Unit _, int fid) => value?.Invoke(fid);
		}
	}

	public new Func<int, Color?> GetPenColorOverride
	{
		set
		{
			base.GetPenColorOverride = (Unit _, int fid) => value?.Invoke(fid);
		}
	}

	public new Func<int, double?> GetPenWidthOverride
	{
		set
		{
			base.GetPenWidthOverride = (Unit _, int fid) => value?.Invoke(fid);
		}
	}

	public ConditionalVectorSymbology(Color penColor, double penWidth, Color fillColor)
		: base(penColor, penWidth, fillColor)
	{
	}

	public Color GetFillColorDynamic(int featureId)
	{
		return GetFillColorDynamic(Unit.Value, featureId);
	}

	public int GetPointSizeDynamic(int featureId)
	{
		return GetPointSizeDynamic(Unit.Value, featureId);
	}

	public Color GetPenColorDynamic(int featureId)
	{
		return GetPenColorDynamic(Unit.Value, featureId);
	}

	public double GetPenWidthDynamic(int featureId)
	{
		return GetPenWidthDynamic(Unit.Value, featureId);
	}
}
