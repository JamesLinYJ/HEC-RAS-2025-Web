using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using TiffBinaryReader;
using Utility.Progress;

namespace Geospatial.IO;

public class GeoTiffWriter<T> : IDisposable where T : unmanaged, IEquatable<T>
{
	private readonly string _filename;

	private readonly TiffFileWriter _writer;

	private readonly IList<RasterDefinition> _exportRasterDefs;

	private readonly Projection _prj;

	private bool _isDisposed;

	public List<ITileReaderWriter<T>> Writers { get; }

	public ITileReaderWriter<T> BaseWriter => Writers[0];

	public static GeoTiffWriter<T> WithDefaultOverviews(string filename, RasterDefinition baseRaster, T? noDataValue, Projection prj = null)
	{
		List<RasterDefinition> defaultOverviews = baseRaster.DefaultOverviews();
		defaultOverviews.Insert(0, baseRaster);
		return new GeoTiffWriter<T>(filename, defaultOverviews, noDataValue, prj);
	}

	public static GeoTiffWriter<T> JustBaseLevel(string filename, RasterDefinition baseRaster, T? noDataValue, Projection prj = null)
	{
		int unity = 1;
		List<RasterDefinition> rasterDefinitions = new List<RasterDefinition>(unity);
		CollectionsMarshal.SetCount(rasterDefinitions, unity);
		Span<RasterDefinition> rasterDefinitionsSpan = CollectionsMarshal.AsSpan(rasterDefinitions);
		int index = 0;
		rasterDefinitionsSpan[index] = baseRaster;
		return new GeoTiffWriter<T>(filename, rasterDefinitions, noDataValue, prj);
	}

	public GeoTiffWriter(string filename, IList<RasterDefinition> pyramidLevelRDefs, T? noDataValue, Projection prj = null)
	{
		if (pyramidLevelRDefs == null || pyramidLevelRDefs.Count < 1)
		{
			throw new ArgumentException("pyramidLevelRDefs");
		}
		_filename = filename;
		_exportRasterDefs = pyramidLevelRDefs;
		_prj = prj;
		List<TagMetadata> tagMetadatas = new List<TagMetadata>();
		for (int i = 0; i < pyramidLevelRDefs.Count; i++)
		{
			RasterDefinition rasterDefinition = pyramidLevelRDefs[i];
			tagMetadatas.Add(TagMetadata.ForWriting(rasterDefinition.Cols, rasterDefinition.Rows, i != 0, noDataValue));
		}
		_writer = new TiffFileWriter(filename, tagMetadatas);
		Writers = new List<ITileReaderWriter<T>>(pyramidLevelRDefs.Count);
		for (int j = 0; j < pyramidLevelRDefs.Count; j++)
		{
			Writers.Add(new TiffTileWriter<T>(_writer, j, pyramidLevelRDefs[j], noDataValue));
		}
	}

	public void ComputeOverviewsFromBaseData(ProgressReporter pr = null)
	{
		if (Writers.Count != 1)
		{
			if (!(typeof(T) == typeof(float)))
			{
				throw new NotImplementedException();
			}
			ExportEngine.CreateOverviews(Writers[0] as ITileProvider<float>, (from w in Writers.Skip(1)
				select w as ITileReaderWriter<float>).ToArray(), pr);
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (disposing)
		{
			_writer?.Dispose();
			GDALRaster.SetLocation(_filename, _exportRasterDefs[0]);
			if (_prj != null)
			{
				GDALRaster.SetProjection(_filename, _prj);
			}
		}
		_isDisposed = true;
	}

	~GeoTiffWriter()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
