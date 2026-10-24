using System;
using System.IO;
using H5Assist;
using Ras.Hydraulics.HydraulicDesign;
using Utility.ORM;

namespace Ras.Layers;

public class StableChannelDesign : CoreLayerBase
{
	public const string Extension = ".scd.h5";

	public const string ExtensionSearchPattern = "*.scd.h5";

	public const string HDF5Group = "/StableChannelDesign";

	public const string FileVersion = "1.0-dev";

	[ComplexMetadata("Copeland Method")]
	public CopelandMethod Copeland { get; set; } = new CopelandMethod();

	[ComplexMetadata("Regime Method")]
	public RegimeMethod Regime { get; set; } = new RegimeMethod();

	[ComplexMetadata("Tractive Force")]
	public TractiveForceMethod TractiveForce { get; set; } = new TractiveForceMethod();

	protected override string GetHDF5Group()
	{
		return "/StableChannelDesign";
	}

	public override string GetCurrentFileFormatVersion()
	{
		return "1.0-dev";
	}

	public static string MakeFilename(string projectDir, string name)
	{
		return Project.MakeFilename<StableChannelDesign>(projectDir, name, ".scd.h5");
	}

	protected StableChannelDesign(InstantiationMode mode)
		: base(mode)
	{
	}

	internal StableChannelDesign(string filename)
		: base(filename)
	{
	}

	public static StableChannelDesign CreateNew(string filename)
	{
		if (string.IsNullOrEmpty(filename))
		{
			throw new ArgumentNullException("filename");
		}
		if (File.Exists(filename))
		{
			throw new Exception(filename + " must not exist.");
		}
		return new StableChannelDesign(InstantiationMode.CreateNewFile)
		{
			Filename = filename,
			_name = Path.GetFileNameWithoutExtension(filename)
		};
	}

	public static StableChannelDesign CreateInMemory(string name = "")
	{
		return new StableChannelDesign(InstantiationMode.InMemory)
		{
			_name = name
		};
	}

	protected override bool NeedsSaveInternal()
	{
		return false;
	}

	protected override void SaveInternal(H5Writer hw)
	{
	}
}
