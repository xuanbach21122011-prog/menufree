using System;
using System.Collections.Concurrent;
using System.Numerics;

namespace SilentAimMem
{
    internal static class Core
    {
        internal static IntPtr Handle;
        internal static IntPtr Overlay;
        internal static int Width = -1;
        internal static int Height = -1;
        internal static bool HaveMatrix = false;
        internal static Matrix4x4 CameraMatrix;
        internal static ulong LocalPlayer;
        internal static Vector3 LocalMainCamera;
        public static ConcurrentDictionary<ulong, Entity> Entities = new();
        internal static Vector3 playerpos;
        internal static int MatchStatus = 0;
    }
}
