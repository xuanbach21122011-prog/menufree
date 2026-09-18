using System;

namespace SilentAimMem
{
    public class GvaMemoryBridge
    {
        private MemoryEngine memoryEngine;
        private ulong guestCr3;

        public void Attach(MemoryEngine engine, ulong cr3)
        {
            memoryEngine = engine;
            guestCr3 = cr3;
        }

        public bool IsReady => memoryEngine != null && guestCr3 != 0;
        public ulong Cr3 => guestCr3;

        public bool Read<T>(ulong guestVa, out T value, bool verbose = false) where T : struct
        {
            value = default;
            if (!IsReady) return false;
            return memoryEngine.ReadGuestUser(guestCr3, guestVa, out value, verbose);
        }

        public T Read<T>(ulong guestVa, bool verbose = false) where T : struct
        {
            if (!IsReady) return default;
            T value;
            memoryEngine.ReadGuestUser(guestCr3, guestVa, out value, verbose);
            return value;
        }

        public bool Write<T>(ulong guestVa, T value, bool verbose = false) where T : struct
        {
            if (!IsReady) return false;
            return memoryEngine.WriteGuestUser(guestCr3, guestVa, value, verbose);
        }

        public bool ReadArray(ulong guestVa, byte[] buffer, int byteCount, bool verbose = false)
        {
            if (!IsReady) return false;
            return memoryEngine.ReadGuestUser(guestCr3, guestVa, buffer, byteCount, verbose);
        }

        public bool WriteArray(ulong guestVa, byte[] buffer, int byteCount, bool verbose = false)
        {
            if (!IsReady) return false;
            return memoryEngine.WriteGuestUser(guestCr3, guestVa, buffer, byteCount, verbose);
        }

        public void ClearTranslationCaches()
        {
            memoryEngine?.ClearCaches();
        }
    }
}
