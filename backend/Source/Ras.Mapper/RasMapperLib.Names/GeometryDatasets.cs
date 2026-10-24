namespace RasMapperLib.Names;

public class GeometryDatasets
{
	public class CrossSections
	{
		public const string BASE = "Geometry/Cross Sections/";

		public const string PolylineInfo = "Geometry/Cross Sections/Polyline Info";

		public const string PolylinePoints = "Geometry/Cross Sections/Polyline Points";
	}

	public class ReferenceLines
	{
		public class HydrographLocations
		{
			public class ProfileLines
			{
				public const string Path = "Reference Lines/Hydrograph Locations/Profile Lines";

				public const string Attributes = "Reference Lines/Hydrograph Locations/Profile Lines/Attributes";

				public const string Faces = "Reference Lines/Hydrograph Locations/Profile Lines/Faces";

				public const string XS = "Reference Lines/Hydrograph Locations/Profile Lines/Cross Sections";

				public const string Polylines = "Reference Lines/Hydrograph Locations/Profile Lines/Polylines";
			}

			public const string Path = "Reference Lines/Hydrograph Locations";

			public const string D2FlowAreas = "Reference Lines/Hydrograph Locations/2D Flow Areas";

			public const string XS = "Reference Lines/Hydrograph Locations/Cross Sections";

			public const string XSFaceIndices = "Reference Lines/Hydrograph Locations/Cross Sections/Face Indices";

			public static string MeshName(RASD2FlowArea d2FA, int idx)
			{
				return "Reference Lines/Hydrograph Locations/2D Flow Areas/" + d2FA.GetFeatureName(idx);
			}

			public static string MeshFaceIndices(RASD2FlowArea d2FA, int idx)
			{
				return MeshName(d2FA, idx) + "/Face Indices";
			}

			public static string MeshCellIndices(RASD2FlowArea d2FA, int idx)
			{
				return MeshName(d2FA, idx) + "/Cell Indices";
			}
		}

		public const string Path = "Reference Lines/";
	}

	private const string BASE = "Geometry/";
}
