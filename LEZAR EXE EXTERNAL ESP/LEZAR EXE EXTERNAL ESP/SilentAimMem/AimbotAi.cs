using System;
using System.Numerics;
using System.Threading;

namespace SilentAimMem
{
    internal static class AimbotAi
    {
        internal static void Work()
        {
            while (true)
            {
                if (!Config.AimbotVisible)
                {
                    Thread.Sleep(2);
                    continue;
                }

                if (Core.Width == -1 || Core.Height == -1 || !Core.HaveMatrix)
                {
                    Thread.Sleep(1);
                    continue;
                }

                Entity target = FindBestTarget();
                if (target != null)
                {
                    AimAtTarget(target);
                }
            }
        }

        private static Entity FindBestTarget()
        {
            Entity bestTarget = null;
            float closestDistance = float.MaxValue;
            var screenCenter = new Vector2(Core.Width / 2f, Core.Height / 2f);

            foreach (var entity in Core.Entities.Values)
            {
                if (entity.IsDead) continue;
                if (Config.IgnoreKnocked && entity.IsKnocked) continue;

                var head2D = W2S.WorldToScreen(Core.CameraMatrix, entity.Head, Core.Width, Core.Height);
                if (head2D.X < 1 || head2D.Y < 1) continue;

                float playerDistance = Vector3.Distance(Core.LocalMainCamera, entity.Head);
                if (playerDistance > Config.AimBotMaxDistance) continue;

                float crosshairDistance = Vector2.Distance(screenCenter, head2D);
                if (crosshairDistance < closestDistance && crosshairDistance <= Config.AimFovCircle)
                {
                    closestDistance = crosshairDistance;
                    bestTarget = entity;
                    Thread.Sleep(Config.AimbotSmoothness);
                }
            }

            return bestTarget;
        }

        private static void AimAtTarget(Entity target)
        {
            if (target == null || target.Address == 0) return;

            if (!Memory.Read<uint>(target.Address + 0x4E8, out uint m_HeadCollider) || m_HeadCollider == 0) return;

            const int repeatCount = 10;
            for (int i = 0; i < repeatCount; i++)
            {
                Memory.Write(target.Address + Offsets.porul, m_HeadCollider);
            }
        }
    }
}
