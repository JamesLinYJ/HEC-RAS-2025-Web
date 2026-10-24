using System.IO;
using System.Text.Json;
using Utility.Files;

namespace Ras.Layers.Helpers;

public abstract class CoreLayerInfo : IJsonSerializable
{
	public string SchemaVersion { get; set; } = "1.0.0";

	public string Name { get; set; }

	public string FileSHA256 { get; set; }

	public string InternalDateLastModified { get; set; }

	public string DateCreated { get; set; }

	public string Description { get; set; }

	public string SoftwareVersion { get; set; }

	public string FileVersion { get; set; }

	public CachedProjectInfo CachedProjectInfo { get; set; }

	public CoreLayerInfo()
	{
	}

	public void Init(CoreLayerBase clb, CoreFileOpenOptions opts = null)
	{
		if (opts == null)
		{
			opts = CoreFileOpenOptions.Default();
		}
		string fileSHA = "";
		if (opts.ComputeFileHash && File.Exists(clb.Filename))
		{
			fileSHA = FileHelpers.ComputeFileSHA256(clb.Filename);
		}
		Name = clb.Name;
		FileSHA256 = fileSHA;
		InternalDateLastModified = clb.DateLastModified;
		DateCreated = clb.DateCreated;
		Description = clb.Description;
		FileVersion = clb.LoadedFileVersion;
		SoftwareVersion = clb.LoadedSoftwareVersion;
		if (opts.LoadCachedProjectInfo)
		{
			clb.LoadProjectMetadataFromH5Header(out var projectName, out var projection, out var units);
			CachedProjectInfo = new CachedProjectInfo(projectName, units, projection);
		}
	}

	public string ToJson()
	{
		return JsonSerializer.Serialize(this, GetType(), ProjectInfo.DefaultSerializerOptions);
	}
}
