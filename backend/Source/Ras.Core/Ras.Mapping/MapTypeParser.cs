using Utility.Extensions;

namespace Ras.Mapping;

public static class MapTypeParser
{
	public static bool TryParse(string mtStr, out MapTypes mt)
	{
		if (EQS(new string[3] { "ws", "wsel", "watersurface" }))
		{
			mt = MapTypes.WaterSurfaceElevation;
			return true;
		}
		if (EQS(new string[2] { "depth", "d" }))
		{
			mt = MapTypes.Depth;
			return true;
		}
		if (EQS(new string[3] { "velocity", "vel", "v" }))
		{
			mt = MapTypes.Velocity;
			return true;
		}
		if (EQS(new string[3] { "depthtimesvelocity", "dv", "depthvelocity" }))
		{
			mt = MapTypes.DepthTimesVelocity;
			return true;
		}
		mt = MapTypes.Unknown;
		return false;
		bool EQ(string textValue)
		{
			return mtStr.RoughlyEquals(textValue);
		}
		bool EQS(params string[] args)
		{
			for (int i = 0; i < args.Length; i++)
			{
				if (EQ(args[i]))
				{
					return true;
				}
			}
			return false;
		}
	}
}
