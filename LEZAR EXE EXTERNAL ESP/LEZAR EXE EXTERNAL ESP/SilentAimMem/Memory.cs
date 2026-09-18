using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SilentAimMem
{
    public static class Memory
    {
        private static MemoryEngine engine = new MemoryEngine();
        private static ulong guestCr3;

        public static bool Setup(bool verbose = false)
        {
            return engine.Initialize(verbose);
        }

        public static void Destroy()
        {
            engine.Shutdown();
            guestCr3 = 0;
        }

        public static void SetCr3(ulong value) => guestCr3 = value;
        public static void ClearCr3() => guestCr3 = 0;
        public static bool IsValid => engine.IsInitialized;
        public static ulong GetActiveCr3() => guestCr3;

        public static bool Read<T>(ulong address, out T value, bool verbose = false) where T : struct
        {
            value = default;
            if (!IsValid) return false;
            try
            {
                if (guestCr3 != 0 && !Utility.IsLikelyGuestKernelPointer(address))
                    return engine.ReadGuestUser(guestCr3, address, out value, verbose);

                if (Utility.IsLikelyGuestKernelPointer(address))
                    return engine.ReadGuestKernel(address, out value, verbose);

                return engine.ReadGuestPhysical(address, out value, verbose);
            }
            catch { return false; }
        }

        public static T Read<T>(ulong address, bool verbose = false) where T : struct
        {
            Read(address, out T value, verbose);
            return value;
        }

        public static bool Read(ulong address, byte[] buffer, int size, bool verbose = false)
        {
            if (!IsValid) return false;
            try
            {
                if (guestCr3 != 0 && !Utility.IsLikelyGuestKernelPointer(address))
                    return engine.ReadGuestUser(guestCr3, address, buffer, size, verbose);

                if (Utility.IsLikelyGuestKernelPointer(address))
                    return engine.ReadGuestKernel(address, buffer, size, verbose);

                return engine.ReadGuestPhysical(address, buffer, size, verbose);
            }
            catch { return false; }
        }

        public static bool ReadKernel<T>(ulong address, out T value, bool verbose = false) where T : struct
        {
            value = default;
            if (!IsValid) return false;
            try { return engine.ReadGuestKernel(address, out value, verbose); }
            catch { return false; }
        }

        public static bool ReadKernel(ulong address, byte[] buffer, int size, bool verbose = false)
        {
            if (!IsValid) return false;
            try { return engine.ReadGuestKernel(address, buffer, size, verbose); }
            catch { return false; }
        }

        public static bool ReadUser(ulong cr3, ulong address, byte[] buffer, int size, bool verbose = false)
        {
            if (!IsValid) return false;
            try { return engine.ReadGuestUser(cr3, address, buffer, size, verbose); }
            catch { return false; }
        }

        public static string ReadString(ulong address, int size, bool unicode = true)
        {
            if (!IsValid || size <= 0) return "";
            byte[] buf = new byte[size];
            if (!Read(address, buf, size)) return "";
            string str = unicode ? Encoding.Unicode.GetString(buf) : Encoding.Default.GetString(buf);
            int nullIdx = str.IndexOf('\0');
            if (nullIdx >= 0) str = str.Substring(0, nullIdx);
            return str;
        }

        public static bool Write<T>(ulong address, T value) where T : struct
        {
            if (!IsValid) return false;
            try
            {
                if (guestCr3 != 0 && !Utility.IsLikelyGuestKernelPointer(address))
                    return engine.WriteGuestUser(guestCr3, address, value);
                if (Utility.IsLikelyGuestKernelPointer(address))
                {
                    int size = Marshal.SizeOf<T>();
                    byte[] buf = new byte[size];
                    var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
                    try { Marshal.StructureToPtr(value, h.AddrOfPinnedObject(), false); }
                    finally { h.Free(); }
                    return engine.WriteGuestPhysical(address, buf, size);
                }
                return engine.WriteGuestPhysical(address, value);
            }
            catch { return false; }
        }

        public static ulong GetCr3(ulong mm)
        {
            var result = engine.ReadMmCr3(mm);
            if (!result.HasValue) return 0;
            return result.Value;
        }
    }

    public static class Utility
    {
        public static bool IsLikelyGuestKernelPointer(ulong value)
        {
            return value >= 0xFFFF000000000000UL;
        }
    }
}
