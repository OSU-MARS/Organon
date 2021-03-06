﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Osu.Cof.Ferm.Organon
{
    public class ThinByPrescription : IHarvest
    {
        private float fromAbovePercentage;
        private float fromBelowPercentage;
        private float proportionalPercentage;

        public int Period { get; private init; }

        public ThinByPrescription(int harvestAtBeginningOfPeriod)
        {
            if (harvestAtBeginningOfPeriod < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(harvestAtBeginningOfPeriod));
            }

            this.fromAbovePercentage = 0.0F;
            this.fromBelowPercentage = 0.0F;
            this.proportionalPercentage = 0.0F;
            this.Period = harvestAtBeginningOfPeriod;
        }

        public float FromAbovePercentage
        {
            get 
            { 
                return this.fromAbovePercentage; 
            }
            set 
            {
                if ((value < 0.0F) || (value > 100.0F))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                this.fromAbovePercentage = value; 
            }
        }

        public float FromBelowPercentage
        {
            get
            {
                return this.fromBelowPercentage;
            }
            set
            {
                if ((value < 0.0F) || (value > 100.0F))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                this.fromBelowPercentage = value;
            }
        }

        public float ProportionalPercentage
        {
            get
            {
                return this.proportionalPercentage;
            }
            set
            {
                if ((value < 0.0F) || (value > 100.0F))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                this.proportionalPercentage = value;
            }
        }

        public IHarvest Clone()
        {
            return new ThinByPrescription(this.Period)
            {
                FromAbovePercentage = this.FromAbovePercentage, 
                ProportionalPercentage = this.ProportionalPercentage, 
                fromBelowPercentage = this.FromBelowPercentage
            };
        }

        public float EvaluateTreeSelection(OrganonStandTrajectory trajectory)
        {
            float totalPercentage = this.fromAbovePercentage + this.fromBelowPercentage + this.proportionalPercentage;
            if ((totalPercentage < 0.0F) || (totalPercentage > 100.0F))
            {
                throw new NotSupportedException("Sum of from above, from below, and proportional removal percentages is negative or greater than 100%.");
            }

            OrganonStand standAtEndOfPreviousPeriod = trajectory.StandByPeriod[this.Period - 1] ?? throw new NotSupportedException("Stand information is not available for period " + (this.Period - 1) + ".");

            // sort trees by diameter
            SortedDictionary<FiaCode, int[]> dbhSortOrderBySpecies = new();
            SortedDictionary<FiaCode, int> thinFromAboveIndexBySpecies = new();
            SortedDictionary<FiaCode, int> thinFromBelowIndexBySpecies = new();
            float maximumDiameter = Single.MinValue;
            float minimumDiameter = Single.MaxValue;
            FiaCode maximumSpecies = default;
            FiaCode minimumSpecies = default;
            foreach (Trees treesOfSpecies in standAtEndOfPreviousPeriod.TreesBySpecies.Values)
            {
                if (treesOfSpecies.Count == 0)
                {
                    // no trees to thin
                    continue;
                }

                int[] dbhSortOrder = treesOfSpecies.GetDbhSortOrder();
                dbhSortOrderBySpecies.Add(treesOfSpecies.Species, dbhSortOrder);
                thinFromAboveIndexBySpecies.Add(treesOfSpecies.Species, treesOfSpecies.Count);
                thinFromBelowIndexBySpecies.Add(treesOfSpecies.Species, 0);

                float largestDbh = treesOfSpecies.Dbh[dbhSortOrder[^1]];
                if (largestDbh > maximumDiameter)
                {
                    maximumDiameter = largestDbh;
                    maximumSpecies = treesOfSpecies.Species;
                }

                float smallestDbh = treesOfSpecies.Dbh[dbhSortOrder[0]];
                if (smallestDbh < minimumDiameter)
                {
                    minimumDiameter = smallestDbh;
                    minimumSpecies = treesOfSpecies.Species;
                }
            }

            // thin from above
            OrganonStandDensity densityAtEndOfPreviousPeriod = trajectory.DensityByPeriod[this.Period - 1];
            float targetBasalArea = 0.01F * this.FromAbovePercentage * densityAtEndOfPreviousPeriod.BasalAreaPerAcre;
            float basalAreaRemovedFromAbove = 0.0F;
            while (basalAreaRemovedFromAbove < targetBasalArea)
            {
                Trees treesWithLargest = standAtEndOfPreviousPeriod.TreesBySpecies[maximumSpecies];
                int thinIndex = thinFromAboveIndexBySpecies[maximumSpecies] - 1;
                int compactedTreeIndex = dbhSortOrderBySpecies[maximumSpecies][thinIndex];
                int uncompactedTreeIndex = treesWithLargest.UncompactedIndex[compactedTreeIndex];
                float basalAreaOfTree = treesWithLargest.GetBasalArea(compactedTreeIndex);

                Debug.Assert(trajectory.IndividualTreeSelectionBySpecies[treesWithLargest.Species][uncompactedTreeIndex] == Constant.NoHarvestPeriod); // can't select trees which have already been harvested
                Debug.Assert(treesWithLargest.LiveExpansionFactor[compactedTreeIndex] > 0.0F);
                trajectory.SetTreeSelection(maximumSpecies, uncompactedTreeIndex, this.Period);

                basalAreaRemovedFromAbove += basalAreaOfTree; // for now, use complete removal of tree's expansion factor
                thinFromAboveIndexBySpecies[maximumSpecies] = thinIndex;

                maximumDiameter = Single.MinValue;
                foreach (Trees treesOfSpecies in standAtEndOfPreviousPeriod.TreesBySpecies.Values)
                {
                    int[] dbhSortOrder = dbhSortOrderBySpecies[treesOfSpecies.Species];
                    thinIndex = thinFromAboveIndexBySpecies[treesOfSpecies.Species];

                    float largestDbh = treesOfSpecies.Dbh[dbhSortOrder[thinIndex]];
                    if (largestDbh > maximumDiameter)
                    {
                        maximumDiameter = largestDbh;
                        maximumSpecies = treesOfSpecies.Species;
                    }
                }
            }

            // thin from below
            targetBasalArea = 0.01F * this.FromBelowPercentage * densityAtEndOfPreviousPeriod.BasalAreaPerAcre;
            float basalAreaRemovedFromBelow = 0.0F;
            while (basalAreaRemovedFromBelow < targetBasalArea)
            {
                Trees treesWithSmallest = standAtEndOfPreviousPeriod.TreesBySpecies[minimumSpecies];
                int thinIndex = thinFromBelowIndexBySpecies[minimumSpecies];
                int compactedTreeIndex = dbhSortOrderBySpecies[minimumSpecies][thinIndex];
                int uncompactedTreeIndex = treesWithSmallest.UncompactedIndex[compactedTreeIndex];
                float basalAreaOfTree = treesWithSmallest.GetBasalArea(compactedTreeIndex);

                Debug.Assert(trajectory.IndividualTreeSelectionBySpecies[treesWithSmallest.Species][uncompactedTreeIndex] == Constant.NoHarvestPeriod); // can't select trees which have already been harvested
                Debug.Assert(treesWithSmallest.LiveExpansionFactor[compactedTreeIndex] > 0.0F);
                trajectory.SetTreeSelection(minimumSpecies, uncompactedTreeIndex, this.Period);

                basalAreaRemovedFromBelow += basalAreaOfTree;
                thinFromBelowIndexBySpecies[minimumSpecies] = thinIndex + 1;

                minimumDiameter = Single.MaxValue;
                foreach (Trees treesOfSpecies in standAtEndOfPreviousPeriod.TreesBySpecies.Values)
                {
                    int[] dbhSortOrder = dbhSortOrderBySpecies[treesOfSpecies.Species];
                    thinIndex = thinFromBelowIndexBySpecies[treesOfSpecies.Species];

                    float smallestDbh = treesOfSpecies.Dbh[dbhSortOrder[thinIndex]];
                    if (smallestDbh < minimumDiameter)
                    {
                        minimumDiameter = smallestDbh;
                        minimumSpecies = treesOfSpecies.Species;
                    }
                }
            }

            // thin remaining trees proportionally
            float proportionalThinAccumulator = 0.0F;
            float proportionalIncrement = 0.01F * this.ProportionalPercentage * 100.0F / (100.0F - this.FromAbovePercentage - this.FromBelowPercentage);
            float basalAreaRemovedProportionally = 0.0F;
            foreach (KeyValuePair<FiaCode, int[]> speciesDbhSortOrder in dbhSortOrderBySpecies)
            {
                int[] dbhSortOrder = speciesDbhSortOrder.Value;
                Trees treesOfSpecies = standAtEndOfPreviousPeriod.TreesBySpecies[speciesDbhSortOrder.Key];
                if (treesOfSpecies.Count == 0)
                {
                    continue;
                }

                for (int thinIndex = thinFromBelowIndexBySpecies[speciesDbhSortOrder.Key]; thinIndex < thinFromAboveIndexBySpecies[speciesDbhSortOrder.Key]; ++thinIndex)
                {
                    proportionalThinAccumulator += proportionalIncrement;
                    if (proportionalThinAccumulator >= 1.0F)
                    {
                        int compactedTreeIndex = dbhSortOrder[thinIndex];
                        int uncompactedTreeIndex = treesOfSpecies.UncompactedIndex[compactedTreeIndex];
                        float basalAreaOfTree = treesOfSpecies.GetBasalArea(compactedTreeIndex);

                        Debug.Assert(trajectory.IndividualTreeSelectionBySpecies[treesOfSpecies.Species][uncompactedTreeIndex] == Constant.NoHarvestPeriod); // can't select trees which have already been harvested
                        Debug.Assert(treesOfSpecies.LiveExpansionFactor[compactedTreeIndex] > 0.0F);
                        trajectory.SetTreeSelection(speciesDbhSortOrder.Key, uncompactedTreeIndex, this.Period);

                        basalAreaRemovedProportionally += basalAreaOfTree;                        
                        proportionalThinAccumulator -= 1.0F;
                    }
                }
            }

            float basalAreaRemoved = basalAreaRemovedFromAbove + basalAreaRemovedProportionally + basalAreaRemovedFromBelow;
            Debug.Assert((totalPercentage >= 0.0F && basalAreaRemoved > 0.0F) || ((int)(0.01F * totalPercentage * dbhSortOrderBySpecies.Values.Sum(sortOrder => sortOrder.Length)) == 0 && basalAreaRemoved == 0.0F));
            return basalAreaRemoved;
        }
    }
}
