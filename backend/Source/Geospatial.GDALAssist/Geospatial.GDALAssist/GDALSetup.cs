using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml;
using Microsoft.Extensions.Logging;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;
using Utility;
using Utility.Logging;

namespace Geospatial.GDALAssist;

public static class GDALSetup
{
	private static readonly ILogger _logger = LogHelper.GetLogger("GDALSetup");

	public static string RAS_VERSION_NUMBER = "0.1.0";

	private static volatile bool _isInitialized;

	private static bool _resolverRegistered;

	private static readonly object _initLock = new object();

	internal static string HECAppDataDirectory;

	public static readonly string[] ResamplingMethodNames = new string[6] { "near", "average", "bilinear", "cubic", "cubicspline", "lanczos" };

	private static readonly GdalConfigState _configState = new GdalConfigState();

	public static bool IsInitialized => _isInitialized;

	public static string GdalInitializedDataDirectory { get; private set; }

	public static string LoadedVersion => Gdal.VersionInfo("");

	public static string MappingDirectory { get; private set; }

	public static string XMLDirectory { get; private set; }

	public static string ToolDirectory { get; private set; }

	public static string ExpectedEnvironmentInitBatchFile => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(GdalInitializedDataDirectory)), "EnvironmentInit.bat");

	private static void SetupNativeLibraryResolver(string libDir)
	{
		if (!_resolverRegistered)
		{
			NativeLibrary.SetDllImportResolver(typeof(Gdal).Assembly, (string libraryName, Assembly assembly, DllImportSearchPath? searchPath) => ResolveGdalLibrary(libraryName, libDir));
			_resolverRegistered = true;
			_logger.LogDebug("Registered native library resolver for GDAL libraries in {libDir}", libDir);
		}
	}

	private static nint ResolveGdalLibrary(string libraryName, string libDir)
	{
		string textValue;
		string textValue2;
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			textValue = ".dll";
			textValue2 = "";
		}
		else
		{
			textValue = (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so");
			textValue2 = "lib";
		}
		string path = Path.Combine(libDir, textValue2 + libraryName + textValue);
		if (File.Exists(path))
		{
			_logger.LogDebug("Resolved {libraryName} to {libPath}", libraryName, path);
			if (NativeLibrary.TryLoad(path, out var handle))
			{
				return handle;
			}
		}
		path = Path.Combine(libDir, libraryName + textValue);
		if (File.Exists(path))
		{
			_logger.LogDebug("Resolved {libraryName} to {libPath}", libraryName, path);
			if (NativeLibrary.TryLoad(path, out var handle2))
			{
				return handle2;
			}
		}
		_logger.LogDebug("Could not resolve {libraryName}, falling back to default", libraryName);
		return IntPtr.Zero;
	}

	public static void InitializeMultiplatform()
	{
		if (!IsInitialized)
		{
			string gdalDirectory = FindGDALDirectory();
			if (string.IsNullOrEmpty(gdalDirectory))
			{
				lock (_initLock)
				{
					if (IsInitialized) return;
					MaxRev.Gdal.Core.GdalBase.ConfigureAll();
					string dataDirectory = Gdal.GetConfigOption("GDAL_DATA", Environment.GetEnvironmentVariable("GDAL_DATA"));
					InitializeCore(AppContext.BaseDirectory, AppContext.BaseDirectory, dataDirectory, "", "", packageConfigured: true);
				}
				return;
			}
			InitializeMultiplatform(gdalDirectory);
		}
	}

	public static string FindGDALDirectory()
	{
		string location = Assembly.GetExecutingAssembly().Location;
		_logger.LogDebug("Searching for GDAL path. Initial assembly location: {asm}", location);
		string path = Path.Combine((!string.IsNullOrEmpty(location)) ? Path.GetDirectoryName(location) : AppContext.BaseDirectory, "GDAL");
		if (Directory.Exists(path))
		{
			_logger.LogDebug("GDAL directory found: {dir}", path);
			return path;
		}
		path = Environment.GetEnvironmentVariable("RAS_GDAL");
		if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
		{
			_logger.LogDebug("GDAL directory found: {dir}", path);
			return path;
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			path = "C:\\Work\\GDAL";
			if (Directory.Exists(path))
			{
				_logger.LogDebug("GDAL directory found: {dir}", path);
				return path;
			}
		}
		else
		{
			path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Work", "GDAL");
			if (Directory.Exists(path))
			{
				_logger.LogDebug("GDAL directory found: {dir}", path);
				return path;
			}
		}
		return "";
	}

	public static void InitializeMultiplatform(string gdalDirectory)
	{
		if (IsInitialized)
		{
			return;
		}
		if (!Directory.Exists(gdalDirectory))
		{
			throw new DirectoryNotFoundException(gdalDirectory);
		}
		string path = Path.Combine(gdalDirectory, "common");
		if (!Directory.Exists(path))
		{
			throw new DirectoryNotFoundException(path);
		}
		string libDir2;
		string libDir;
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			libDir2 = (Environment.Is64BitProcess ? Path.Combine(gdalDirectory, "bin64") : Path.Combine(gdalDirectory, "bin32"));
			if (!Directory.Exists(libDir2) || Directory.GetFiles(libDir2).Length == 0)
			{
				libDir2 = Path.Combine(gdalDirectory, "bin");
			}
			libDir = libDir2;
		}
		else
		{
			libDir2 = Path.Combine(gdalDirectory, "bin");
			libDir = Path.Combine(gdalDirectory, "lib");
		}
		string dataDir = Path.Combine(path, "data");
		string wmsDir = Path.Combine(path, "Web Map Services");
		string pluginDir = Path.Combine(libDir2, "gdalplugins");
		Initialize(libDir2, libDir, dataDir, pluginDir, wmsDir);
		_logger.LogInformation("GDAL initialized from {Directory} ({Version})", gdalDirectory, LoadedVersion);
	}

	private static void Initialize(string toolDir, string libDir, string dataDir, string pluginDir, string wmsDir)
	{
		if (IsInitialized)
		{
			return;
		}
		lock (_initLock)
		{
			if (!IsInitialized)
			{
				SetupNativeLibraryResolver(libDir);
				InitializeCore(toolDir, libDir, dataDir, pluginDir, wmsDir);
			}
		}
	}

	private static void InitializeCore(string toolDir, string libDir, string dataDir, string pluginDir, string wmsDir, bool packageConfigured = false)
	{
		if (!Directory.Exists(toolDir))
		{
			throw new DirectoryNotFoundException(toolDir);
		}
		if (!Directory.Exists(dataDir))
		{
			throw new DirectoryNotFoundException(dataDir);
		}
		ToolDirectory = toolDir;
		string toolDirectory = ToolDirectory;
		if (toolDirectory[toolDirectory.Length - 1] != Path.DirectorySeparatorChar)
		{
			ToolDirectory += Path.DirectorySeparatorChar;
		}
		GdalInitializedDataDirectory = dataDir;
		Environment.SetEnvironmentVariable("GDAL_TIFF_OVR_BLOCKSIZE", "256");
		string isOSPlatformRuntimeInformationTerm = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
		Func<string, ProcessStartInfo> func = (string exeName) =>
		{
			exeName = packageConfigured && OperatingSystem.IsMacOS()
				? Path.Combine(AppContext.BaseDirectory, exeName)
				: Path.Combine(ToolDirectory, exeName);
			return (!File.Exists(exeName)) ? null : new ProcessStartInfo(exeName)
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
		};
		Executables.PsiWarp = func("gdalwarp" + isOSPlatformRuntimeInformationTerm);
		Executables.PsiTranslate = func("gdal_translate" + isOSPlatformRuntimeInformationTerm);
		Executables.PsiOgr = func("ogr2ogr" + isOSPlatformRuntimeInformationTerm);
		Executables.PsiGdalInfo = func("gdalinfo" + isOSPlatformRuntimeInformationTerm);
		Executables.PsiOgrInfo = func("ogrinfo" + isOSPlatformRuntimeInformationTerm);
		Executables.PsiAddo = func("gdaladdo" + isOSPlatformRuntimeInformationTerm);
		Executables.PsiDem = func("gdaldem" + isOSPlatformRuntimeInformationTerm);
		Executables.PsiContour = func("gdal_contour" + isOSPlatformRuntimeInformationTerm);
		Executables.PsiVRT = func("gdalbuildvrt" + isOSPlatformRuntimeInformationTerm);
		Executables.PsiRasterize = func("gdal_rasterize" + isOSPlatformRuntimeInformationTerm);
		string path = ((!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : (Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetEnvironmentVariable("APPDATA")));
		HECAppDataDirectory = Path.Combine(path, "HEC");
		MappingDirectory = Path.Combine(HECAppDataDirectory, "Mapping");
		XMLDirectory = Path.Combine(MappingDirectory, RAS_VERSION_NUMBER, "XML");
		if (!Directory.Exists(HECAppDataDirectory))
		{
			Directory.CreateDirectory(HECAppDataDirectory);
		}
		if (!Directory.Exists(MappingDirectory))
		{
			Directory.CreateDirectory(MappingDirectory);
		}
		if (!Directory.Exists(XMLDirectory))
		{
			Directory.CreateDirectory(XMLDirectory);
		}
		if (Directory.Exists(wmsDir))
		{
			string[] files = Directory.GetFiles(wmsDir);
			foreach (string textValue in files)
			{
				if (!string.Equals(Path.GetExtension(textValue), ".xml", StringComparison.CurrentCultureIgnoreCase))
				{
					continue;
				}
				try
				{
					XmlTextReader xmlTextReader = new XmlTextReader(textValue);
					XmlDocument xmlDocument = new XmlDocument();
					xmlDocument.Load(xmlTextReader);
					foreach (XmlNode elementsByTagName in xmlDocument.GetElementsByTagName("Path"))
					{
						elementsByTagName.InnerText = MappingDirectory;
					}
					xmlTextReader.Close();
					string filename = XMLDirectory + "//" + Path.GetFileName(textValue);
					xmlDocument.Save(filename);
				}
				catch
				{
				}
			}
		}
		OS.PrependToPathIfDNE(toolDir);
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && libDir != toolDir)
		{
			OS.PrependToPathIfDNE(libDir);
		}
		if (!packageConfigured)
		{
			Environment.SetEnvironmentVariable("GDAL_DATA", dataDir);
			Gdal.SetConfigOption("GDAL_DATA", dataDir);
			Environment.SetEnvironmentVariable("GDAL_DRIVER_PATH", pluginDir);
			Gdal.SetConfigOption("GDAL_DRIVER_PATH", pluginDir);
			Environment.SetEnvironmentVariable("PROJ_LIB", dataDir);
			Gdal.SetConfigOption("PROJ_LIB", dataDir);
			Osr.SetPROJSearchPath(dataDir);
		}
		Gdal.SetCacheMax(268435456);
		string pszValue = Path.GetTempPath();
		Gdal.SetConfigOption("CPL_TMPDIR", pszValue);
		Gdal.AllRegister();
		Ogr.RegisterAll();
		_isInitialized = true;
	}

	public static void ErrorHandler(int eclass, int code, nint msg)
	{
		if (msg != IntPtr.Zero)
		{
			Console.WriteLine(" Message:" + Marshal.PtrToStringAnsi(msg));
		}
	}

	public static void SetConfigOption(string key, string configOption)
	{
		_configState.SetConfigOption(key, configOption);
	}

	public static void RestoreConfigOptions()
	{
		_configState.Restore();
	}

	public static void Dispose()
	{
		lock (_initLock)
		{
			Gdal.GDALDestroyDriverManager();
			Projection.DisposeAll();
			_isInitialized = false;
		}
	}
}
