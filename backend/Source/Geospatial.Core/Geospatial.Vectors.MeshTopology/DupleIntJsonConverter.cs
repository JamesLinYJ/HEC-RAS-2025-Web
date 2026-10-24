using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geospatial.Vectors.MeshTopology;

public class DupleIntJsonConverter : JsonConverter<Duple<int>>
{
	public override Duple<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
		{
			throw new JsonException();
		}
		Duple<int> result = default;
		reader.Read();
		result.A = reader.GetInt32();
		reader.Read();
		result.B = reader.GetInt32();
		reader.Read();
		if (reader.TokenType != JsonTokenType.EndArray)
		{
			throw new JsonException();
		}
		return result;
	}

	public override void Write(Utf8JsonWriter writer, Duple<int> obj, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteNumberValue(obj.A);
		writer.WriteNumberValue(obj.B);
		writer.WriteEndArray();
	}
}
