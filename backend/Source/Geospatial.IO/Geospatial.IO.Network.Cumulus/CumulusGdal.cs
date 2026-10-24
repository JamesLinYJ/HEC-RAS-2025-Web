using Geospatial.GDALAssist;

namespace Geospatial.IO.Network.Cumulus;

public static class CumulusGdal
{
	public static void ConfigureHttpAuth(string bearerToken)
	{
		string isNullOrEmptyBearerTokenTerm = (string.IsNullOrEmpty(bearerToken) ? "" : ("Authorization: Bearer " + bearerToken));
		GDALSetup.SetConfigOption("GDAL_HTTP_HEADERS", isNullOrEmptyBearerTokenTerm);
		GDALSetup.SetConfigOption("GDAL_DISABLE_READDIR_ON_OPEN", "EMPTY_DIR");
		GDALSetup.SetConfigOption("GDAL_HTTP_MAX_RETRY", "3");
		GDALSetup.SetConfigOption("GDAL_HTTP_RETRY_DELAY", "1");
		GDALSetup.SetConfigOption("CPL_VSIL_CURL_CHUNK_SIZE", "1048576");
		GDALSetup.SetConfigOption("CPL_VSIL_CURL_CACHE_SIZE", "67108864");
		GDALSetup.SetConfigOption("GDAL_HTTP_MERGE_CONSECUTIVE_RANGES", "YES");
		GDALSetup.SetConfigOption("CPL_VSIL_CURL_USE_HEAD", "NO");
		GDALSetup.SetConfigOption("GDAL_HTTP_VERSION", "2");
		GDALSetup.SetConfigOption("GDAL_HTTP_MULTIPLEX", "YES");
	}
}
