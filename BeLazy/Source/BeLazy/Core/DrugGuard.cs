using RimWorld;
using Verse;
using Verse.AI;

namespace BeLazy.Core
{
    // 2026-11-17, order "yes, build both I want drug craving % slider in
    // DNBL." (bl-architecture.md section 14). A pawn taking a drug is never
    // cut off by Go to Bed or Get Rec'd: Sheriff was ordered to bed while
    // drinking psychite tea. The test is an Ingest job whose target's def is
    // a drug (ThingDef.IsDrug, verified in lib\Assembly-CSharp.dll; psychite
    // tea, beer and smokeleaf all carry it). No Harmony patch.
    public static class DrugGuard
    {
        // True, with a description, when the pawn's current job is an Ingest
        // of a drug.
        public static bool IsTakingDrug(Pawn pawn, out string what)
        {
            what = null;
            Job job = pawn?.jobs?.curJob;
            if (job == null || job.def != JobDefOf.Ingest)
            {
                return false;
            }

            Thing target = job.targetA.Thing;
            if (target == null || target.def == null || !target.def.IsDrug)
            {
                return false;
            }

            what = target.LabelShort;
            return true;
        }

        // The hold-back: logs one line per pawn per press and returns true
        // when the pawn must be left alone.
        public static bool HoldBack(Pawn pawn, string orderName)
        {
            if (!IsTakingDrug(pawn, out string what))
            {
                return false;
            }

            Logger.Message(pawn.LabelShortCap + ": " + orderName + " held back - the pawn is taking a drug (" + what
                + "); it is not interrupted and no order was issued.");
            return true;
        }
    }
}
