using System.Linq;
using System.Text.Json;
using Ras.Layers.Results;
using Ras.Output;
using Utility.Files;

namespace Ras.Layers.Helpers;

public class ResultInfo : IJsonSerializable
{
	public string SchemaVersion { get; set; } = "1.0.0";

	public string Name { get; set; }

	public string FileSHA256 { get; set; }

	public ComputeSummary ComputeSummary { get; set; }

	public GeometryInfo Geometry { get; set; }

	public OutputBlockInfo[] OutputBlocks { get; set; }

	public CachedProjectInfo CachedProjectInfo { get; set; }

	public static ResultInfo From(string filename, CoreFileOpenOptions opts = null)
	{
		if (opts == null)
		{
			opts = CoreFileOpenOptions.Default();
		}
		string fileSHA = FileHelpers.ComputeFileSHA256(filename);
		using Result result = new Result(filename);
		ResultInfo resultInfo = new ResultInfo();
		resultInfo.Name = result.Name;
		resultInfo.FileSHA256 = fileSHA;
		resultInfo.Geometry = GeometryInfo.From(result.Geometry, new CoreFileOpenOptions
		{
			LoadCachedProjectInfo = false,
			ComputeFileHash = false
		});
		resultInfo.ComputeSummary = result.ComputeSummary;
		if (opts.LoadCachedProjectInfo)
		{
			result.LoadProjectMetadataFromH5Header(out var projectName, out var projection, out var units);
			resultInfo.CachedProjectInfo = new CachedProjectInfo(projectName, units, projection);
		}
		resultInfo.OutputBlocks = (from ob in result.OutputBlocks
			where ob is OutputBlock
			select new OutputBlockInfo(ob as OutputBlock)).ToArray();
		return resultInfo;
	}

	public string ToJson()
	{
		return JsonSerializer.Serialize(this, ProjectInfo.DefaultSerializerOptions);
	}
}
