using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace BetterFoodPolicy
{
    // JobGiver_MoveDrugsToInventory 의 음식판. 정책에 지정된 개수보다 적게 들고 있으면 가장 가까운 것을 집어온다.
    public class JobGiver_MoveFoodToInventory : ThinkNode_JobGiver
    {
        // 찾다가 실패하면 그 정착민·음식은 이 시간 동안 다시 찾지 않는다(맵 전체 헛검색 반복 방지).
        private const int SearchCooldownTicks = 2500;

        private static readonly Dictionary<(int, ThingDef), int> retryAt = new Dictionary<(int, ThingDef), int>();

        private static bool InCooldown(Pawn pawn, ThingDef def)
        {
            if (!retryAt.TryGetValue((pawn.thingIDNumber, def), out int tick))
            {
                return false;
            }
            int now = Find.TickManager.TicksGame;
            // 세이브를 다시 불러와 시간이 되돌아간 경우도 만료로 본다
            if (now >= tick || tick - now > SearchCooldownTicks)
            {
                retryAt.Remove((pawn.thingIDNumber, def));
                return false;
            }
            return true;
        }

        public override float GetPriority(Pawn pawn)
        {
            if (DropSuspectFood(pawn))
            {
                return 0f;
            }
            if (!BfpMod.Settings.carryEnabled)
            {
                return 0f;
            }
            FoodCarryData data = FoodCarry.For(pawn);
            if (data == null || data.counts.Count == 0 || pawn.inventory == null)
            {
                return 0f;
            }
            foreach (var kv in data.counts)
            {
                if (SuspectFoodUtil.CleanCount(pawn.inventory.innerContainer, kv.Key) < kv.Value && !InCooldown(pawn, kv.Key))
                {
                    return 7.5f;
                }
            }
            return 0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!BfpMod.Settings.carryEnabled)
            {
                return null;
            }
            FoodCarryData data = FoodCarry.For(pawn);
            if (data == null || pawn.inventory == null || pawn.Map == null)
            {
                return null;
            }
            foreach (var kv in data.counts)
            {
                int num = kv.Value - SuspectFoodUtil.CleanCount(pawn.inventory.innerContainer, kv.Key);
                if (num <= 0 || InCooldown(pawn, kv.Key))
                {
                    continue;
                }
                Thing thing = FindFoodFor(pawn, kv.Key, num);
                if (thing != null)
                {
                    Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, thing);
                    job.count = Mathf.Min(num, thing.stackCount);
                    return job;
                }
                retryAt[(pawn.thingIDNumber, kv.Key)] = Find.TickManager.TicksGame + SearchCooldownTicks;
            }
            return null;
        }

        private Thing FindFoodFor(Pawn pawn, ThingDef def, int desired)
        {
            // 맵에 그 음식이 하나도 없으면 길찾기 검색 자체를 건너뛴다
            if (pawn.Map.listerThings.ThingsOfDef(def).Count == 0)
            {
                return null;
            }
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(def), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                (Thing x) => !x.IsForbidden(pawn) && x.IngestibleNow && pawn.CanReserve(x, 10, Mathf.Min(x.stackCount, desired)));
        }

        // 식중독 발생 음식을 들고 있으면 그 자리에 내려놓는다(드러날 때 못 내려놨거나 다른 모드가 주워 온 경우)
        private static bool DropSuspectFood(Pawn pawn)
        {
            if (BfpMod.Settings == null || !BfpMod.Settings.revealEnabled || pawn.inventory == null || !pawn.Spawned)
            {
                return false;
            }
            ThingOwner owner = pawn.inventory.innerContainer;
            bool dropped = false;
            for (int i = owner.Count - 1; i >= 0; i--)
            {
                Thing t = owner[i];
                if (SuspectFoodUtil.IsRevealed(t))
                {
                    dropped |= owner.TryDrop(t, pawn.Position, pawn.Map, ThingPlaceMode.Near, out Thing _);
                }
            }
            return dropped;
        }
    }
}
