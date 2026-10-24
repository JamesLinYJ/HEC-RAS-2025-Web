using System.Collections.Generic;

namespace Ras.Migration.Native;

public class HDFWriteParams
{
	public bool WriteWarmup;

	public bool WriteTimeSlices;

	public bool Flush;

	public List<string> AdditionalOutputVars = new List<string>();

	public string Compression;

	public string ChunkSize;

	public string SpatialParts;

	public bool UseMaxRows;

	public string FixedRows;
}
