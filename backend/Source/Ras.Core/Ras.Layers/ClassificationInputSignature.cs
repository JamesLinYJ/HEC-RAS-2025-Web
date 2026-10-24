using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Utility.Files;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Ras.Layers;

[DebuggerDisplay("{TypeName,nq}: {Name,nq}")]
public class ClassificationInputSignature
{
	[Metadata("Name")]
	public string Name { get; set; }

	[Metadata("Type Name")]
	public string TypeName { get; set; }

	[Metadata("Data Hash")]
	public int DataHash { get; set; }

	[ComplexMetadata(Optional = true)]
	public FileSignature FileSignature { get; set; }

	public ClassificationInputSignature()
	{
		FileSignature = new FileSignature();
	}

	public OperationResult Compare(ClassificationInputSignature other, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (Name != other.Name)
		{
			pr.ReportMessage("Classification input layer name changed: {FromName}->{ToName} (this will not invalidate the comparison).", LogLevel.Information, Name, other.Name);
		}
		if (TypeName == "Raster")
		{
			return FileSignature.Compare(other.FileSignature, FileCompareOptions.Hash, pr);
		}
		if (TypeName == "PolyClassifier")
		{
			if (DataHash == other.DataHash)
			{
				pr.ReportMessage("Polygon classification data hash matches ({Hash}).", LogLevel.Debug, DataHash);
				return true;
			}
			return OperationResult.Fail(new Message("Data hash mismatch: {ThisHash} vs {ThatHash}.", DataHash, other.DataHash));
		}
		throw new NotImplementedException();
	}
}
