using System.Numerics;

namespace SilentAimMem
{
    internal class Entity
    {
        internal ulong Address;
        internal bool IsKnown;
        internal Bool3 IsTeam;
        internal Vector3 Head;
        internal Vector3 Neck;
        internal Vector3 Root;
        internal Vector3 LeftHand;
        internal Vector3 RightHand;
        internal Vector3 Eye;
        internal Vector3 LeftShoulder;
        internal Vector3 RightShoulder;
        internal Vector3 LeftFoot;
        internal Vector3 RightFoot;
        internal Vector3 LeftElbow;
        internal Vector3 RightElbow;
        internal Vector3 Hip;
        internal Vector3 LeftAnkle;
        internal Vector3 RightAnkle;
        internal Vector3 Breast;
        internal Vector3 LeftLeg;
        internal Vector3 RightLeg;
        internal short Health;
        internal bool IsDead;
        internal bool IsKnocked;
        internal string Name;
        internal float Distance;
        internal bool IsFiring;
        internal bool IsVisible;
        internal bool IsBot;
        internal ulong Avatar;
        internal string WeaponName;
    }
}
