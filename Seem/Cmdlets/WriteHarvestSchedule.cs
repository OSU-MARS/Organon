﻿using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommunications.Write, "HarvestSchedule")]
    public class WriteHarvestSchedule : WriteCmdlet
    {
        [Parameter(Mandatory = true)]
        [ValidateNotNull]
        public HeuristicResultSet? Results { get; set; }

        protected override void ProcessRecord()
        {
            Debug.Assert(this.Results != null);
            if (this.Results.Distributions.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.Results));
            }

            using StreamWriter writer = this.GetWriter();
            if (this.ShouldWriteHeader())
            {
                HeuristicParameters? heuristicParameters = this.Results.Distributions[0].HeuristicParameters;
                if (heuristicParameters == null)
                {
                    throw new NotSupportedException("Cannot generate schedule header because first run has no heuristic parameters.");
                }
                writer.WriteLine("stand,heuristic," + heuristicParameters.GetCsvHeader() + "," + WriteCmdlet.RateAndAgeCsvHeader + ",plot,tag,lowSelection,highSelection,highThin1dbh,highThin1height,highThin1cr,highThin1ef,highThin1bf,highThin2dbh,highThin2height,highThin2cr,highThin2ef,highThin2bf,highThin3dbh,highThin3height,highThin3cr,highThin3ef,highThin3bf,highFinalDbh,highFinalHeight,highFinalCR,highFinalEF,highFinalBF");
            }

            for (int resultIndex = 0; resultIndex < this.Results!.Distributions.Count; ++resultIndex)
            {
                HeuristicDistribution distribution = this.Results.Distributions[resultIndex];
                if (distribution.HeuristicParameters == null)
                {
                    throw new NotSupportedException("Result " + resultIndex + " is missing heuristic parameters.");
                }
                HeuristicSolutionPool solution = this.Results.SolutionIndex[this.Results.Distributions[resultIndex]];
                if ((solution.High == null) ||
                    (solution.High.BestTrajectory == null) ||
                    (solution.High.BestTrajectory.Heuristic == null) ||
                    (solution.Low == null))
                {
                    throw new NotSupportedException("Result " + resultIndex + " is missing a high solution, low solution, high heuristic trajectory, or back link from high trajectory to its generating heuristic.");
                }

                OrganonStandTrajectory highTrajectory = solution.High.BestTrajectory;
                int firstThinPeriod = highTrajectory.GetFirstThinPeriod();
                int periodBeforeFirstThin = firstThinPeriod - 1;
                if (periodBeforeFirstThin < 0)
                {
                    periodBeforeFirstThin = highTrajectory.PlanningPeriods - 1;
                }
                int secondThinPeriod = highTrajectory.GetSecondThinPeriod();
                int periodBeforeSecondThin = secondThinPeriod - 1;
                if (periodBeforeSecondThin < 0)
                {
                    periodBeforeSecondThin = highTrajectory.PlanningPeriods - 1;
                }
                int thirdThinPeriod = highTrajectory.GetThirdThinPeriod();
                int periodBeforeThirdThin = thirdThinPeriod - 1;
                if (periodBeforeThirdThin < 0)
                {
                    periodBeforeThirdThin = highTrajectory.PlanningPeriods - 1;
                }

                float discountRate = this.Results.DiscountRates[distribution.DiscountRateIndex];
                string linePrefix = highTrajectory.Name + "," + 
                    highTrajectory.Heuristic.GetName() + "," + 
                    distribution.HeuristicParameters.GetCsvValues() + "," +
                    WriteCmdlet.GetRateAndAgeCsvValues(highTrajectory, discountRate);

                Stand? highStandBeforeFirstThin = highTrajectory.StandByPeriod[periodBeforeFirstThin];
                Stand? highStandBeforeSecondThin = highTrajectory.StandByPeriod[periodBeforeSecondThin];
                Stand? highStandBeforeThirdThin = highTrajectory.StandByPeriod[periodBeforeThirdThin];
                Stand? highStandAtEnd = highTrajectory.StandByPeriod[^1];
                if ((highStandBeforeFirstThin == null) || (highStandBeforeSecondThin == null) || (highStandBeforeThirdThin == null) || (highStandAtEnd == null))
                {
                    throw new ParameterOutOfRangeException(nameof(this.Results), "High trajectory in run has not been fully simulated. Did the heuristic perform at least one move?");
                }
                Units finalUnits = highStandAtEnd.GetUnits();
                if ((highStandBeforeFirstThin.GetUnits() != finalUnits) || (highStandBeforeSecondThin.GetUnits() != finalUnits))
                {
                    throw new NotSupportedException("Units differ between simulation periods.");
                }

                OrganonStandTrajectory lowTrajectory = solution.Low.BestTrajectory;
                int previousSpeciesCount = 0;
                foreach (KeyValuePair<FiaCode, int[]> highTreeSelectionNForSpecies in highTrajectory.IndividualTreeSelectionBySpecies)
                {
                    Trees highTreesBeforeFirstThin = highStandBeforeFirstThin.TreesBySpecies[highTreeSelectionNForSpecies.Key];
                    Trees highTreesBeforeSecondThin = highStandBeforeSecondThin.TreesBySpecies[highTreeSelectionNForSpecies.Key];
                    Trees highTreesBeforeThirdThin = highStandBeforeThirdThin.TreesBySpecies[highTreeSelectionNForSpecies.Key];
                    Trees highTreesAtFinal = highStandAtEnd.TreesBySpecies[highTreeSelectionNForSpecies.Key];
                    WriteHarvestSchedule.GetDimensionConversions(highTreesBeforeFirstThin.Units, Units.Metric, out float areaConversionFactor, out float dbhConversionFactor, out float heightConversionFactor);

                    // uncompactedTreeIndex: tree index in periods before thinned trees are removed
                    // compactedTreeIndex: index of retained trees in periods after thinning
                    int[] lowTreeSelection = lowTrajectory.IndividualTreeSelectionBySpecies[highTreeSelectionNForSpecies.Key];
                    int[] highTreeSelection = highTreeSelectionNForSpecies.Value;
                    Debug.Assert(highTreesBeforeFirstThin.Capacity == highTreeSelection.Length);
                    int secondThinCompactedTreeIndex = 0;
                    int thirdThinCompactedTreeIndex = 0;
                    int finalCompactedTreeIndex = 0;
                    for (int uncompactedTreeIndex = 0; uncompactedTreeIndex < highTreesBeforeFirstThin.Count; ++uncompactedTreeIndex)
                    {
                        // TODO: replace FiaVolume calls with single-tree version of highTrajectory.TimberValue.ScaledVolumeThinning.GetHarvestedScribnerVolume();
                        float highThinBoardFeet = FiaVolume.GetScribnerBoardFeet(highTreesBeforeFirstThin, uncompactedTreeIndex);

                        string? highFirstThinDbh = null;
                        string? highFirstThinHeight = null;
                        string? highFirstThinCrownRatio = null;
                        string? highFirstThinExpansionFactor = null;
                        string? highFirstThinBoardFeet = null;
                        // properties before first thin are undefined if no thinning occurred
                        if (highTrajectory.GetFirstThinPeriod() != Constant.NoThinPeriod)
                        {
                            highFirstThinDbh = (dbhConversionFactor * highTreesBeforeFirstThin.Dbh[uncompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highFirstThinHeight = (heightConversionFactor * highTreesBeforeFirstThin.Height[uncompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highFirstThinCrownRatio = highTreesBeforeFirstThin.CrownRatio[uncompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highFirstThinExpansionFactor = highTreesBeforeFirstThin.LiveExpansionFactor[uncompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highFirstThinBoardFeet = FiaVolume.GetScribnerBoardFeet(highTreesBeforeFirstThin, uncompactedTreeIndex).ToString("0.00", CultureInfo.InvariantCulture);
                        }

                        string? highSecondThinDbh = null;
                        string? highSecondThinHeight = null;
                        string? highSecondThinCrownRatio = null;
                        string? highSecondThinExpansionFactor = null;
                        string? highSecondThinBoardFeet = null;
                        // properties before second thin are undefined if no thinning, only a first thin, or if tree was removed in first thin
                        bool isRemovedInFirstThin = highTreeSelection[uncompactedTreeIndex] == firstThinPeriod;
                        if ((highTrajectory.GetSecondThinPeriod() != Constant.NoThinPeriod) && (isRemovedInFirstThin == false))
                        {
                            Debug.Assert(highTreesBeforeSecondThin.Tag[secondThinCompactedTreeIndex] == highTreesBeforeFirstThin.Tag[uncompactedTreeIndex]);
                            highSecondThinDbh = (dbhConversionFactor * highTreesBeforeSecondThin.Dbh[secondThinCompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highSecondThinHeight = (heightConversionFactor * highTreesBeforeSecondThin.Height[secondThinCompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highSecondThinCrownRatio = highTreesBeforeSecondThin.CrownRatio[secondThinCompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highSecondThinExpansionFactor = highTreesBeforeSecondThin.LiveExpansionFactor[secondThinCompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highSecondThinBoardFeet = FiaVolume.GetScribnerBoardFeet(highTreesBeforeSecondThin, secondThinCompactedTreeIndex).ToString("0.00", CultureInfo.InvariantCulture);
                            ++secondThinCompactedTreeIndex; // only need to increment on retained trees, OK to increment here as not referenced below
                        }

                        string? highThirdThinDbh = null;
                        string? highThirdThinHeight = null;
                        string? highThirdThinCrownRatio = null;
                        string? highThirdThinExpansionFactor = null;
                        string? highThirdThinBoardFeet = null;
                        // properties before Third thin are undefined if no thinning, only a first thin, or if tree was removed in first thin
                        bool isRemovedInFirstOrSecondThin = isRemovedInFirstThin || (highTreeSelection[uncompactedTreeIndex] == secondThinPeriod);
                        if ((highTrajectory.GetThirdThinPeriod() != Constant.NoThinPeriod) && (isRemovedInFirstOrSecondThin == false))
                        {
                            Debug.Assert(highTreesBeforeThirdThin.Tag[thirdThinCompactedTreeIndex] == highTreesBeforeFirstThin.Tag[uncompactedTreeIndex]);
                            highThirdThinDbh = (dbhConversionFactor * highTreesBeforeThirdThin.Dbh[thirdThinCompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highThirdThinHeight = (heightConversionFactor * highTreesBeforeThirdThin.Height[thirdThinCompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highThirdThinCrownRatio = highTreesBeforeThirdThin.CrownRatio[thirdThinCompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highThirdThinExpansionFactor = highTreesBeforeThirdThin.LiveExpansionFactor[thirdThinCompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highThirdThinBoardFeet = FiaVolume.GetScribnerBoardFeet(highTreesBeforeThirdThin, thirdThinCompactedTreeIndex).ToString("0.00", CultureInfo.InvariantCulture);
                            ++thirdThinCompactedTreeIndex; // only need to increment on retained trees, OK to increment here as not referenced below
                        }

                        string? highFinalDbh = null;
                        string? highFinalHeight = null;
                        string? highFinalCrownRatio = null;
                        string? highFinalExpansionFactor = null;
                        string? highFinalBoardFeet = null;
                        bool isThinned = highTreeSelection[uncompactedTreeIndex] != Constant.NoHarvestPeriod;
                        if (isThinned == false)
                        {
                            Debug.Assert(highTreesAtFinal.Tag[finalCompactedTreeIndex] == highTreesBeforeFirstThin.Tag[uncompactedTreeIndex]);
                            highFinalDbh = (dbhConversionFactor * highTreesAtFinal.Dbh[finalCompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highFinalHeight = (heightConversionFactor * highTreesAtFinal.Height[finalCompactedTreeIndex]).ToString("0.00", CultureInfo.InvariantCulture);
                            highFinalCrownRatio = highTreesAtFinal.CrownRatio[finalCompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highFinalExpansionFactor = highTreesAtFinal.LiveExpansionFactor[finalCompactedTreeIndex].ToString("0.000", CultureInfo.InvariantCulture);
                            highFinalBoardFeet = FiaVolume.GetScribnerBoardFeet(highTreesAtFinal, finalCompactedTreeIndex).ToString("0.00", CultureInfo.InvariantCulture);
                            ++finalCompactedTreeIndex; // only need to increment on retained trees, OK to increment here as not referenced below
                        }

                        // for now, make best guess of using tree tag or index as unique identifier 
                        int tag = highTreesBeforeFirstThin.Tag[uncompactedTreeIndex] < 0 ? previousSpeciesCount + uncompactedTreeIndex : highTreesBeforeFirstThin.Tag[uncompactedTreeIndex];
                        writer.WriteLine(linePrefix + "," +
                                         highTreesBeforeFirstThin.Plot[uncompactedTreeIndex].ToString(CultureInfo.InvariantCulture) + "," +
                                         tag.ToString(CultureInfo.InvariantCulture) + "," +
                                         lowTreeSelection[uncompactedTreeIndex].ToString(CultureInfo.InvariantCulture) + "," +
                                         highTreeSelection[uncompactedTreeIndex].ToString(CultureInfo.InvariantCulture) + "," +
                                         highFirstThinDbh + "," +
                                         highFirstThinHeight + "," +
                                         highFirstThinCrownRatio + "," +
                                         highFirstThinExpansionFactor + "," +
                                         highFirstThinBoardFeet + "," +
                                         highSecondThinDbh + "," +
                                         highSecondThinHeight + "," +
                                         highSecondThinCrownRatio + "," +
                                         highSecondThinExpansionFactor + "," +
                                         highSecondThinBoardFeet + "," +
                                         highThirdThinDbh + "," +
                                         highThirdThinHeight + "," +
                                         highThirdThinCrownRatio + "," +
                                         highThirdThinExpansionFactor + "," +
                                         highThirdThinBoardFeet + "," +
                                         highFinalDbh + "," +
                                         highFinalHeight + "," +
                                         highFinalCrownRatio + "," +
                                         highFinalExpansionFactor + "," +
                                         highFinalBoardFeet);
                    }

                    previousSpeciesCount += highTreeSelection.Length;
                }
            }
        }
    }
}
