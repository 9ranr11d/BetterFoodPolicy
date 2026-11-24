using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace BetterFoodPolicy
{
    // 음식 정책 하나에 딸린 "소지 개수" 목록. FoodPolicy 에 필드를 못 붙이니 ConditionalWeakTable 로 매단다.
    public class FoodCarryData
    {
        public Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();

        public int Get(ThingDef def) => counts.TryGetValue(def, out int n) ? n : 0;

        public void Set(ThingDef def, int n)
        {
            if (n <= 0) counts.Remove(def);
            else counts[def] = n;
        }
    }

    public static class FoodCarry
    {
        private static readonly ConditionalWeakTable<FoodPolicy, FoodCarryData> table =
            new ConditionalWeakTable<FoodPolicy, FoodCarryData>();

        private static List<ThingDef> carriableFoods;

        public static FoodCarryData For(FoodPolicy policy) => table.GetValue(policy, _ => new FoodCarryData());

        public static FoodCarryData For(Pawn pawn)
        {
            FoodPolicy policy = pawn.foodRestriction?.CurrentFoodPolicy;
            return policy == null ? null : For(policy);
        }

        public static int CarryCount(Pawn pawn, ThingDef def) => For(pawn)?.Get(def) ?? 0;

        public static List<ThingDef> CarriableFoods
        {
            get
            {
                // 언어 변경 등으로 림월드가 def 를 전부 새로 읽으면 캐시가 옛 ThingDef 를 가리킨다.
                // 첫 항목이 지금 DefDatabase 의 것과 같은 객체인지만 봐서(조회 1번) 낡았으면 다시 만든다.
                if (carriableFoods == null
                    || (carriableFoods.Count > 0 && DefDatabase<ThingDef>.GetNamedSilentFail(carriableFoods[0].defName) != carriableFoods[0]))
                {
                    carriableFoods = DefDatabase<ThingDef>.AllDefs
                        .Where(d => d.category == ThingCategory.Item && d.IsNutritionGivingIngestible && !d.IsDrug && !d.IsCorpse)
                        .OrderBy(d => d.label)
                        .ToList();
                }
                return carriableFoods;
            }
        }
    }
}
