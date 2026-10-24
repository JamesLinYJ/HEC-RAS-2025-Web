using System;
using System.Collections.Generic;
using System.Linq;

namespace RasMapperLib;

public class MeshFeatureLayer : FeatureLayer
{
	private const string sCellCount = "Cells";

	private const string sFaceCount = "Faces";

	private const string sFacePointCount = "Facepoints";

	public override string SimpleFeatureTypeName { get; set; }

	public override bool CanExportAsShapefile { get; }

	public override bool CanExportAsPoints { get; }

	public override bool CanFeaturesBeCopied { get; }

	public int MeshCount => FeatureCount();

	public MeshFV2D this[int index] => Mesh(index);

	public MeshFeatureLayer(string name)
		: base(name)
	{
		SimpleFeatureTypeName = "2D Area";
		CanExportAsShapefile = false;
		CanExportAsPoints = false;
		CanFeaturesBeCopied = false;
	}

	public MeshFeatureLayer(string name, string filename)
		: base(name, filename)
	{
		SimpleFeatureTypeName = "2D Area";
		CanExportAsShapefile = false;
		CanExportAsPoints = false;
		CanFeaturesBeCopied = false;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Cells", typeof(int), _featureColumnGroup, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Faces", typeof(int), _featureColumnGroup, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Facepoints", typeof(int), _featureColumnGroup, DataColumnViewState.GrayedOut);
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.AddRange(MeshFV2D.GetAvailablePlotOptions(SharedData.DebugMode));
		return staticPlotOptions;
	}

	public IEnumerable<MeshFV2D> Meshes()
	{
		foreach (MeshFV2D meshFV2D in FeaturesOfType<MeshFV2D>())
		{
			yield return meshFV2D;
		}
	}

	public MeshFV2D Mesh(int fid)
	{
		if (!(Feature(fid) is MeshFV2D result))
		{
			return MeshFV2D.NullMesh();
		}
		return result;
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(MeshFV2D) }.ToList();
	}

	protected override void ResetFeatureAttributes(int fid)
	{
		FeatureRow(fid)["Cells"] = Mesh(fid).CellCount;
		FeatureRow(fid)["Faces"] = Mesh(fid).FaceCount;
		FeatureRow(fid)["Facepoints"] = Mesh(fid).FacePointCount;
	}
}
