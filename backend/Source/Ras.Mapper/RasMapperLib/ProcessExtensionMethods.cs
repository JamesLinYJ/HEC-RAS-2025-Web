using System;
using System.Diagnostics;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class ProcessExtensionMethods
{
	public static string ToMemoryUsageString(this Process proc)
	{
		return "Mem Usage (Working Set): " + Strings.Format((double)proc.WorkingSet64 / 1024.0 / 1024.0, "0.0") + " MB   VM Size (Private Bytes): " + Strings.Format((double)proc.PagedMemorySize64 / 1024.0 / 1024.0, "0.0") + " MB  GC TotalMemory: " + Strings.Format((double)GC.GetTotalMemory(forceFullCollection: true) / 1024.0 / 1024.0, "0.0") + " MB";
	}
}
