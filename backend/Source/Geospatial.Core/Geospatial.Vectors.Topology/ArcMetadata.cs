namespace Geospatial.Vectors.Topology;

public struct ArcMetadata
{
	public const LinearSizeFunction DefaultSizeFunction = LinearSizeFunction.Constant;

	public static readonly float DefaultConstantSize = float.NaN;

	public const int DefaultCount = 8;

	public const float DefaultBias = 1f;

	public const ConstrainStretchingOptions DefaultConstrainStretching = ConstrainStretchingOptions.Default;

	public const LaplaceBCOptions DefaultLaplaceBC = LaplaceBCOptions.Default;

	public const bool DefaultDistanceStraightLine = true;

	public const float DefaultAreaWeight = 0f;

	public const float DefaultDiscretizeMagnetism = 0.1f;

	public const bool DefaultFollowExactly = true;

	public const float DefaultArcAngle = 0f;

	public LinearSizeFunction SizeFunction;

	public float ConstantSize;

	public float ComputedConstantSize;

	public int Count;

	public float Bias;

	public ConstrainStretchingOptions ConstrainStretching;

	public LaplaceBCOptions LaplaceBC;

	public bool DistanceStraightLine;

	public float AreaWeight;

	public float DiscretizeMagnetism;

	public bool FollowExactly;

	public float ArcAngleLeft;

	public float ArcAngleRight;

	public float ActualPrimaryCellSize
	{
		get
		{
			if (!float.IsNaN(ConstantSize))
			{
				return ConstantSize;
			}
			return ComputedConstantSize;
		}
	}
}
