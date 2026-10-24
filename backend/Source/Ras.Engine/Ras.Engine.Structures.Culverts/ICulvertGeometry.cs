namespace Ras.Engine.Structures.Culverts;

public interface ICulvertGeometry
{
	CulvertShape Shape { get; }

	double FullRise { get; }

	double Span { get; }

	double Length { get; }

	double Mann { get; }

	bool IsMannBottom { get; }

	double MannBottom { get; }

	double DepthMannBottom { get; }

	bool IsPartiallyBlocked { get; }

	double DepthBlocked { get; }
}
