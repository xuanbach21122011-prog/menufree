using System.Drawing;
using System.Numerics;
using System.Windows.Forms;

namespace SilentAimMem
{
    internal static class Config
    {
        internal static AimBotType AimBotType;
        internal static bool AimBot = false;
        internal static Keys AimbotKey = Keys.LButton;
        internal static int AimFovCircle = 500;
        internal static int SmoothnessX = 0;
        internal static int UppDistence = 200;
        internal static bool TeleMe = false;
        internal static bool SPAWN = false;
        internal static bool hamba = false;
        internal static int AimbotSmoothness = 0;
        internal static bool SilentAim1 = false;
        internal static bool Speed = false;
        internal static int EspLineThickNess = 2;
        internal static bool EnableGlow = false;
        public static float GlowRadius = 9;
        public static float GlowRadius1 = 10;
        public static float GlowRadiusdot = 10;
        public static float FeatherAmount = 2f;
        internal static bool enmyteleport2 = false;
        public static float GlowOpacity = 0.02f;
        public static float dotRadius = 3.0f;
        internal static bool IgnoreKnocked = false;
        internal static bool NoCache = false;
        internal static bool StreamMode = false;
        internal static bool ESPLine = false;
        internal static bool SniperSwitch = false;
        internal static bool SniperSwitchOff = false;
        internal static bool FASTRELOAD = false;
        internal static bool Wallhack = false;
        internal static bool SilentAim2 = false;
        internal static bool SilentAim = false;
        internal static bool Tele = false;
        internal static bool ESPWeapon = false;
        internal static bool ESPWeaponIcon = false;
        internal static bool ESPCorneredBox = false;
        internal static bool ESPFillBox = false;
        internal static bool ESPFullBox = false;
        internal static bool Playerup = false;
        internal static int playerupdelay = 25;
        internal static bool UpPlayer = false;
        internal static bool enmyteleport = false;
        public static float fly = 0.150f;
        internal static bool ESPBox = false;
        internal static bool ESPName = false;
        internal static bool ESPHealth = false;
        internal static bool ESPDistance = false;
        internal static bool Cross = false;
        internal static bool NoRecoil = false;
        internal static bool ESPSkeleton = false;
        internal static string ESPMode = "1";
        internal static bool enableAimBot = false;
        internal static bool AimBotRage = false;
        internal static TargetingMode TargetingMode = TargetingMode.ClosestToCrosshair;
        internal static float AimFov = 200f;
        internal static int AimBotMaxDistance = 150;
        internal static bool AimbotVisible = false;
        internal static bool Crosshair = false;
        internal static bool CrossHair = false;
        internal static bool EnemyCount = false;
        internal static bool RGB = false;
        internal static bool FOV = false;
        internal static bool Snapline = false;
        internal static bool AimBotSilent = false;
        internal static float Fov = 200f;
        internal static AimBotTarget AimBotTarget = AimBotTarget.Neck;

        internal static Color ESPLineColor = Color.White;
        internal static Color ESPFullBoxColor = Color.White;
        internal static Color ESPBoxColor = Color.White;
        internal static Color ESPNameColor = Color.White;
        internal static Color KnockedlineClr = Color.Red;
        internal static Color ESPSkeletonColor = Color.White;
        internal static Color FOVColor = Color.White;
        internal static Color CrossHairColor = Color.White;
        internal static Color EnemyCountColor = Color.White;
        internal static Color MocoColor = Color.White;
        internal static Color ESPFillBoxColor = Color.Red;
        internal static float AimBotSmooth = 0f;
    }

    internal enum AimBotType
    {
        Silent,
        Rage
    }

    internal enum LinePosition
    {
        Top,
        Center,
        Bottom
    }

    internal enum TargetingMode
    {
        ClosestToCrosshair,
        Target360,
        ClosestToPlayer,
        LowestHealth,
    }

    internal enum AimBotTarget
    {
        Neck,
        Head,
        Body,
        Spine
    }
}
