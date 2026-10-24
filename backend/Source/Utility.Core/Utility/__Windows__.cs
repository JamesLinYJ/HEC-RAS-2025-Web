using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Utility;

public static class __Windows__
{
	[Flags]
	private enum AssocF : uint
	{
		None = 0u,
		Init_NoRemapCLSID = 1u,
		Init_ByExeName = 2u,
		Open_ByExeName = Init_ByExeName,
		Init_DefaultToStar = 4u,
		Init_DefaultToFolder = 8u,
		NoUserSettings = 0x10u,
		NoTruncate = 0x20u,
		Verify = 0x40u,
		RemapRunDll = 0x80u,
		NoFixUps = 0x100u,
		IgnoreBaseClass = 0x200u,
		Init_IgnoreUnknown = 0x400u,
		Init_FixedProgId = 0x800u,
		IsProtocol = 0x1000u,
		InitForFile = 0x2000u
	}

	public enum AssocStr
	{
		Command = 1,
		Executable,
		FriendlyDocName,
		FriendlyAppName,
		NoOpen,
		ShellNewValue,
		DDECommand,
		DDEIfExec,
		DDEApplication,
		DDETopic,
		InfoTip,
		QuickTip,
		TileInfo,
		ContentType,
		DefaultIcon,
		ShellExtension,
		DropTarget,
		DelegateExecute,
		SupportedUriProtocols,
		Max
	}

	private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	public struct WNDCLASSEX
	{
		[MarshalAs(UnmanagedType.U4)]
		public int cbSize;

		[MarshalAs(UnmanagedType.U4)]
		public int style;

		public nint lpfnWndProc;

		public int cbClsExtra;

		public int cbWndExtra;

		public nint hInstance;

		public nint hIcon;

		public nint hCursor;

		public nint hbrBackground;

		public string lpszMenuName;

		public string lpszClassName;

		public nint hIconSm;

		public static WNDCLASSEX Build()
		{
			return new WNDCLASSEX
			{
				cbSize = Marshal.SizeOf(typeof(WNDCLASSEX))
			};
		}
	}

	public static nint HWND_MESSAGE = new IntPtr(-3);

	public const uint CW_USEDEFAULT = 2147483648u;

	[DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
	private static extern uint AssocQueryString(AssocF flags, AssocStr str, string pszAssoc, string pszExtra, [Out] StringBuilder pszOut, ref uint pcchOut);

	public static bool AssocQueryString(AssocStr association, string extension, out string execName)
	{
		uint pcchOut = 0u;
		if (AssocQueryString(AssocF.None, association, extension, null, null, ref pcchOut) != 1)
		{
			execName = "";
			return false;
		}
		StringBuilder stringBuilder = new StringBuilder((int)pcchOut);
		if (AssocQueryString(AssocF.None, association, extension, null, stringBuilder, ref pcchOut) != 0)
		{
			execName = "";
			return false;
		}
		execName = stringBuilder.ToString();
		return true;
	}

	[DllImport("user32.dll", SetLastError = true)]
	public static extern nint CreateWindowEx(WindowStylesEx dwExStyle, [MarshalAs(UnmanagedType.LPStr)] string lpClassName, [MarshalAs(UnmanagedType.LPStr)] string lpWindowName, WindowStyles dwStyle, uint x, uint y, uint nWidth, uint nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern nint CreateWindowEx(WindowStylesEx dwExStyle, nint classRegisterValue, [MarshalAs(UnmanagedType.LPStr)] string lpWindowName, WindowStyles dwStyle, uint x, uint y, uint nWidth, uint nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.U2)]
	private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

	public static nint CreateMessagingWindow()
	{
		Marshal.GetLastWin32Error();
		WNDCLASSEX lpwcx = WNDCLASSEX.Build();
		lpwcx.lpszClassName = "alexClass";
		ushort registerClassEx = RegisterClassEx(ref lpwcx);
		Marshal.GetLastWin32Error();
		nint result = IntPtr.Zero;
		try
		{
			result = CreateWindowEx(WindowStylesEx.WS_EX_LEFT, new IntPtr(registerClassEx), "akaka", WindowStyles.WS_OVERLAPPED, 2147483648u, 2147483648u, 2147483648u, 2147483648u, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		}
		catch
		{
		}
		Marshal.GetLastWin32Error();
		return result;
	}
}
