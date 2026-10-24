using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using H5Assist.Identifiers;
using Utility.Extensions;
using Utility.Progress;

namespace H5Assist.Differences;

public static class H5Diff
{
	public const string IncludePrefix = "+";

	public const string DisincludePrefix = "!";

	public static void Diff(string fileBase, string fileNew, ProgressReporter prog, IList<string> extraArgs = null)
	{
		if (extraArgs == null)
		{
			extraArgs = new List<string>();
		}
		try
		{
			if (!File.Exists(fileBase))
			{
				prog.ReportMessage("Base file " + fileBase.InDoubleQuotes() + " does not exist.");
				return;
			}
			if (!File.Exists(fileNew))
			{
				prog.ReportMessage("New file " + fileNew.InDoubleQuotes() + " does not exist.");
				return;
			}
			prog.ReportMessage("BASE Filename: " + fileBase.InParentheses());
			prog.ReportMessage("NEW Filename: " + fileNew.InParentheses() + Environment.NewLine);
			List<string> stringList = new List<string>();
			List<string> stringListList = new List<string>();
			foreach (string extraArg in extraArgs)
			{
				if (extraArg.StartsWith("!"))
				{
					stringListList.Add(extraArg.Substring(1));
					prog.ReportMessage("Skipping " + stringListList.Last().InDoubleQuotes());
				}
				else if (extraArg.StartsWith("+"))
				{
					stringList.Add(extraArg.Substring(1));
					prog.ReportMessage("Including " + stringList.Last().InDoubleQuotes());
				}
				else
				{
					stringList.Add(extraArg);
					prog.ReportMessage("Including " + stringList.Last().InDoubleQuotes());
				}
			}
			if (stringList.Count == 0)
			{
				stringList.Add("/");
				prog.ReportMessage("Starting at /");
			}
			using H5Reader h5Reader = new H5Reader(fileBase);
			using H5Reader h5Reader2 = new H5Reader(fileNew);
			foreach (string textValue in stringList)
			{
				bool existsAsGroup = h5Reader.ExistsAsGroup(textValue);
				bool existsAsGroup2 = h5Reader2.ExistsAsGroup(textValue);
				if (!existsAsGroup && !existsAsGroup2)
				{
					prog.ReportError("Error: Selected path does not exist in either file.");
				}
				else if (!existsAsGroup)
				{
					prog.ReportError("Error: Selected path does not exist in " + fileBase.InDoubleQuotes());
				}
				else if (!existsAsGroup2)
				{
					prog.ReportError("Error: Selected path does not exist in " + fileNew.InDoubleQuotes());
				}
				else
				{
					DiffRecurseGroup(h5Reader, h5Reader2, textValue, stringListList, prog);
				}
			}
		}
		catch (Exception ex)
		{
			prog.ReportError(ex.Message);
		}
	}

	private static void DiffRecurseGroup(H5Reader hrA, H5Reader hrB, string path, List<string> ignorePaths, ProgressReporter prog)
	{
		Console.ForegroundColor = ConsoleColor.Green;
		prog.ReportMessage("GROUP: " + path);
		Console.ForegroundColor = ConsoleColor.Red;
		DiffAttributes(hrA, hrB, path, prog);
		DiffGroupLocalDatasets(hrA, hrB, path, prog);
		DiffSubgroups(hrA, hrB, path, ignorePaths, prog);
		Console.ResetColor();
	}

	private static void DiffGroupLocalDatasets(H5Reader hrA, H5Reader hrB, string path, ProgressReporter prog)
	{
		List<string> fullyQualifiedDatasetNames = hrA.GetFullyQualifiedDatasetNames(path);
		List<string> fullyQualifiedDatasetNames2 = hrB.GetFullyQualifiedDatasetNames(path);
		DiffPrintBoth(fullyQualifiedDatasetNames, fullyQualifiedDatasetNames2, "Datasets", out var commonElements, prog);
		foreach (string commonElement in commonElements)
		{
			DiffDataset(hrA, hrB, commonElement, prog);
		}
	}

