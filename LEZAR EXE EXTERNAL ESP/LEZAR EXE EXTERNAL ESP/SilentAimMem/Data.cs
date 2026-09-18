using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;

namespace SilentAimMem
{
    internal static class Data
    {
        private static int failedReadCount = 0;
        private static int frameCount = 0;
        private static bool loggedOnce = false;

        private static bool diagnosticDone = false;

        internal static void Work()
        {
            Console.WriteLine("[Data] Data.Work thread started");
            Thread.Sleep(500);

            while (true)
            {
                Thread.Sleep(1);

                if (!Memory.IsValid) continue;

                if (!Memory.Read<uint>(Offsets.Il2CppBase + Offsets.InitBase, out var baseGameFacade) || baseGameFacade == 0)
                {
                    failedReadCount++;
                    if (failedReadCount > 30)
                    {
                        ResetCache();
                        failedReadCount = 0;
                    }
                    if (!loggedOnce)
                    {
                        Console.WriteLine($"[Data] FAIL: InitBase read failed. Il2CPP=0x{Offsets.Il2CppBase:X} offset=0x{Offsets.InitBase:X}");
                        loggedOnce = true;
                    }
                    continue;
                }

                if (loggedOnce)
                {
                    Console.WriteLine($"[Data] InitBase OK: 0x{baseGameFacade:X}");
                    loggedOnce = false;
                }

                failedReadCount = 0;

                if (!Memory.Read<uint>(baseGameFacade, out var gameFacade) || gameFacade == 0)
                {
                    if (frameCount % 300 == 0)
                        Console.WriteLine($"[Data] FAIL: gameFacade=0 (base=0x{baseGameFacade:X})");
                    continue;
                }

                if (!Memory.Read<uint>(gameFacade + Offsets.StaticClass, out var staticGameFacade) || staticGameFacade == 0)
                {
                    if (frameCount % 300 == 0)
                        Console.WriteLine($"[Data] FAIL: StaticClass (gf=0x{gameFacade:X}+0x{Offsets.StaticClass:X})");
                    continue;
                }

                if (!Memory.Read<uint>(staticGameFacade, out var currentGame) || currentGame == 0)
                {
                    if (frameCount % 300 == 0)
                        Console.WriteLine($"[Data] FAIL: currentGame (sc=0x{staticGameFacade:X})");
                    continue;
                }

                if (!Memory.Read<uint>(currentGame + Offsets.CurrentMatch, out var currentMatch) || currentMatch == 0)
                {
                    Core.MatchStatus = 0;
                    if (frameCount % 300 == 0)
                        Console.WriteLine($"[Data] FAIL: CurrentMatch (cg=0x{currentGame:X}+0x{Offsets.CurrentMatch:X})");
                    continue;
                }

                if (!Memory.Read<uint>(currentMatch + Offsets.LocalPlayer, out var localPlayer) || localPlayer == 0)
                {
                    if (Memory.Read<uint>(currentMatch + Offsets.CurrentObserver, out var observer) && observer != 0)
                    {
                        if (Memory.Read<uint>(observer + Offsets.ObserverPlayer, out var observerPlayer) && observerPlayer != 0)
                        {
                            localPlayer = observerPlayer;
                        }
                    }
                }

                if (localPlayer == 0)
                {
                    if (frameCount % 300 == 0)
                        Console.WriteLine($"[Data] FAIL: localPlayer=0 (match=0x{currentMatch:X})");
                    continue;
                }

                Core.LocalPlayer = localPlayer;

                if (!diagnosticDone)
                {
                    diagnosticDone = true;
                    Console.WriteLine($"=== FULL CHAIN DIAGNOSTIC ===");
                    Console.WriteLine($"  Il2CppBase=0x{Offsets.Il2CppBase:X}");
                    Console.WriteLine($"  InitBaseOffset=0x{Offsets.InitBase:X}");
                    Console.WriteLine($"  baseGameFacade=0x{baseGameFacade:X}");
                    Console.WriteLine($"  gameFacade=0x{gameFacade:X}");
                    Console.WriteLine($"  staticGameFacade=0x{staticGameFacade:X}");
                    Console.WriteLine($"  currentGame=0x{currentGame:X}");
                    Console.WriteLine($"  currentMatch=0x{currentMatch:X}");
                    Console.WriteLine($"  localPlayer=0x{localPlayer:X}");
                    Console.WriteLine($"  guestCr3=0x{Memory.GetActiveCr3():X}");

                    byte[] dump = new byte[64];
                    bool readOk = Memory.Read((ulong)localPlayer, dump, 64);
                    Console.WriteLine($"  localPlayer read64bytes: {(readOk ? "OK" : "FAIL")}");
                    if (readOk)
                    {
                        Console.Write("  hex: ");
                        for (int i = 0; i < 64; i += 4)
                            Console.Write($"{BitConverter.ToUInt32(dump, i):X8} ");
                        Console.WriteLine();
                    }

                    ulong fcAddr = (ulong)(localPlayer + Offsets.FollowCamera);
                    bool fcRead = Memory.Read<uint>(fcAddr, out uint fcVal);
                    Console.WriteLine($"  FollowCamera read at 0x{fcAddr:X}: {(fcRead ? $"OK val=0x{fcVal:X}" : "FAIL")}");

                    Console.WriteLine($"=== END DIAGNOSTIC ===");
                }

                if (Memory.Read<uint>(localPlayer + (uint)Bones.Root, out var localhipBone) && localhipBone != 0)
                {
                    Transform.GetNodePosition(localhipBone, out var hipTransformlcal);
                    Core.playerpos = hipTransformlcal;
                }

                if (Config.NoRecoil)
                {
                    if (Memory.Read<uint>(localPlayer + Offsets.Weapon, out var weapon) && weapon != 0)
                    {
                        if (Memory.Read<uint>(weapon + Offsets.WeaponData, out var weaponData) && weaponData != 0)
                        {
                            if (Memory.Read<float>(weaponData + Offsets.WeaponRecoil, out var recoil) && recoil != 0)
                            {
                                Memory.Write(weaponData + Offsets.WeaponRecoil, 0f);
                            }
                        }
                    }
                }

                if (Memory.Read<uint>(localPlayer + Offsets.MainCameraTransform, out var mainTransform) && mainTransform != 0)
                {
                    if (Transform.GetPosition(mainTransform, out var mainPos))
                    {
                        Core.LocalMainCamera = mainPos;
                    }
                }

                if (!Memory.Read<uint>(localPlayer + Offsets.FollowCamera, out var followCamera) || followCamera == 0)
                {
                    if (frameCount % 300 == 0)
                        Console.WriteLine($"[Data] FAIL: FollowCamera=0 (lp=0x{localPlayer:X}+0x{Offsets.FollowCamera:X})");
                    frameCount++;
                    continue;
                }

                if (!Memory.Read<uint>(followCamera + Offsets.Camera, out var camera) || camera == 0)
                {
                    if (frameCount % 300 == 0)
                        Console.WriteLine($"[Data] FAIL: Camera=0 (fc=0x{followCamera:X}+0x{Offsets.Camera:X})");
                    frameCount++;
                    continue;
                }

                if (!Memory.Read<uint>(camera + 0x8, out var cameraBase) || cameraBase == 0)
                {
                    if (frameCount % 300 == 0)
                        Console.WriteLine($"[Data] FAIL: CameraBase=0 (cam=0x{camera:X}+0x8)");
                    frameCount++;
                    continue;
                }

                if (!Memory.Read<Matrix4x4>(cameraBase + Offsets.ViewMatrix, out var viewMatrix))
                {
                    if (frameCount % 300 == 0)
                        Console.WriteLine($"[Data] FAIL: ViewMatrix read failed (cb=0x{cameraBase:X}+0x{Offsets.ViewMatrix:X})");
                    frameCount++;
                    continue;
                }

                if (!Core.HaveMatrix)
                    Console.WriteLine($"[Data] ViewMatrix acquired! HaveMatrix=true");

                Core.CameraMatrix = viewMatrix;
                Core.HaveMatrix = true;

                int entIterated = 0, entKnown = 0, entFiltered = 0, entAvatarFail = 0;
                foreach (var entity in GetEntities(currentGame, Offsets.DictionaryEntities))
                {
                    if (entity == 0) continue;
                    if (entity == localPlayer) continue;
                    entIterated++;

                    if (Core.Entities.TryGetValue(entity, out var player))
                    {
                        player.Address = entity;

                        if (player.IsTeam == Bool3.True) continue;

                        if (player.IsTeam == Bool3.Unknown)
                        {
                            bool avatarOk = false;
                            if (Memory.Read<uint>(entity + Offsets.AvatarManager, out var avatarManager) && avatarManager != 0)
                            {
                                if (Memory.Read<uint>(avatarManager + Offsets.Avatar, out var avatar) && avatar != 0)
                                {
                                    if (Memory.Read<bool>(avatar + Offsets.Avatar_IsVisible, out var isVisible) && isVisible)
                                    {
                                        if (Memory.Read<uint>(avatar + Offsets.Avatar_Data, out var avatarData) && avatarData != 0)
                                        {
                                            if (Memory.Read<bool>(avatarData + Offsets.Avatar_Data_IsTeam, out var isTeam))
                                            {
                                                avatarOk = true;
                                                if (isTeam)
                                                    player.IsTeam = Bool3.True;
                                                else
                                                {
                                                    player.IsTeam = Bool3.False;
                                                    player.IsKnown = true;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            if (!avatarOk) entAvatarFail++;
                            if (!player.IsKnown)
                            {
                                player.IsKnown = true;
                                player.IsTeam = Bool3.False;
                            }
                        }

                        if (!player.IsKnown) continue;
                        entKnown++;

                        if (Config.IgnoreKnocked)
                        {
                            if (Memory.Read<uint>(entity + Offsets.Player_ShadowBase, out var shadowBase) && shadowBase != 0)
                            {
                                if (Memory.Read<int>(shadowBase + Offsets.XPose, out var xpose))
                                {
                                    player.IsKnocked = xpose == 8;
                                }
                            }
                        }

                        if (Memory.Read<bool>(entity + Offsets.Player_IsDead, out var isDead))
                        {
                            player.IsDead = isDead;
                        }

                        if (Config.ESPWeapon || Config.ESPName || Config.ESPHealth)
                        {
                            if (Memory.Read<uint>(entity + Offsets.Player_Data, out var dataPool) && dataPool != 0)
                            {
                                if (Memory.Read<uint>(dataPool + 0x8, out var poolObj) && poolObj != 0)
                                {
                                    if (Config.ESPWeapon)
                                    {
                                        bool weaponFound = false;
                                        if (Memory.Read<uint>(poolObj + 0x20, out var pool) && pool != 0)
                                        {
                                            if (Memory.Read<short>(pool + 0x10, out var weaponid) && weaponid != 0)
                                            {
                                                player.WeaponName = WeaponIndex.GetWeaponName(weaponid);
                                                weaponFound = true;
                                            }
                                        }
                                        if (!weaponFound)
                                        {
                                            if (Memory.Read<uint>(poolObj + 0x10, out var poolAlt) && poolAlt != 0)
                                            {
                                                if (Memory.Read<short>(poolAlt + 0x10, out var weaponid) && weaponid != 0)
                                                {
                                                    player.WeaponName = WeaponIndex.GetWeaponName(weaponid);
                                                }
                                            }
                                        }
                                    }

                                    if (Config.ESPName)
                                    {
                                        if (Memory.Read<uint>(entity + Offsets.Player_Name, out var nameAddr) && nameAddr != 0)
                                        {
                                            if (Memory.Read<int>(nameAddr + 0x8, out var nameLen) && nameLen > 0)
                                            {
                                                var name = Memory.ReadString(nameAddr + 0xC, nameLen * 2);
                                                if (name != "") player.Name = name;
                                            }
                                        }
                                    }

                                    if (Config.ESPHealth)
                                    {
                                        if (Memory.Read<uint>(poolObj + 0x10, out var pool) && pool != 0)
                                        {
                                            if (Memory.Read<uint>(pool + 0xC, out var healthAddr) && healthAddr != 0)
                                            {
                                                if (Memory.Read<short>(healthAddr, out var health))
                                                {
                                                    player.Health = health;
                                                }
                                            }
                                            else
                                            {
                                                if (Memory.Read<short>(pool + 0x10, out var healthShort))
                                                {
                                                    player.Health = healthShort;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        var rHeadBone = Memory.Read<uint>(entity + (uint)Bones.Head, out var headBone);
                        if (rHeadBone && headBone != 0)
                        {
                            if (Transform.GetNodePosition(headBone, out var headTransform))
                            {
                                player.Head = headTransform;
                                player.Distance = Vector3.Distance(Core.LocalMainCamera, headTransform);
                            }
                        }

                        var rRootBone = Memory.Read<uint>(entity + (uint)Bones.Root, out var rootBone);
                        if (rRootBone && rootBone != 0)
                        {
                            if (Transform.GetNodePosition(rootBone, out var rootTransform))
                            {
                                player.Root = rootTransform;
                            }
                        }

                        var rBreastBone = Memory.Read<uint>(entity + (uint)Bones.Breast, out var breastBone);
                        if (rBreastBone && breastBone != 0)
                        {
                            if (Transform.GetNodePosition(breastBone, out var breastTransform))
                            {
                                player.Breast = breastTransform;
                            }
                        }

                        var rHipBone = Memory.Read<uint>(entity + (uint)Bones.hip, out var hipBone);
                        if (rHipBone && hipBone != 0)
                        {
                            if (Transform.GetNodePosition(hipBone, out var hipTransform))
                            {
                                player.Hip = hipTransform;
                            }
                        }

                        var rLeftHandBone = Memory.Read<uint>(entity + (uint)Bones.LeftHand, out var leftHandBone);
                        if (rLeftHandBone && leftHandBone != 0)
                        {
                            if (Transform.GetNodePosition(leftHandBone, out var leftHandTransform))
                            {
                                player.LeftHand = leftHandTransform;
                            }
                        }

                        var rRightHandBone = Memory.Read<uint>(entity + (uint)Bones.RightHand, out var rightHandBone);
                        if (rRightHandBone && rightHandBone != 0)
                        {
                            if (Transform.GetNodePosition(rightHandBone, out var rightHandTransform))
                            {
                                player.RightHand = rightHandTransform;
                            }
                        }

                        var rLeftShoulderBone = Memory.Read<uint>(entity + (uint)Bones.LeftShoulder, out var leftShoulderBone);
                        if (rLeftShoulderBone && leftShoulderBone != 0)
                        {
                            if (Transform.GetNodePosition(leftShoulderBone, out var leftShoulderTransform))
                            {
                                player.LeftShoulder = leftShoulderTransform;
                            }
                        }

                        var rRightShoulderBone = Memory.Read<uint>(entity + (uint)Bones.RightShoulder, out var rightShoulderBone);
                        if (rRightShoulderBone && rightShoulderBone != 0)
                        {
                            if (Transform.GetNodePosition(rightShoulderBone, out var rightShoulderTransform))
                            {
                                player.RightShoulder = rightShoulderTransform;
                            }
                        }

                        var rLeftFootBone = Memory.Read<uint>(entity + (uint)Bones.LeftFoot, out var leftFootBone);
                        if (rLeftFootBone && leftFootBone != 0)
                        {
                            if (Transform.GetNodePosition(leftFootBone, out var leftFootTransform))
                            {
                                player.LeftFoot = leftFootTransform;
                            }
                        }

                        var rRightFootBone = Memory.Read<uint>(entity + (uint)Bones.RightFoot, out var rightFootBone);
                        if (rRightFootBone && rightFootBone != 0)
                        {
                            if (Transform.GetNodePosition(rightFootBone, out var rightFootTransform))
                            {
                                player.RightFoot = rightFootTransform;
                            }
                        }

                        var rLeftLegBone = Memory.Read<uint>(entity + (uint)Bones.LeftLeg, out var leftLegBone);
                        if (rLeftLegBone && leftLegBone != 0)
                        {
                            if (Transform.GetNodePosition(leftLegBone, out var leftLegTransform))
                            {
                                player.LeftLeg = leftLegTransform;
                            }
                        }

                        var rRightLegBone = Memory.Read<uint>(entity + (uint)Bones.RightLeg, out var rightLegBone);
                        if (rRightLegBone && rightLegBone != 0)
                        {
                            if (Transform.GetNodePosition(rightLegBone, out var rightLegTransform))
                            {
                                player.RightLeg = rightLegTransform;
                            }
                        }

                        var rRightAnkleBone = Memory.Read<uint>(entity + (uint)Bones.rightankle, out var rightAnkleBone);
                        if (rRightAnkleBone && rightAnkleBone != 0)
                        {
                            if (Transform.GetNodePosition(rightAnkleBone, out var rightAnkleTransform))
                            {
                                player.RightAnkle = rightAnkleTransform;
                            }
                        }

                        var rLeftAnkleBone = Memory.Read<uint>(entity + (uint)Bones.leftankle, out var leftAnkleBone);
                        if (rLeftAnkleBone && leftAnkleBone != 0)
                        {
                            if (Transform.GetNodePosition(leftAnkleBone, out var leftAnkleTransform))
                            {
                                player.LeftAnkle = leftAnkleTransform;
                            }
                        }

                        if (Config.Playerup)
                        {
                            if (Memory.Read<uint>(entity + (uint)Bones.Root, out var EntityrootBone) && EntityrootBone != 0)
                            {
                                if (Memory.Read<uint>(EntityrootBone + 0x8, out var transformValue) && transformValue != 0)
                                {
                                    if (Memory.Read<uint>(transformValue + 0x8, out var rootBoneclass) && rootBoneclass != 0)
                                    {
                                        if (Memory.Read<uint>(rootBoneclass + 0x20, out var roootmatrixValuelist) && roootmatrixValuelist != 0)
                                        {
                                            if (Memory.Read<float>(roootmatrixValuelist + 0x84, out var resultValuebone))
                                            {
                                                Memory.Write(roootmatrixValuelist + 0x84, resultValuebone + Config.fly);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        player = new Entity();
                        player.Address = entity;
                        player.IsTeam = Bool3.Unknown;
                        player.IsKnown = false;
                        player.IsDead = true;
                        player.Health = 0;
                        player.IsKnocked = false;
                        player.Head = Vector3.Zero;
                        player.Root = Vector3.Zero;
                        player.Name = "";
                        player.IsFiring = false;
                        player.IsVisible = false;
                        Core.Entities.TryAdd(entity, player);
                        continue;
                    }
                }

                frameCount++;
            }
        }

        private static int entityDiagCount = 0;

        static List<uint> GetEntities(uint baseGame, uint offset)
        {
            List<uint> entityList = new List<uint>();

            if (!Memory.Read<uint>(baseGame + offset, out uint dict) || dict == 0)
            {
                if (entityDiagCount < 5)
                    Console.WriteLine($"[ENT] FAIL: dict=0 (baseGame=0x{baseGame:X}+0x{offset:X}=0x{baseGame + offset:X})");
                entityDiagCount++;
                return entityList;
            }

            if (!Memory.Read<int>(dict + 0x10, out int count) || count < 1 || count > 10000)
            {
                if (entityDiagCount < 5)
                    Console.WriteLine($"[ENT] FAIL: count={count} (dict=0x{dict:X}+0x10)");
                entityDiagCount++;
                return entityList;
            }

            if (!Memory.Read<uint>(dict + 0xC, out uint entries) || entries == 0)
            {
                if (entityDiagCount < 5)
                    Console.WriteLine($"[ENT] FAIL: entries=0 (dict=0x{dict:X}+0xC)");
                entityDiagCount++;
                return entityList;
            }

            if (entityDiagCount < 5)
                Console.WriteLine($"[ENT] dict=0x{dict:X} count={count} entries=0x{entries:X}");

            uint start = entries + 0x10;

            int readOk = 0, readFail = 0;
            for (uint i = 0; i < (uint)count; i++)
            {
                uint entry = start + (i * 0x10);

                if (!Memory.Read<uint>(entry + 0xC, out uint entity) || entity == 0)
                {
                    readFail++;
                    continue;
                }

                entityList.Add(entity);
                readOk++;
            }

            if (entityDiagCount < 5)
                Console.WriteLine($"[ENT] entries: ok={readOk} fail={readFail} total={entityList.Count}");
            entityDiagCount++;

            return entityList;
        }

        internal static void ResetCache()
        {
            Core.HaveMatrix = false;
            Core.MatchStatus = 0;
            Core.Entities.Clear();
        }
    }
}
