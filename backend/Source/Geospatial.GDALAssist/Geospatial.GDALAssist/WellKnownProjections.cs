using System;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.GDALAssist;

public static class WellKnownProjections
{
	private static readonly object _lock = new object();

	public const double MERCATOR_EDGE = Math.PI * 6378137.0;

	private static Projection _wgs84;

	private static Projection _webMerc;

	public static readonly string MuncieWKT = "PROJCS[\"NAD83 / Indiana East (ftUS)\",GEOGCS[\"NAD83\",DATUM[\"North_American_Datum_1983\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"6269\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4269\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",37.5],PARAMETER[\"central_meridian\",-85.6666666666667],PARAMETER[\"scale_factor\",0.999966667],PARAMETER[\"false_easting\",328083.333],PARAMETER[\"false_northing\",820208.333],UNIT[\"US survey foot\",0.304800609601219,AUTHORITY[\"EPSG\",\"9003\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"2965\"]]";

	private static Projection _muncie;

	public static readonly string BaldEagleWKT = "PROJCS[\"NAD_1983_StatePlane_Pennsylvania_North_FIPS_3701_Feet\",GEOGCS[\"NAD83\",DATUM[\"North_American_Datum_1983\",SPHEROID[\"GRS 1980\",6378137,298.257222101004,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"6269\"]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4269\"]],PROJECTION[\"Lambert_Conformal_Conic_2SP\"],PARAMETER[\"latitude_of_origin\",40.1666666666667],PARAMETER[\"central_meridian\",-77.75],PARAMETER[\"standard_parallel_1\",40.8833333333333],PARAMETER[\"standard_parallel_2\",41.95],PARAMETER[\"false_easting\",1968500],PARAMETER[\"false_northing\",0],UNIT[\"US survey foot\",0.304800609601219,AUTHORITY[\"EPSG\",\"9003\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH]]";

	private static Projection _baldEagle;

	public static readonly Extent CONUSExtentWGS84 = new Extent(-124.74, -66.95, 24.52, 49.38);

	public static readonly Extent CONUSExtentWebMercator = new Extent(-13885993.0, -7452839.0, 2816901.0, 6339587.0);

	public static readonly Extent DavisWebMercator = new Extent(-13559503.0, -13544386.0, 4651270.0, 4662091.0);

	private static Projection _albersNA;

	public static string USGSConusAlbersWKT = "PROJCRS[\"NAD83(2011) / Conus Albers\",\r\nBASEGEOGCRS[\"NAD83(2011)\",\r\n    DATUM[\"NAD83 (National Spatial Reference System 2011)\",\r\n        ELLIPSOID[\"GRS 1980\",6378137,298.257222101,\r\n            LENGTHUNIT[\"metre\",1]]],\r\n    PRIMEM[\"Greenwich\",0,\r\n        ANGLEUNIT[\"degree\",0.0174532925199433]],\r\n    ID[\"EPSG\",6318]],\r\nCONVERSION[\"Conus Albers\",\r\n    METHOD[\"Albers Equal Area\",\r\n        ID[\"EPSG\",9822]],\r\n    PARAMETER[\"Latitude of false origin\",23,\r\n        ANGLEUNIT[\"degree\",0.0174532925199433],\r\n        ID[\"EPSG\",8821]],\r\n    PARAMETER[\"Longitude of false origin\",-96,\r\n        ANGLEUNIT[\"degree\",0.0174532925199433],\r\n        ID[\"EPSG\",8822]],\r\n    PARAMETER[\"Latitude of 1st standard parallel\",29.5,\r\n        ANGLEUNIT[\"degree\",0.0174532925199433],\r\n        ID[\"EPSG\",8823]],\r\n    PARAMETER[\"Latitude of 2nd standard parallel\",45.5,\r\n        ANGLEUNIT[\"degree\",0.0174532925199433],\r\n        ID[\"EPSG\",8824]],\r\n    PARAMETER[\"Easting at false origin\",0,\r\n        LENGTHUNIT[\"metre\",1],\r\n        ID[\"EPSG\",8826]],\r\n    PARAMETER[\"Northing at false origin\",0,\r\n        LENGTHUNIT[\"metre\",1],\r\n        ID[\"EPSG\",8827]]],\r\nCS[Cartesian,2],\r\n    AXIS[\"easting (X)\",east,\r\n        ORDER[1],\r\n        LENGTHUNIT[\"metre\",1]],\r\n    AXIS[\"northing (Y)\",north,\r\n        ORDER[2],\r\n        LENGTHUNIT[\"metre\",1]],\r\nUSAGE[\r\n    SCOPE[\"Data analysis and small scale data presentation for contiguous lower 48 states.\"],\r\n    AREA[\"United States (USA) - CONUS onshore - Alabama; Arizona; Arkansas; California; Colorado; Connecticut; Delaware; Florida; Georgia; Idaho; Illinois; Indiana; Iowa; Kansas; Kentucky; Louisiana; Maine; Maryland; Massachusetts; Michigan; Minnesota; Mississippi; Missouri; Montana; Nebraska; Nevada; New Hampshire; New Jersey; New Mexico; New York; North Carolina; North Dakota; Ohio; Oklahoma; Oregon; Pennsylvania; Rhode Island; South Carolina; South Dakota; Tennessee; Texas; Utah; Vermont; Virginia; Washington; West Virginia; Wisconsin; Wyoming.\"],\r\n    BBOX[24.41,-124.79,49.38,-66.91]],\r\nID[\"EPSG\",6350]]";

	private static Projection _usgsConusAlbers;

	public static Projection WGS84 => LazyThreadsafe<Projection>.AllocateOnce(ref _wgs84, _lock, () => new EPSGProjection(4326));

	public static Projection WebMercator => LazyThreadsafe<Projection>.AllocateOnce(ref _webMerc, _lock, () => new EPSGProjection(900913));

	public static Projection Muncie => LazyThreadsafe<Projection>.AllocateOnce(ref _muncie, _lock, () => new WKTProjection(MuncieWKT));

	public static Projection BaldEagle => LazyThreadsafe<Projection>.AllocateOnce(ref _baldEagle, _lock, () => new WKTProjection(BaldEagleWKT));

	public static Extent WebMercatorExtents => new Extent(Math.PI * -6378137.0, Math.PI * 6378137.0, Math.PI * -6378137.0, Math.PI * 6378137.0);

	public static Projection AlbersNorthAmerica => LazyThreadsafe<Projection>.AllocateOnce(ref _albersNA, _lock, () => new Proj4Projection("+proj=aea +lat_0=23 +lat_1=29.5 +lat_2=42.5 +lon_0=-96"));

	public static Projection USGSConusAlbers => LazyThreadsafe<Projection>.AllocateOnce(ref _usgsConusAlbers, _lock, () => new WKTProjection(USGSConusAlbersWKT));
}