	private static void DiffDataset(H5Reader hrA, H5Reader hrB, string path, ProgressReporter prog)
	{
		Console.ForegroundColor = ConsoleColor.Green;
		prog.ReportMessage("DATASET: " + path);
		Console.ForegroundColor = ConsoleColor.Red;
		DiffAttributes(hrA, hrB, path, prog);
		Type typeBase = hrA.GetDatasetType(path);
		Type datasetType = hrB.GetDatasetType(path);
		bool condition = true;
		if (typeBase != datasetType)
		{
			prog.ReportMessage("  Different dataset types. Base: " + typeBase.ToString() + ", New: " + datasetType.ToString());
			condition = false;
		}
		long[] baseDims = hrA.GetDatasetDimensions(path);
		long[] datasetDimensions = hrB.GetDatasetDimensions(path);
		if (baseDims.Length != datasetDimensions.Length)
		{
			prog.ReportMessage("  Different number of dataset dimensions.");
			condition = false;
		}
		else
		{
			for (int i = 0; i < baseDims.Length; i++)
			{
				long baseDim = baseDims[i];
				long datasetDimension = datasetDimensions[i];
				if (baseDim != datasetDimension)
				{
					prog.ReportMessage("  Dimension " + (i + 1) + " different sizes: " + baseDim + " vs " + datasetDimension);
					condition = false;
				}
			}
		}
		if (!condition)
		{
			return;
		}
		prog = prog.WithMessageModifier("  ", "");
		if (typeBase == typeof(float))
		{
			if (baseDims.Length == 1)
			{
				hrA.ReadDataset(path, out float[] values);
				hrB.ReadDataset(path, out float[] values2);
				DiffBuffer(values, values2, prog, 10);
			}
			else if (baseDims.Length == 2)
			{
				hrA.ReadDataset(path, out float[,] values3);
				hrB.ReadDataset(path, out float[,] values4);
				DiffBuffer(values3, values4, prog, 10);
			}
			else
			{
				PrintNotImpl();
			}
		}
		else if (typeBase == typeof(double))
		{
			if (baseDims.Length == 1)
			{
				hrA.ReadDataset(path, out double[] values5);
				hrB.ReadDataset(path, out double[] values6);
				DiffBuffer(values5, values6, prog, 10);
			}
			else if (baseDims.Length == 2)
			{
				hrA.ReadDataset(path, out double[,] values7);
				hrB.ReadDataset(path, out double[,] values8);
				DiffBuffer(values7, values8, prog, 10);
			}
			else
			{
				PrintNotImpl();
			}
		}
		else if (typeBase == typeof(int))
		{
			if (baseDims.Length == 1)
			{
				hrA.ReadDataset(path, out int[] values9);
				hrB.ReadDataset(path, out int[] values10);
				DiffBuffer(values9, values10, prog, 10);
			}
			else if (baseDims.Length == 2)
			{
				hrA.ReadDataset(path, out int[,] values11);
				hrB.ReadDataset(path, out int[,] values12);
				DiffBuffer(values11, values12, prog, 10);
			}
			else
			{
				PrintNotImpl();
			}
		}
		else if (typeBase == typeof(long))
		{
			if (baseDims.Length == 1)
			{
				hrA.ReadDataset(path, out long[] values13);
				hrB.ReadDataset(path, out long[] values14);
				DiffBuffer(values13, values14, prog, 10);
			}
			else if (baseDims.Length == 2)
			{
				hrA.ReadDataset(path, out long[,] values15);
				hrB.ReadDataset(path, out long[,] values16);
				DiffBuffer(values15, values16, prog, 10);
			}
			else
			{
				PrintNotImpl();
			}
		}
		else if (typeBase == typeof(string))
		{
			if (baseDims.Length == 1)
			{
				hrA.ReadDataset(path, out var values17);
				hrB.ReadDataset(path, out var values18);
				DiffBuffer(values17, values18, prog, 10);
			}
			else
			{
				PrintNotImpl();
			}
		}
		else if (typeBase == typeof(CompoundFieldDefinition))
		{
			Dictionary<CompoundFieldDefinition, Array> cfdBase = hrA.ReadCompoundElements(path);
			Dictionary<CompoundFieldDefinition, Array> cfdNew = hrB.ReadCompoundElements(path);
			DiffCompound(cfdBase, cfdNew, prog);
		}
		else
		{
			PrintNotImpl();
		}
		void PrintNotImpl()
		{
			ConsoleColor foregroundColor = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.White;
			prog.ReportMessage("Diff not implemented for " + typeBase.ToString() + " with " + baseDims.Length + " dimensions.");
			Console.ForegroundColor = foregroundColor;
		}
	}

