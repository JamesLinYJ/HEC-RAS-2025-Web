using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geospatial.Vectors.MeshTopology;

[JsonConverter(typeof(SizeMeshJsonConverter))]
public class SizeMesh : DynamicMesh
{
	public class SizeMeshJsonConverter : JsonConverter<SizeMesh>
	{
		public override SizeMesh Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			throw new JsonException();
		}

		public override void Write(Utf8JsonWriter writer, SizeMesh obj, JsonSerializerOptions options)
		{
			string json = JsonSerializer.Serialize((object)obj.SimpleRepr(), options);
			writer.WriteRawValue(json);
		}
	}

	public List<float> NodeScale;

	public List<float> FaceScale;

	public List<float> CellScale;

	public DynamicTINFactory<FilteredPredicates> DTin;

	public override int AddNode(Point point)
	{
		int result = base.AddNode(point);
		NodeScale.Add(0f);
		return result;
	}

	public override void DeleteNode(int nIdx)
	{
		NodeScale[nIdx] = float.NaN;
		base.DeleteNode(nIdx);
	}

	public override int AddFace(int nodeA, int nodeB, int cellA = -99, int cellB = -99)
	{
		int result = base.AddFace(nodeA, nodeB, cellA, cellB);
		FaceScale.Add(0f);
		return result;
	}

	public override void DeleteFace(int fIdx, bool checkCells = true)
	{
		FaceScale[fIdx] = float.NaN;
		base.DeleteFace(fIdx, checkCells);
	}

	public override int AddCell()
	{
		int result = base.AddCell();
		CellScale.Add(0f);
		return result;
	}

	public override void DeleteCell(int cIdx)
	{
		CellScale[cIdx] = float.NaN;
		base.DeleteCell(cIdx);
	}

	public SizeMesh(DynamicMesh dMesh)
		: base(dMesh)
	{
		NodeScale = Enumerable.Repeat(0f, NodeCount).ToList();
		FaceScale = Enumerable.Repeat(0f, FaceCount).ToList();
		CellScale = Enumerable.Repeat(0f, CellCount).ToList();
	}

	public SizeMesh()
	{
		NodeScale = new List<float>();
		FaceScale = new List<float>();
		CellScale = new List<float>();
	}

	public override void UpdateFaceFromJson(int fIdx, JsonElement rec)
	{
		base.UpdateFaceFromJson(fIdx, rec);
		if (rec.TryGetProperty("scale", out var localValue))
		{
			FaceScale[fIdx] = (float)localValue.GetDouble();
		}
	}

	public override Dictionary<string, object> FaceSimpleRepr(int fIdx)
	{
		Dictionary<string, object> faceSimpleRepr = base.FaceSimpleRepr(fIdx);
		faceSimpleRepr["scale"] = FaceScale[fIdx];
		return faceSimpleRepr;
	}

	public override void UpdateNodeFromJson(int nIdx, JsonElement rec)
	{
		base.UpdateNodeFromJson(nIdx, rec);
		if (rec.TryGetProperty("scale", out var localValue))
		{
			NodeScale[nIdx] = (float)localValue.GetDouble();
		}
	}

	public override Dictionary<string, object> NodeSimpleRepr(int nIdx)
	{
		Dictionary<string, object> nodeSimpleRepr = base.NodeSimpleRepr(nIdx);
		nodeSimpleRepr["scale"] = NodeScale[nIdx];
		return nodeSimpleRepr;
	}

	public override void UpdateCellFromJson(int cIdx, JsonElement rec)
	{
		base.UpdateCellFromJson(cIdx, rec);
		if (rec.TryGetProperty("scale", out var localValue))
		{
			CellScale[cIdx] = (float)localValue.GetDouble();
		}
	}

	public override Dictionary<string, object> CellSimpleRepr(int cIdx)
	{
		Dictionary<string, object> cellSimpleRepr = base.CellSimpleRepr(cIdx);
		cellSimpleRepr["scale"] = CellScale[cIdx];
		return cellSimpleRepr;
	}

	public new static SizeMesh ReadJson(string filename)
	{
		using FileStream utf8Json = File.OpenRead(filename);
		return FromSimpleRepr(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(utf8Json, DynamicMesh.JsonOptions()));
	}

	public new static SizeMesh FromSimpleRepr(Dictionary<string, JsonElement> simpleRepr)
	{
		SizeMesh sizeMesh = new SizeMesh();
		sizeMesh.UpdateFromJson(simpleRepr);
		return sizeMesh;
	}

	public virtual Tin ComputeTin(double fillBias, double fillScale)
	{
		DTin = TriangulateMesh<FilteredPredicates>((int nIdx) => NodeScale[nIdx]);
		if (fillBias > 0.0)
		{
			DTin.AddInteriorFill(fillBias, fillScale);
		}
		return DTin.Tin;
	}
}
