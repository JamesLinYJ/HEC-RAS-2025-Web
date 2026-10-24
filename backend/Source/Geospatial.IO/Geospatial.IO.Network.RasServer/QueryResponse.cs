using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.RasServer;

public record QueryResponse([property: JsonPropertyName("results")] IReadOnlyList<TiffFileInfo> Results, [property: JsonPropertyName("projections")] IReadOnlyList<ProjectionInfo> Projections, [property: JsonPropertyName("returnedCount")] int ReturnedCount, [property: JsonPropertyName("totalCount")] int TotalCount, [property: JsonPropertyName("hasMore")] bool HasMore, [property: JsonPropertyName("message")] string Message)
{
	private Dictionary<long, ProjectionInfo> _projectionLookup;

	public ProjectionInfo GetProjection(long? projectionId)
	{
		if (!projectionId.HasValue)
		{
			return null;
		}
		if (_projectionLookup == null)
		{
			_projectionLookup = BuildProjectionLookup();
		}
		if (!_projectionLookup.TryGetValue(projectionId.Value, out var localValue))
		{
			return null;
		}
		return localValue;
	}

	public string GetUnits(TiffFileInfo tiff)
	{
		return GetProjection(tiff.ProjectionId)?.Units ?? "unknown";
	}

	private Dictionary<long, ProjectionInfo> BuildProjectionLookup()
	{
		Dictionary<long, ProjectionInfo> projectionLookup = new Dictionary<long, ProjectionInfo>();
		if (Projections != null)
		{
			foreach (ProjectionInfo projection in Projections)
			{
				projectionLookup[projection.Id] = projection;
			}
		}
		return projectionLookup;
	}
}
