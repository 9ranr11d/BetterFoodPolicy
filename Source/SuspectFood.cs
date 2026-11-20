using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace BetterFoodPolicy
{
    public class CompProperties_SuspectFood : CompProperties
    {
        public CompProperties_SuspectFood()
        {
            compClass = typeof(CompSuspectFood);
        }
    }

    // 오염 묶음 번호만 기억한다(오염률은 저장·표시하지 않음). 식중독이 터지면 같은 묶음이 섞인 음식이 revealed 가 된다.
    public class CompSuspectFood : ThingComp
    {
        // 대부분의 음식은 null 로 둔다
        public List<int> batches;
        public bool revealed;

        public void AddBatch(int id)
        {
            if (batches == null) batches = new List<int>();
            if (!batches.Contains(id)) batches.Add(id);
        }

        public bool SharesBatch(List<int> other)
        {
            if (batches == null || other == null) return false;
            for (int i = 0; i < other.Count; i++)
            {
                if (batches.Contains(other[i])) return true;
            }
            return false;
        }

        private void MergeBatches(List<int> other)
        {
            if (other == null) return;
            for (int i = 0; i < other.Count; i++) AddBatch(other[i]);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref batches, "bfp_batches", LookMode.Value);
            Scribe_Values.Look(ref revealed, "bfp_revealed", false);
        }

        public override void PostSplitOff(Thing piece)
        {
            base.PostSplitOff(piece);
            CompSuspectFood c = piece.TryGetComp<CompSuspectFood>();
            if (c == null) return;
            c.batches = batches == null ? null : new List<int>(batches);
            c.revealed = revealed;
        }

        public override void PreAbsorbStack(Thing otherStack, int count)
        {
            base.PreAbsorbStack(otherStack, count);
            CompSuspectFood c = otherStack.TryGetComp<CompSuspectFood>();
            if (c == null) return;
            MergeBatches(c.batches);
            revealed |= c.revealed;
        }

        public override bool AllowStackWith(Thing other)
        {
            CompSuspectFood c = other.TryGetComp<CompSuspectFood>();
            if (c != null && c.revealed != revealed) return false;
            return base.AllowStackWith(other);
        }

        public override string CompInspectStringExtra()
        {
            return revealed ? "BFP_SuspectInspect".Translate().ToString() : null;
        }
    }

    public static class SuspectFoodUtil
    {
        public static bool IsRevealed(Thing t) => t is ThingWithComps c && c.GetComp<CompSuspectFood>()?.revealed == true;

        // 소지 수량 계산용: 식중독 발생 음식은 들고 있어도 안 센다
        public static int CleanCount(ThingOwner owner, ThingDef def)
        {
            int n = 0;
            for (int i = 0; i < owner.Count; i++)
            {
                Thing t = owner[i];
                if (t.def == def && !IsRevealed(t)) n += t.stackCount;
            }
            return n;
        }
    }

    public class GameComponent_SuspectFood : GameComponent
    {
        public int nextBatch;

        public GameComponent_SuspectFood(Game game) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref nextBatch, "bfp_nextBatch", 0);
        }
    }

    public class SpecialThingFilterWorker_SuspectFood : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            return t.TryGetComp<CompSuspectFood>()?.revealed == true;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            return def.GetCompProperties<CompProperties_FoodPoisonable>() != null;
        }
    }

    public class SpecialThingFilterWorker_NotSuspectFood : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            return t.TryGetComp<CompSuspectFood>()?.revealed != true;
        }

        // 레시피에서 "드러나지 않은 음식 전부"를 막는 용도라, 오염될 수 없는 날음식 등도 반드시 걸려야 한다
        public override bool CanEverMatch(ThingDef def)
        {
            return true;
        }

        public override bool AlwaysMatches(ThingDef def)
        {
            return def.GetCompProperties<CompProperties_FoodPoisonable>() == null;
        }
    }

    [StaticConstructorOnStartup]
    public static class SuspectFoodInit
    {
        static SuspectFoodInit()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.comps == null || def.GetCompProperties<CompProperties_FoodPoisonable>() == null) continue;
                if (def.GetCompProperties<CompProperties_SuspectFood>() != null) continue;
                def.comps.Add(new CompProperties_SuspectFood());
            }
        }
    }

    [HarmonyPatch(typeof(CompFoodPoisonable), nameof(CompFoodPoisonable.SetPoisoned))]
    public static class Patch_CompFoodPoisonable_SetPoisoned
    {
        public static void Postfix(CompFoodPoisonable __instance)
        {
            CompSuspectFood c = __instance.parent.TryGetComp<CompSuspectFood>();
            GameComponent_SuspectFood gc = Current.Game?.GetComponent<GameComponent_SuspectFood>();
            if (c == null || gc == null) return;
            c.AddBatch(gc.nextBatch++);
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.AddFoodPoisoningHediff))]
    public static class Patch_FoodUtility_AddFoodPoisoningHediff
    {
        private static readonly AccessTools.FieldRef<CompFoodPoisonable, float> PoisonPctRef =
            AccessTools.FieldRefAccess<CompFoodPoisonable, float>("poisonPct");

        public static void Postfix(Pawn pawn, Thing ingestible)
        {
            if (!BfpMod.Settings.revealEnabled) return;
            if (ingestible == null) return;
            List<int> ids = ingestible.TryGetComp<CompSuspectFood>()?.batches;
            // 모드 설치 전에 오염된 음식은 묶음 번호가 없다 → 같은 종류 중 오염 표시가 있고 번호가 없는 더미를 같은 묶음으로 본다
            bool legacy = (ids == null || ids.Count == 0) && (ingestible.TryGetComp<CompFoodPoisonable>()?.PoisonPercent ?? 0f) > 0f;
            if (!legacy && (ids == null || ids.Count == 0)) return;

            int total = 0;
            List<Thing> targets = new List<Thing>();
            ThingDef def = ingestible.def;

            // 오염된 개수(오염률 × 개수)만큼만 떼어 표시하고, 남은 더미는 깨끗하게 되돌린다
            void Reveal(Thing t, Pawn holder, ThingOwner owner)
            {
                CompSuspectFood c = t.TryGetComp<CompSuspectFood>();
                if (c == null || c.revealed) return;
                CompFoodPoisonable fp = t.TryGetComp<CompFoodPoisonable>();
                float pct = fp?.PoisonPercent ?? 0f;
                if (legacy ? !(c.batches == null && pct > 0f) : !c.SharesBatch(ids)) return;
                int bad = pct > 0f ? Mathf.Clamp(Mathf.RoundToInt(pct * t.stackCount), 1, t.stackCount) : t.stackCount;
                Thing piece = t;
                if (bad < t.stackCount)
                {
                    bool spawned = t.Spawned;
                    IntVec3 pos = t.PositionHeld;
                    Map pmap = t.MapHeld;
                    piece = t.SplitOff(bad);
                    // 남은 더미: 오염 없음
                    if (fp != null) PoisonPctRef(fp) = 0f;
                    c.batches = null;
                    // 떼어 낸 몫은 합쳐지기 전에 먼저 표시
                    piece.TryGetComp<CompSuspectFood>().revealed = true;
                    CompFoodPoisonable pfp = piece.TryGetComp<CompFoodPoisonable>();
                    if (pfp != null) PoisonPctRef(pfp) = 1f;
                    if (spawned)
                    {
                        GenPlace.TryPlaceThing(piece, pos, pmap, ThingPlaceMode.Near, out Thing placed);
                        piece = placed ?? piece;
                    }
                    else if (owner != null)
                    {
                        owner.TryAdd(piece, canMergeWithExistingStacks: false);
                    }
                }
                else
                {
                    c.revealed = true;
                }
                total += piece.stackCount;
                if (holder != null && holder.Spawned && holder.inventory.innerContainer.Contains(piece)
                    && holder.inventory.innerContainer.TryDrop(piece, holder.Position, holder.Map, ThingPlaceMode.Near, out Thing dropped))
                {
                    targets.Add(dropped ?? piece);
                }
                else if (holder == null)
                {
                    targets.Add(piece);
                }
            }

            foreach (Map map in Find.Maps)
            {
                List<Thing> things = new List<Thing>(map.listerThings.ThingsOfDef(def));
                foreach (Thing t in things) Reveal(t, null, null);
                List<Pawn> pawns = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
                foreach (Pawn p in pawns)
                {
                    if (p.inventory == null) continue;
                    List<Thing> inv = new List<Thing>(p.inventory.innerContainer);
                    foreach (Thing t in inv)
                    {
                        if (t.def == def) Reveal(t, p, p.inventory.innerContainer);
                    }
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                foreach (Pawn p in caravan.PawnsListForReading)
                {
                    if (p.inventory == null) continue;
                    foreach (Thing t in new List<Thing>(p.inventory.innerContainer))
                    {
                        if (t.def == def) Reveal(t, null, p.inventory.innerContainer);
                    }
                }
            }

            if (total <= 0) return;
            // 캐러밴 소지품은 월드 물건이라 표시 대상에서 뺀다
            targets.RemoveAll(t => !t.Spawned);
            Find.LetterStack.ReceiveLetter(
                "BFP_SuspectLetterLabel".Translate(),
                "BFP_SuspectLetterText".Translate(pawn.LabelShort, def.label, total),
                LetterDefOf.NegativeEvent,
                targets.Count > 0 ? new LookTargets(targets) : LookTargets.Invalid);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.IngestibleNow), MethodType.Getter)]
    public static class Patch_Thing_IngestibleNow
    {
        public static void Postfix(Thing __instance, ref bool __result)
        {
            if (__result && __instance is ThingWithComps t && t.GetComp<CompSuspectFood>()?.revealed == true)
            {
                __result = false;
            }
        }
    }
}
