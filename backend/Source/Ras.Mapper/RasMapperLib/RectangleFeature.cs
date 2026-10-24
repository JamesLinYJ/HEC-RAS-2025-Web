using System;
using System.Collections.Generic;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class RectangleFeature : AffineFeature
{
	private double _maxX;

	private double _minX;

	private double _maxY;

	private double _minY;

	private double _width;

	private double _height;

	public bool IsSquare => _width == _height;

	public double Width => _width;

	public double Height => _height;

	public override string SummaryText => "Center: " + Center.ToString() + " Width: " + Width + " Height: " + Height;

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

	public RectangleFeature(double x, double y, double width)
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
		Initialize(new PointM(x, y), width, width, 0.0);
	}

	public RectangleFeature(double x, double y, double width, double height)
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
		Initialize(new PointM(x, y), width, height, 0.0);
	}

	public RectangleFeature(double x, double y, double width, double height, double angleRotation)
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
		Initialize(new PointM(x, y), width, height, angleRotation);
	}

	public RectangleFeature(PointM center, double width)
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
		Initialize(center, width, width, 0.0);
	}

	public RectangleFeature(PointM center, double width, double height)
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
		Initialize(center, width, height, 0.0);
	}

	public RectangleFeature(PointM center, double width, double height, double angleRotation)
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
		Initialize(center, width, height, angleRotation);
	}

	protected void Initialize(PointM c, double width, double height, double angleRotation)
	{
		_center = c;
		_width = width;
		_height = height;
		_maxX = c.X + width / 2.0;
		_minX = c.X - width / 2.0;
		_maxY = c.Y + height / 2.0;
		_minY = c.Y - height / 2.0;
		_angleRotation = angleRotation;
		MakeModificationBoundingBox(Center);
		CanDrawModificationBoundingBox = true;
	}

	public override bool Contains(Polyline pline)
	{
		return ModificationBoundingBox.Contains(pline);
	}

	public override bool Contains(PointM ptm)
	{
		return ModificationBoundingBox.Contains(ptm);
	}

	public override List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline)
	{
		return ModificationBoundingBox.GetContainedSubsegments(polyline);
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
		if (feature is RectangleFeature)
		{
			RectangleFeature rectangleFeature = (RectangleFeature)feature;
			if (rectangleFeature.Width == Width && rectangleFeature.Height == Height && rectangleFeature.Center.EqualXY(Center) && rectangleFeature.AngleRotation == AngleRotation)
			{
				return true;
			}
			return false;
		}
		return false;
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		return ModificationBoundingBox.Intersects(PointM, nearDistance);
	}

	public override bool Intersects(Extent extent)
	{
		return ModificationBoundingBox.Intersects(extent);
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>();
	}

	public override void MD5HashAppend(Hash hash)
	{
		hash.Add(new double[5] { Center.X, Center.Y, Width, Height, AngleRotation });
	}

	public override IEditableFeature GetMutableView()
	{
		return new RectangleEditable(this);
	}

	public static bool IsValid(RectangleFeature rect)
	{
		if (rect == null)
		{
			return false;
		}
		if (double.IsNaN(rect.Center.X) || double.IsNaN(rect.Center.Y) || double.IsNaN(rect.Width) || double.IsNaN(rect.Height))
		{
			return false;
		}
		return true;
	}

	public override Extent GetUnrotatedBoundingBox()
	{
		return GetExtent();
	}

	public override void ScaleFeature(Transform transformer, ModificationBoundingBoxIndices index)
	{
		throw new NotImplementedException("System.Drawing.Matrix removed");
	}

	public override void RotateFeature(Transform transformer)
	{
		double angleRotationAngleRotationDegreesTerm = (_angleRotation - ((Affine)transformer).AngleRotationDegrees) % 360.0;
		if (angleRotationAngleRotationDegreesTerm < 0.0)
		{
			angleRotationAngleRotationDegreesTerm += 360.0;
		}
		Initialize(Center, Width, Height, angleRotationAngleRotationDegreesTerm);
	}

	public override Polygon DiscretizeToPolygon()
	{
		PointMs pointMs = new PointMs();
		pointMs.Add(ModificationBoundingBox[0]);
		pointMs.Add(ModificationBoundingBox[2]);
		pointMs.Add(ModificationBoundingBox[4]);
		pointMs.Add(ModificationBoundingBox[6]);
		pointMs.Add(ModificationBoundingBox[0]);
		return new Polygon(pointMs);
	}

	public override Feature SetZAll(double z)
	{
		return new RectangleFeature(new PointM(Center.X, Center.Y, z, Center.M), Width, Height, AngleRotation);
	}

	public override Feature SetMAll(double m)
	{
		return new RectangleFeature(new PointM(Center.X, Center.Y, Center.Z, m), Width, Height, AngleRotation);
	}

	public override IList<double> GetZPerVertex()
	{
		return new double[1] { Center.Z };
	}

	public override IList<double> GetMPerVertex()
	{
		return new double[1] { Center.M };
	}

	public override Feature SetZPerVertex(IList<double> zs)
	{
		return SetZAll(zs[0]);
	}

	public override Feature SetMPerVertex(IList<double> ms)
	{
		return SetMAll(ms[0]);
	}
}
