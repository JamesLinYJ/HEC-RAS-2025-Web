using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Utility.Files;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers;

[DebuggerDisplay("{TypeName,nq}: {Name,nq}")]
public class TerrainInputSignature
{
	[Metadata("Name")]
	public string Name { get; set; }

	[Metadata("Type Name")]
	public string TypeName { get; set; }

	[Metadata("Data Hash")]
	public int DataHash { get; set; }

	[ComplexMetadata(Optional = true)]
	public FileSignature FileSignature { get; set; }

	public TerrainInputSignature()
	{
		FileSignature = new FileSignature();
	}

	public OperationResult Compare(TerrainInputSignature other, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (TypeName == "Raster File")
		{
			return FileSignature.Compare(other.FileSignature, FileCompareOptions.Hash, pr);
		}
		if (TypeName == "Constant" || TypeName == "Functional" || TypeName == "Levee")
		{
			if (DataHash == other.DataHash)
			{
				pr.ReportMessage("{TypeName} terrain data hash matches ({Hash}).", LogLevel.Debug, TypeName, DataHash);
				return true;
			}
			return OperationResult.Fail(new Message("Data hash mismatch: {ThisHash} vs {ThatHash}.", DataHash, other.DataHash));
		}
		if (TypeName == "Debug Terrain Input")
		{
			pr.ReportMessage("Debug terrain input - cannot verify, treating as matching.");
			return true;
		}
		throw new NotImplementedException("Terrain type not recognized: " + TypeName);
	}
}
