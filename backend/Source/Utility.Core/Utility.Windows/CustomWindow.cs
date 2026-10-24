using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Utility.Windows;

public class CustomWindow : IDisposable
{
	private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WNDCLASS
	{
		public uint style;

		public nint lpfnWndProc;

		public int cbClsExtra;

		public int cbWndExtra;

		public nint hInstance;

		public nint hIcon;

		public nint hCursor;

		public nint hbrBackground;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string lpszMenuName;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string lpszClassName;
	}

	public static readonly nint HWND_MESSAGE = new IntPtr(-3);

	private const int ERROR_CLASS_ALREADY_EXISTS = 1410;

	private bool isDisposed;

	private nint _hwnd;

	private string _shortClsName;

	private string _clsName;

	private static Dictionary<uint, string> _commandMap = new Dictionary<uint, string>
	{
		{ 1u, "WM_CREATE" },
		{ 12u, "WM_SETTEXT" },
		{ 36u, "WM_GETMINMAXINFO" },
		{ 129u, "WM_NCCREATE" },
		{ 131u, "WM_NCCALCSIZE" }
	};

	private Action<string> _wndProcOverride;

	private static bool DEBUG = false;

	private WndProc m_wnd_proc_delegate;

	public nint HWND => _hwnd;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern ushort RegisterClassW([In] ref WNDCLASS lpWndClass);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint CreateWindowExW(uint dwExStyle, [MarshalAs(UnmanagedType.LPWStr)] string lpClassName, [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool DestroyWindow(nint hWnd);

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	private void Dispose(bool disposing)
	{
		if (!isDisposed)
		{
			if (_hwnd != IntPtr.Zero)
			{
				DestroyWindow(_hwnd);
				_hwnd = IntPtr.Zero;
			}
			isDisposed = true;
		}
	}

	public CustomWindow(string class_name, Action<string> wndProcMessage)
	{
		if (string.IsNullOrEmpty(class_name))
		{
			throw new ArgumentNullException("class_name");
		}
		_clsName = class_name;
		_shortClsName = class_name.Substring(0, 5);
		_wndProcOverride = wndProcMessage;
		m_wnd_proc_delegate = CustomWndProc;
		WNDCLASS lpWndClass = new WNDCLASS
		{
			lpszClassName = class_name,
			lpfnWndProc = Marshal.GetFunctionPointerForDelegate(m_wnd_proc_delegate)
		};
		ushort registerClassW = RegisterClassW(ref lpWndClass);
		int lastWin32Error = Marshal.GetLastWin32Error();
		if (registerClassW == 0 && lastWin32Error != 1410)
		{
			throw new Exception("Could not register window class");
		}
		_hwnd = CreateWindowExW(0u, class_name, string.Empty, 0u, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
	}

	private nint CustomWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
	{
		if (DEBUG)
		{
			string textValue = "";
			_commandMap.TryGetValue(msg, out textValue);
		}
		_wndProcOverride?.Invoke(Marshal.PtrToStringAuto(lParam));
		return DefWindowProcW(hWnd, msg, wParam, lParam);
	}

	private static nint DefaultWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
	{
		return DefWindowProcW(hWnd, msg, wParam, lParam);
	}
}
