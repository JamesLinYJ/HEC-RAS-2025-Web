using Utility.Hashing;
using Utility.ORM;
using Utility.Units;

namespace Geospatial.Vectors.Meshes;

public class PropertyTableOptions : IDataHash
{
	public const double DefaultFaceConveyanceTolerance = 0.019999999552965164;

	public const double MinimumFaceConveyanceTolerance = 9.999999747378752E-05;

	public const double DefaultCellMinAreaFraction = 0.009999999776482582;

	public bool UseSimpleTables;

	public int SampledPointsPerCell = 1000;

	public int BinsPerCell = 256;

	public int SampledPointsPerFace = 100;

	[HashContributor]
	[Metadata("Cell Vol Tol")]
	public double CellVolumeFilterTolerance { get; set; }

	[HashContributor]
	[Metadata("Face Conv Ratio")]
	public double FaceAreaConveyanceRatio { get; set; }

	[HashContributor]
	[Metadata("Cell Min Area Fraction")]
	public double CellMinAreaFraction { get; set; }

	[HashContributor]
	[Metadata("Face Profile Tol")]
	public double FaceProfileFilterTolerance { get; set; }

	[HashContributor]
	[Metadata("Face Area Tol")]
	public double FaceAreaElevFilterTolerance { get; set; }

	[HashContributor]
	[Metadata("Laminar Depth")]
	public double FaceLaminarDepthTolerance { get; set; }

	public static double DefaultCellVolumeFilterTolerance(UnitSystem units)
	{
		if (units == UnitSystem.SI)
		{
			return 0.003;
		}
		return 0.01;
	}

	public static double DefaultFaceProfileFilterTolerance(UnitSystem units)
	{
		if (units == UnitSystem.SI)
		{
			return 0.003;
		}
		return 0.01;
	}

	public static double DefaultFaceAreaElevFilterTolerance(UnitSystem units)
	{
		if (units == UnitSystem.SI)
		{
			return 0.003;
		}
		return 0.01;
	}

	public static double DefaultLaminarDepthTolerance(UnitSystem units)
	{
		if (units == UnitSystem.SI)
		{
			return 0.06;
		}
		return 0.01;
	}

	public static PropertyTableOptions Default(UnitSystem units)
	{
		return new PropertyTableOptions
		{
			CellVolumeFilterTolerance = DefaultCellVolumeFilterTolerance(units),
			FaceAreaConveyanceRatio = 0.019999999552965164,
			CellMinAreaFraction = 0.009999999776482582,
			FaceProfileFilterTolerance = DefaultFaceProfileFilterTolerance(units),
			FaceAreaElevFilterTolerance = DefaultFaceAreaElevFilterTolerance(units),
			FaceLaminarDepthTolerance = DefaultLaminarDepthTolerance(units)
		};
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public PropertyTableOptions Clone()
	{
		return new PropertyTableOptions
		{
			CellVolumeFilterTolerance = CellVolumeFilterTolerance,
			FaceAreaConveyanceRatio = FaceAreaConveyanceRatio,
			CellMinAreaFraction = CellMinAreaFraction,
			FaceProfileFilterTolerance = FaceProfileFilterTolerance,
			FaceAreaElevFilterTolerance = FaceAreaElevFilterTolerance,
			FaceLaminarDepthTolerance = FaceLaminarDepthTolerance,
			UseSimpleTables = UseSimpleTables
		};
	}
}
