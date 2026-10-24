namespace RasMapperLib.TinHelpers;

public enum TriangleFlags : byte
{
	E0Break = 1,
	E1Break = 2,
	E2Break = 4,
	E0Perim = 8,
	E1Perim = 16,
	E2Perim = 32,
	Deleted = 64,
	AnyPerimOrBreak = 63
}
