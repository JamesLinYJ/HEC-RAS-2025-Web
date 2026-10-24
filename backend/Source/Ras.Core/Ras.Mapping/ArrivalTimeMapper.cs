using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Ras.Layers.Results;
using Ras.Mapping.ArrivalTime;
using Ras.Output;

namespace Ras.Mapping;

public class ArrivalTimeMapper
{
	private MeshPropertyTables[] _tables;

	public ArrivalTimeRenderOptions RenderOptions { get; }

	public ArrivalTimeMapper(PostProcessor pp, LegacyOutputBlock ob, MeshPropertyTables[] tables, ArrivalTimeRenderOptions opts = null)
	{
		RenderOptions = opts ?? new ArrivalTimeRenderOptions();
		_tables = tables;
		if (RenderOptions.StartTimeDays > 0.0)
		{
			throw new NotImplementedException("TODO: StartTime offset");
		}
		throw new NotImplementedException();
	}

	private void ComputeArrivalTime(GeometryMapPoints mps, Memory<float> terrain, Memory<float> arrivalTimes)
	{
		arrivalTimes.Span.Fill(-9999f);
		_ = mps.InputCount;
	}

	private List<LocalExtrema>[] ParseLocalExtrema(int[,] rgnInfo, int[] rgnValues)
	{
		List<LocalExtrema>[] localExtremaListArray = new List<LocalExtrema>[rgnInfo.GetLength(0) - 1 + 1];
		for (int i = 0; i <= localExtremaListArray.Count() - 1; i++)
		{
			localExtremaListArray[i] = ParseLocalExtremaRow(rgnInfo, rgnValues, i);
		}
		return localExtremaListArray;
	}

	private List<LocalExtrema> ParseLocalExtremaRow(int[,] rgnInfo, int[] rgnValues, int row)
	{
		List<LocalExtrema> localExtremas = new List<LocalExtrema>();
		ExtremaType extremaType = (ExtremaType)rgnInfo[row, 2];
		for (int i = rgnInfo[row, 0]; i < rgnInfo[row, 0] + rgnInfo[row, 1]; i++)
		{
			localExtremas.Add(new LocalExtrema(extremaType, rgnValues[i]));
			extremaType = extremaType.SwapType();
		}
		return localExtremas;
	}

	private List<LocalExtrema> ParseLocalExtremaRow(int[] rgnInfo, int[] rgnValues)
	{
		List<LocalExtrema> localExtremas = new List<LocalExtrema>();
		ExtremaType extremaType = (ExtremaType)rgnInfo[2];
		for (int i = rgnInfo[0]; i < rgnInfo[0] + rgnInfo[1]; i++)
		{
			localExtremas.Add(new LocalExtrema(extremaType, rgnValues[i]));
			extremaType = extremaType.SwapType();
		}
		return localExtremas;
	}

	private WindowSeries Get2DWindows(int[] info, int[] extrema, float[] cellTimeSeries)
	{
		info[0] = 0;
		List<LocalExtrema> parseLocalExtremaRow = ParseLocalExtremaRow(info, extrema);
		TimeSeriesExtrema timeSeriesExtrema = new TimeSeriesExtrema(parseLocalExtremaRow.Count);
		foreach (LocalExtrema parseLocalExtremaRow2 in parseLocalExtremaRow)
		{
			timeSeriesExtrema.Add(new LocalExtremaValue(parseLocalExtremaRow2, cellTimeSeries[parseLocalExtremaRow2.Time]));
		}
		return timeSeriesExtrema.ToWindowSeries();
	}

