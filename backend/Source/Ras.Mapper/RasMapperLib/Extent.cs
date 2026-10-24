using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class Extent : Feature
{
	public static readonly Extent NullExtent = new Extent(double.NaN, double.NaN, double.NaN, double.NaN);

	public static readonly Extent Infinite = new Extent(double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity);

	private const double PracticalMax = 1.7976931348623158E+307;

	public static readonly Extent PracticallyInfinite = new Extent(1.7976931348623158E+307, -1.7976931348623158E+307, 1.7976931348623158E+307, -1.7976931348623158E+307);

	private readonly double _maxX;

	private readonly double _minX;

	private readonly double _maxY;

	private readonly double _minY;

	private readonly double _maxZ;

	private readonly double _minZ;

	private readonly double _maxM;

	private readonly double _minM;

	public const double EffectivelyInfinite = 1E+256;

	public double Left => _minX;

	public double Right => _maxX;

	public double Top => _maxY;

	public double Bottom => _minY;

	public double Width => _maxX - _minX;

	public double Height => _maxY - _minY;

	public double DeltaX
	{
		get
		{
			if (double.IsNaN(_maxX) || double.IsNaN(_minX))
			{
				return 0.0;
			}
			return MaxX - MinX;
		}
	}

	public double DeltaY
	{
		get
		{
			if (double.IsNaN(_maxY) || double.IsNaN(_minY))
			{
				return 0.0;
			}
			return _maxY - _minY;
		}
	}

	public double DeltaZ
	{
		get
		{
			if (double.IsNaN(_maxZ) || double.IsNaN(_minZ))
			{
				return 0.0;
			}
			return _maxZ - _minZ;
		}
	}

	public double DeltaM
	{
		get
		{
			if (double.IsNaN(_maxM) || double.IsNaN(_minM))
			{
				return 0.0;
			}
			return _maxM - _minM;
		}
	}

	public double DeltaXY => System.Math.Pow(System.Math.Pow(DeltaX, 2.0) + System.Math.Pow(DeltaY, 2.0), 0.5);

	public PointM UpperLeft => new PointM(_minX, _maxY);

	public PointM UpperRight => new PointM(_maxX, _maxY);

	public PointM LowerLeft => new PointM(_minX, _minY);

	public PointM LowerRight => new PointM(_maxX, _minY);

	public PointM UpperMidside => new PointM((_minX + _maxX) * 0.5, _maxY);

	public PointM LeftMidside => new PointM(_minX, (_maxY + _minY) * 0.5);

	public PointM RightMidside => new PointM(_maxX, (_maxY + _minY) * 0.5);

	public PointM LowerMidside => new PointM((_minX + _maxX) * 0.5, _minY);

	public PointM Middle => new PointM((_minX + _maxX) * 0.5, (_maxY + _minY) * 0.5);

	public override bool UsesPointSymbol { get; }

	public override bool UsesPen { get; }

	public override bool UsesBrush { get; }

	public override bool UsesSurfaceFill { get; }

	public override int Count { get; }

	public override bool HasZ => !double.IsNaN(_maxZ);

	public override bool HasM => !double.IsNaN(_maxM);

	public override double MaxX => _maxX;

	public override double MinX => _minX;

	public override double MaxY => _maxY;

	public override double MinY => _minY;

	public override double MaxZ => _maxZ;

	public override double MinZ => _minZ;

	public override double MaxM => _maxM;

	public override double MinM => _minM;

	public override bool MaxMinXYDefined
	{
		get
		{
			if (double.IsNaN(_maxX) || double.IsNaN(_minX) || double.IsNaN(_maxY) || double.IsNaN(_minY))
			{
				return false;
			}
			return true;
		}
	}

	public double MaxDeltaXY => System.Math.Max(DeltaX, DeltaY);

	public override string SummaryText
	{
		get
		{
			string maxXMinXTerm = MaxX + "," + MinX + " ";
			maxXMinXTerm = maxXMinXTerm + MaxY + "," + MinY + " ";
			if (HasZ)
			{
				maxXMinXTerm = maxXMinXTerm + MaxZ + "," + MinZ + " ";
			}
			if (HasM)
			{
				maxXMinXTerm = maxXMinXTerm + MaxM + "," + MinM + " ";
			}
			return maxXMinXTerm;
		}
	}

	public Extent(IExtent ext)
		: this(ext.MaxX, ext.MinX, ext.MaxY, ext.MinY, ext.MaxZ, ext.MinZ, ext.MaxM, ext.MinM)
	{
	}

	public Extent(Point point)
		: this(point.X, point.X, point.Y, point.Y, point.Z, point.Z, point.M, point.M)
	{
	}

	public Extent(double maxX, double minX, double maxY, double minY)
		: this(maxX, minX, maxY, minY, double.NaN, double.NaN, double.NaN, double.NaN)
	{
	}

	public Extent(double maxX, double minX, double maxY, double minY, double maxZ, double minZ)
		: this(maxX, minX, maxY, minY, maxZ, minZ, double.NaN, double.NaN)
	{
	}

	public Extent(Point firstPoint, Point secondPoint)
		: this(firstPoint.PointM(), secondPoint.PointM())
	{
	}

	public Extent(PointM firstPoint, PointM secondPoint)
	{
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		_maxX = RasMapperLib.Utilities.Math.GetMaxValue(firstPoint.X, secondPoint.X, AllowMaxMinValues: true);
		_minX = RasMapperLib.Utilities.Math.GetMinValue(firstPoint.X, secondPoint.X, AllowMaxMinValues: true);
		_maxY = RasMapperLib.Utilities.Math.GetMaxValue(firstPoint.Y, secondPoint.Y, AllowMaxMinValues: true);
		_minY = RasMapperLib.Utilities.Math.GetMinValue(firstPoint.Y, secondPoint.Y, AllowMaxMinValues: true);
		_maxZ = RasMapperLib.Utilities.Math.GetMaxValue(firstPoint.Z, secondPoint.Z, AllowMaxMinValues: true);
		_minZ = RasMapperLib.Utilities.Math.GetMinValue(firstPoint.Z, secondPoint.Z, AllowMaxMinValues: true);
		_maxM = RasMapperLib.Utilities.Math.GetMaxValue(firstPoint.M, secondPoint.M, AllowMaxMinValues: true);
		_minM = RasMapperLib.Utilities.Math.GetMinValue(firstPoint.M, secondPoint.M, AllowMaxMinValues: true);
	}

	public Extent(PointM centerPoint, double wd, double ht)
		: this(centerPoint.X + wd * 0.5, centerPoint.X - wd * 0.5, centerPoint.Y + ht * 0.5, centerPoint.Y - ht * 0.5)
	{
	}

	public Extent(params IExtent[] extents)
	{
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		_maxX = double.MinValue;
		_minX = double.MaxValue;
		_maxY = double.MinValue;
		_minY = double.MaxValue;
		_maxZ = double.MinValue;
		_minZ = double.MaxValue;
		_maxM = double.MinValue;
		_minM = double.MaxValue;
		foreach (IExtent extent in extents)
		{
			if (extent != null)
			{
				_maxX = RasMapperLib.Utilities.Math.GetMaxValue(_maxX, extent.MaxX, AllowMaxMinValues: true);
				_minX = RasMapperLib.Utilities.Math.GetMinValue(_minX, extent.MinX, AllowMaxMinValues: true);
				_maxY = RasMapperLib.Utilities.Math.GetMaxValue(_maxY, extent.MaxY, AllowMaxMinValues: true);
				_minY = RasMapperLib.Utilities.Math.GetMinValue(_minY, extent.MinY, AllowMaxMinValues: true);
				_maxZ = RasMapperLib.Utilities.Math.GetMaxValue(_maxZ, extent.MaxZ, AllowMaxMinValues: true);
				_minZ = RasMapperLib.Utilities.Math.GetMinValue(_minZ, extent.MinZ, AllowMaxMinValues: true);
				_maxM = RasMapperLib.Utilities.Math.GetMaxValue(_maxM, extent.MaxM, AllowMaxMinValues: true);
				_minM = RasMapperLib.Utilities.Math.GetMinValue(_minM, extent.MinM, AllowMaxMinValues: true);
			}
		}
		if (_maxX == double.MinValue)
		{
			_maxX = double.NaN;
		}
		if (_minX == double.MaxValue)
		{
			_minX = double.NaN;
		}
		if (_maxY == double.MinValue)
		{
			_maxY = double.NaN;
		}
		if (_minY == double.MaxValue)
		{
			_minY = double.NaN;
		}
		if (_maxZ == double.MinValue)
		{
			_maxZ = double.NaN;
		}
		if (_minZ == double.MaxValue)
		{
			_minZ = double.NaN;
		}
		if (_maxM == double.MinValue)
		{
			_maxM = double.NaN;
		}
		if (_minM == double.MaxValue)
		{
			_minM = double.NaN;
		}
	}

	public Extent(IEnumerable<Point2Double> points)
	{
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		_maxX = double.MinValue;
		_minX = double.MaxValue;
		_maxY = double.MinValue;
		_minY = double.MaxValue;
		foreach (Point2Double point in points)
		{
			if (point.X > _maxX)
			{
				_maxX = point.X;
			}
			if (point.X < _minX)
			{
				_minX = point.X;
			}
			if (point.Y > _maxY)
			{
				_maxY = point.Y;
			}
			if (point.Y < _minY)
			{
				_minY = point.Y;
			}
		}
		_maxZ = double.NaN;
		_minZ = double.NaN;
		_maxM = double.NaN;
		_minM = double.NaN;
	}

	public Extent(IEnumerable<PointM> points)
	{
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		_maxX = double.MinValue;
		_minX = double.MaxValue;
		_maxY = double.MinValue;
		_minY = double.MaxValue;
		foreach (PointM point in points)
		{
			if (point.X > _maxX)
			{
				_maxX = point.X;
			}
			if (point.X < _minX)
			{
				_minX = point.X;
			}
			if (point.Y > _maxY)
			{
				_maxY = point.Y;
			}
			if (point.Y < _minY)
			{
				_minY = point.Y;
			}
		}
		_maxZ = double.NaN;
		_minZ = double.NaN;
		_maxM = double.NaN;
		_minM = double.NaN;
	}

	public Extent(Feature feature)
		: this(feature.MaxX, feature.MinX, feature.MaxY, feature.MinY, feature.MaxZ, feature.MinZ, feature.MaxM, feature.MinM)
	{
	}

	public Extent(XmlNode node)
	{
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		_maxX = XML.DoubleTextElement(node, "MaxX");
		_minX = XML.DoubleTextElement(node, "MinX");
		_maxY = XML.DoubleTextElement(node, "MaxY");
		_minY = XML.DoubleTextElement(node, "MinY");
		_maxZ = double.NaN;
		_maxZ = double.NaN;
		_maxM = double.NaN;
		_maxM = double.NaN;
	}

	public Extent(double maxX, double minX, double maxY, double minY, double maxZ, double minZ, double maxM, double minM)
	{
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		_maxX = maxX;
		_minX = minX;
		_maxY = maxY;
		_minY = minY;
		_maxZ = maxZ;
		_minZ = minZ;
		_maxM = maxM;
		_minM = minM;
	}

	public Extent(double[] a)
	{
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		_minX = System.Math.Min(a[0], a[1]);
		_maxX = System.Math.Max(a[0], a[1]);
		_minY = System.Math.Min(a[2], a[3]);
		_maxY = System.Math.Max(a[2], a[3]);
		_maxZ = double.NaN;
		_maxZ = double.NaN;
		_maxM = double.NaN;
		_maxM = double.NaN;
	}

	public static bool IsValid(Extent ext)
	{
		return !IsNullOrInfinite(ext);
	}

	public static bool IsNullOrInfinite(Extent ext)
	{
		if (ext == null)
		{
			return true;
		}
		if (ext.IsFinite())
		{
			return false;
		}
		return true;
	}

	public bool IsFinite()
	{
		if (double.IsNaN(MinX) || double.IsNaN(MaxX) || double.IsNaN(MinY) || double.IsNaN(MaxY) || double.IsNegativeInfinity(MinX) || double.IsPositiveInfinity(MaxX) || double.IsNegativeInfinity(MinY) || double.IsPositiveInfinity(MaxY) || MinX < -1E+256 || MaxX > 1E+256 || MinY < -1E+256 || MaxY > 1E+256)
		{
			return false;
		}
		return true;
	}

	public bool HasNullValues()
	{
		if (double.IsNaN(MinX) || double.IsNaN(MaxX) || double.IsNaN(MinY) || double.IsNaN(MaxY) || MinX == double.MaxValue || MinY == double.MaxValue || MaxX == double.MinValue || MaxY == double.MinValue)
		{
			return true;
		}
		return false;
	}

	public Extent EnforceMinDimensions(double width, double height)
	{
		Extent extent = new Extent(this);
		if (width > 0.0 && DeltaX < width)
		{
			PointM pointM = CenterPointM();
			extent = new Extent(pointM.X + width / 2.0, pointM.X - width / 2.0, extent.Top, extent.Bottom);
		}
		if (height > 0.0 && DeltaY < height)
		{
			PointM pointM2 = CenterPointM();
			extent = new Extent(extent.Right, extent.Left, pointM2.Y + height / 2.0, pointM2.Y - height / 2.0);
		}
		return extent;
	}

	public Extent EnforceMinWidth(double width)
	{
		return EnforceMinDimensions(width, 0.0);
	}

	public Extent EnforceMinHeight(double height)
	{
		return EnforceMinDimensions(0.0, height);
	}

	public Extent EnforceMinDXDYRatio(double ratio)
	{
		if (DeltaX > DeltaY)
		{
			double deltaXTimesRatio = DeltaX * ratio;
			if (DeltaY < deltaXTimesRatio)
			{
				return EnforceMinHeight(deltaXTimesRatio);
			}
		}
		else
		{
			double deltaYTimesRatio = DeltaY * ratio;
			if (DeltaX < deltaYTimesRatio)
			{
				return EnforceMinWidth(deltaYTimesRatio);
			}
		}
		return this;
	}

	public override void MD5HashAppend(Hash hash)
	{
		hash.Add(new double[8] { _minX, _maxX, _minY, _maxY, _minZ, _maxZ, _minM, _maxM });
	}

	public double[] MinXMaxXMinYMaxY()
	{
		return new double[4] { _minX, _maxX, _minY, _maxY };
	}

	public static string XMLNodeName()
	{
		return "Extent";
	}

	public void XMLSave(XmlDocument doc, XmlElement sourceElement)
	{
		XmlNode xmlNode = doc.CreateElement(XMLNodeName());
		sourceElement.AppendChild(xmlNode);
		double maxX = _maxX;
		XML.AddTextElementSanitized(doc, xmlNode, "MaxX", maxX.ToString());
		maxX = _minX;
		XML.AddTextElementSanitized(doc, xmlNode, "MinX", maxX.ToString());
		maxX = _maxY;
		XML.AddTextElementSanitized(doc, xmlNode, "MaxY", maxX.ToString());
		maxX = _minY;
		XML.AddTextElementSanitized(doc, xmlNode, "MinY", maxX.ToString());
		if (HasZ)
		{
			maxX = _maxZ;
			XML.AddTextElementSanitized(doc, xmlNode, "MaxZ", maxX.ToString());
			maxX = _minZ;
			XML.AddTextElementSanitized(doc, xmlNode, "MinZ", maxX.ToString());
		}
		if (HasM)
		{
			maxX = _maxM;
			XML.AddTextElementSanitized(doc, xmlNode, "MaxM", maxX.ToString());
			maxX = _minM;
			XML.AddTextElementSanitized(doc, xmlNode, "MinM", maxX.ToString());
		}
	}

	public void XMLSave(XmlDocument doc, XmlNode node)
	{
		double maxX = _maxX;
		XML.AddTextElementSanitized(doc, node, "MaxX", maxX.ToString());
		maxX = _minX;
		XML.AddTextElementSanitized(doc, node, "MinX", maxX.ToString());
		maxX = _maxY;
		XML.AddTextElementSanitized(doc, node, "MaxY", maxX.ToString());
		maxX = _minY;
		XML.AddTextElementSanitized(doc, node, "MinY", maxX.ToString());
	}

	public static Extent XMLLoad(XmlElement baseNode)
	{
		double maxX = XML.DoubleTextElement(baseNode, "MaxX");
		double minX = XML.DoubleTextElement(baseNode, "MinX");
		double maxY = XML.DoubleTextElement(baseNode, "MaxY");
		double minY = XML.DoubleTextElement(baseNode, "MinY");
		double maxZ = XML.DoubleTextElement(baseNode, "MaxZ");
		double minZ = XML.DoubleTextElement(baseNode, "MinZ");
		double maxM = XML.DoubleTextElement(baseNode, "MaxM");
		double minM = XML.DoubleTextElement(baseNode, "MinM");
		return new Extent(maxX, minX, maxY, minY, maxZ, minZ, maxM, minM);
	}

	public PointM RandomPointM(Random rand)
	{
		PointM result = default;
		result.X = _minX + DeltaX * rand.NextDouble();
		result.Y = _minY + DeltaY * rand.NextDouble();
		result.Z = double.NaN;
		result.M = double.NaN;
		return result;
	}

	public PointM PointAtFraction(double xFraction, double yFraction)
	{
		PointM result = default;
		result.X = _minX + DeltaX * xFraction;
		result.Y = _minY + DeltaY * yFraction;
		result.Z = double.NaN;
		result.M = double.NaN;
		return result;
	}

	public Extent CenterAtPoint(Point point)
	{
		if (point == null)
		{
			throw new ArgumentNullException("point");
		}
		return CenterAtPoint(point.PointM());
	}

	public Extent CenterAtPoint(PointM point)
	{
		double maxX = double.NaN;
		double minX = double.NaN;
		if (!double.IsNaN(_maxX) & !double.IsNaN(point.X))
		{
			maxX = point.X + DeltaX * 0.5;
			minX = point.X - DeltaX * 0.5;
		}
		double maxY = double.NaN;
		double minY = double.NaN;
		if (!double.IsNaN(_maxY) & !double.IsNaN(point.Y))
		{
			maxY = point.Y + DeltaY * 0.5;
			minY = point.Y - DeltaY * 0.5;
		}
		double maxZ = double.NaN;
		double minZ = double.NaN;
		if (!double.IsNaN(_maxZ) & !double.IsNaN(point.Z))
		{
			maxZ = point.Z + DeltaZ * 0.5;
			minZ = point.Z - DeltaZ * 0.5;
		}
		double maxM = double.NaN;
		double minM = double.NaN;
		if (!double.IsNaN(_maxM) & !double.IsNaN(point.M))
		{
			maxM = point.M + DeltaM * 0.5;
			minM = point.M - DeltaM * 0.5;
		}
		return new Extent(maxX, minX, maxY, minY, maxZ, minZ, maxM, minM);
	}

	public Point CenterPoint()
	{
		double x = double.NaN;
		double y = double.NaN;
		double z = double.NaN;
		double m = double.NaN;
		if (!double.IsNaN(_maxX))
		{
			x = (_maxX + _minX) * 0.5;
		}
		if (!double.IsNaN(_maxY))
		{
			y = (_maxY + _minY) * 0.5;
		}
		if (!double.IsNaN(_maxZ))
		{
			z = (_maxZ + _minZ) * 0.5;
		}
		if (!double.IsNaN(_maxM))
		{
			m = (_maxM + _minM) * 0.5;
		}
		return new Point(x, y, z, m);
	}

	public PointM CenterPointM()
	{
		PointM result = default;
		if (!double.IsNaN(_maxX))
		{
			result.X = (_maxX + _minX) * 0.5;
		}
		else
		{
			result.X = double.NaN;
		}
		if (!double.IsNaN(_maxY))
		{
			result.Y = (_maxY + _minY) * 0.5;
		}
		else
		{
			result.Y = double.NaN;
		}
		if (!double.IsNaN(_maxZ))
		{
			result.Z = (_maxZ + _minZ) * 0.5;
		}
		else
		{
			result.Z = double.NaN;
		}
		if (!double.IsNaN(_maxM))
		{
			result.M = (_maxM + _minM) * 0.5;
		}
		else
		{
			result.M = double.NaN;
		}
		return result;
	}

	public Extent Offset(double offsetX, double offsetY)
	{
		return Offset(offsetX, offsetY, 0.0, 0.0);
	}

	public Extent Offset(double offsetX, double offsetY, double offsetZ)
	{
		return Offset(offsetX, offsetY, offsetZ, 0.0);
	}

	public Extent Offset(double offsetX, double offsetY, double offsetZ, double offsetM)
	{
		double maxX = double.NaN;
		double minX = double.NaN;
		if (!double.IsNaN(_maxX))
		{
			maxX = _maxX + offsetX;
			minX = _minX - offsetX;
		}
		double maxY = double.NaN;
		double minY = double.NaN;
		if (!double.IsNaN(_maxY))
		{
			maxY = _maxY + offsetY;
			minY = _minY - offsetY;
		}
		double maxZ = double.NaN;
		double minZ = double.NaN;
		if (!double.IsNaN(_maxZ))
		{
			maxZ = _maxZ + offsetZ;
			minZ = _minZ - offsetZ;
		}
		double maxM = double.NaN;
		double minM = double.NaN;
		if (!double.IsNaN(_maxM))
		{
			maxM = _maxM + offsetM;
			minM = _minM - offsetM;
		}
		return new Extent(maxX, minX, maxY, minY, maxZ, minZ, maxM, minM);
	}

	public Extent Extend(double dist)
	{
		return new Extent(MaxX + dist, MinX - dist, MaxY + dist, MinY - dist);
	}

	public Extent Extend(double distLeft, double distRight, double distUp, double distDown)
	{
		return new Extent(MaxX + distRight, MinX - distLeft, MaxY + distUp, MinY - distDown);
	}

	public Extent Ratio(double fractionOfOriginal)
	{
		PointM pointM = CenterPointM();
		double maxX = double.NaN;
		double minX = double.NaN;
		if (!double.IsNaN(_maxX))
		{
			double deltaXTimesFractionOfOriginal = DeltaX * fractionOfOriginal;
			maxX = pointM.X + deltaXTimesFractionOfOriginal * 0.5;
			minX = pointM.X - deltaXTimesFractionOfOriginal * 0.5;
		}
		double maxY = double.NaN;
		double minY = double.NaN;
		if (!double.IsNaN(_maxY))
		{
			double deltaYTimesFractionOfOriginal = DeltaY * fractionOfOriginal;
			maxY = pointM.Y + deltaYTimesFractionOfOriginal * 0.5;
			minY = pointM.Y - deltaYTimesFractionOfOriginal * 0.5;
		}
		double maxZ = double.NaN;
		double minZ = double.NaN;
		if (!double.IsNaN(_maxZ))
		{
			_ = DeltaZ;
			maxZ = pointM.Z + DeltaZ * 0.5;
			minZ = pointM.Z - DeltaZ * 0.5;
		}
		double maxM = double.NaN;
		double minM = double.NaN;
		if (!double.IsNaN(_maxM))
		{
			double deltaMTimesFractionOfOriginal = DeltaM * fractionOfOriginal;
			maxM = pointM.M + deltaMTimesFractionOfOriginal * 0.5;
			minM = pointM.M - deltaMTimesFractionOfOriginal * 0.5;
		}
		return new Extent(maxX, minX, maxY, minY, maxZ, minZ, maxM, minM);
	}

	public Extent RatioAboutPoint(double fractionOfOriginal, Point point)
	{
		return RatioAboutPoint(fractionOfOriginal, point.PointM());
	}

	public Extent RatioAboutPoint(double fractionOfOriginal, PointM PointM)
	{
		double maxX = double.NaN;
		double minX = double.NaN;
		if (!double.IsNaN(_maxX) & !double.IsNaN(PointM.X))
		{
			maxX = PointM.X + (_maxX - PointM.X) * fractionOfOriginal;
			minX = PointM.X - (PointM.X - _minX) * fractionOfOriginal;
		}
		double maxY = double.NaN;
		double minY = double.NaN;
		if (!double.IsNaN(_maxY) & !double.IsNaN(PointM.Y))
		{
			maxY = PointM.Y + (_maxY - PointM.Y) * fractionOfOriginal;
			minY = PointM.Y - (PointM.Y - _minY) * fractionOfOriginal;
		}
		double maxZ = double.NaN;
		double minZ = double.NaN;
		if (!double.IsNaN(_maxZ) & !double.IsNaN(PointM.Z))
		{
			maxZ = PointM.Z + (_maxZ - PointM.Z) * fractionOfOriginal;
			minZ = PointM.Z - (PointM.Z - _minZ) * fractionOfOriginal;
		}
		double maxM = double.NaN;
		double minM = double.NaN;
		if (!double.IsNaN(_maxM) & !double.IsNaN(PointM.M))
		{
			maxM = PointM.M + (_maxM - PointM.M) * fractionOfOriginal;
			minM = PointM.M - (PointM.M - _minM) * fractionOfOriginal;
		}
		return new Extent(maxX, minX, maxY, minY, maxZ, minZ, maxM, minM);
	}

	public Extent RatioAboutCenter(double fractionOfOriginal)
	{
		return RatioAboutPoint(fractionOfOriginal, CenterPointM());
	}

	public Extent ExpandToMaxDimension()
	{
		double deltaX = DeltaX;
		double deltaY = DeltaY;
		if (System.Math.Abs(deltaX - deltaY) < 1E-07)
		{
			return new Extent(this);
		}
		if (deltaX > deltaY)
		{
			double deltaXDeltaYTerm = (deltaX - deltaY) / 2.0;
			double maxY = MaxY + deltaXDeltaYTerm;
			double minY = MinY - deltaXDeltaYTerm;
			return new Extent(MaxX, MinX, maxY, minY);
		}
		double deltaYDeltaXTerm = (deltaY - deltaX) / 2.0;
		double maxX = MaxX + deltaYDeltaXTerm;
		double minX = MinX - deltaYDeltaXTerm;
		return new Extent(maxX, minX, MaxY, MinY);
	}

	public Extent Translate(PointM offset)
	{
		return Translate(offset.X, offset.Y);
	}

	public Extent Translate(double offsetX, double offsetY)
	{
		double maxX = double.NaN;
		double minX = double.NaN;
		if (!double.IsNaN(_maxX))
		{
			maxX = _maxX + offsetX;
			minX = _minX + offsetX;
		}
		double maxY = double.NaN;
		double minY = double.NaN;
		if (!double.IsNaN(_maxY))
		{
			maxY = _maxY + offsetY;
			minY = _minY + offsetY;
		}
		return new Extent(maxX, minX, maxY, minY, _maxZ, _minZ, _maxM, _minM);
	}

	public Extent IsotropicExtent(int height, int width)
	{
		double heightOverWidth = (double)height / (double)width;
		double deltaX = DeltaX;
		double deltaY = DeltaY;
		double maxX;
		double minX;
		double maxY;
		double minY;
		if (deltaX != 0.0)
		{
			double deltaYOverDeltaX = deltaY / deltaX;
			PointM pointM = CenterPointM();
			if (heightOverWidth < deltaYOverDeltaX)
			{
				double deltaYOverHeightOverWidth = deltaY / heightOverWidth;
				maxX = pointM.X + deltaYOverHeightOverWidth * 0.5;
				minX = pointM.X - deltaYOverHeightOverWidth * 0.5;
				maxY = _maxY;
				minY = _minY;
			}
			else
			{
				maxX = _maxX;
				minX = _minX;
				double deltaXTerm = deltaX * heightOverWidth;
				maxY = pointM.Y + deltaXTerm * 0.5;
				minY = pointM.Y - deltaXTerm * 0.5;
			}
		}
		else
		{
			double deltaYOverHeightOverWidth2 = deltaY / heightOverWidth;
			maxX = _minX + deltaYOverHeightOverWidth2 * 0.5;
			minX = _minX - deltaYOverHeightOverWidth2 * 0.5;
			maxY = _maxY;
			minY = _minY;
		}
		return new Extent(maxX, minX, maxY, minY);
	}

	public bool Overlaps(Feature feature)
	{
		return MBROverlaps(feature);
	}

	public Extent IntersectionXY(Extent ext)
	{
		if (!Overlaps(ext))
		{
			return null;
		}
		return new Extent(ReplaceOrMin(MaxX, ext.MaxX), ReplaceOrMax(MinX, ext.MinX), ReplaceOrMin(MaxY, ext.MaxY), ReplaceOrMax(MinY, ext.MinY));
	}

	public Extent UnionXY(Extent ext)
	{
		if (ext == null)
		{
			return this;
		}
		return new Extent(ReplaceOrMax(MaxX, ext.MaxX), ReplaceOrMin(MinX, ext.MinX), ReplaceOrMax(MaxY, ext.MaxY), ReplaceOrMin(MinY, ext.MinY));
	}

	private static double ReplaceOrMin(double computed, double computed2)
	{
		if (double.IsNaN(computed))
		{
			return computed2;
		}
		if (double.IsNaN(computed2))
		{
			return computed;
		}
		return System.Math.Min(computed, computed2);
	}

	private static double ReplaceOrMax(double computed, double computed2)
	{
		if (double.IsNaN(computed))
		{
			return computed2;
		}
		if (double.IsNaN(computed2))
		{
			return computed;
		}
		return System.Math.Max(computed, computed2);
	}

	public static Extent Union(Extent ext1, Extent ext2)
	{
		if (ext1 == null)
		{
			return ext2;
		}
		if (ext2 == null)
		{
			return ext1;
		}
		return ext1.UnionXY(ext2);
	}

	public static Extent Combine(IEnumerable<Extent> exts)
	{
		return new Extent(exts.ToArray());
	}

	public bool Contains(Point2Double pt)
	{
		return Contains(pt.X, pt.Y);
	}

	public bool Contains(double x, double y)
	{
		if (x < _minX)
		{
			return false;
		}
		if (x > _maxX)
		{
			return false;
		}
		if (y < _minY)
		{
			return false;
		}
		if (y > _maxY)
		{
			return false;
		}
		return true;
	}

	public bool Contains(PointM PointM)
	{
		if (PointM.X < _minX)
		{
			return false;
		}
		if (PointM.X > _maxX)
		{
			return false;
		}
		if (PointM.Y < _minY)
		{
			return false;
		}
		if (PointM.Y > _maxY)
		{
			return false;
		}
		return true;
	}

	public bool RoughlyContains(PointM ptm, double tol)
	{
		if (ptm.X < _minX - tol)
		{
			return false;
		}
		if (ptm.X > _maxX + tol)
		{
			return false;
		}
		if (ptm.Y < _minY - tol)
		{
			return false;
		}
		if (ptm.Y > _maxY + tol)
		{
			return false;
		}
		return true;
	}

	public bool Contains(Point point)
	{
		if (point == null)
		{
			return false;
		}
		if (point.X < _minX)
		{
			return false;
		}
		if (point.X > _maxX)
		{
			return false;
		}
		if (point.Y < _minY)
		{
			return false;
		}
		if (point.Y > _maxY)
		{
			return false;
		}
		return true;
	}

	public bool Contains(Feature extent)
	{
		if (extent == null)
		{
			return false;
		}
		if (extent.MinX < _minX)
		{
			return false;
		}
		if (extent.MaxX > _maxX)
		{
			return false;
		}
		if (extent.MinY < _minY)
		{
			return false;
		}
		if (extent.MaxY > _maxY)
		{
			return false;
		}
		return true;
	}

	public Polygon ToPolygon()
	{
		PointMs pointMs = new PointMs();
		pointMs.Add(UpperLeft);
		pointMs.Add(UpperRight);
		pointMs.Add(LowerRight);
		pointMs.Add(LowerLeft);
		pointMs.Add(UpperLeft);
		return new Polygon(pointMs);
	}

	public double DistanceTo(PointM pt)
	{
		return pt.DistanceTo(NearestPointInPerimeter(pt));
	}

	public PointM NearestPointInPerimeter(PointM pt)
	{
		double utilitiesRasMapperLibTerm = RasMapperLib.Utilities.Math.Clamp(pt.X, Left, Right);
		double utilitiesRasMapperLibTerm2 = RasMapperLib.Utilities.Math.Clamp(pt.Y, Bottom, Top);
		double systemLeftTerm = System.Math.Abs(utilitiesRasMapperLibTerm - Left);
		double systemRightTerm = System.Math.Abs(utilitiesRasMapperLibTerm - Right);
		double systemTopTerm = System.Math.Abs(utilitiesRasMapperLibTerm2 - Top);
		double systemBottomTerm = System.Math.Abs(utilitiesRasMapperLibTerm2 - Bottom);
		double systemTerm = System.Math.Min(systemLeftTerm, System.Math.Min(systemRightTerm, System.Math.Min(systemTopTerm, systemBottomTerm)));
		PointM result;
		if (systemTerm == systemTopTerm)
		{
			result = new PointM(utilitiesRasMapperLibTerm, Top);
		}
		else if (systemTerm == systemBottomTerm)
		{
			result = new PointM(utilitiesRasMapperLibTerm, Bottom);
		}
		else
		{
			result = ((systemTerm != systemLeftTerm) ? new PointM(Right, utilitiesRasMapperLibTerm2) : new PointM(Left, utilitiesRasMapperLibTerm2));
		}
		return result;
	}

	public override string ToString()
	{
		return "Extent" + PointLabel(HasZ, HasM);
	}

	public string ToArgString()
	{
		return MaxX + "," + MinX + ";" + MaxY + "," + MinY;
	}

	public static Extent FromArgString(string stringValue)
	{
		string[] parts = stringValue.Split(new char[2] { ',', ';' });
		if (parts.Length == 4 && double.TryParse(parts[0], out var result) && double.TryParse(parts[1], out var result2) && double.TryParse(parts[2], out var result3) && double.TryParse(parts[3], out var result4))
		{
			return new Extent(result, result2, result3, result4);
		}
		return NullExtent;
	}

	public override bool EqualXY(Feature feature)
	{
		if (feature == null)
		{
			return false;
		}
		if (_maxX == feature.MaxX && _minX == feature.MinX && _maxY == feature.MaxY && _minY == feature.MinY)
		{
			return true;
		}
		return false;
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		return MBROverlaps(PointM);
	}

	public override bool Intersects(Extent extent)
	{
		return Overlaps(extent);
	}

	public override PointM LabelPoint(FeatureLayer layer)
	{
		return CenterPointM();
	}

	public double GeometricMeanDeltaXY()
	{
		return System.Math.Sqrt(DeltaX * DeltaY);
	}

	public override IEditableFeature GetMutableView()
	{
		throw new NotImplementedException();
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>();
	}

	public override Feature SetZAll(double z)
	{
		return new Extent(MaxX, MinX, MaxY, MinY, z, z, MaxM, MinM);
	}

	public override Feature SetMAll(double m)
	{
		return new Extent(MaxX, MinX, MaxY, MinY, MaxZ, MinZ, m, m);
	}

	public override IList<double> GetZPerVertex()
	{
		return new double[2] { MinZ, MaxZ };
	}

	public override IList<double> GetMPerVertex()
	{
		return new double[2] { MinM, MaxM };
	}

	public override Feature SetZPerVertex(IList<double> zs)
	{
		return new Extent(MaxX, MinX, MaxY, MinY, zs[0], zs[1], MaxM, MinM);
	}

	public override Feature SetMPerVertex(IList<double> ms)
	{
		return new Extent(MaxX, MinX, MaxY, MinY, MaxZ, MinZ, ms[0], ms[1]);
	}
}
