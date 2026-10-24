using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Ras.Backend;

/// <summary>
/// Console.SetOut only redirects managed writes. DSS and other native
/// libraries use the OS stdout descriptor, including buffers flushed at exit.
/// Keep a private duplicate for JSON and route native Unix stdout to stderr.
/// Each host process handles one request and exits, so no descriptor restore
/// can accidentally send a delayed native flush back into the protocol.
/// </summary>
public static class BackendStandardOutput
{
    public static TextWriter OpenProtocolWriter()
    {
        if (OperatingSystem.IsWindows()) return Console.Out;
        int descriptor = Duplicate(1);
        if (descriptor < 0) throw new IOException("Cannot capture the backend protocol output.",
            new Win32Exception(Marshal.GetLastPInvokeError()));
        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        try
        {
            if (DuplicateTo(2, 1) < 0)
                throw new IOException("Cannot separate native diagnostics from the backend protocol.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
            return new StreamWriter(new FileStream(handle, FileAccess.Write), new UTF8Encoding(false))
            { AutoFlush = true };
        }
        catch { handle.Dispose(); throw; }
    }

    [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static extern int Duplicate(int descriptor);

    [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
    private static extern int DuplicateTo(int source, int destination);
}