	private void ParseWindowForward<T>(WindowSeries series, int startTimeIndex, float depthThreshold, Func<T, float> getElev, Func<T, int, bool> isPixelWet, Action<T, int> writeValue, IEnumerable<T> items)
	{
		Parallel.ForEach(items, (T pixel) =>
		{
			float getElev2 = getElev(pixel);
			if (getElev2 == -9999f)
			{
				return;
			}
			foreach (Window window in series.Windows)
			{
				if (window.StopTime >= startTimeIndex)
				{
					int startTimeWindowTerm = Math.Max(window.StartTime, startTimeIndex);
					if (window.Slope == Slope.Decreasing)
					{
						if (isPixelWet(pixel, startTimeWindowTerm))
						{
							writeValue(pixel, startTimeWindowTerm);
							break;
						}
						if (window.Slope == Slope.Increasing)
						{
							if (!(window.MaxValue < getElev2 + depthThreshold))
							{
								if (isPixelWet(pixel, startTimeWindowTerm))
								{
									writeValue(pixel, startTimeWindowTerm);
									break;
								}
								if (isPixelWet(pixel, startTimeWindowTerm + 1))
								{
									writeValue(pixel, startTimeWindowTerm + 1);
									break;
								}
								if (isPixelWet(pixel, window.StopTime))
								{
									int ordinal = startTimeWindowTerm;
									int stopTime = window.StopTime;
									while (true)
									{
										int stopTime2 = Convert.ToInt32((double)(ordinal + stopTime) / 2.0);
										if (stopTime2 == ordinal || stopTime2 == stopTime)
										{
											break;
										}
										bool isPixelWet2 = isPixelWet(pixel, stopTime2);
										bool isPixelWet3 = isPixelWet(pixel, stopTime2 + 1);
										if (!isPixelWet2 && isPixelWet3)
										{
											writeValue(pixel, stopTime2 + 1);
											break;
										}
										if (!isPixelWet2)
										{
											ordinal = stopTime2;
										}
										else
										{
											if (!isPixelWet2)
											{
												throw new NotImplementedException();
											}
											stopTime = stopTime2;
										}
									}
								}
							}
						}
						else
						{
							if (window.Slope != Slope.Indeterminate)
							{
								throw new Exception("Slope type unrecognized.");
							}
							if (!(window.MaxValue < getElev2 + depthThreshold))
							{
								bool condition = false;
								for (int i = startTimeWindowTerm; i <= window.StopTime; i++)
								{
									if (isPixelWet(pixel, i))
									{
										writeValue(pixel, i);
										condition = true;
										break;
									}
								}
								if (condition)
								{
									break;
								}
							}
						}
					}
				}
			}
		});
	}

	private void ParseWindowBackward<T>(WindowSeries series, int startTimeIndex, float depthThreshold, Func<T, float> getElev, Func<T, int, bool> isPixelWet, Action<T, int> writeValue, IEnumerable<T> items)
	{
		Parallel.ForEach(items, (T pixel) =>
		{
			float getElev2 = getElev(pixel);
			if (getElev2 == -9999f)
			{
				return;
			}
			foreach (Window reverse in series.Windows.AsEnumerable().Reverse())
			{
				if (isPixelWet(pixel, reverse.StopTime))
				{
					writeValue(pixel, reverse.StopTime);
					break;
				}
				if (reverse.StopTime >= startTimeIndex)
				{
					int startTimeReverseTerm = Math.Max(reverse.StartTime, startTimeIndex);
					if (reverse.Slope == Slope.Increasing)
					{
						if (isPixelWet(pixel, reverse.StopTime))
						{
							writeValue(pixel, reverse.StopTime);
							break;
						}
					}
					else if (reverse.Slope == Slope.Decreasing)
					{
						if (!(reverse.MaxValue < getElev2 + depthThreshold) && isPixelWet(pixel, startTimeReverseTerm))
						{
							if (isPixelWet(pixel, reverse.StopTime))
							{
								writeValue(pixel, reverse.StopTime);
								break;
							}
							if (isPixelWet(pixel, reverse.StopTime - 1))
							{
								writeValue(pixel, reverse.StopTime - 1);
								break;
							}
							int ordinal = startTimeReverseTerm;
							int stopTime = reverse.StopTime;
							while (true)
							{
								int stopTime2 = Convert.ToInt32((double)(ordinal + stopTime) / 2.0);
								if (stopTime2 == ordinal || stopTime2 == stopTime)
								{
									break;
								}
								bool isPixelWet2 = isPixelWet(pixel, stopTime2);
								bool isPixelWet3 = isPixelWet(pixel, stopTime2 + 1);
								if (isPixelWet2 && !isPixelWet3)
								{
									writeValue(pixel, stopTime2 + 1);
									break;
								}
								if (!isPixelWet2)
								{
									stopTime = stopTime2;
								}
								else
								{
									if (!isPixelWet2)
									{
										throw new NotImplementedException();
									}
									ordinal = stopTime2;
								}
							}
						}
					}
					else
					{
						if (reverse.Slope != Slope.Indeterminate)
						{
							throw new NotSupportedException();
						}
						if (!(reverse.MaxValue < getElev2 + depthThreshold))
						{
							bool condition = false;
							for (int i = reverse.StopTime; i >= startTimeReverseTerm; i += -1)
							{
								if (isPixelWet(pixel, i))
								{
									writeValue(pixel, i);
									condition = true;
									break;
								}
							}
							if (condition)
							{
								break;
							}
						}
					}
				}
			}
		});
	}
}
