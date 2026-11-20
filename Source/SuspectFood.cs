using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
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

            void Reveal(Thing t, Pawn holder)
            {
                CompSuspectFood c = t.TryGetComp<CompSuspectFood>();
                if (c == null || c.revealed) return;
                if (legacy ? !(c.batches == null && (t.TryGetComp<CompFoodPoisonable>()?.PoisonPercent ?? 0f) > 0f) : !c.SharesBatch(ids)) return;
                c.revealed = true;
                total += t.stackCount;
                if (holder != null && holder.Spawned && holder.inventory.innerContainer.TryDrop(t, holder.Position, holder.Map, ThingPlaceMode.Near, out Thing dropped))
                {
                    targets.Add(dropped ?? t);
                }
                else if (holder == null)
                {
                    targets.Add(t);
                }
            }

            foreach (Map map in Find.Maps)
            {
                List<Thing> things = new List<Thing>(map.listerThings.ThingsOfDef(def));
                foreach (Thing t in things) Reveal(t, null);
                List<Pawn> pawns = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
                foreach (Pawn p in pawns)
                {
                    if (p.inventory == null) continue;
                    List<Thing> inv = new List<Thing>(p.inventory.innerContainer);
                    foreach (Thing t in inv)
                    {
                        if (t.def == def) Reveal(t, p);
                    }
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                foreach (Pawn p in caravan.PawnsListForReading)
                {
                    if (p.inventory == null) continue;
                    foreach (Thing t in p.inventory.innerContainer)
                    {
                        if (t.def == def) Reveal(t, null);
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
