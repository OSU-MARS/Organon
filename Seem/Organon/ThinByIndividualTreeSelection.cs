﻿using System;
using System.Collections.Generic;

namespace Osu.Cof.Ferm.Organon
{
    public class ThinByIndividualTreeSelection : IHarvest
    {
        public int Period { get; private init; }

        public ThinByIndividualTreeSelection(int harvestPeriod)
        {
            if (harvestPeriod < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(harvestPeriod));
            }

            this.Period = harvestPeriod;
        }

        public IHarvest Clone()
        {
            return new ThinByIndividualTreeSelection(this.Period);
        }

        public float EvaluateTreeSelection(OrganonStandTrajectory trajectory)
        {
            float basalAreaRemoved = 0.0F;
            OrganonStand standAtEndOfPreviousPeriod = trajectory.StandByPeriod[this.Period - 1] ?? throw new NotSupportedException("No stand information prior to thinning.");
            foreach (KeyValuePair<FiaCode, int[]> treeSelectionForSpecies in trajectory.IndividualTreeSelectionBySpecies)
            {
                Trees treesOfSpecies = standAtEndOfPreviousPeriod.TreesBySpecies[treeSelectionForSpecies.Key];
                for (int treeIndex = 0; treeIndex < treesOfSpecies.Count; ++treeIndex)
                {
                    int harvestPeriod = treeSelectionForSpecies.Value[treeIndex];
                    if (harvestPeriod == this.Period)
                    {
                        float basalArea = treesOfSpecies.GetBasalArea(treeIndex);
                        basalAreaRemoved += basalArea;
                    }
                }
            }

            return basalAreaRemoved;
        }
    }
}