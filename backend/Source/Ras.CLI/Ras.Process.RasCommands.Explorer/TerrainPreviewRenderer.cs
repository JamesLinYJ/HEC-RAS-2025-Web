using System;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Ras.Layers.Terrains;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ras.Process.RasCommands.Explorer;

public static class TerrainPreviewRenderer
{
	public static IRenderable RenderPreview(ITerrainInput terrainInput, int width, int height)
	{
		if (terrainInput == null)
		{
			return new Markup("[dim]No terrain data[/]");
		}
		IResample<float> resampler = terrainInput.GetResampler();
		if (resampler == null)
		{
			return new Markup("[dim]Unable to load terrain[/]");
		}
		try
		{
			return RenderRasterPreview(resampler, width, height);
		}
		catch (Exception ex)
		{
			return new Markup("[red]Render error: " + ex.Message + "[/]");
		}
	}

	public static IRenderable RenderRasterPreview(IResample<float> raster, int width, int height)
	{
		RasterRenderLayer rasterRenderLayer = new RasterRenderLayer(raster);
		rasterRenderLayer.SetDefaultTerrainHistoSymbology();
		Extent validExtent = raster.ValidExtent;
		using PlotRequest plotRequest = new PlotRequest(new RasterDefinition(validExtent, height, width));
		rasterRenderLayer.Render(plotRequest);
		return ConvertToCanvas(plotRequest.ColorBufferARGB.Span, width, height);
	}

	private static IRenderable ConvertToCanvas(ReadOnlySpan<int> argbBuffer, int width, int height)
	{
		Canvas canvas = new Canvas(width, height);
		for (int i = 0; i < height; i++)
		{
			for (int j = 0; j < width; j++)
			{
				int widthTerm = i * width + j;
				if (widthTerm < argbBuffer.Length)
				{
					int argbBuffer2 = argbBuffer[widthTerm];
					byte red = (byte)((argbBuffer2 >> 16) & 0xFF);
					byte green = (byte)((argbBuffer2 >> 8) & 0xFF);
					byte blue = (byte)(argbBuffer2 & 0xFF);
					canvas.SetPixel(j, i, new Color(red, green, blue));
				}
			}
		}
		return canvas;
	}
}
