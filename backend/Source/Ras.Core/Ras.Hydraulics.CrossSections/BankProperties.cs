using System;
using System.Diagnostics;
using System.Linq;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.CrossSections;

[DebuggerDisplay("{LeftStation}, {RightStation}")]
public class BankProperties : IDataHash
{
	private readonly Func<Polyline> _getPolyline;

	private long _attributeVersion;

	public long AttributeVersion => _attributeVersion;

	[HashContributor]
	[Metadata("Left Bank")]
	public double LeftStation
	{
		get => field;
		set
		{
			if (!field.Equals(value))
			{
				field = value;
				_attributeVersion++;
			}
		}
	}

	public double CenterStation
	{
		get => field;
		set
		{
			if (!field.Equals(value))
			{
				field = value;
				_attributeVersion++;
			}
		}
	}

	public bool HasCenterStation => !double.IsNaN(CenterStation);

	[HashContributor]
	[Metadata("Right Bank")]
	public double RightStation
	{
		get => field;
		set
		{
			if (!field.Equals(value))
			{
				field = value;
				_attributeVersion++;
			}
		}
	}

	public Point LeftBankPoint
	{
		get
		{
			Polyline polyline = _getPolyline();
			if (polyline == null || !polyline.IsValidPolyline())
			{
				return Point.NullPoint;
			}
			double leftStation = LeftStation;
			if (double.IsNaN(leftStation) || leftStation <= 0.0)
			{
				return polyline.Points.First();
			}
			PointOnPolyline pointOnPolyline = polyline.PointAtDistance(leftStation);
			if (pointOnPolyline.IsNull())
			{
				return polyline.LastPointOnPolyline().Point;
			}
			return pointOnPolyline.Point;
		}
	}

	public Point RightBankPoint
	{
		get
		{
			Polyline polyline = _getPolyline();
			if (polyline == null || !polyline.IsValidPolyline())
			{
				return Point.NullPoint;
			}
			double length = polyline.Length;
			double rightStation = RightStation;
			if (double.IsNaN(rightStation) || rightStation >= length)
			{
				return polyline.Points.Last();
			}
			if (rightStation <= 0.0)
			{
				return polyline.Points.First();
			}
			PointOnPolyline pointOnPolyline = polyline.PointAtDistance(rightStation);
			if (pointOnPolyline.IsNull())
			{
				return polyline.LastPointOnPolyline().Point;
			}
			return pointOnPolyline.Point;
		}
	}

	public bool HasValidStations
	{
		get
		{
			if (!double.IsNaN(LeftStation))
			{
				return !double.IsNaN(RightStation);
			}
			return false;
		}
	}

	public BankProperties()
	{
	}

	public BankProperties(Func<Polyline> getPl)
	{
		_getPolyline = getPl;
	}

	public bool Matches(BankProperties other)
	{
		bool isNaNAndIsNaN = LeftStation == other.LeftStation || (double.IsNaN(LeftStation) && double.IsNaN(other.LeftStation));
		bool isNaNAndIsNaN2 = RightStation == other.RightStation || (double.IsNaN(RightStation) && double.IsNaN(other.RightStation));
		return isNaNAndIsNaN & isNaNAndIsNaN2;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
