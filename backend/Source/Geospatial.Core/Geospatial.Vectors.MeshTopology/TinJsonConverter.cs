using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geospatial.Vectors.MeshTopology;

public class TinJsonConverter : JsonConverter<Tin>
{
	public override Tin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		throw new NotImplementedException();
	}

	public override void Write(Utf8JsonWriter writer, Tin tin, JsonSerializerOptions options)
	{
		DynamicMesh dynamicMesh = new DynamicMesh();
		for (int i = 0; i < tin.Count; i++)
		{
			dynamicMesh.AddNode(tin[i]);
		}
		for (int j = 0; j < tin.TriangleDefinition.Count; j++)
		{
			TriangleDefinition triangleDefinition = tin.TriangleDefinition[j];
			int cIdx = dynamicMesh.AddCell();
			dynamicMesh.SetCellNodes(cIdx, new List<int> { triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2 }, ElementMode.CREATE_OR_EXISTING);
		}
		Dictionary<string, object> simpleRepr = dynamicMesh.SimpleRepr();
		List<Dictionary<string, object>> simpleReprList = (List<Dictionary<string, object>>)simpleRepr["Cells"];
		for (int k = 0; k < tin.TriangleDefinition.Count; k++)
		{
			TrianglePaver.TriState triState = (tin.TriangleDefinition[k].IsDeleted ? TrianglePaver.TriState.EXTERNAL : TrianglePaver.TriState.INTERNAL);
			simpleReprList[k]["state"] = triState;
		}
		List<Dictionary<string, object>> simpleRepr2 = (List<Dictionary<string, object>>)simpleRepr["Nodes"];
		if (tin.HasZ || tin.HasM)
		{
			for (int l = 0; l < tin.PointCount; l++)
			{
				if (tin.HasZ)
				{
					simpleRepr2[l]["Z"] = tin.Z[l];
				}
				if (tin.HasM)
				{
					simpleRepr2[l]["M"] = tin.M[l];
				}
			}
		}
		string json = JsonSerializer.Serialize(simpleRepr, options);
		writer.WriteRawValue(json);
	}
}
