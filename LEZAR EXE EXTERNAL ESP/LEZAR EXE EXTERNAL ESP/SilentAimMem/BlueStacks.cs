using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SilentAimMem
{
    public class BlueStacks
    {
        private string packageName;
        private Task taskValue;
        private ulong guestCr3;

        private const ulong InitTask = 0xFFFFFFFF822147C0UL;

        private static class TaskOffset
        {
            public const int Mm = 0x4C0;
            public const int Tasks = 0x470;
            public const int Pid = 0x570;
            public const int Tgid = 0x574;
            public const int Comm = 0x720;
        }

        private static class ListHeadOffset
        {
            public const int Next = 0x0;
            public const int Prev = 0x8;
        }

        private static class MmOffset
        {
            public const int Mmap = 0x0;
            public const int ArgStart = 0x138;
            public const int ArgEnd = 0x140;
        }

        private static class VmaOffset
        {
            public const int VmStart = 0x0;
            public const int VmNext = 0x10;
            public const int VmFile = 0xA0;
        }

        private static class DentryOffset
        {
            public const int DName = 0x20;
            public const int DNameLen = 0x24;
            public const int DNameName = 0x28;
            public const int DIname = 0x38;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuestListHead
        {
            public ulong Next;
            public ulong Prev;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuestQstr
        {
            public uint Hash;
            public uint Len;
            public ulong Name;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuestPath
        {
            public ulong Mnt;
            public ulong Dentry;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuestFile
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
            public byte[] Pad0;
            public GuestPath FPath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuestDentry
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x18)]
            public byte[] Pad0;
            public ulong DParent;
            public GuestQstr DName;
            public ulong DInode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] DIname;
        }

        private struct Task
        {
            public ulong TaskAddr;
            public ulong Mm;
            public uint Pid;
            public uint Tgid;
            public string Comm;
            public string Cmdline;
        }

        public bool Setup(string pkg, bool verbose = false, Action<int, int> onAttempt = null)
        {
            packageName = pkg;
            taskValue = default;
            guestCr3 = 0;

            Memory.Destroy();

            for (int attempt = 1; attempt <= 20; attempt++)
            {
                onAttempt?.Invoke(attempt, 20);
                Memory.ClearCr3();

                if (Memory.Setup(verbose))
                {
                    if (FindTask(packageName, out taskValue, verbose))
                    {
                        guestCr3 = Memory.GetCr3(taskValue.Mm);
                        if (guestCr3 != 0)
                        {
                            Memory.SetCr3(guestCr3);
                            return true;
                        }
                    }
                }

                Memory.Destroy();

                if (attempt < 20)
                    System.Threading.Thread.Sleep(500);
            }

            packageName = null;
            taskValue = default;
            guestCr3 = 0;
            return false;
        }

        public void Destroy()
        {
            packageName = null;
            taskValue = default;
            guestCr3 = 0;
            Memory.Destroy();
        }

        public ulong GetCr3() => guestCr3;

        public ulong GetModuleBase(string moduleName, int mapIndex = 1, bool verbose = false)
        {
            if (taskValue.Mm == 0 || guestCr3 == 0 || mapIndex == 0)
                return 0;

            ulong currentVma = 0;
            if (!Memory.ReadKernel<ulong>(taskValue.Mm + MmOffset.Mmap, out currentVma, verbose))
                return 0;

            var seenVmas = new List<ulong>();
            var moduleBases = new List<ulong>();
            ulong matchedFile = 0;

            while (currentVma != 0 && seenVmas.Count < 8192)
            {
                if (seenVmas.Contains(currentVma))
                    break;

                seenVmas.Add(currentVma);

                ulong vmStart = 0, vmNext = 0, vmFile = 0;
                if (!Memory.ReadKernel<ulong>(currentVma + VmaOffset.VmStart, out vmStart, verbose) ||
                    !Memory.ReadKernel<ulong>(currentVma + VmaOffset.VmNext, out vmNext, verbose) ||
                    !Memory.ReadKernel<ulong>(currentVma + VmaOffset.VmFile, out vmFile, verbose))
                    break;

                if (vmFile != 0)
                {
                    string fileName = ReadFileName(vmFile, verbose);
                    bool fileMatch = MatchesLibraryName(fileName, moduleName);

                    if (fileMatch && matchedFile == 0)
                        matchedFile = vmFile;

                    if (fileMatch || (matchedFile != 0 && vmFile == matchedFile))
                    {
                        if (!moduleBases.Contains(vmStart))
                            moduleBases.Add(vmStart);
                    }
                }

                if (vmNext == 0 || vmNext == currentVma)
                    break;

                currentVma = vmNext;
            }

            if (moduleBases.Count < mapIndex)
                return 0;

            return moduleBases[mapIndex - 1];
        }

        private bool ReadTask(ulong task, out Task outTask, bool verbose)
        {
            outTask = new Task { TaskAddr = task };

            if (!Memory.ReadKernel<ulong>(task + TaskOffset.Mm, out outTask.Mm, verbose) ||
                !Memory.ReadKernel<uint>(task + TaskOffset.Pid, out outTask.Pid, verbose) ||
                !Memory.ReadKernel<uint>(task + TaskOffset.Tgid, out outTask.Tgid, verbose))
                return false;

            outTask.Comm = ReadTaskComm(task, verbose);
            if (string.IsNullOrEmpty(outTask.Comm))
                return false;

            if (outTask.Mm != 0 && Utility.IsLikelyGuestKernelPointer(outTask.Mm))
            {
                ulong taskCr3 = Memory.GetCr3(outTask.Mm);
                if (taskCr3 != 0)
                    outTask.Cmdline = ReadTaskCmdline(outTask.Mm, taskCr3, verbose);
            }

            return true;
        }

        private string ReadTaskComm(ulong task, bool verbose)
        {
            byte[] comm = new byte[16];
            if (!Memory.ReadKernel(task + TaskOffset.Comm, comm, 16, verbose))
                return "";
            return SanitizeString(Encoding.ASCII.GetString(comm).TrimEnd('\0'));
        }

        private string ReadTaskCmdline(ulong mm, ulong guestCr3, bool verbose)
        {
            ulong argStart = 0, argEnd = 0;
            if (!Memory.ReadKernel<ulong>(mm + MmOffset.ArgStart, out argStart, verbose) ||
                !Memory.ReadKernel<ulong>(mm + MmOffset.ArgEnd, out argEnd, verbose) ||
                argStart == 0 || argEnd == 0 || argEnd <= argStart)
                return "";

            ulong span = argEnd - argStart;
            if (span > 0x2000) span = 0x2000;
            if (span == 0) return "";

            byte[] data = new byte[span];
            if (!Memory.ReadUser(guestCr3, argStart, data, (int)span, verbose))
                return "";

            return SanitizeString(Encoding.ASCII.GetString(data).TrimEnd('\0'));
        }

        private string ReadKernelString(ulong address, int maxLength, bool verbose)
        {
            if (address == 0 || maxLength == 0) return "";
            byte[] data = new byte[maxLength + 1];
            if (!Memory.ReadKernel(address, data, maxLength, verbose))
                return "";
            return SanitizeString(Encoding.UTF8.GetString(data).TrimEnd('\0'));
        }

        private string ReadDentryName(ulong dentry, bool verbose)
        {
            byte[] buf = new byte[128];
            if (!Memory.ReadKernel(dentry, buf, 128, verbose))
                return "";

            uint dNameLen = BitConverter.ToUInt32(buf, DentryOffset.DNameLen);
            ulong dNameName = BitConverter.ToUInt64(buf, DentryOffset.DNameName);

            if (dNameLen > 0 && dNameLen <= 256 && dNameName != 0)
            {
                string name = ReadKernelString(dNameName, (int)dNameLen, verbose);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }

            byte[] iname = new byte[32];
            Array.Copy(buf, DentryOffset.DIname, iname, 0, 32);
            return SanitizeString(Encoding.ASCII.GetString(iname).TrimEnd('\0'));
        }

        private string ReadFileName(ulong file, bool verbose)
        {
            byte[] fileBuf = new byte[128];
            if (!Memory.ReadKernel(file, fileBuf, 128, verbose))
                return "";

            ulong dentry = BitConverter.ToUInt64(fileBuf, 0x18);
            if (dentry == 0) return "";

            return ReadDentryName(dentry, verbose);
        }

        private int ScoreTask(Task task)
        {
            if (string.IsNullOrEmpty(packageName))
                return 1;

            int score = 0;

            if (!string.IsNullOrEmpty(task.Cmdline))
            {
                if (task.Cmdline.Equals(packageName, StringComparison.OrdinalIgnoreCase))
                    score = Math.Max(score, 100);
                else if (task.Cmdline.IndexOf(packageName, StringComparison.OrdinalIgnoreCase) >= 0)
                    score = Math.Max(score, 90);
                else if (packageName.IndexOf(task.Cmdline, StringComparison.OrdinalIgnoreCase) >= 0)
                    score = Math.Max(score, 80);
            }

            if (task.Comm.Equals(packageName, StringComparison.OrdinalIgnoreCase))
                score = Math.Max(score, 60);
            else if (task.Comm.Length > 0 && packageName.StartsWith(task.Comm, StringComparison.OrdinalIgnoreCase))
                score = Math.Max(score, 50);

            if (task.Pid == task.Tgid && score > 0)
                score++;

            return score;
        }

        private bool FindTask(string packageName, out Task outTask, bool verbose)
        {
            outTask = default;

            ulong initTasksHead = InitTask + TaskOffset.Tasks;

            ulong initTasksNext = 0, initTasksPrev = 0;
            if (!Memory.ReadKernel<ulong>(initTasksHead + ListHeadOffset.Next, out initTasksNext, verbose) ||
                !Memory.ReadKernel<ulong>(initTasksHead + ListHeadOffset.Prev, out initTasksPrev, verbose))
                return false;

            var seenHeads = new List<ulong>();
            Task bestTask = default;
            int bestScore = 0;

            Action<ulong, bool> walk = (startHead, reverse) =>
            {
                ulong currentHead = startHead;
                while (currentHead != 0 && currentHead != initTasksHead && seenHeads.Count < 4096)
                {
                    if (currentHead < (ulong)TaskOffset.Tasks)
                        break;

                    if (seenHeads.Contains(currentHead))
                        break;

                    seenHeads.Add(currentHead);

                    ulong taskAddr = currentHead - (ulong)TaskOffset.Tasks;

                    Task currentTask;
                    if (ReadTask(taskAddr, out currentTask, verbose) &&
                        currentTask.Mm != 0 &&
                        Utility.IsLikelyGuestKernelPointer(currentTask.Mm))
                    {
                        int currentScore = ScoreTask(currentTask);
                        if (currentScore > bestScore)
                        {
                            bestScore = currentScore;
                            bestTask = currentTask;
                        }
                    }

                    ulong nextHead = 0;
                    int linkOffset = reverse ? ListHeadOffset.Prev : ListHeadOffset.Next;
                    if (!Memory.ReadKernel<ulong>(currentHead + (ulong)linkOffset, out nextHead, false))
                        break;

                    if (nextHead == 0 || nextHead == currentHead)
                        break;

                    currentHead = nextHead;
                }
            };

            walk(initTasksNext, false);
            walk(initTasksPrev, true);

            if (bestScore == 0 || bestTask.Mm == 0 || !Utility.IsLikelyGuestKernelPointer(bestTask.Mm))
                return false;

            outTask = bestTask;
            return true;
        }

        private static bool MatchesLibraryName(string fileName, string wanted)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(wanted))
                return false;
            return fileName.Equals(wanted, StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (c >= 32 && c < 127)
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
