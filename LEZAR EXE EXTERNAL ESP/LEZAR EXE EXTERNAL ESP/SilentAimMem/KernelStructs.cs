using System;
using System.Runtime.InteropServices;

namespace SilentAimMem
{
    public static class KernelOffsets
    {
        public const ulong LinuxDirectMapBase = 0xFFFF888000000000UL;
        public const ulong LinuxDirectMapEnd = 0xFFFFC87FFFFFFFFFUL;
        public const ulong LinuxKernelImageBase = 0xFFFFFFFF80000000UL;
        public const ulong LinuxKernelImageEnd = 0xFFFFFFFFFF000000UL;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestListHead
    {
        public ulong Next;
        public ulong Prev;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestQstr
    {
        public uint Hash;
        public uint Len;
        public ulong Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestPath
    {
        public ulong Mnt;
        public ulong Dentry;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestVfsMount
    {
        public ulong MntRoot;
        public ulong MntSb;
        public int MntFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestMount
    {
        public ulong Pad0;
        public ulong Pad1;
        public ulong MntParent;
        public ulong MntMountpoint;
        public GuestVfsMount Mnt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestDentry
    {
        public ulong Pad0;
        public ulong Pad1;
        public ulong Pad2;
        public ulong DParent;
        public GuestQstr DName;
        public ulong DInode;
        public unsafe fixed byte DIname[32];
        public ulong Pad3;
        public ulong DOp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestFile
    {
        public ulong Pad0;
        public ulong Pad1;
        public GuestPath FPath;
        public ulong FInode;
        public ulong FOp;
        public ulong Pad2;
        public ulong FCount;
        public uint FFlags;
        public uint FMode;
        public ulong Pad3;
        public ulong Pad4;
        public ulong FPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestInode
    {
        public ushort IMode;
        public unsafe fixed byte Pad0[30];
        public ulong IOp;
        public ulong ISb;
        public ulong Pad1;
        public ulong Pad2;
        public ulong IIno;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestMmStruct
    {
        public ulong Mmap;
        public ulong MmRbRoot;
        public ulong Pad0;
        public ulong Pad1;
        public ulong Pad2;
        public ulong Pad3;
        public ulong Pad4;
        public ulong Pad5;
        public ulong Pad6;
        public ulong Pad7;
        public ulong Pad8;
        public ulong PgD;
        public ulong Pad9;
        public ulong Pad10;
        public ulong Pad11;
        public ulong Pad12;
        public ulong ArgStart;
        public ulong ArgEnd;
        public ulong EnvStart;
        public ulong EnvEnd;
        public ulong Pad13;
        public ulong Pad14;
        public ulong Pad15;
        public ulong Pad16;
        public ulong Pad17;
        public ulong Pad18;
        public ulong Pad19;
        public ulong Pad20;
        public ulong Pad21;
        public ulong Pad22;
        public ulong Pad23;
        public ulong Pad24;
        public ulong Pad25;
        public ulong Pad26;
        public ulong Pad27;
        public ulong Pad28;
        public ulong Pad29;
        public ulong Pad30;
        public ulong Pad31;
        public ulong Pad32;
        public ulong Pad33;
        public ulong Pad34;
        public ulong Pad35;
        public ulong Pad36;
        public ulong Pad37;
        public ulong Pad38;
        public ulong Pad39;
        public ulong Pad40;
        public ulong Pad41;
        public ulong Pad42;
        public ulong Pad43;
        public ulong Pad44;
        public ulong Pad45;
        public ulong Pad46;
        public ulong Pad47;
        public ulong Pad48;
        public ulong ExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuestTaskStruct
    {
        public ulong Pad0;
        public ulong Pad1;
        public ulong Pad2;
        public ulong Pad3;
        public ulong Pad4;
        public ulong Pad5;
        public ulong Pad6;
        public ulong Pad7;
        public ulong Pad8;
        public ulong Pad9;
        public ulong Pad10;
        public ulong Pad11;
        public ulong Pad12;
        public ulong Pad13;
        public ulong Pad14;
        public ulong Pad15;
        public ulong Pad16;
        public ulong Pad17;
        public ulong Pad18;
        public ulong Pad19;
        public ulong Pad20;
        public ulong Pad21;
        public ulong Pad22;
        public ulong Pad23;
        public ulong Pad24;
        public ulong Pad25;
        public ulong Pad26;
        public ulong Pad27;
        public ulong Pad28;
        public ulong Pad29;
        public ulong Pad30;
        public ulong Pad31;
        public ulong Pad32;
        public ulong Pad33;
        public ulong Pad34;
        public ulong Pad35;
        public ulong Pad36;
        public ulong Pad37;
        public ulong Pad38;
        public ulong Pad39;
        public ulong Pad40;
        public ulong Pad41;
        public ulong Pad42;
        public ulong Pad43;
        public ulong Pad44;
        public ulong Pad45;
        public ulong Pad46;
        public ulong Pad47;
        public ulong Pad48;
        public ulong Pad49;
        public ulong Pad50;
        public ulong Pad51;
        public ulong Pad52;
        public ulong Pad53;
        public ulong Pad54;
        public ulong Pad55;
        public ulong Pad56;
        public ulong Pad57;
        public ulong Pad58;
        public ulong Pad59;
        public ulong Pad60;
        public ulong Pad61;
        public ulong Pad62;
        public ulong Pad63;
        public ulong Pad64;
        public ulong Pad65;
        public ulong Pad66;
        public ulong Pad67;
        public ulong Pad68;
        public ulong Pad69;
        public ulong Pad70;
        public ulong Pad71;
        public ulong Pad72;
        public ulong Pad73;
        public ulong Pad74;
        public ulong Pad75;
        public ulong Pad76;
        public ulong Pad77;
        public ulong Pad78;
        public ulong Pad79;
        public ulong Pad80;
        public ulong Pad81;
        public ulong Pad82;
        public ulong Pad83;
        public ulong Pad84;
        public ulong Pad85;
        public ulong Pad86;
        public ulong Pad87;
        public ulong Pad88;
        public ulong Pad89;
        public ulong Pad90;
        public ulong Pad91;
        public ulong Pad92;
        public ulong Pad93;
        public ulong Pad94;
        public ulong Pad95;
        public GuestListHead Tasks;
        public ulong Pad96;
        public ulong Pad97;
        public ulong Pad98;
        public ulong Pad99;
        public ulong Pad100;
        public ulong Pad101;
        public ulong Pad102;
        public ulong Pad103;
        public ulong Pad104;
        public ulong Pad105;
        public ulong Mm;
        public ulong ActiveMm;
        public ulong Pad106;
        public ulong Pad107;
        public ulong Pad108;
        public ulong Pad109;
        public ulong Pad110;
        public ulong Pad111;
        public ulong Pad112;
        public ulong Pad113;
        public ulong Pad114;
        public ulong Pad115;
        public ulong Pad116;
        public ulong Pad117;
        public ulong Pad118;
        public ulong Pad119;
        public ulong Pid;
        public uint Tgid;
    }
}
