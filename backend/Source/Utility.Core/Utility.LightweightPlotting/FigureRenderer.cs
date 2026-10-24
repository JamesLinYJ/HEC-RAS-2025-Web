using System;
using System.Diagnostics;
using System.IO;

namespace Utility.LightweightPlotting;

public static class FigureRenderer
{
	public static bool TryRenderToPng(string sourcePath, string pngPath, int width, int height, int deviceScaleFactor = 8, int timeoutMs = 30000)
	{
		if (!File.Exists(sourcePath))
		{
			Console.WriteLine("FigureRenderer: source not found: " + sourcePath);
			return false;
		}
		string fileName = LocateEdge();
		if (fileName == null)
		{
			Console.WriteLine("FigureRenderer: Microsoft Edge not found; skipping PNG export.");
			return false;
		}
		string fullPath = Path.GetFullPath(sourcePath);
		string fullPath2 = Path.GetFullPath(pngPath);
		string absoluteUri = new Uri(fullPath).AbsoluteUri;
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = fileName,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true
		};
		processStartInfo.ArgumentList.Add("--headless=new");
		processStartInfo.ArgumentList.Add("--disable-gpu");
		processStartInfo.ArgumentList.Add("--hide-scrollbars");
		processStartInfo.ArgumentList.Add("--no-sandbox");
		processStartInfo.ArgumentList.Add($"--force-device-scale-factor={deviceScaleFactor}");
		processStartInfo.ArgumentList.Add("--virtual-time-budget=5000");
		processStartInfo.ArgumentList.Add($"--window-size={width},{height}");
		processStartInfo.ArgumentList.Add("--screenshot=" + fullPath2);
		processStartInfo.ArgumentList.Add(absoluteUri);
		try
		{
			using Process process = Process.Start(processStartInfo);
			if (process == null)
			{
				Console.WriteLine("FigureRenderer: failed to start Edge for " + sourcePath);
				return false;
			}
			if (!process.WaitForExit(timeoutMs))
			{
				process.Kill(entireProcessTree: true);
				Console.WriteLine($"FigureRenderer: timeout after {timeoutMs}ms rendering {sourcePath}");
				return false;
			}
			if (!File.Exists(fullPath2))
			{
				Console.WriteLine($"FigureRenderer: Edge exited {process.ExitCode} but no PNG was produced for {sourcePath}");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine("FigureRenderer: error rendering " + sourcePath + ": " + ex.Message);
			return false;
		}
	}

	public static bool TryRenderToPdf(string sourcePath, string pdfPath, int width, int height, int timeoutMs = 30000)
	{
		if (!File.Exists(sourcePath))
		{
			Console.WriteLine("FigureRenderer: source not found: " + sourcePath);
			return false;
		}
		string fileName = LocateEdge();
		if (fileName == null)
		{
			Console.WriteLine("FigureRenderer: Microsoft Edge not found; skipping PDF export.");
			return false;
		}
		string fullPath = Path.GetFullPath(sourcePath);
		string fullPath2 = Path.GetFullPath(pdfPath);
		string path = Path.Combine(Path.GetDirectoryName(fullPath), $".pdf_wrap_{Guid.NewGuid():N}.html");
		string toLowerInvariant = Path.GetExtension(fullPath).ToLowerInvariant();
		string message = $"<style>@page {{ size: {width}px {height}px; margin: 0; }} html,body{{margin:0;padding:0;}}</style>";
		if (toLowerInvariant == ".svg")
		{
			string allText = File.ReadAllText(fullPath);
			string message2 = $"<style>svg{{display:block;width:{width}px;height:{height}px;}}</style>";
			File.WriteAllText(path, $"<!DOCTYPE html><html><head>{message}{message2}</head><body>{allText}</body></html>");
		}
		else
		{
			string allText2 = File.ReadAllText(fullPath);
			int indexOf = allText2.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
			string contents = ((indexOf >= 0) ? allText2.Insert(indexOf + 6, message) : (message + allText2));
			File.WriteAllText(path, contents);
		}
		string absoluteUri = new Uri(path).AbsoluteUri;
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = fileName,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true
		};
		processStartInfo.ArgumentList.Add("--headless=new");
		processStartInfo.ArgumentList.Add("--disable-gpu");
		processStartInfo.ArgumentList.Add("--no-sandbox");
		processStartInfo.ArgumentList.Add("--no-pdf-header-footer");
		processStartInfo.ArgumentList.Add("--virtual-time-budget=5000");
		processStartInfo.ArgumentList.Add("--print-to-pdf=" + fullPath2);
		processStartInfo.ArgumentList.Add(absoluteUri);
		try
		{
			using Process process = Process.Start(processStartInfo);
			if (process == null)
			{
				Console.WriteLine("FigureRenderer: failed to start Edge for " + sourcePath);
				return false;
			}
			if (!process.WaitForExit(timeoutMs))
			{
				process.Kill(entireProcessTree: true);
				Console.WriteLine($"FigureRenderer: timeout after {timeoutMs}ms rendering {sourcePath}");
				return false;
			}
			if (!File.Exists(fullPath2))
			{
				Console.WriteLine($"FigureRenderer: Edge exited {process.ExitCode} but no PDF was produced for {sourcePath}");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine("FigureRenderer: error rendering " + sourcePath + ": " + ex.Message);
			return false;
		}
		finally
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch
			{
			}
		}
	}

	private static string LocateEdge()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		string[] strings = new string[2];
		_003C_003Ey__InlineArray5<string> buffer = default;
		buffer[0] = folderPath;
		buffer[1] = "Microsoft";
		buffer[2] = "Edge";
		buffer[3] = "Application";
		buffer[4] = "msedge.exe";
		strings[0] = Path.Combine(buffer);
		_003C_003Ey__InlineArray5<string> buffer2 = default;
		buffer2[0] = folderPath2;
		buffer2[1] = "Microsoft";
		buffer2[2] = "Edge";
		buffer2[3] = "Application";
		buffer2[4] = "msedge.exe";
		strings[1] = Path.Combine(buffer2);
		string[] stringsArray = strings;
		foreach (string textValue in stringsArray)
		{
			if (File.Exists(textValue))
			{
				return textValue;
			}
		}
		return null;
	}
}