	private static void DiffAttributes(H5Reader hrA, H5Reader hrB, string path, ProgressReporter prog)
	{
		Dictionary<string, Type> attrA = hrA.GetAttributeNamesAndTypes(path);
		Dictionary<string, Type> attrB = hrB.GetAttributeNamesAndTypes(path);
		List<string> baseList = attrA.Select((KeyValuePair<string, Type> kvp) => kvp.Key).ToList();
		List<string> newList = attrB.Select((KeyValuePair<string, Type> kvp) => kvp.Key).ToList();
		DiffPrintBoth(baseList, newList, "Attributes", out var commonElements, prog, (string attr) => (attrA.TryGetValue(attr, out var localValue) || attrB.TryGetValue(attr, out localValue)) ? (attr + " " + localValue.ToString().InParentheses()) : "(Error)");
		Dictionary<string, object> allAttributes = hrA.ReadAllAttributes(path);
		Dictionary<string, object> allAttributes2 = hrB.ReadAllAttributes(path);
		bool hasHeadered = false;
		foreach (string commonElement in commonElements)
		{
			Type type = attrA[commonElement];
			Type type2 = attrB[commonElement];
			if (type != type2)
			{
				EnsureHeader();
				prog.ReportMessage("  </> " + commonElement + ": Types differ, " + type.ToString() + " [Base] vs " + type2.ToString() + " [New]");
				continue;
			}
			object allAttribute = allAttributes[commonElement];
			object allAttribute2 = allAttributes2[commonElement];
			if (type.IsPrimitive || type == typeof(string))
			{
				if (!allAttribute.Equals(allAttribute2))
				{
					EnsureHeader();
					prog.ReportMessage("  </> " + commonElement + ": " + allAttribute.ToString() + " [Base] vs " + allAttribute2.ToString() + " [New]");
				}
				continue;
			}
			Array allAttribute3 = allAttribute as Array;
			Array allAttribute4 = allAttribute2 as Array;
			if (allAttribute3 == null || allAttribute4 == null)
			{
				EnsureHeader();
				prog.ReportMessage("  Error comparing " + commonElement);
				continue;
			}
			if (allAttribute3.Rank != allAttribute4.Rank)
			{
				EnsureHeader();
				prog.ReportMessage("  </> " + commonElement + ": Array Rank differs, " + allAttribute3.Rank + " vs " + allAttribute4.Rank);
				continue;
			}
			if (allAttribute3.Length != allAttribute4.Length)
			{
				EnsureHeader();
				prog.ReportMessage("  </> " + commonElement + ": Array lengths differ, " + allAttribute3.Length + " vs " + allAttribute4.Length);
				continue;
			}
			bool condition = true;
			int rank = allAttribute3.Rank;
			switch (rank)
			{
			case 1:
			{
				for (int ordinal = 0; ordinal < allAttribute3.Length; ordinal++)
				{
					if (!allAttribute3.GetValue(ordinal).Equals(allAttribute4.GetValue(ordinal)))
					{
						condition = false;
						break;
					}
				}
				break;
			}
			case 2:
			{
				for (int ordinal2 = 0; ordinal2 < allAttribute3.GetLength(0); ordinal2++)
				{
					for (int ordinal3 = 0; ordinal3 < allAttribute3.GetLength(1); ordinal3++)
					{
						if (!allAttribute3.GetValue(ordinal2, ordinal3).Equals(allAttribute4.GetValue(ordinal2, ordinal3)))
						{
							condition = false;
							break;
						}
					}
					if (!condition)
					{
						break;
					}
				}
				break;
			}
			default:
				EnsureHeader();
				prog.ReportMessage("  Data comparison not supported for Rank=" + rank);
				break;
			}
			if (condition)
			{
				continue;
			}
			EnsureHeader();
			if (rank == 1)
			{
				_ = new string[allAttribute3.Length];
				_ = new string[allAttribute3.Length];
				StringBuilder stringBuilder = new StringBuilder();
				int ordinal4 = 0;
				int ordinal5 = 0;
				for (int ordinal6 = 0; ordinal6 < allAttribute3.Length; ordinal6++)
				{
					string getValue = allAttribute3.GetValue(ordinal6).ToString();
					string getValue2 = allAttribute4.GetValue(ordinal6).ToString();
					if (!(getValue == getValue2))
					{
						ordinal4++;
						if (ordinal4 > 20)
						{
							ordinal5++;
							continue;
						}
						stringBuilder.Append("IDX[" + ordinal6 + "]: " + getValue + " -> " + getValue2 + ", ");
					}
				}
				if (ordinal5 > 0)
				{
					stringBuilder.Append(" (and " + ordinal5 + " more)");
				}
				prog.ReportMessage("  </> " + commonElement + ": " + stringBuilder.ToString());
			}
			else
			{
				prog.ReportMessage("  </> " + commonElement);
			}
		}
		void EnsureHeader()
		{
			if (!hasHeadered)
			{
				hasHeadered = true;
				prog.ReportMessage("Attribute Differences:");
			}
		}
	}

