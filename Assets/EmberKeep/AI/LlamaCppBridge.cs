using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EmberKeep.AI {
    public static class LlamaCppBridge {
        const string DLL = "emberkeep_native";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void TokenCallback(IntPtr tokenUtf8, IntPtr userData);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ek_init([MarshalAs(UnmanagedType.LPStr)] string modelPath);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ek_generate(
            [MarshalAs(UnmanagedType.LPStr)] string prompt,
            int maxTokens,
            TokenCallback cb,
            IntPtr userData);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ek_shutdown();

        public static string PtrToUtf8(IntPtr ptr) {
            if (ptr == IntPtr.Zero) return string.Empty;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
