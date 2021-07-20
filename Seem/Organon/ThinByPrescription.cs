using Osu.Cof.Ferm.Tree;
using System;
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

        public int Period { get; set; }

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
                Debug.Assert(Single.IsNaN(value) == false);
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
                Debug.Assert(Single.IsNaN(value) == false);
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
                Debug.Assert(Single.IsNaN(value) == false);
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

        public void CopyFrom(IHarvest other)
        {
            if (other is ThinByIndividualTreeSelection thinByIndividualTreeSelection)
            {
                // this is, for now, just a stub to enable test cases so only flow period
                // If needed, APIs can be created to calculate approximate above, proportional, and below percentages from the tree selection.
                this.Period = thinByIndividualTreeSelection.Period;
            }
            else if (other is ThinByPrescription thinByPrescription)
            {
                this.FromAbovePercentage = thinByPrescription.FromAbovePercentage;
                this.FromBelowPercentage = thinByPrescription.FromBelowPercentage;
                this.Period = thinByPrescription.Period;
                this.ProportionalPercentage = thinByPrescription.ProportionalPercentage;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(other));
            }
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
            SortedList<FiaCode, int[]> dbhSortOrderBySpecies = new();
            SortedList<FiaCode, int> thinFromAboveIndexBySpecies = new();
            SortedList<FiaCode, int> thinFromBelowIndexBySpecies = new();
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
                Debug.Assert(thinIndex >= 0);
                int compactedTreeIndex = dbhSortOrderBySpecies[maximumSpecies][thinIndex];
                int uncompactedTreeIndex = treesWithLargest.UncompactedIndex[compactedTreeIndex];
                float basalAreaOfTree = treesWithLargest.GetBasalArea(compactedTreeIndex);

                // selection of previously harvested trees is a defect but trees 1) not selected for thinning, selected for thinning in
                // 2) this period or 3) later periods are eligible for removal in this period
                int currentHarvestPeriod = trajectory.IndividualTreeSelectionBySpecies[treesWithLargest.Species][uncompactedTreeIndex];
                if ((currentHarvestPeriod != Constant.NoHarvestPeriod) && (currentHarvestPeriod < this.Period))
                {
                    throw new NotSupportedException("Could not select tree " + treesWithLargest.Tag[compactedTreeIndex] + " for proportional thinning in period " + this.Period + " because it is assigned to period " + currentHarvestPeriod + ".");
                }
                if (treesWithLargest.LiveExpansionFactor[compactedTreeIndex] <= 0.0F)
                {
                    throw new NotSupportedException("Could not select tree " + treesWithLargest.Tag[compactedTreeIndex] + " for proportional thinning in period " + this.Period + " because its expansion factor is " + treesWithLargest.LiveExpansionFactor[compactedTreeIndex].ToString("0.00") + ".");
                }
                trajectory.SetTreeSelection(maximumSpecies, uncompactedTreeIndex, this.Period);

                basalAreaRemovedFromAbove += basalAreaOfTree; // for now, use complete removal of tree's expansion factor
                thinFromAboveIndexBySpecies[maximumSpecies] = thinIndex;

                // find next largest tree (by diameter) which hasn't been thinned
                maximumDiameter = Single.MinValue;
                bool foundNextTree = false;
                foreach (Trees treesOfSpecies in standAtEndOfPreviousPeriod.TreesBySpecies.Values)
                {
                    int[] dbhSortOrder = dbhSortOrderBySpecies[treesOfSpecies.Species];
                    thinIndex = thinFromAboveIndexBySpecies[treesOfSpecies.Species];
                    Debug.Assert((thinIndex >= 0) && (thinIndex < dbhSortOrder.Length));

                    float largestDbh = treesOfSpecies.Dbh[dbhSortOrder[thinIndex]];
                    if (largestDbh > maximumDiameter)
                    {
                        maximumDiameter = largestDbh;
                        maximumSpecies = treesOfSpecies.Species;
                        foundNextTree = true;
                    }
                }
                if (foundNextTree == false)
                {
                    // avoid looping forever if, for some reason, basal area target cannot be reached
                    break;
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

                int currentHarvestPeriod = trajectory.IndividualTreeSelectionBySpecies[treesWithSmallest.Species][uncompactedTreeIndex];
                if ((currentHarvestPeriod != Constant.NoHarvestPeriod) && (currentHarvestPeriod < this.Period))
                {
                    throw new NotSupportedException("Could not select tree " + treesWithSmallest.Tag[compactedTreeIndex] + " for proportional thinning in period " + this.Period + " because it is assigned to period " + currentHarvestPeriod + ".");
                }
                if (treesWithSmallest.LiveExpansionFactor[compactedTreeIndex] <= 0.0F)
                {
                    throw new NotSupportedException("Could not select tree " + treesWithSmallest.Tag[compactedTreeIndex] + " for proportional thinning in period " + this.Period + " because its expansion factor is " + treesWithSmallest.LiveExpansionFactor[compactedTreeIndex].ToString("0.00") + ".");
                }
                trajectory.SetTreeSelection(minimumSpecies, uncompactedTreeIndex, this.Period);

                basalAreaRemovedFromBelow += basalAreaOfTree;
                thinFromBelowIndexBySpecies[minimumSpecies] = thinIndex + 1;

                // find next smallest tree (by diameter) which hasn't been thinned
                minimumDiameter = Single.MaxValue;
                bool foundNextTree = false;
                foreach (Trees treesOfSpecies in standAtEndOfPreviousPeriod.TreesBySpecies.Values)
                {
                    int[] dbhSortOrder = dbhSortOrderBySpecies[treesOfSpecies.Species];
                    thinIndex = thinFromBelowIndexBySpecies[treesOfSpecies.Species];
                    if (thinIndex >= dbhSortOrder.Length)
                    {
                        // no more trees to remove in this species
                        continue;
                    }

                    float smallestDbh = treesOfSpecies.Dbh[dbhSortOrder[thinIndex]];
                    if (smallestDbh < minimumDiameter)
                    {
                        minimumDiameter = smallestDbh;
                        minimumSpecies = treesOfSpecies.Species;
                        foundNextTree = true;
                    }
                }
                if (foundNextTree == false)
                {
                    break;
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
                    int compactedTreeIndex = dbhSortOrder[thinIndex];
                    int uncompactedTreeIndex = treesOfSpecies.UncompactedIndex[compactedTreeIndex];
                    int currentHarvestPeriod = trajectory.IndividualTreeSelectionBySpecies[treesOfSpecies.Species][uncompactedTreeIndex];

                    proportionalThinAccumulator += proportionalIncrement;
                    if (proportionalThinAccumulator >= 1.0F)
                    {
                        float basalAreaOfTree = treesOfSpecies.GetBasalArea(compactedTreeIndex);
                        if ((currentHarvestPeriod != Constant.NoHarvestPeriod) && (currentHarvestPeriod < this.Period))
                        {
                            throw new NotSupportedException("Could not select tree " + treesOfSpecies.Tag[compactedTreeIndex] + " for proportional thinning in period " + this.Period + " because it is assigned to period " + currentHarvestPeriod + ".");
                        }
                        if (treesOfSpecies.LiveExpansionFactor[compactedTreeIndex] <= 0.0F)
                        {
                            throw new NotSupportedException("Could not select tree " + treesOfSpecies.Tag[compactedTreeIndex] + " for proportional thinning in period " + this.Period + " because its expansion factor is " + treesOfSpecies.LiveExpansionFactor[compactedTreeIndex].ToString("0.00") + ".");
                        }
                        trajectory.SetTreeSelection(speciesDbhSortOrder.Key, uncompactedTreeIndex, this.Period);

                        basalAreaRemovedProportionally += basalAreaOfTree;                        
                        proportionalThinAccumulator -= 1.0F;
                    }
                    else if (currentHarvestPeriod == this.Period)
                    {
                        // for now, assume this is the only harvest prescription active for this period
                        // This makes the prescription authorative for tree assignments in the period and, therefore, able to release trees from
                        // harvest.
                        trajectory.SetTreeSelection(speciesDbhSortOrder.Key, uncompactedTreeIndex, Constant.NoHarvestPeriod);
                    }
                    else if ((currentHarvestPeriod != Constant.NoHarvestPeriod) && (currentHarvestPeriod < this.Period))
                    {
                        // tree is expected to be retained through this period (but possible harvested later), so prior removal is an error
                        throw new NotSupportedException("Tree " + treesOfSpecies.Tag[compactedTreeIndex] + " is thinned in period " + currentHarvestPeriod + " but is expected to be retained through period " + this.Period + ".");
                    }
                }
            }

            float basalAreaRemoved = basalAreaRemovedFromAbove + basalAreaRemovedProportionally + basalAreaRemovedFromBelow;
            Debug.Assert((totalPercentage >= 0.0F && basalAreaRemoved > 0.0F) || ((int)(0.01F * totalPercentage * dbhSortOrderBySpecies.Values.Sum(sortOrder => sortOrder.Length)) == 0 && basalAreaRemoved == 0.0F));
            return basalAreaRemoved;
        }
    }
}