	private static void DiffSubgroups(H5Reader hrA, H5Reader hrB, string path, List<string> ignorePaths, ProgressReporter prog)
	{
		List<string> fullyQualifiedGroupNames = hrA.GetFullyQualifiedGroupNames(path);
		List<string> fullyQualifiedGroupNames2 = hrB.GetFullyQualifiedGroupNames(path);
		DiffPrintBoth(fullyQualifiedGroupNames, fullyQualifiedGroupNames2, "Groups", out var commonElements, prog);
		prog = prog.WithMessageModifier("  ", "");
		foreach (string commonElement in commonElements)
		{
			if (ignorePaths.Contains(commonElement))
			{
				ConsoleColor foregroundColor = Console.ForegroundColor;
				Console.ResetColor();
				prog.ReportMessage("SKIPPING " + commonElement);
				Console.ForegroundColor = foregroundColor;
			}
			else
			{
				DiffRecurseGroup(hrA, hrB, commonElement, ignorePaths, prog);
			}
		}
	}

	private static void DiffPrintBoth(IList<string> baseList, IList<string> newList, string elementTypeName, out List<string> commonElements, ProgressReporter prog, Func<string, string> diffPrinter = null)
	{
		if (diffPrinter == null)
		{
			diffPrinter = (string a) => a;
		}
		DiffCollections(baseList, newList, out var aOnly, out var bOnly, out commonElements);
		if (aOnly.Count > 0)
		{
			prog.ReportMessage("BASE " + elementTypeName + ":");
			foreach (string aOnly2 in aOnly)
			{
				prog.ReportMessage("  + " + diffPrinter(aOnly2));
			}
		}
		if (bOnly.Count <= 0)
		{
			return;
		}
		prog.ReportMessage("NEW " + elementTypeName + ":");
		foreach (string bOnly2 in bOnly)
		{
			prog.ReportMessage("  + " + diffPrinter(bOnly2));
		}
	}

	private static void DiffCollections(IList<string> listA, IList<string> listB, out List<string> aOnly, out List<string> bOnly, out List<string> both)
	{
		aOnly = new List<string>();
		bOnly = new List<string>();
		both = new List<string>();
		foreach (string listum in listA)
		{
			if (listB.Contains(listum))
			{
				both.Add(listum);
			}
			else
			{
				aOnly.Add(listum);
			}
		}
		foreach (string listB2 in listB)
		{
			if (!listA.Contains(listB2))
			{
				bOnly.Add(listB2);
			}
		}
	}

