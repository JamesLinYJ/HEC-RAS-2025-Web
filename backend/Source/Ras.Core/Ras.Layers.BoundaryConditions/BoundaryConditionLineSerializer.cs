using System.Collections.Generic;
using Geospatial.IO.ORM;
using Geospatial.Vectors;
using H5Assist;
using Ras.Layers.Payloads;
using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

public class BoundaryConditionLineSerializer
{
	public class BoundaryConditionLineEntity
	{
		[DatasetName("Polyline")]
		public Polyline Polyline { get; set; }

		[Metadata("Name")]
		public string Name { get; set; }

		[DatasetName("Description")]
		public string Description { get; set; }

		[Metadata("Enabled")]
		public bool IsEnabled { get; set; }

		[Metadata("Start")]
		public int PayloadStartIndex { get; set; }

		[Metadata("Count")]
		public int PayloadCount { get; set; }

		public static BoundaryConditionLineEntity From(BoundaryConditionLine bcl)
		{
			return new BoundaryConditionLineEntity
			{
				Name = bcl.Name,
				Description = bcl.Description,
				Polyline = bcl.Polyline,
				IsEnabled = bcl.IsEnabled,
				PayloadCount = bcl.Payloads.Count
			};
		}
	}

	public List<BoundaryConditionLineEntity> BoundaryConditionLines { get; set; }

	public PayloadSerializer Payloads { get; set; }

	public static BoundaryConditionLineSerializer From(IReadOnlyList<BoundaryConditionLine> bcLines)
	{
		BoundaryConditionLineSerializer boundaryConditionLineSerializer = new BoundaryConditionLineSerializer();
		boundaryConditionLineSerializer.BoundaryConditionLines = new List<BoundaryConditionLineEntity>(bcLines.Count);
		boundaryConditionLineSerializer.Payloads = PayloadSerializer.From(bcLines);
		int payloadCountSum = 0;
		foreach (BoundaryConditionLine bcLine in bcLines)
		{
			BoundaryConditionLineEntity boundaryConditionLineEntity = BoundaryConditionLineEntity.From(bcLine);
			boundaryConditionLineEntity.PayloadStartIndex = payloadCountSum;
			payloadCountSum += boundaryConditionLineEntity.PayloadCount;
			boundaryConditionLineSerializer.BoundaryConditionLines.Add(boundaryConditionLineEntity);
		}
		return boundaryConditionLineSerializer;
	}

	public static void Save(List<BoundaryConditionLine> bcLines, H5Writer h5w, string group)
	{
		From(bcLines).Save(h5w, group);
	}

	public void Save(H5Writer h5w, string group)
	{
		FeatureIO.Write(h5w, group, BoundaryConditionLines);
		Payloads.Save(h5w, group + "/Payloads");
	}

	public static List<BoundaryConditionLine> Load(H5Reader h5r, string group)
	{
		BoundaryConditionLineSerializer boundaryConditionLineSerializer = LoadInternal(h5r, group);
		PayloadSerializer payloads = boundaryConditionLineSerializer.Payloads;
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>(boundaryConditionLineSerializer.BoundaryConditionLines.Count);
		for (int i = 0; i < boundaryConditionLineSerializer.BoundaryConditionLines.Count; i++)
		{
			BoundaryConditionLineEntity boundaryConditionLineEntity = boundaryConditionLineSerializer.BoundaryConditionLines[i];
			BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine();
			boundaryConditionLine.Polyline = boundaryConditionLineEntity.Polyline;
			boundaryConditionLine.Name = boundaryConditionLineEntity.Name;
			boundaryConditionLine.Description = boundaryConditionLineEntity.Description;
			boundaryConditionLine.IsEnabled = boundaryConditionLineEntity.IsEnabled;
			payloads.LoadInto(boundaryConditionLine, boundaryConditionLineEntity.PayloadStartIndex, boundaryConditionLineEntity.PayloadCount);
			boundaryConditionLines.Add(boundaryConditionLine);
		}
		return boundaryConditionLines;
	}

	private static BoundaryConditionLineSerializer LoadInternal(H5Reader h5r, string group)
	{
		BoundaryConditionLineSerializer boundaryConditionLineSerializer = new BoundaryConditionLineSerializer();
		boundaryConditionLineSerializer.BoundaryConditionLines = new List<BoundaryConditionLineEntity>();
		boundaryConditionLineSerializer.Payloads = new PayloadSerializer();
		boundaryConditionLineSerializer.BoundaryConditionLines = FeatureIO.Read<BoundaryConditionLineEntity>(h5r, group);
		boundaryConditionLineSerializer.Payloads = PayloadSerializer.Load(h5r, group + "/Payloads");
		return boundaryConditionLineSerializer;
	}
}
