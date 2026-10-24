using System.Runtime.InteropServices;

namespace Utility.Processes;

public struct ParentProcessUtilities
{
	internal nint Reserved1;

	internal nint PebBaseAddress;

	internal nint Reserved2_0;

	internal nint Reserved2_1;

	internal nint UniqueProcessId;

	internal nint InheritedFromUniqueProcessId;

	[DllImport("ntdll.dll")]
	public static extern int NtQueryInformationProcess(nint processHandle, int processInformationClass, ref ParentProcessUtilities processInformation, int processInformationLength, out int returnLength);
}