	private static void DiffBuffer(float[] baseBuf, float[] newBuf, ProgressReporter prog, int numPrinted, string header = null)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		if (string.IsNullOrEmpty(header))
		{
			header = "Dataset";
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.Length; i++)
		{
			float baseBuf2 = baseBuf[i];
			float newBuf2 = newBuf[i];
			if (baseBuf2 != newBuf2 && (!float.IsNaN(baseBuf2) || !float.IsNaN(newBuf2)))
			{
				ordinal++;
				if (ordinal <= numPrinted)
				{
					prog.ReportMessage("</> " + header + "[" + i + "] " + baseBuf2 + " [Base] vs " + newBuf2 + " [New]");
				}
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffBuffer(float[,] baseBuf, float[,] newBuf, ProgressReporter prog, int numPrinted)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.GetLength(0); i++)
		{
			for (int j = 0; j < baseBuf.GetLength(1); j++)
			{
				float baseBufTerm = baseBuf[i, j];
				float newBufTerm = newBuf[i, j];
				if (baseBufTerm != newBufTerm && (!float.IsNaN(baseBufTerm) || !float.IsNaN(newBufTerm)))
				{
					ordinal++;
					if (ordinal <= numPrinted)
					{
						prog.ReportMessage("</> Dataset[" + i + "," + j + "] " + baseBufTerm + " [Base] vs " + newBufTerm + " [New]");
					}
				}
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffBuffer(double[] baseBuf, double[] newBuf, ProgressReporter prog, int numPrinted, string header = null)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		if (string.IsNullOrEmpty(header))
		{
			header = "Dataset";
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.Length; i++)
		{
			double baseBuf2 = baseBuf[i];
			double newBuf2 = newBuf[i];
			if (baseBuf2 != newBuf2 && (!double.IsNaN(baseBuf2) || !double.IsNaN(newBuf2)))
			{
				ordinal++;
				if (ordinal <= numPrinted)
				{
					prog.ReportMessage("</> " + header + "[" + i + "] " + baseBuf2 + " [Base] vs " + newBuf2 + " [New]");
				}
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffBuffer(double[,] baseBuf, double[,] newBuf, ProgressReporter prog, int numPrinted)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.GetLength(0); i++)
		{
			for (int j = 0; j < baseBuf.GetLength(1); j++)
			{
				double baseBufTerm = baseBuf[i, j];
				double newBufTerm = newBuf[i, j];
				if (baseBufTerm != newBufTerm && (!double.IsNaN(baseBufTerm) || !double.IsNaN(newBufTerm)))
				{
					ordinal++;
					if (ordinal <= numPrinted)
					{
						prog.ReportMessage("</> Dataset[" + i + "," + j + "] " + baseBufTerm + " [Base] vs " + newBufTerm + " [New]");
					}
				}
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffBuffer(int[] baseBuf, int[] newBuf, ProgressReporter prog, int numPrinted, string header = null)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		if (string.IsNullOrEmpty(header))
		{
			header = "Dataset";
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.Length; i++)
		{
			int baseBuf2 = baseBuf[i];
			int newBuf2 = newBuf[i];
			if (baseBuf2 != newBuf2)
			{
				ordinal++;
				if (ordinal <= numPrinted)
				{
					prog.ReportMessage("</> " + header + "[" + i + "] " + baseBuf2 + " [Base] vs " + newBuf2 + " [New]");
				}
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffBuffer(long[] baseBuf, long[] newBuf, ProgressReporter prog, int numPrinted, string header = null)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		if (string.IsNullOrEmpty(header))
		{
			header = "Dataset";
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.Length; i++)
		{
			long baseBuf2 = baseBuf[i];
			long newBuf2 = newBuf[i];
			if (baseBuf2 != newBuf2)
			{
				ordinal++;
				if (ordinal <= numPrinted)
				{
					prog.ReportMessage("</> " + header + "[" + i + "] " + baseBuf2 + " [Base] vs " + newBuf2 + " [New]");
				}
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffBuffer(int[,] baseBuf, int[,] newBuf, ProgressReporter prog, int numPrinted)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.GetLength(0); i++)
		{
			for (int j = 0; j < baseBuf.GetLength(1); j++)
			{
				int baseBufTerm = baseBuf[i, j];
				int newBufTerm = newBuf[i, j];
				if (baseBufTerm != newBufTerm)
				{
					ordinal++;
					if (ordinal <= numPrinted)
					{
						prog.ReportMessage("</> Dataset[" + i + "," + j + "] " + baseBufTerm + " [Base] vs " + newBufTerm + " [New]");
					}
				}
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffBuffer(long[,] baseBuf, long[,] newBuf, ProgressReporter prog, int numPrinted)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.GetLength(0); i++)
		{
			for (int j = 0; j < baseBuf.GetLength(1); j++)
			{
				long baseBufTerm = baseBuf[i, j];
				long newBufTerm = newBuf[i, j];
				if (baseBufTerm != newBufTerm)
				{
					ordinal++;
					if (ordinal <= numPrinted)
					{
						prog.ReportMessage("</> Dataset[" + i + "," + j + "] " + baseBufTerm + " [Base] vs " + newBufTerm + " [New]");
					}
				}
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffBuffer(string[] baseBuf, string[] newBuf, ProgressReporter prog, int numPrinted, string header = null)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		if (string.IsNullOrEmpty(header))
		{
			header = "Dataset";
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.Length; i++)
		{
			string baseBuf2 = baseBuf[i];
			string newBuf2 = newBuf[i];
			if (!(baseBuf2 != newBuf2))
			{
				continue;
			}
			ordinal++;
			if (ordinal <= numPrinted)
			{
				string baseBuf3 = baseBuf2;
				if (baseBuf3.Length > 256)
				{
					baseBuf3 = baseBuf2.Substring(0, 256) + " (...)";
				}
				string newBuf3 = newBuf2;
				if (newBuf3.Length > 256)
				{
					newBuf3 = newBuf2.Substring(0, 256) + " (...)";
				}
				prog.ReportMessage("</> " + header + "[" + i + "] " + baseBuf3 + " [Base] vs " + newBuf3 + " [New]");
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffBuffer(bool[] baseBuf, bool[] newBuf, ProgressReporter prog, int numPrinted, string header = null)
	{
		if (numPrinted < 0)
		{
			numPrinted = int.MaxValue;
		}
		if (string.IsNullOrEmpty(header))
		{
			header = "Dataset";
		}
		int ordinal = 0;
		for (int i = 0; i < baseBuf.Length; i++)
		{
			bool baseBuf2 = baseBuf[i];
			bool newBuf2 = newBuf[i];
			if (baseBuf2 != newBuf2)
			{
				ordinal++;
				if (ordinal <= numPrinted)
				{
					prog.ReportMessage("</> " + header + "[" + i + "] " + baseBuf2 + " [Base] vs " + newBuf2 + " [New]");
				}
			}
		}
		if (ordinal > numPrinted)
		{
			prog.ReportMessage("</> ... and " + (ordinal - numPrinted) + " more.");
		}
	}

	private static void DiffCompound(Dictionary<CompoundFieldDefinition, Array> cfdBase, Dictionary<CompoundFieldDefinition, Array> cfdNew, ProgressReporter prog)
	{
		List<string> baseList = cfdBase.Keys.Select((CompoundFieldDefinition f) => f.Name).ToList();
		List<string> newList = cfdNew.Keys.Select((CompoundFieldDefinition f) => f.Name).ToList();
		DiffPrintBoth(baseList, newList, "Columns", out var commonElements, prog);
		for (int intervalIndex = 0; intervalIndex < commonElements.Count; intervalIndex++)
		{
			string cName = commonElements[intervalIndex];
			KeyValuePair<CompoundFieldDefinition, Array> firstOrDefault = cfdBase.FirstOrDefault((KeyValuePair<CompoundFieldDefinition, Array> cfd) => cfd.Key.Name == cName);
			KeyValuePair<CompoundFieldDefinition, Array> firstOrDefault2 = cfdNew.FirstOrDefault((KeyValuePair<CompoundFieldDefinition, Array> cfd) => cfd.Key.Name == cName);
			if (firstOrDefault.Key.Type != firstOrDefault2.Key.Type)
			{
				prog.ReportMessage("Column types differ: " + firstOrDefault.Key.Type.ToString() + " [BASE] vs " + firstOrDefault2.Key.Type.ToString() + " [NEW]");
			}
			else if (firstOrDefault.Key.Type == typeof(float))
			{
				float[] baseBuf = firstOrDefault.Value as float[];
				float[] newBuf = firstOrDefault2.Value as float[];
				DiffBuffer(baseBuf, newBuf, prog, -1, firstOrDefault.Key.Name);
			}
			else if (firstOrDefault.Key.Type == typeof(double))
			{
				double[] baseBuf2 = firstOrDefault.Value as double[];
				double[] newBuf2 = firstOrDefault2.Value as double[];
				DiffBuffer(baseBuf2, newBuf2, prog, -1, firstOrDefault.Key.Name);
			}
			else if (firstOrDefault.Key.Type == typeof(int))
			{
				int[] baseBuf3 = firstOrDefault.Value as int[];
				int[] newBuf3 = firstOrDefault2.Value as int[];
				DiffBuffer(baseBuf3, newBuf3, prog, -1, firstOrDefault.Key.Name);
			}
			else if (firstOrDefault.Key.Type == typeof(string))
			{
				string[] baseBuf4 = firstOrDefault.Value as string[];
				string[] newBuf4 = firstOrDefault2.Value as string[];
				DiffBuffer(baseBuf4, newBuf4, prog, -1, firstOrDefault.Key.Name);
			}
			else if (firstOrDefault.Key.Type == typeof(bool))
			{
				bool[] baseBuf5 = firstOrDefault.Value as bool[];
				bool[] newBuf5 = firstOrDefault2.Value as bool[];
				DiffBuffer(baseBuf5, newBuf5, prog, -1, firstOrDefault.Key.Name);
			}
			else
			{
				prog.ReportMessage("Diff not implemented for column of type " + firstOrDefault.Key.Type.ToString());
			}
		}
	}

	public static void MakeDiff(string baseFilename, string compareFilename, string outputFilename)
	{
		using H5Reader h5Reader = new H5Reader(baseFilename);
		using H5Reader h5Reader2 = new H5Reader(compareFilename);
		using (new H5Writer(outputFilename))
		{
			string groupPath = "/";
			h5Reader.GetFullyQualifiedGroupNames(groupPath);
			h5Reader2.GetFullyQualifiedGroupNames(groupPath);
		}
	}

	private static void Recurse()
	{
	}
}
