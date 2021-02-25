﻿using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommunications.Write, "HarvestSchedule")]
    public class WriteHarvestSchedule : WriteCmdlet
    {
        public FiaVolume fiaVolume;

        [Parameter(Mandatory = true)]
        [ValidateNotNull]
        public List<HeuristicSolutionDistribution>? Runs { get; set; }

        public WriteHarvestSchedule()
        {
            this.fiaVolume = new FiaVolume();
        }

        protected override void ProcessRecord()
        {
            if (this.Runs!.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.Runs));
            }

            using StreamWriter writer = this.GetWriter();
            StringBuilder line = new StringBuilder();
            if (this.Append == false)
            {
                HeuristicParameters? highestHeuristicParameters = this.Runs![0].HighestHeuristicParameters;
                if (highestHeuristicParameters == null)
                {
                    throw new NotSupportedException("Cannot generate schedule header because first run has no heuristic parameters.");
                }
                line.Append("stand,heuristic," + highestHeuristicParameters.GetCsvHeader() + ",discount rate,first thin age,second thin age,rotation,tree,lowest selection,highest selection,highest thin DBH,highest thin height,highest thin CR,highest thin EF,highest thin BF,highest final DBH,highest final height,highest final CR,highest final EF,highest final BF");
                writer.WriteLine(line);
            }

            for (int runIndex = 0; runIndex < this.Runs!.Count; ++runIndex)
            {
                HeuristicSolutionDistribution distribution = this.Runs[runIndex];
                if ((distribution.HighestHeuristicParameters == null) ||
                    (distribution.HighestSolution == null) ||
                    (distribution.HighestSolution.BestTrajectory == null) ||
                    (distribution.HighestSolution.BestTrajectory.Heuristic == null) ||
                    (distribution.LowestSolution == null))
                {
                    throw new NotSupportedException("Run " + runIndex + " is missing a highest solution, lowest solution, highest solution parameters, highest heuristic trajectory, or back link from highest trajectory to is generating heuristic.");
                }

                OrganonStandTrajectory highestTrajectoryN = distribution.HighestSolution.BestTrajectory;
                int periodBeforeFirstThin = highestTrajectoryN.GetFirstHarvestPeriod() - 1;
                if (periodBeforeFirstThin < 0)
                {
                    periodBeforeFirstThin = highestTrajectoryN.PlanningPeriods - 1;
                }
                int firstThinAge = highestTrajectoryN.GetFirstHarvestAge();
                string? firstThinAgeString = firstThinAge != -1 ? firstThinAge.ToString(CultureInfo.InvariantCulture) : null;
                int secondThinAge = highestTrajectoryN.GetSecondHarvestAge();
                string? secondThinAgeString = secondThinAge != -1 ? secondThinAge.ToString(CultureInfo.InvariantCulture) : null;

                string linePrefix = highestTrajectoryN.Name + "," + highestTrajectoryN.Heuristic.GetName() + "," + 
                    distribution.HighestHeuristicParameters.GetCsvValues() + "," + 
                    highestTrajectoryN.TimberValue.DiscountRate.ToString(CultureInfo.InvariantCulture) + "," +
                    firstThinAgeString + "," +
                    secondThinAgeString + "," +
                    highestTrajectoryN.GetRotationLength().ToString(CultureInfo.InvariantCulture);

                OrganonStandTrajectory lowestTrajectoryN = distribution.LowestSolution.BestTrajectory;
                int previousSpeciesCount = 0;
                foreach (KeyValuePair<FiaCode, int[]> highestTreeSelectionNForSpecies in highestTrajectoryN.IndividualTreeSelectionBySpecies)
                {
                    Stand? highestStandNbeforeFirstHarvest = highestTrajectoryN.StandByPeriod[periodBeforeFirstThin];
                    Stand? highestStandNatEnd = highestTrajectoryN.StandByPeriod[^1];
                    if ((highestStandNbeforeFirstHarvest == null) || (highestStandNatEnd == null))
                    {
                        throw new ParameterOutOfRangeException(nameof(this.Runs), "Highest stand in run has not been fully simulated. Did the heuristic perform at least one move?");
                    }
                    Trees highestTreesBeforeFirstThin = highestStandNbeforeFirstHarvest.TreesBySpecies[highestTreeSelectionNForSpecies.Key];
                    Trees highestTreesAtFinal = highestStandNatEnd.TreesBySpecies[highestTreeSelectionNForSpecies.Key];
                    if (highestTreesBeforeFirstThin.Units != highestTreesAtFinal.Units)
                    {
                        throw new NotSupportedException();
                    }
                    WriteHarvestSchedule.GetDimensionConversions(highestTreesBeforeFirstThin.Units, Units.Metric, out float areaConversionFactor, out float dbhConversionFactor, out float heightConversionFactor);

                    // uncompactedTreeIndex: tree index in periods before thinned trees are removed
                    // compactedTreeIndex: index of retained trees in periods after thinning
                    int[] lowestTreeSelectionN = lowestTrajectoryN.IndividualTreeSelectionBySpecies[highestTreeSelectionNForSpecies.Key];
                    int[] highestTreeSelectionN = highestTreeSelectionNForSpecies.Value;
                    Debug.Assert(highestTreesBeforeFirstThin.Capacity == highestTreeSelectionN.Length);
                    for (int compactedTreeIndex = 0, uncompactedTreeIndex = 0; uncompactedTreeIndex < highestTreesBeforeFirstThin.Count; ++uncompactedTreeIndex)
                    {
                        line.Clear();

                        float highestThinBoardFeet = FiaVolume.GetScribnerBoardFeet(highestTreesBeforeFirstThin, uncompactedTreeIndex);

                        string? highestFinalDbh = null;
                        string? highestFinalHeight = null;
                        string? highestFinalCrownRatio = null;
                        string? highestFinalExpansionFactor = null;
                        string? highestFinalBoardFeet = null;
                        bool isThinnedInHighestTrajectory = highestTreeSelectionN[uncompactedTreeIndex] != Constant.NoHarvestPeriod;
                        if (isThinnedInHighestTrajectory == false)
                        {
                            Debug.Assert(highestTreesAtFinal.Tag[compactedTreeIndex] == highestTreesBeforeFirstThin.Tag[uncompactedTreeIndex]);
                            highestFinalDbh = (dbhConversionFactor * highestTreesAtFinal.Dbh[compactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highestFinalHeight = (heightConversionFactor * highestTreesAtFinal.Height[compactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highestFinalCrownRatio = highestTreesAtFinal.CrownRatio[compactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highestFinalExpansionFactor = highestTreesAtFinal.LiveExpansionFactor[compactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highestFinalBoardFeet = FiaVolume.GetScribnerBoardFeet(highestTreesAtFinal, compactedTreeIndex).ToString("0.00", CultureInfo.InvariantCulture);
                            ++compactedTreeIndex; // only need to increment on retained trees, OK to increment here as not referenced below
                        }

                        // for now, make best guess of using tree tag or index as unique identifier
                        int treeID = highestTreesBeforeFirstThin.Tag[uncompactedTreeIndex] < 0 ? previousSpeciesCount + uncompactedTreeIndex : highestTreesBeforeFirstThin.Tag[uncompactedTreeIndex];
                        line.Append(linePrefix + "," + treeID + "," +
                                    lowestTreeSelectionN[uncompactedTreeIndex].ToString(CultureInfo.InvariantCulture) + "," +
                                    highestTreeSelectionN[uncompactedTreeIndex].ToString(CultureInfo.InvariantCulture) + "," +
                                    (dbhConversionFactor * highestTreesBeforeFirstThin.Dbh[uncompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture) + "," +
                                    (heightConversionFactor * highestTreesBeforeFirstThin.Height[uncompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture) + "," +
                                    highestTreesBeforeFirstThin.CrownRatio[uncompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                    highestTreesBeforeFirstThin.LiveExpansionFactor[uncompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                    highestThinBoardFeet.ToString("0.00", CultureInfo.InvariantCulture) + "," +
                                    highestFinalDbh + "," +
                                    highestFinalHeight + "," +
                                    highestFinalCrownRatio + "," +
                                    highestFinalExpansionFactor + "," +
                                    highestFinalBoardFeet);

                        writer.WriteLine(line);
                    }

                    previousSpeciesCount += highestTreeSelectionN.Length;
                }
            }
        }
    }
}
