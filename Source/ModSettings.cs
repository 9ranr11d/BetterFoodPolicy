using UnityEngine;
using Verse;

namespace BetterFoodPolicy
{
    public class BfpSettings : ModSettings
    {
        public bool carryEnabled = true;
        public bool revealEnabled = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref carryEnabled, "carryEnabled", true);
            Scribe_Values.Look(ref revealEnabled, "revealEnabled", true);
        }
    }

    public class BfpMod : Mod
    {
        public static BfpSettings Settings;

        public BfpMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BfpSettings>();
        }

        public override string SettingsCategory() => "Better Food Policy";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard l = new Listing_Standard();
            l.Begin(inRect);
            l.CheckboxLabeled("BFP_SettingCarry".Translate(), ref Settings.carryEnabled);
            l.CheckboxLabeled("BFP_SettingReveal".Translate(), ref Settings.revealEnabled);
            l.End();
        }
    }
}
