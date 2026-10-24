namespace Ras;

public static class NamedResources
{
	public static class Terrain
	{
		public static class Modification
		{
			public static readonly string Base = "Terrain.Modification";

			public static readonly string AddNew = "Terrain.Modification.AddNew";

			public static readonly string AddNewLevee = Geometry.Levee.AddNew;
		}

		public static readonly string Base = "Terrain";

		public static readonly string AddNew = "Terrain.AddNew";

		public static readonly string Import = "Terrain.Import";

		public static readonly string Remove = "Terrain.Remove";
	}

	public static class LandCover
	{
		public static readonly string Base = "LandCover";

		public static readonly string AddNew = "LandCover.AddNew";
	}

	public static class Geometry
	{
		public static class Levee
		{
			public static readonly string Base = "Levee";

			public static readonly string AddNew = "Levee.AddNew";
		}

		public static readonly string Base = "Geometry";

		public static readonly string AddNew = "Geometry.AddNew";
	}

	public static class ResultMapping
	{
		public static readonly string Base = "ResultMap";

		public static readonly string AddNew = "ResultMap.AddNew";

		public static readonly string VelocityTracers = "ResultMap.VelocityTracers";
	}

	public static class WebMapImagery
	{
		public static readonly string Base = "WebMap";

		public static readonly string AddNew = "WebMap.AddNew";
	}
}
