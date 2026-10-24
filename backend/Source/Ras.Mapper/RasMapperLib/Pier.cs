using System;
using System.Collections.Generic;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class Pier : AffineFeature
{
	public enum PierCapType
	{
		Round,
		Sharp
	}

	private RectangleFeature _body;

	private AffineFeature _topCap;

	private AffineFeature _bottomCap;

	private double _maxX;

	private double _minX;

	private double _maxY;

	private double _minY;

	public static Dictionary<Type, int> CapTypeToInt = new Dictionary<Type, int>
	{
		{
			typeof(Ellipse),
			0
		},
		{
			typeof(Triangle),
			1
		}
	};

	public static Dictionary<int, Type> IntToCapType = new Dictionary<int, Type>
	{
		{
			0,
			typeof(Ellipse)
		},
		{
			1,
			typeof(Triangle)
		}
	};

	public RectangleFeature Body => _body;

	public AffineFeature TopCap => _topCap;

	public AffineFeature BottomCap => _bottomCap;

	public override string SummaryText => "";

	public override double MaxX => _maxX;

	public override double MinX => _minX;

	public override double MaxY => _maxY;

	public override double MinY => _minY;

	public override bool HasZ { get; }

	public override bool HasM { get; }

	public override double MaxZ { get; }

	public override double MinZ { get; }

	public override double MaxM { get; }

	public override double MinM { get; }

	public override int Count { get; }

	public override bool MaxMinXYDefined { get; }

	public override bool UsesPointSymbol { get; }

	public override bool UsesPen { get; }

	public override bool UsesBrush { get; }

	public override bool UsesSurfaceFill { get; }

	public Pier(double x, double y, double angleRotation, double width = 0.0, double rectangleHeight = 0.0, double topCapRadiusY = 0.0, Type topCapType = null, double bottomCapRadiusY = 0.0, Type botCapType = null)
	{
		HasZ = false;
		HasM = false;
		MaxZ = double.NaN;
		MinZ = double.NaN;
		MaxM = double.NaN;
		MinM = double.NaN;
		Count = 1;
		MaxMinXYDefined = true;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
		PointM center = new PointM(x, y);
		MakePierFromParams(center, angleRotation, width, rectangleHeight, topCapRadiusY, topCapType, bottomCapRadiusY, botCapType);
	}

	public Pier(PointM center, double angleRotation, double width = 0.0, double rectangleHeight = 0.0, double topCapRadiusY = 0.0, Type topCapType = null, double bottomCapRadiusY = 0.0, Type botCapType = null)
	{
		HasZ = false;
		HasM = false;
		MaxZ = double.NaN;
		MinZ = double.NaN;
		MaxM = double.NaN;
		MinM = double.NaN;
		Count = 1;
		MaxMinXYDefined = true;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
		MakePierFromParams(center, angleRotation, width, rectangleHeight, topCapRadiusY, topCapType, bottomCapRadiusY, botCapType);
	}

	public Pier(PointM center, RectangleFeature body, AffineFeature topCap, AffineFeature bottomCap, double angleRotation)
	{
		HasZ = false;
		HasM = false;
		MaxZ = double.NaN;
		MinZ = double.NaN;
		MaxM = double.NaN;
		MinM = double.NaN;
		Count = 1;
		MaxMinXYDefined = true;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
		Initialize(center, body, topCap, bottomCap, angleRotation);
	}

	private void MakePierFromParams(PointM center, double angleRotation, double width, double rectangleHeight, double topCapRadiusY, Type topCapType, double bottomCapRadiusY, Type botCapType)
	{
		RectangleFeature rectangleFeature = null;
		AffineFeature affineFeature = null;
		AffineFeature bottomCap = null;
		_ = angleRotation * System.Math.PI / 180.0;
		if (width > 0.0 && rectangleHeight > 0.0)
		{
			rectangleFeature = new RectangleFeature(center, width, rectangleHeight, angleRotation);
		}
		else if ((topCapRadiusY > 0.0 || bottomCapRadiusY > 0.0) && width > 0.0)
		{
			if (topCapRadiusY > 0.0)
			{
				if (topCapType == typeof(Ellipse) || topCapType.IsSubclassOf(typeof(Ellipse)))
				{
					affineFeature = new Ellipse(center, width / 2.0, topCapRadiusY, angleRotation, EllipseSection.Top);
				}
				else
				{
					if (!(topCapType == typeof(Triangle)) && !topCapType.IsSubclassOf(typeof(Triangle)))
					{
						throw new NotImplementedException("Unsupported nose type");
					}
					affineFeature = new Triangle(center, width, topCapRadiusY * 2.0, angleRotation, bottomCapRadiusY > 0.0);
				}
			}
			if (bottomCapRadiusY > 0.0)
			{
				if (botCapType == typeof(Ellipse) || botCapType.IsSubclassOf(typeof(Ellipse)))
				{
					bottomCap = new Ellipse(center, width / 2.0, bottomCapRadiusY, angleRotation, EllipseSection.Bottom);
				}
				else
				{
					if (!(botCapType == typeof(Triangle)) && !botCapType.IsSubclassOf(typeof(Triangle)))
					{
						throw new NotImplementedException("Unsupported nose type");
					}
					bottomCap = new Triangle(center, width, bottomCapRadiusY * 2.0, (angleRotation + 180.0) % 360.0, affineFeature != null);
				}
			}
		}
		if (rectangleFeature != null)
		{
			if (topCapRadiusY > 0.0)
			{
				if (topCapType == typeof(Ellipse) || topCapType.IsSubclassOf(typeof(Ellipse)))
				{
					affineFeature = new Ellipse(new PointM(rectangleFeature.ModificationBoundingBox[1].X, rectangleFeature.ModificationBoundingBox[1].Y), rectangleFeature.Width / 2.0, topCapRadiusY, angleRotation, EllipseSection.Top);
				}
				else
				{
					if (!(topCapType == typeof(Triangle)) && !topCapType.IsSubclassOf(typeof(Triangle)))
					{
						throw new NotImplementedException("Unsupported nose type");
					}
					affineFeature = new Triangle(rectangleFeature.ModificationBoundingBox[1], rectangleFeature.Width, topCapRadiusY * 2.0, angleRotation, rotateOnAB: true);
				}
			}
			if (bottomCapRadiusY > 0.0)
			{
				if (botCapType == typeof(Ellipse) || botCapType.IsSubclassOf(typeof(Ellipse)))
				{
					bottomCap = new Ellipse(new PointM(rectangleFeature.ModificationBoundingBox[5].X, rectangleFeature.ModificationBoundingBox[5].Y), rectangleFeature.Width / 2.0, bottomCapRadiusY, angleRotation, EllipseSection.Bottom);
				}
				else
				{
					if (!(botCapType == typeof(Triangle)) && !botCapType.IsSubclassOf(typeof(Triangle)))
					{
						throw new NotImplementedException("Unsupported nose type");
					}
					bottomCap = new Triangle(rectangleFeature.ModificationBoundingBox[5], rectangleFeature.Width, bottomCapRadiusY * 2.0, (angleRotation + 180.0) % 360.0, rotateOnAB: true);
				}
			}
		}
		Initialize(center, rectangleFeature, affineFeature, bottomCap, angleRotation);
	}

	public void Initialize(PointM center, RectangleFeature body, AffineFeature topCap, AffineFeature bottomCap, double angleRotation)
	{
		_center = center;
		_body = body;
		_topCap = topCap;
		_bottomCap = bottomCap;
		_angleRotation = angleRotation;
		_maxX = System.Math.Max(System.Math.Max((_body != null) ? _body.MaxX : double.MinValue, (_topCap != null) ? _topCap.MaxX : double.MinValue), (_bottomCap != null) ? _bottomCap.MaxX : double.MinValue);
		if (_maxX == double.MinValue)
		{
			_maxX = 0.0;
		}
		_maxY = System.Math.Max(System.Math.Max((_body != null) ? _body.MaxY : double.MinValue, (_topCap != null) ? _topCap.MaxY : double.MinValue), (_bottomCap != null) ? _bottomCap.MaxY : double.MinValue);
		if (_maxY == double.MinValue)
		{
			_maxY = 0.0;
		}
		_minX = System.Math.Min(System.Math.Min((_body != null) ? _body.MinX : double.MaxValue, (_topCap != null) ? _topCap.MinX : double.MaxValue), (_bottomCap != null) ? _bottomCap.MinX : double.MaxValue);
		if (_minX == double.MaxValue)
		{
			_minX = 0.0;
		}
		_minY = System.Math.Min(System.Math.Min((_body != null) ? _body.MinY : double.MaxValue, (_topCap != null) ? _topCap.MinY : double.MaxValue), (_bottomCap != null) ? _bottomCap.MinY : double.MaxValue);
		if (_minY == double.MaxValue)
		{
			_minY = 0.0;
		}
		MakeModificationBoundingBox(center);
		CanDrawModificationBoundingBox = true;
	}

	public override void ScaleFeature(Transform transformer, ModificationBoundingBoxIndices index)
	{
	}

	public override void RotateFeature(Transform transformer)
	{
		double angleRotationAngleRotationDegreesTerm = (_angleRotation - ((Affine)transformer).AngleRotationDegrees) % 360.0;
		if (angleRotationAngleRotationDegreesTerm < 0.0)
		{
			angleRotationAngleRotationDegreesTerm += 360.0;
		}
		double width = 0.0;
		double topCapRadiusY = 0.0;
		double bottomCapRadiusY = 0.0;
		if (_body != null)
		{
			width = _body.Width;
		}
		else if (_topCap != null)
		{
			if (_topCap is Ellipse)
			{
				width = ((Ellipse)_topCap).RadiusX * 2.0;
			}
			else
			{
				if (!(_topCap is Triangle))
				{
					throw new NotImplementedException("Unsupported nose type");
				}
				width = ((Triangle)_topCap).Width;
			}
		}
		else if (_bottomCap != null)
		{
			if (_bottomCap is Ellipse)
			{
				width = ((Ellipse)_bottomCap).RadiusX * 2.0;
			}
			else
			{
				if (!(_bottomCap is Triangle))
				{
					throw new NotImplementedException("Unsupported nose type");
				}
				width = ((Triangle)_bottomCap).Width;
			}
		}
		if (_topCap != null)
		{
			if (_topCap is Ellipse)
			{
				topCapRadiusY = ((Ellipse)_topCap).RadiusY;
			}
			else
			{
				if (!(_topCap is Triangle))
				{
					throw new NotImplementedException("Unsupported nose type");
				}
				topCapRadiusY = ((Triangle)_topCap).Height / 2.0;
			}
		}
		if (_bottomCap != null)
		{
			if (_bottomCap is Ellipse)
			{
				bottomCapRadiusY = ((Ellipse)_bottomCap).RadiusY;
			}
			else
			{
				if (!(_bottomCap is Triangle))
				{
					throw new NotImplementedException("Unsupported nose type");
				}
				bottomCapRadiusY = ((Triangle)_bottomCap).Height / 2.0;
			}
		}
		MakePierFromParams(Center, angleRotationAngleRotationDegreesTerm, width, (_body != null) ? _body.Height : 0.0, topCapRadiusY, (_topCap != null) ? _topCap.GetType() : null, bottomCapRadiusY, (_bottomCap != null) ? _bottomCap.GetType() : null);
	}

	public override void MD5HashAppend(Hash hash)
	{
		bool hasBody = _body != null;
		bool hasTopCap = _topCap != null;
		bool hasBottomCap = _bottomCap != null;
		double unity = -1.0;
		double unity2 = -1.0;
		double unity3 = -1.0;
		double unity4 = -1.0;
		if (hasTopCap)
		{
			if (_topCap is Ellipse)
			{
				unity = ((Ellipse)_topCap).RadiusX;
				unity2 = ((Ellipse)_topCap).RadiusY;
			}
			else
			{
				if (!(_topCap is Triangle))
				{
					throw new NotImplementedException("Unsupported nose type");
				}
				unity = ((Triangle)_topCap).Width / 2.0;
				unity2 = ((Triangle)_topCap).Height / 2.0;
			}
		}
		if (hasBottomCap)
		{
			if (_bottomCap is Ellipse)
			{
				unity3 = ((Ellipse)_bottomCap).RadiusX;
				unity4 = ((Ellipse)_bottomCap).RadiusY;
			}
			else
			{
				if (!(_topCap is Triangle))
				{
					throw new NotImplementedException("Unsupported nose type");
				}
				unity3 = ((Triangle)_bottomCap).Width / 2.0;
				unity4 = ((Triangle)_bottomCap).Height / 2.0;
			}
		}
		hash.Add(new double[8]
		{
			Center.X,
			Center.Y,
			hasBody ? _body.Width : (-1.0),
			hasBody ? _body.Height : (-1.0),
			unity,
			unity2,
			unity3,
			unity4
		});
	}

	public override Extent GetUnrotatedBoundingBox()
	{
		Extent extent = Extent.NullExtent;
		if (_body != null)
		{
			extent = ((!Extent.IsNullOrInfinite(extent)) ? extent.UnionXY(_body.GetUnrotatedBoundingBox()) : _body.GetUnrotatedBoundingBox());
		}
		if (_topCap != null)
		{
			if (Extent.IsNullOrInfinite(extent))
			{
				extent = _topCap.GetUnrotatedBoundingBox();
			}
			else if (_topCap is Ellipse)
			{
				Ellipse ellipse = (Ellipse)_topCap;
				Ellipse ellipse2 = new Ellipse(new PointM(extent.UpperMidside.X, extent.UpperMidside.Y), ellipse.RadiusX, ellipse.RadiusY, 0.0);
				extent = extent.UnionXY(ellipse2.GetUnrotatedBoundingBox());
			}
			else
			{
				if (!(_topCap is Triangle))
				{
					throw new NotImplementedException("Unsupported nose type");
				}
				Triangle triangle = (Triangle)_topCap;
				Triangle triangle2 = new Triangle(new PointM(extent.UpperMidside.X, extent.UpperMidside.Y + triangle.Height / 2.0), triangle.Width, triangle.Height);
				extent = extent.UnionXY(triangle2.GetUnrotatedBoundingBox());
			}
		}
		if (_bottomCap != null)
		{
			if (Extent.IsNullOrInfinite(extent))
			{
				extent = _bottomCap.GetUnrotatedBoundingBox();
			}
			else if (_body != null)
			{
				Extent unrotatedBoundingBox = _body.GetUnrotatedBoundingBox();
				if (_bottomCap is Ellipse)
				{
					Ellipse ellipse3 = (Ellipse)_bottomCap;
					Ellipse ellipse4 = new Ellipse(new PointM(unrotatedBoundingBox.LowerMidside.X, unrotatedBoundingBox.LowerMidside.Y), ellipse3.RadiusX, ellipse3.RadiusY, 1.0);
					extent = extent.UnionXY(ellipse4.GetUnrotatedBoundingBox());
				}
				else
				{
					if (!(_bottomCap is Triangle))
					{
						throw new NotImplementedException("Unsupported nose type");
					}
					Triangle triangle3 = (Triangle)_bottomCap;
					Triangle triangle4 = new Triangle(new PointM(unrotatedBoundingBox.LowerMidside.X, unrotatedBoundingBox.LowerMidside.Y - triangle3.Height / 2.0), triangle3.Width, triangle3.Height);
					extent = extent.UnionXY(triangle4.GetUnrotatedBoundingBox());
				}
			}
			else if (_bottomCap is Ellipse)
			{
				extent = extent.UnionXY(_bottomCap.GetUnrotatedBoundingBox());
			}
			else
			{
				Triangle triangle5 = (Triangle)_bottomCap;
				Triangle triangle6 = new Triangle(_bottomCap.Center, new Point(extent.LowerLeft), new Point(extent.LowerRight), new Point(extent.LowerMidside.X, extent.LowerMidside.Y - triangle5.Height), 0.0);
				extent = extent.UnionXY(triangle6.GetUnrotatedBoundingBox());
			}
		}
		return extent;
	}

	public override string ToString()
	{
		return SummaryText;
	}

	public override PointM LabelPoint(FeatureLayer layer)
	{
		return _center;
	}

	public override bool EqualXY(Feature feature)
	{
		if (feature == null)
		{
			return false;
		}
		if (feature is Pier)
		{
			Pier pier = (Pier)feature;
			if (pier.Center.EqualXY(Center))
			{
				if ((_body != null) & (pier.Body != null))
				{
					return _body.EqualXY(pier.Body);
				}
				if ((_topCap != null) & (pier.TopCap != null))
				{
					return _topCap.EqualXY(pier.TopCap);
				}
				if ((_bottomCap != null) & (pier.BottomCap != null))
				{
					return _bottomCap.EqualXY(pier.BottomCap);
				}
			}
			return false;
		}
		return false;
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		bool condition = false;
		if (_body != null)
		{
			condition |= _body.Intersects(PointM, nearDistance);
		}
		if (_topCap != null)
		{
			condition |= _topCap.Intersects(PointM, nearDistance);
		}
		if (_bottomCap != null)
		{
			condition |= _bottomCap.Intersects(PointM, nearDistance);
		}
		return condition;
	}

	public override bool Intersects(Extent extent)
	{
		bool condition = false;
		if (_body != null)
		{
			condition |= _body.Intersects(extent);
		}
		if (_topCap != null)
		{
			condition |= _topCap.Intersects(extent);
		}
		if (_bottomCap != null)
		{
			condition |= _bottomCap.Intersects(extent);
		}
		return condition;
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>();
	}

	public static bool IsValid(Pier pierFeat)
	{
		if (pierFeat == null)
		{
			return false;
		}
		if ((double.IsNaN(pierFeat.Center.X) || double.IsNaN(pierFeat.Center.Y) || double.IsNaN(pierFeat.AngleRotation)) && (pierFeat.Body != null || pierFeat.TopCap != null || pierFeat.BottomCap != null))
		{
			return false;
		}
		return true;
	}

	public override IEditableFeature GetMutableView()
	{
		return new PierEditable(this);
	}

	public override bool Contains(Polyline pline)
	{
		bool condition = false;
		if (_body != null)
		{
			condition |= _body.Contains(pline);
		}
		if (_topCap != null)
		{
			condition |= _topCap.Contains(pline);
		}
		if (_bottomCap != null)
		{
			condition |= _bottomCap.Contains(pline);
		}
		return condition;
	}

	public override bool Contains(PointM ptm)
	{
		bool condition = false;
		if (_body != null)
		{
			condition |= _body.Contains(ptm);
			if (condition)
			{
				return condition;
			}
		}
		if (_topCap != null)
		{
			condition |= _topCap.Contains(ptm);
			if (condition)
			{
				return condition;
			}
		}
		if (_bottomCap != null)
		{
			return condition | _bottomCap.Contains(ptm);
		}
		return condition;
	}

	public bool ContainsRotatedPt(PointM originalPt, PointM rotatedPt, bool bodyInView, bool topCapinView, bool botCapInView)
	{
		bool condition = false;
		if (_body != null)
		{
			if (bodyInView)
			{
				condition |= _body.Contains(originalPt);
			}
			if (condition)
			{
				return condition;
			}
		}
		if (_topCap != null)
		{
			if (topCapinView)
			{
				if (_topCap is Ellipse)
				{
					condition |= ((Ellipse)_topCap).ContainsRotatedPt(originalPt, rotatedPt);
				}
				else
				{
					if (!(_topCap is Triangle))
					{
						throw new NotImplementedException("Unsupported nose type");
					}
					condition |= ((Triangle)_topCap).Contains(originalPt);
				}
			}
			if (condition)
			{
				return condition;
			}
		}
		if (_bottomCap != null)
		{
			if (botCapInView)
			{
				if (_bottomCap is Ellipse)
				{
					condition |= ((Ellipse)_bottomCap).ContainsRotatedPt(originalPt, rotatedPt);
				}
				else
				{
					if (!(_bottomCap is Triangle))
					{
						throw new NotImplementedException("Unsupported nose type");
					}
					condition |= ((Triangle)_bottomCap).Contains(originalPt);
				}
			}
			return condition;
		}
		return condition;
	}

	public override List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline)
	{
		List<Tuple<double, double>> containedSubsegments = new List<Tuple<double, double>>();
		if (_body != null)
		{
			containedSubsegments.AddRange(_body.GetContainedSubsegments(polyline));
		}
		if (_topCap != null)
		{
			containedSubsegments.AddRange(_topCap.GetContainedSubsegments(polyline));
		}
		if (_bottomCap != null)
		{
			containedSubsegments.AddRange(_bottomCap.GetContainedSubsegments(polyline));
		}
		return containedSubsegments;
	}

	public override Polygon DiscretizeToPolygon()
	{
		List<Polygon> polygons = new List<Polygon>();
		if (_body != null)
		{
			polygons.Add(_body.DiscretizeToPolygon());
		}
		if (_topCap != null)
		{
			polygons.Add(_topCap.DiscretizeToPolygon());
		}
		if (_bottomCap != null)
		{
			polygons.Add(_bottomCap.DiscretizeToPolygon());
		}
		return GeoProcessing.MergePolygons(polygons);
	}

	public override Feature SetZAll(double z)
	{
		Feature feature = null;
		Feature feature2 = null;
		Feature feature3 = null;
		if (_body != null)
		{
			feature = _body.SetZAll(z);
		}
		if (_topCap != null)
		{
			feature2 = _topCap.SetZAll(z);
		}
		if (_bottomCap != null)
		{
			feature3 = _bottomCap.SetZAll(z);
		}
		return new Pier(new PointM(Center.X, Center.Y, z, Center.M), (RectangleFeature)feature, (AffineFeature)feature2, (AffineFeature)feature3, AngleRotation);
	}

	public override Feature SetMAll(double m)
	{
		Feature feature = null;
		Feature feature2 = null;
		Feature feature3 = null;
		if (_body != null)
		{
			feature = _body.SetMAll(m);
		}
		if (_topCap != null)
		{
			feature2 = _topCap.SetMAll(m);
		}
		if (_bottomCap != null)
		{
			feature3 = _bottomCap.SetMAll(m);
		}
		return new Pier(new PointM(Center.X, Center.Y, Center.Z, m), (RectangleFeature)feature, (AffineFeature)feature2, (AffineFeature)feature3, AngleRotation);
	}

	public override IList<double> GetZPerVertex()
	{
		List<double> zPerVertex = new List<double>();
		if (_body != null)
		{
			zPerVertex.AddRange(_body.GetZPerVertex());
		}
		if (_topCap != null)
		{
			zPerVertex.AddRange(_topCap.GetZPerVertex());
		}
		if (_bottomCap != null)
		{
			zPerVertex.AddRange(_bottomCap.GetZPerVertex());
		}
		zPerVertex.Add(Center.Z);
		return zPerVertex;
	}

	public override IList<double> GetMPerVertex()
	{
		List<double> mPerVertex = new List<double>();
		if (_body != null)
		{
			mPerVertex.AddRange(_body.GetMPerVertex());
		}
		if (_topCap != null)
		{
			mPerVertex.AddRange(_topCap.GetMPerVertex());
		}
		if (_bottomCap != null)
		{
			mPerVertex.AddRange(_bottomCap.GetMPerVertex());
		}
		mPerVertex.Add(Center.M);
		return mPerVertex;
	}

	public override Feature SetZPerVertex(IList<double> zs)
	{
		Feature feature = null;
		Feature feature2 = null;
		Feature feature3 = null;
		int zPerVertexCount = ((_body != null) ? _body.GetZPerVertex().Count : 0);
		int zPerVertexCount2 = ((_topCap != null) ? _topCap.GetZPerVertex().Count : 0);
		int zPerVertexCount3 = ((_bottomCap != null) ? _bottomCap.GetZPerVertex().Count : 0);
		List<double> doubleList = new List<double>();
		checked
		{
			int count = zPerVertexCount - 1;
			for (int i = 0; i <= count; i++)
			{
				doubleList.Add(zs[i]);
			}
			if (_body != null)
			{
				feature = _body.SetZPerVertex(doubleList);
			}
			List<double> doubleListList = new List<double>();
			int count2 = zPerVertexCount2 - 1;
			for (int j = 0; j <= count2; j++)
			{
				doubleListList.Add(zs[zPerVertexCount + j]);
			}
			if (_topCap != null)
			{
				feature2 = _topCap.SetZPerVertex(doubleListList);
			}
			List<double> doubleList2 = new List<double>();
			int count3 = zPerVertexCount3 - 1;
			for (int k = 0; k <= count3; k++)
			{
				doubleList2.Add(zs[zPerVertexCount + zPerVertexCount2 + k]);
			}
			if (_bottomCap != null)
			{
				feature3 = _bottomCap.SetZPerVertex(doubleList2);
			}
			return new Pier(new PointM(Center.X, Center.Y, zs[zPerVertexCount + zPerVertexCount2 + zPerVertexCount3], Center.M), (RectangleFeature)feature, (AffineFeature)feature2, (AffineFeature)feature3, AngleRotation);
		}
	}

	public override Feature SetMPerVertex(IList<double> ms)
	{
		Feature feature = null;
		Feature feature2 = null;
		Feature feature3 = null;
		int mPerVertexCount = ((_body != null) ? _body.GetMPerVertex().Count : 0);
		int mPerVertexCount2 = ((_topCap != null) ? _topCap.GetMPerVertex().Count : 0);
		int mPerVertexCount3 = ((_bottomCap != null) ? _bottomCap.GetMPerVertex().Count : 0);
		List<double> doubleList = new List<double>();
		checked
		{
			int count = mPerVertexCount - 1;
			for (int i = 0; i <= count; i++)
			{
				doubleList.Add(ms[i]);
			}
			if (_body != null)
			{
				feature = _body.SetZPerVertex(doubleList);
			}
			List<double> doubleListList = new List<double>();
			int count2 = mPerVertexCount2 - 1;
			for (int j = 0; j <= count2; j++)
			{
				doubleListList.Add(ms[mPerVertexCount + j]);
			}
			if (_topCap != null)
			{
				feature2 = _topCap.SetZPerVertex(doubleListList);
			}
			List<double> doubleList2 = new List<double>();
			int count3 = mPerVertexCount3 - 1;
			for (int k = 0; k <= count3; k++)
			{
				doubleList2.Add(ms[mPerVertexCount + mPerVertexCount2 + k]);
			}
			if (_bottomCap != null)
			{
				feature3 = _bottomCap.SetZPerVertex(doubleList2);
			}
			return new Pier(new PointM(Center.X, Center.Y, Center.Z, ms[mPerVertexCount + mPerVertexCount2 + mPerVertexCount3]), (RectangleFeature)feature, (AffineFeature)feature2, (AffineFeature)feature3, AngleRotation);
		}
	}
}
