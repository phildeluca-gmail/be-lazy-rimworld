using System.Collections.Generic;
using BeLazy.Core;
using Verse;
using Verse.AI;

namespace BeLazy.Food
{
    // Reports how a "Go Eat" order ended - bl-architecture.md 5.x, built
    // 2026-09-28. Same shape as Sleep/ForceSleepWatch.cs and Joy/JoyWatch.cs:
    // a small unsaved set held by a GameComponent, polled every 60 ticks,
    // at most one log line per pawn, at the tick its state actually changes.
    //
    // Unlike sleep (3.8's forceSleep/asleep flags) or recreation (5.2b's
    // playerForced bypass of JoyUtility.JoyTickCheckEnd), Ingest needs no
    // help ending itself: RimWorld.JobDriver_Ingest completes the job on its
    // own once the pawn has eaten the amount FoodFinder.TryFindBestFoodJob
    // asked for (FoodUtility.WillIngestStackCountOf), regardless of
    // playerForced. That is what "it ends after one meal" means in practice
    // - nothing here has to force a stop the way JoyWatch does. This watch
    // exists only to log what happened, not to end anything.
    //
    // Telling "ate" from "did not eat" without a held
    // Pawn_JobTracker.EndCurrentJob patch is the same gap bl-architecture.md
    // 5.5a records for sleep and recreation - JobCondition is a parameter,
    // never stored anywhere a later poll can read it. Rather than guess,
    // this compares Need_Food.CurLevel at the moment the job was issued
    // against its value when the job is found gone: a real, measured rise in
    // nutrition, not an inference about which JobCondition ended the job.
    // The job is matched by Job.loadID (public int, confirmed - the same
    // field 5.5a already uses for this purpose), because by the time a poll
    // finds the job gone, pawn.jobs.curJob may already be a different Job
    // instance.
    public class FoodWatch : GameComponent
    {
        private const int CheckIntervalTicks = 60;

        // A meal raises nutrition by a large fraction of Need_Food's 0-1
        // range; anything above this is unambiguously "some food was eaten,"
        // not float noise from an unrelated tick update.
        private const float NutritionRiseThreshold = 0.01f;

        private class Entry
        {
            public int jobLoadId;
            public float nutritionAtOrder;
        }

        private readonly Dictionary<Pawn, Entry> watching = new Dictionary<Pawn, Entry>();

        // RimWorld constructs this itself - Game.FillComponents walks every
        // loaded assembly for GameComponent subclasses and news them up with
        // the Game. The (Game) constructor has to exist even though it is
        // otherwise unused.
        public FoodWatch(Game game)
        {
        }

        public static void Watch(Pawn pawn, int jobLoadId, float nutritionAtOrder)
        {
            FoodWatch component = Current.Game?.GetComponent<FoodWatch>();
            component?.watching.Remove(pawn);
            component?.watching.Add(pawn, new Entry { jobLoadId = jobLoadId, nutritionAtOrder = nutritionAtOrder });
        }

        public override void GameComponentTick()
        {
            if (watching.Count == 0)
            {
                return;
            }

            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }

            // Snapshot the drop list - the set is mutated after the loop,
            // not during it.
            var drop = new List<Pawn>();

            foreach (KeyValuePair<Pawn, Entry> watch in watching)
            {
                Pawn pawn = watch.Key;
                if (pawn == null)
                {
                    drop.Add(pawn);
                    continue;
                }

                Job job = pawn.jobs?.curJob;
                if (job != null && job.loadID == watch.Value.jobLoadId)
                {
                    // Still the job Be Lazy issued - the pawn is still
                    // travelling to the food or still eating it. Keep
                    // watching.
                    continue;
                }

                // The job is gone or replaced - read the need directly
                // rather than guess at why.
                float nutritionNow = pawn.needs?.food?.CurLevel ?? watch.Value.nutritionAtOrder;
                float rise = nutritionNow - watch.Value.nutritionAtOrder;

                if (rise >= NutritionRiseThreshold)
                {
                    // COMPLETED - the meal was eaten. "Ends after one meal"
                    // is vanilla's own JobDriver_Ingest behaviour; this line
                    // only reports that it happened.
                    Logger.Message(pawn.LabelShortCap + ": Go Eat completed - ate, nutrition +"
                        + rise.ToString("F2") + ".");
                }
                else
                {
                    // INTERRUPTED before any nutrition was gained - a draft,
                    // another order, or something else replaced the job
                    // before the pawn reached the food.
                    Logger.Message(pawn.LabelShortCap
                        + ": Go Eat interrupted before eating - now "
                        + (job?.def?.label ?? "nothing") + ".");
                }

                drop.Add(pawn);
            }

            foreach (Pawn pawn in drop)
            {
                watching.Remove(pawn);
            }
        }
    }
}
