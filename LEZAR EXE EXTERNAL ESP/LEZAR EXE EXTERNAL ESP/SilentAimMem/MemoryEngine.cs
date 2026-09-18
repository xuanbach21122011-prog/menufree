using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SilentAimMem
{
    public struct VmInfo
    {
        public ulong Pvm;
        public ulong Pvcpu0;
        public uint CpuCount;
        public ulong ReadCounter;
        public ulong RegionSize;
    }

    public struct TranslationResult
    {
        public ulong GuestPhysical;
        public ulong HostPointer;
    }

    public class MemoryEngine
    {
        private static class Offsets
        {
            public const ulong VmCpuCount = 0x3C;
            public const ulong VmReadCounter = 0x10AFF0;
            public const ulong VmDirectRamWindow = 0x104000;
            public const ulong VmDirectRamTag = 0x106000;
            public const ulong VmRamRangeMode = 0x106019;
            public const ulong VmRangeCache = 0x106050;
            public const ulong VmRangeTreeRoot = 0x106098;
            public const ulong VmMmio2Table = 0x1060B0;
            public const ulong VmChunkCache = 0x106860;
            public const ulong VmChunkTreeRoot = 0x106C60;
            public const ulong VmPageMapCache = 0x106C80;
            public const ulong VmCpuArray = 0x11FE00;
            public const ulong BstkMapChunkRva = 0xDA070;
            public const ulong ExpectedPvcpuDelta = 0x120000;
            public const ulong RangeStart = 0x0;
            public const ulong RangeSize = 0x8;
            public const ulong RangeHostBase = 0x30;
            public const ulong RangeLeft = 0x50;
            public const ulong RangeRight = 0x58;
            public const ulong RangePageDescTable = 0x70;
            public const ulong RangePageDescStride = 0x10;
            public const ulong PageDescState = 0x0;
            public const ulong PageDescInfo = 0x8;
            public const ulong ChunkNodeLeft = 0x0;
            public const ulong ChunkNodeRight = 0x8;
            public const ulong ChunkNodeKey = 0x10;
            public const ulong ChunkHostBase = 0x28;
            public const ulong MmioRangeHostBase = 0x70;
            public const ulong PageMapCacheStride = 0x20;
            public const ulong PageMapCachedTag = 0x0;
            public const ulong PageMapPageDesc = 0x8;
            public const ulong PageMapHostBase = 0x18;
            public const ulong RemoteStubPvm = 6;
            public const ulong RemoteStubChunkId = 15;
            public const ulong RemoteStubResultPtr0 = 21;
            public const ulong RemoteStubTargetFn = 31;
            public const ulong RemoteStubResultPtr1 = 43;
        }

        private static class Util
        {
            public const ulong LongModePageFrameMask = 0x000FFFFFFFFFF000UL;
            public const ulong PageSize = 0x1000UL;
            public const ulong LargePage1GbMask = 0x000FFFFFFFE00000UL;
            public const ulong LargePage2MbMask = 0x000FFFFFFFE00000UL;
            public const ulong UserPointerMin = 0x10000UL;
            public const ulong UserPointerMax = 0x00007FFFFFFFFFFFUL;
            public const ulong ReadCounterLimit = 1000000000000UL;
            public const ulong DirectStateMaskHigh = 0x7000000000000UL;
            public const ulong DirectStateMaskLow = 0x0FFFFFFFFFF000UL;
            public const ulong PresentBit = 1UL << 0;
            public const ulong PageSizeBit = 1UL << 7;
            public const ulong PageMask = ~0xFFFUL;
            public const ulong PageOffsetMask = 0xFFFUL;
            public const uint ChunkCacheMask = 0x3F;
            public const ulong RangeCacheMask = 0x7UL;
            public const ulong PageMapCacheMask = 0xFFUL;
            public const int RemoteStubSize = 64;
            public const int PEB_LDR = 0x18;

            public static bool IsLikelyUserPointer(ulong value)
            {
                return value >= UserPointerMin && value < UserPointerMax;
            }

            public static bool IsLikelyGuestKernelPointer(ulong value)
            {
                return value >= 0xFFFF000000000000UL;
            }

            public static string Hex(ulong value)
            {
                return $"0x{value:X}";
            }
        }

        private IntPtr processHandle;
        private uint processId;
        private ulong moduleBase;
        private VmInfo vmInfo;
        private readonly object cacheLock = new object();
        private readonly Dictionary<(ulong cr3, ulong va), ulong> gvaToPhysicalPageCache = new();
        private readonly Dictionary<(ulong cr3, ulong va), ulong> gvaToHostPageCache = new();
        private readonly Dictionary<ulong, ulong> physToHostPageCache = new();

        public bool Initialize(bool verbose = false)
        {
            Shutdown();

            var processes = Process.GetProcesses();

            var pid = FindProcessIdByName("HD-Player.exe");
            if (pid == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[mem] Process 'HD-Player.exe' not found!");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[mem] Found 'HD-Player.exe' PID={pid}");
            Console.ForegroundColor = ConsoleColor.Yellow;

            processHandle = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE | NativeMethods.PROCESS_VM_OPERATION,
                false, (int)pid);
            if (processHandle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[mem] OpenProcess failed! Error={err}");
                Console.ForegroundColor = ConsoleColor.White;
                return false;
            }
            processId = pid;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[mem] OpenProcess OK");

            var mod = FindModuleInProcess(processId, "BstkVMM.dll");
            if (mod.Base == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[mem] Module 'BstkVMM.dll' not found in process!");
                Console.ForegroundColor = ConsoleColor.White;
                Shutdown();
                return false;
            }
            moduleBase = mod.Base;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[mem] Module base=0x{mod.Base:X} size={mod.Size}");
            Console.ForegroundColor = ConsoleColor.Yellow;

            var vmi = LocateVmInfo(verbose);
            if (!vmi.HasValue)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[mem] VM info NOT found!");
                Console.ForegroundColor = ConsoleColor.White;
                Shutdown();
                return false;
            }
            vmInfo = vmi.Value;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[mem] VM found! pvm=0x{vmInfo.Pvm:X} pvcpu=0x{vmInfo.Pvcpu0:X} cpus={vmInfo.CpuCount}");
            Console.WriteLine("[mem] Memory engine initialized OK!");
            Console.ForegroundColor = ConsoleColor.White;
            return true;
        }

        public void Shutdown()
        {
            ClearCaches();
            if (processHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(processHandle);
                processHandle = IntPtr.Zero;
            }
            processId = 0;
            moduleBase = 0;
            vmInfo = default;
        }

        public bool IsInitialized => processHandle != IntPtr.Zero && processId != 0 && vmInfo.Pvm != 0 && vmInfo.Pvcpu0 != 0;
        public ulong Pvm => vmInfo.Pvm;
        public ulong Pvcpu => vmInfo.Pvcpu0;
        public uint ProcessId => processId;

        public void ClearCaches()
        {
            lock (cacheLock)
            {
                gvaToPhysicalPageCache.Clear();
                gvaToHostPageCache.Clear();
                physToHostPageCache.Clear();
            }
        }

        private void ClearTranslationCaches()
        {
            ClearCaches();
        }

        private bool ReadHostBytes(ulong address, byte[] buffer, int size)
        {
            if (processHandle == IntPtr.Zero || buffer == null || size == 0)
                return false;
            UIntPtr bytesRead;
            bool ok = NativeMethods.ReadProcessMemory(processHandle, (UIntPtr)address, buffer, (UIntPtr)size, out bytesRead);
            return ok && (int)bytesRead == size;
        }

        private bool ReadHostBytesAt(ulong address, byte[] buffer, int size, int bufferOffset)
        {
            if (processHandle == IntPtr.Zero || buffer == null || size == 0)
                return false;
            GCHandle h = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr dest = IntPtr.Add(h.AddrOfPinnedObject(), bufferOffset);
                UIntPtr bytesRead;
                bool ok = NativeMethods.ReadProcessMemory(processHandle, (UIntPtr)address, dest, (UIntPtr)size, out bytesRead);
                return ok && (int)bytesRead == size;
            }
            finally
            {
                h.Free();
            }
        }

        private bool ReadHostBytes(ulong address, IntPtr buffer, int size)
        {
            if (processHandle == IntPtr.Zero || buffer == IntPtr.Zero || size == 0)
                return false;
            UIntPtr bytesRead;
            bool ok = NativeMethods.ReadProcessMemory(processHandle, (UIntPtr)address, buffer, (UIntPtr)size, out bytesRead);
            return ok && (int)bytesRead == size;
        }

        private bool WriteHostBytes(ulong address, byte[] buffer, int size)
        {
            if (processHandle == IntPtr.Zero || buffer == null || size == 0)
                return false;
            UIntPtr bytesWritten;
            bool ok = NativeMethods.WriteProcessMemory(processHandle, (UIntPtr)address, buffer, (UIntPtr)size, out bytesWritten);
            return ok && (int)bytesWritten == size;
        }

        private bool WriteHostBytesAt(ulong address, byte[] buffer, int size, int bufferOffset)
        {
            if (processHandle == IntPtr.Zero || buffer == null || size == 0)
                return false;
            GCHandle h = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr src = IntPtr.Add(h.AddrOfPinnedObject(), bufferOffset);
                UIntPtr bytesWritten;
                bool ok = NativeMethods.WriteProcessMemory(processHandle, (UIntPtr)address, src, (UIntPtr)size, out bytesWritten);
                return ok && (int)bytesWritten == size;
            }
            finally
            {
                h.Free();
            }
        }

        private T ReadHostValue<T>(ulong address) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] buf = new byte[size];
            if (!ReadHostBytes(address, buf, size))
                return default;
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(h.AddrOfPinnedObject());
            }
            finally
            {
                h.Free();
            }
        }

        private bool ReadHostValue<T>(ulong address, out T value) where T : struct
        {
            value = default;
            int size = Marshal.SizeOf<T>();
            byte[] buf = new byte[size];
            if (!ReadHostBytes(address, buf, size))
                return false;
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                value = Marshal.PtrToStructure<T>(h.AddrOfPinnedObject());
                return true;
            }
            finally
            {
                h.Free();
            }
        }

        private VmInfo? LocateVmInfo(bool verbose)
        {
            var mbi = new NativeMethods.MEMORY_BASIC_INFORMATION();
            ulong currentAddress = 0;
            VmInfo? fallbackResult = null;
            int mbiSize = Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

            while (NativeMethods.VirtualQueryEx(processHandle, (UIntPtr)currentAddress, out mbi, (UIntPtr)mbiSize) == (UIntPtr)mbiSize)
            {
                uint protection = mbi.Protect & 0xFF;
                bool readableWritable = protection == NativeMethods.PAGE_READWRITE || protection == NativeMethods.PAGE_EXECUTE_READWRITE;

                if (mbi.State == NativeMethods.MEM_COMMIT && readableWritable && (ulong)mbi.RegionSize >= 128 * 1024)
                {
                    ulong candidate = (ulong)mbi.BaseAddress;

                    uint cpuCount = ReadHostValue<uint>(candidate + Offsets.VmCpuCount);
                    bool cpuCountOk = cpuCount >= 1 && cpuCount <= 8;
                    bool cpuWindowOk = Offsets.VmCpuArray + sizeof(ulong) <= (ulong)mbi.RegionSize;
                    ulong cpu0 = ReadHostValue<ulong>(candidate + Offsets.VmCpuArray);
                    bool cpu0ReadOk = cpu0 != 0;
                    bool cpu0Ok = cpu0ReadOk && cpu0 == candidate + Offsets.ExpectedPvcpuDelta;
                    ulong readCounter = ReadHostValue<ulong>(candidate + Offsets.VmReadCounter);
                    bool readCounterReadOk = readCounter != 0;
                    bool readCounterOk = readCounterReadOk && readCounter <= Util.ReadCounterLimit;

                    if (cpuCountOk && cpuWindowOk && cpu0Ok && readCounterOk)
                    {
                        return new VmInfo
                        {
                            Pvm = candidate,
                            Pvcpu0 = cpu0,
                            CpuCount = cpuCount,
                            ReadCounter = readCounter,
                            RegionSize = (ulong)mbi.RegionSize
                        };
                    }

                    if (!fallbackResult.HasValue && cpuCountOk && cpuWindowOk && cpu0ReadOk && readCounterOk && (ulong)mbi.RegionSize == Offsets.ExpectedPvcpuDelta)
                    {
                        fallbackResult = new VmInfo
                        {
                            Pvm = candidate,
                            Pvcpu0 = cpu0,
                            CpuCount = cpuCount,
                            ReadCounter = readCounter,
                            RegionSize = (ulong)mbi.RegionSize
                        };
                    }
                }

                ulong nextAddr = currentAddress + (ulong)mbi.RegionSize;
                if (nextAddr <= currentAddress) break;
                currentAddress = nextAddr;
            }

            return fallbackResult;
        }

        private static uint FindProcessIdByName(string processName)
        {
            string searchName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.ProcessName.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                    {
                        return (uint)proc.Id;
                    }
                }
                catch { }
            }
            return 0;
        }

        private (ulong Base, uint Size) FindModuleInProcess(uint pid, string moduleName)
        {
            string searchName = moduleName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);

            try
            {
                var proc = Process.GetProcessById((int)pid);
                foreach (ProcessModule mod in proc.Modules)
                {
                    try
                    {
                        string modName = mod.ModuleName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                        if (modName.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                        {
                            return ((ulong)(long)mod.BaseAddress, (uint)mod.ModuleMemorySize);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return (0, 0);
        }

        private bool MapChunkViaBstkVmm(uint chunkId, out ulong outChunkPointer, bool verbose)
        {
            outChunkPointer = 0;
            if (moduleBase == 0 || processId == 0)
                return false;

            var remoteProcess = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_CREATE_THREAD | NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE,
                false, (int)processId);
            if (remoteProcess == IntPtr.Zero)
                return false;

            int resultSize = Marshal.SizeOf<ulong>() + Marshal.SizeOf<long>();
            int totalSize = resultSize + Util.RemoteStubSize;
            var remoteMem = NativeMethods.VirtualAllocEx(remoteProcess, UIntPtr.Zero, (UIntPtr)totalSize, NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_EXECUTE_READWRITE);
            if (remoteMem == UIntPtr.Zero)
            {
                NativeMethods.CloseHandle(remoteProcess);
                return false;
            }

            ulong remoteResult = (ulong)(long)remoteMem;
            ulong remoteCode = remoteResult + (ulong)resultSize;
            ulong remoteFn = moduleBase + Offsets.BstkMapChunkRva;

            byte[] stub = new byte[Util.RemoteStubSize];
            stub[0] = 0x48; stub[1] = 0x83; stub[2] = 0xEC; stub[3] = 0x28;
            stub[4] = 0x48; stub[5] = 0xB9;
            BitConverter.GetBytes(vmInfo.Pvm).CopyTo(stub, 6);
            stub[14] = 0xBA;
            BitConverter.GetBytes(chunkId).CopyTo(stub, 15);
            stub[19] = 0x49; stub[20] = 0xB8;
            BitConverter.GetBytes(remoteResult).CopyTo(stub, 21);
            stub[29] = 0x48; stub[30] = 0xB8;
            BitConverter.GetBytes(remoteFn).CopyTo(stub, 31);
            stub[39] = 0xFF; stub[40] = 0xD0;
            stub[41] = 0x48; stub[42] = 0xB9;
            BitConverter.GetBytes(remoteResult).CopyTo(stub, 43);
            stub[51] = 0x48; stub[52] = 0x63; stub[53] = 0xC0;
            stub[54] = 0x48; stub[55] = 0x89; stub[56] = 0x41; stub[57] = 0x08;
            stub[58] = 0x48; stub[59] = 0x83; stub[60] = 0xC4; stub[61] = 0x28;
            stub[62] = 0xC3;

            byte[] initialResult = new byte[resultSize];
            UIntPtr bytesWritten;
            NativeMethods.WriteProcessMemory(remoteProcess, remoteMem, initialResult, (UIntPtr)resultSize, out bytesWritten);
            NativeMethods.WriteProcessMemory(remoteProcess, (UIntPtr)remoteCode, stub, (UIntPtr)Util.RemoteStubSize, out bytesWritten);

            IntPtr threadId;
            IntPtr remoteThread = NativeMethods.CreateRemoteThread(remoteProcess, IntPtr.Zero, UIntPtr.Zero, (IntPtr)(long)remoteCode, UIntPtr.Zero, 0, out threadId);
            if (remoteThread == IntPtr.Zero)
            {
                NativeMethods.VirtualFreeEx(remoteProcess, remoteMem, UIntPtr.Zero, NativeMethods.MEM_RELEASE);
                NativeMethods.CloseHandle(remoteProcess);
                return false;
            }

            uint waitResult = NativeMethods.WaitForSingleObject(remoteThread, 5000);
            NativeMethods.CloseHandle(remoteThread);
            if (waitResult != 0)
            {
                NativeMethods.VirtualFreeEx(remoteProcess, remoteMem, UIntPtr.Zero, NativeMethods.MEM_RELEASE);
                NativeMethods.CloseHandle(remoteProcess);
                return false;
            }

            byte[] finalResult = new byte[resultSize];
            UIntPtr bytesRead;
            bool readOk = NativeMethods.ReadProcessMemory(remoteProcess, remoteMem, finalResult, (UIntPtr)resultSize, out bytesRead);
            NativeMethods.VirtualFreeEx(remoteProcess, remoteMem, UIntPtr.Zero, NativeMethods.MEM_RELEASE);
            NativeMethods.CloseHandle(remoteProcess);

            if (!readOk) return false;

            ulong chunkPointer = BitConverter.ToUInt64(finalResult, 0);
            long status = BitConverter.ToInt64(finalResult, 8);

            if (status < 0 || chunkPointer == 0)
                return false;

            outChunkPointer = chunkPointer;
            return true;
        }

        private ulong? FindChunkFromCache(uint chunkId)
        {
            uint slot = chunkId & Util.ChunkCacheMask;
            ulong slotAddress = vmInfo.Pvm + Offsets.VmChunkCache + (ulong)slot * 0x10;

            uint cachedId = ReadHostValue<uint>(slotAddress);
            ulong chunkPointer = ReadHostValue<ulong>(slotAddress + 0x8);

            if (cachedId != chunkId || !Util.IsLikelyUserPointer(chunkPointer))
                return null;

            return chunkPointer;
        }

        private ulong? FindChunkFromTree(uint chunkId, bool verbose)
        {
            ulong node = ReadHostValue<ulong>(vmInfo.Pvm + Offsets.VmChunkTreeRoot);

            while (node != 0)
            {
                uint key = ReadHostValue<uint>(node + Offsets.ChunkNodeKey);

                if (key == chunkId) return node;

                ulong nextOffset = chunkId < key ? Offsets.ChunkNodeLeft : Offsets.ChunkNodeRight;
                node = ReadHostValue<ulong>(node + nextOffset);
            }
            return null;
        }

        private ulong? FindPhysicalRange(ulong guestPhys)
        {
            ulong slot = (guestPhys >> 20) & Util.RangeCacheMask;
            ulong cacheAddress = vmInfo.Pvm + Offsets.VmRangeCache + slot * sizeof(ulong);
            ulong node = ReadHostValue<ulong>(cacheAddress);

            if (node != 0)
            {
                ulong rangeStart = ReadHostValue<ulong>(node + Offsets.RangeStart);
                ulong rangeSize = ReadHostValue<ulong>(node + Offsets.RangeSize);
                if (guestPhys >= rangeStart && guestPhys - rangeStart < rangeSize)
                    return node;
            }

            node = ReadHostValue<ulong>(vmInfo.Pvm + Offsets.VmRangeTreeRoot);
            while (node != 0)
            {
                ulong rangeStart = ReadHostValue<ulong>(node + Offsets.RangeStart);
                ulong rangeSize = ReadHostValue<ulong>(node + Offsets.RangeSize);

                if (guestPhys >= rangeStart && guestPhys - rangeStart < rangeSize)
                    return node;

                ulong nextOffset = guestPhys < rangeStart ? Offsets.RangeLeft : Offsets.RangeRight;
                node = ReadHostValue<ulong>(node + nextOffset);
            }
            return null;
        }

        private ulong? TryPageMapCache(ulong guestPhys, bool verbose)
        {
            ulong slot = (guestPhys >> 12) & Util.PageMapCacheMask;
            ulong entryAddress = vmInfo.Pvm + Offsets.VmPageMapCache + slot * Offsets.PageMapCacheStride;
            ulong pageTag = guestPhys & Util.PageMask;

            ulong cachedTag = ReadHostValue<ulong>(entryAddress + Offsets.PageMapCachedTag);
            ulong pageDescription = ReadHostValue<ulong>(entryAddress + Offsets.PageMapPageDesc);
            ulong hostBase = ReadHostValue<ulong>(entryAddress + Offsets.PageMapHostBase);

            if (cachedTag == pageTag && Util.IsLikelyUserPointer(hostBase))
                return hostBase | (guestPhys & Util.PageOffsetMask);

            return null;
        }

        private ulong? MapPhysicalPageToHostBase(ulong guestPhys, bool verbose)
        {
            var rangeResult = FindPhysicalRange(guestPhys);
            if (!rangeResult.HasValue)
                return null;

            ulong rangeStart = ReadHostValue<ulong>(rangeResult.Value + Offsets.RangeStart);
            ulong pageIndex = (guestPhys - rangeStart) >> 12;
            ulong pageDescriptionAddr = rangeResult.Value + Offsets.RangePageDescTable + pageIndex * Offsets.RangePageDescStride;

            ulong state = ReadHostValue<ulong>(pageDescriptionAddr + Offsets.PageDescState);
            uint info = ReadHostValue<uint>(pageDescriptionAddr + Offsets.PageDescInfo);

            uint pageClass = (uint)((state >> 51) & 0x7);
            uint readMapKind = (uint)((state >> 48) & 0x3);

            byte ramRangeMode = 0;
            ReadHostValue(vmInfo.Pvm + Offsets.VmRamRangeMode, out ramRangeMode);

            if (pageClass != 2 && pageClass != 3 && ramRangeMode != 0)
            {
                ulong rangeHostBase = ReadHostValue<ulong>(rangeResult.Value + Offsets.RangeHostBase);
                if (!Util.IsLikelyUserPointer(rangeHostBase))
                    return null;
                return rangeHostBase + ((guestPhys - rangeStart) & Util.PageMask);
            }

            if (readMapKind == 0)
                return vmInfo.Pvm + Offsets.VmDirectRamWindow;

            if (pageClass == 2 || pageClass == 3)
            {
                uint mmioId = info >> 24;
                uint mmioPage = info & 0x00FFFFFF;

                ulong mmioRange = ReadHostValue<ulong>(vmInfo.Pvm + Offsets.VmMmio2Table + (ulong)mmioId * sizeof(ulong));
                if (!Util.IsLikelyUserPointer(mmioRange))
                    return null;

                ulong hostBase = ReadHostValue<ulong>(mmioRange + Offsets.MmioRangeHostBase);
                return hostBase + ((ulong)mmioPage << 12);
            }

            uint chunkId = info >> 9;
            uint pageInChunk = info & 0x1FF;

            if (chunkId != 0)
            {
                var chunkResult = FindChunkFromCache(chunkId);
                if (!chunkResult.HasValue)
                {
                    chunkResult = FindChunkFromTree(chunkId, verbose);
                    if (!chunkResult.HasValue)
                    {
                        ulong remoteChunk;
                        if (MapChunkViaBstkVmm(chunkId, out remoteChunk, verbose))
                            chunkResult = remoteChunk;
                    }
                    if (!chunkResult.HasValue)
                        return null;
                }

                ulong chunkHostBase = ReadHostValue<ulong>(chunkResult.Value + Offsets.ChunkHostBase);
                return chunkHostBase + ((ulong)pageInChunk << 12);
            }

            if (info == 0)
            {
                ulong directTag = ReadHostValue<ulong>(vmInfo.Pvm + Offsets.VmDirectRamTag);
                bool directClass = pageClass == 4;
                bool directStateMatch = (state & Util.DirectStateMaskHigh) == 0 && (state & Util.DirectStateMaskLow) == directTag;
                if (directClass || directStateMatch)
                    return vmInfo.Pvm + Offsets.VmDirectRamWindow;
            }

            return null;
        }

        private ulong? MapPhysicalToHost(ulong guestPhys, bool verbose)
        {
            ulong guestPhysPage = guestPhys & Util.PageMask;
            lock (cacheLock)
            {
                if (physToHostPageCache.TryGetValue(guestPhysPage, out ulong cachedHostPage))
                    return cachedHostPage | (guestPhys & Util.PageOffsetMask);
            }

            var pageMapResult = TryPageMapCache(guestPhys, verbose);
            if (pageMapResult.HasValue)
            {
                lock (cacheLock)
                {
                    physToHostPageCache[guestPhysPage] = (ulong)pageMapResult & Util.PageMask;
                }
                return pageMapResult;
            }

            var pageBase = MapPhysicalPageToHostBase(guestPhys, verbose);
            if (!pageBase.HasValue)
                return null;

            lock (cacheLock)
            {
                physToHostPageCache[guestPhysPage] = pageBase.Value;
            }
            return pageBase.Value | (guestPhys & Util.PageOffsetMask);
        }

        public TranslationResult? ResolveGuestPhysical(ulong guestPhys, bool verbose = false)
        {
            var hostPointer = MapPhysicalToHost(guestPhys, verbose);
            if (!hostPointer.HasValue)
                return null;
            return new TranslationResult { GuestPhysical = guestPhys, HostPointer = hostPointer.Value };
        }

        public bool ReadGuestPhysical(ulong guestPhys, byte[] buffer, int size, bool verbose = false)
        {
            int copied = 0;
            while (copied < size)
            {
                ulong currentPhys = guestPhys + (ulong)copied;
                int chunkSize = Math.Min(size - copied, (int)(Util.PageSize - (currentPhys & Util.PageOffsetMask)));

                var tr = ResolveGuestPhysical(currentPhys, verbose);
                if (!tr.HasValue)
                    return false;

                if (!ReadHostBytesAt(tr.Value.HostPointer, buffer, chunkSize, copied))
                {
                    lock (cacheLock) { physToHostPageCache.Remove(currentPhys & Util.PageMask); }
                    var retry = ResolveGuestPhysical(currentPhys, verbose);
                    if (!retry.HasValue || !ReadHostBytesAt(retry.Value.HostPointer, buffer, chunkSize, copied))
                        return false;
                }
                copied += chunkSize;
            }
            return true;
        }

        public bool ReadGuestPhysical<T>(ulong guestPhys, out T value, bool verbose = false) where T : struct
        {
            value = default;
            int size = Marshal.SizeOf<T>();
            byte[] buf = new byte[size];
            if (!ReadGuestPhysical(guestPhys, buf, size, verbose))
                return false;
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                value = Marshal.PtrToStructure<T>(h.AddrOfPinnedObject());
                return true;
            }
            finally
            {
                h.Free();
            }
        }

        public bool ReadGuestKernel(ulong kernelVa, byte[] buffer, int size, bool verbose = false)
        {
            int copied = 0;
            while (copied < size)
            {
                ulong currentVa = kernelVa + (ulong)copied;
                int chunkSize = Math.Min(size - copied, (int)(Util.PageSize - (currentVa & Util.PageOffsetMask)));

                ulong currentPhys;
                if (!TryConvertKernelVirtualToPhysical(currentVa, out currentPhys))
                    return false;

                var tr = ResolveGuestPhysical(currentPhys, verbose);
                if (!tr.HasValue)
                    return false;

                if (!ReadHostBytesAt(tr.Value.HostPointer, buffer, chunkSize, copied))
                {
                    lock (cacheLock) { physToHostPageCache.Remove(currentPhys & Util.PageMask); }
                    var retry = ResolveGuestPhysical(currentPhys, verbose);
                    if (!retry.HasValue || !ReadHostBytesAt(retry.Value.HostPointer, buffer, chunkSize, copied))
                        return false;
                }
                copied += chunkSize;
            }
            return true;
        }

        public bool ReadGuestKernel<T>(ulong kernelVa, out T value, bool verbose = false) where T : struct
        {
            value = default;
            int size = Marshal.SizeOf<T>();
            byte[] buf = new byte[size];
            if (!ReadGuestKernel(kernelVa, buf, size, verbose))
                return false;
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                value = Marshal.PtrToStructure<T>(h.AddrOfPinnedObject());
                return true;
            }
            finally
            {
                h.Free();
            }
        }

        public bool WriteGuestPhysical(ulong guestPhys, byte[] buffer, int size, bool verbose = false)
        {
            int written = 0;
            while (written < size)
            {
                ulong currentPhys = guestPhys + (ulong)written;
                int chunkSize = Math.Min(size - written, (int)(Util.PageSize - (currentPhys & Util.PageOffsetMask)));

                var tr = ResolveGuestPhysical(currentPhys, verbose);
                if (!tr.HasValue)
                    return false;

                if (!WriteHostBytesAt(tr.Value.HostPointer, buffer, chunkSize, written))
                {
                    lock (cacheLock) { physToHostPageCache.Remove(currentPhys & Util.PageMask); }
                    var retry = ResolveGuestPhysical(currentPhys, verbose);
                    if (!retry.HasValue || !WriteHostBytesAt(retry.Value.HostPointer, buffer, chunkSize, written))
                        return false;
                }
                written += chunkSize;
            }
            return true;
        }

        public bool WriteGuestPhysical<T>(ulong guestPhys, T value, bool verbose = false) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] buf = new byte[size];
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(value, h.AddrOfPinnedObject(), false);
            }
            finally
            {
                h.Free();
            }
            return WriteGuestPhysical(guestPhys, buf, size, verbose);
        }

        public bool ReadGuestUser(ulong guestCr3, ulong guestVa, byte[] buffer, int size, bool verbose = false)
        {
            int copied = 0;
            while (copied < size)
            {
                ulong currentVa = guestVa + (ulong)copied;
                ulong pageOffset = currentVa & Util.PageOffsetMask;
                int chunkSize = Math.Min(size - copied, (int)(Util.PageSize - pageOffset));
                ulong currentVaPage = currentVa & Util.PageMask;
                ulong cr3Page = guestCr3 & Util.LongModePageFrameMask;

                ulong cachedHostPage;
                bool cacheHit = false;
                lock (cacheLock)
                {
                    cacheHit = gvaToHostPageCache.TryGetValue((cr3Page, currentVaPage), out cachedHostPage);
                }

                if (cacheHit)
                {
                    if (ReadHostBytesAt(cachedHostPage + pageOffset, buffer, chunkSize, copied))
                    {
                        copied += chunkSize;
                        continue;
                    }
                    lock (cacheLock) { gvaToHostPageCache.Remove((cr3Page, currentVaPage)); }
                }

                bool readOk = false;
                for (int attempt = 0; attempt < 2 && !readOk; attempt++)
                {
                    var physResult = TranslateGuestVa(guestCr3, currentVa, verbose);
                    if (!physResult.HasValue)
                    {
                        lock (cacheLock) { gvaToPhysicalPageCache.Remove((cr3Page, currentVaPage)); }
                        continue;
                    }

                    var hostResult = MapPhysicalToHost(physResult.Value, verbose);
                    if (!hostResult.HasValue)
                    {
                        lock (cacheLock)
                        {
                            gvaToPhysicalPageCache.Remove((cr3Page, currentVaPage));
                            physToHostPageCache.Remove(physResult.Value);
                        }
                        continue;
                    }

                    if (ReadHostBytesAt(hostResult.Value, buffer, chunkSize, copied))
                    {
                        lock (cacheLock)
                        {
                            gvaToHostPageCache[(cr3Page, currentVaPage)] = (ulong)hostResult & Util.PageMask;
                        }
                        readOk = true;
                        break;
                    }

                    lock (cacheLock)
                    {
                        gvaToHostPageCache.Remove((cr3Page, currentVaPage));
                        gvaToPhysicalPageCache.Remove((cr3Page, currentVaPage));
                        physToHostPageCache.Remove(physResult.Value);
                    }
                }

                if (!readOk) return false;
                copied += chunkSize;
            }
            return true;
        }

        public bool ReadGuestUser<T>(ulong guestCr3, ulong guestVa, out T value, bool verbose = false) where T : struct
        {
            value = default;
            int size = Marshal.SizeOf<T>();
            byte[] buf = new byte[size];
            if (!ReadGuestUser(guestCr3, guestVa, buf, size, verbose))
                return false;
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                value = Marshal.PtrToStructure<T>(h.AddrOfPinnedObject());
                return true;
            }
            finally
            {
                h.Free();
            }
        }

        public bool WriteGuestUser(ulong guestCr3, ulong guestVa, byte[] buffer, int size, bool verbose = false)
        {
            int written = 0;
            while (written < size)
            {
                ulong currentVa = guestVa + (ulong)written;
                ulong pageOffset = currentVa & Util.PageOffsetMask;
                int chunkSize = Math.Min(size - written, (int)(Util.PageSize - pageOffset));
                ulong currentVaPage = currentVa & Util.PageMask;
                ulong cr3Page = guestCr3 & Util.LongModePageFrameMask;

                var physResult = TranslateGuestVa(guestCr3, currentVa, verbose);
                if (!physResult.HasValue)
                    return false;

                var hostResult = MapPhysicalToHost(physResult.Value, verbose);
                if (!hostResult.HasValue)
                    return false;

                if (!WriteHostBytesAt(hostResult.Value, buffer, chunkSize, written))
                    return false;

                written += chunkSize;
            }
            return true;
        }

        public bool WriteGuestUser<T>(ulong guestCr3, ulong guestVa, T value, bool verbose = false) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] buf = new byte[size];
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(value, h.AddrOfPinnedObject(), false);
            }
            finally
            {
                h.Free();
            }
            return WriteGuestUser(guestCr3, guestVa, buf, size, verbose);
        }

        public ulong? TranslateGuestVa(ulong guestCr3, ulong guestVa, bool verbose = false)
        {
            ulong canonicalCheck = guestVa >> 47;
            if (canonicalCheck != 0 && canonicalCheck != 0x1FFFFUL)
                return null;

            ulong cr3Page = guestCr3 & Util.LongModePageFrameMask;
            ulong vaPage = guestVa & Util.PageMask;

            lock (cacheLock)
            {
                if (gvaToPhysicalPageCache.TryGetValue((cr3Page, vaPage), out ulong cachedPage))
                    return cachedPage | (guestVa & Util.PageOffsetMask);
            }

            ulong pml4e;
            if (!ReadGuestPhysical(cr3Page + ((guestVa >> 39) & 0x1FFUL) * sizeof(ulong), out pml4e, verbose) || (pml4e & Util.PresentBit) == 0)
                return null;

            ulong pdptBase = pml4e & Util.LongModePageFrameMask;
            ulong pdpte;
            if (!ReadGuestPhysical(pdptBase + ((guestVa >> 30) & 0x1FFUL) * sizeof(ulong), out pdpte, verbose) || (pdpte & Util.PresentBit) == 0)
                return null;

            if ((pdpte & Util.PageSizeBit) != 0)
            {
                ulong resolved = (pdpte & Util.LargePage1GbMask) | (guestVa & 0x3FFFFFFFUL);
                lock (cacheLock) { gvaToPhysicalPageCache[(cr3Page, vaPage)] = resolved & Util.PageMask; }
                return resolved;
            }

            ulong pdBase = pdpte & Util.LongModePageFrameMask;
            ulong pde;
            if (!ReadGuestPhysical(pdBase + ((guestVa >> 21) & 0x1FFUL) * sizeof(ulong), out pde, verbose) || (pde & Util.PresentBit) == 0)
                return null;

            if ((pde & Util.PageSizeBit) != 0)
            {
                ulong resolved = (pde & Util.LargePage2MbMask) | (guestVa & 0x1FFFFFUL);
                lock (cacheLock) { gvaToPhysicalPageCache[(cr3Page, vaPage)] = resolved & Util.PageMask; }
                return resolved;
            }

            ulong ptBase = pde & Util.LongModePageFrameMask;
            ulong pte;
            if (!ReadGuestPhysical(ptBase + ((guestVa >> 12) & 0x1FFUL) * sizeof(ulong), out pte, verbose) || (pte & Util.PresentBit) == 0)
                return null;

            ulong resolvedFinal = (pte & Util.LongModePageFrameMask) | (guestVa & Util.PageOffsetMask);
            lock (cacheLock) { gvaToPhysicalPageCache[(cr3Page, vaPage)] = resolvedFinal & Util.PageMask; }
            return resolvedFinal;
        }

        public ulong? ReadMmCr3(ulong mmVa, bool verbose = false)
        {
            ulong pgdKernelVa;
            if (!ReadGuestKernel(mmVa + 0x50, out pgdKernelVa, verbose) || pgdKernelVa == 0)
                return null;

            ulong pgdPhys;
            if (!TryConvertKernelVirtualToPhysical(pgdKernelVa, out pgdPhys))
                return null;

            return pgdPhys;
        }

        public static bool TryConvertKernelVirtualToPhysical(ulong kernelVa, out ulong outPhys)
        {
            outPhys = 0;
            if (kernelVa >= 0xFFFF888000000000UL && kernelVa < 0xFFFFC88000000000UL)
            {
                outPhys = kernelVa - 0xFFFF888000000000UL;
                return true;
            }
            if (kernelVa >= 0xFFFF880000000000UL && kernelVa < 0xFFFFC80000000000UL)
            {
                outPhys = kernelVa - 0xFFFF880000000000UL;
                return true;
            }
            if (kernelVa >= 0xFFFFFFFF80000000UL && kernelVa < 0xFFFFFFFFFF000000UL)
            {
                outPhys = kernelVa - 0xFFFFFFFF80000000UL;
                return true;
            }
            return false;
        }
    }
}
