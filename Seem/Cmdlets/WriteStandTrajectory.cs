using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using Osu.Cof.Ferm.Tree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommunications.Write, "StandTrajectory")]
    public class WriteStandTrajectory : WriteCmdlet
    {
        [Parameter]
        [ValidateRange(0.1F, 100.0F)]
        public float DiameterClassSize { get; set; } // cm

        [Parameter]
        [ValidateNotNull]
        public FinancialScenarios Financial { get; set; }

        [Parameter]
        [ValidateRange(1.0F, 1000.0F)]
        public float MaximumDiameter { get; set; } // cm

        [Parameter]
        [ValidateNotNull]
        public HeuristicResults? Results { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        public List<OrganonStandTrajectory>? Trajectories { get; set; }

        public WriteStandTrajectory()
        {
            this.DiameterClassSize = Constant.Bucking.DiameterClassSizeInCentimeters;
            this.Financial = FinancialScenarios.Default;
            this.MaximumDiameter = Constant.Bucking.DefaultMaximumDiameterInCentimeters;
            this.Results = null;
            this.Trajectories = null;
        }

        protected OrganonStandTrajectory GetHighestTrajectoryAndLinePrefix(int positionOrTrajectoryIndex, out StringBuilder linePrefix, out int endOfRotationPeriod, out int financialIndex)
        {
            OrganonStandTrajectory highTrajectory;
            HeuristicParameters? heuristicParameters = null;
            string scenarioName;
            if (this.Results != null)
            {
                HeuristicResultPosition position = this.Results.PositionsEvaluated[positionOrTrajectoryIndex];
                HeuristicSolutionPool solutionPool = this.Results[position].Pool;
                if (solutionPool.High == null)
                {
                    throw new NotSupportedException("Run " + positionOrTrajectoryIndex + " is missing a high solution.");
                }
                highTrajectory = solutionPool.High.GetBestTrajectoryWithDefaulting(position);
                endOfRotationPeriod = this.Results.RotationLengths[position.RotationIndex];
                financialIndex = position.FinancialIndex;
                scenarioName = this.Results.FinancialScenarios.Name[financialIndex];
            }
            else
            {
                highTrajectory = this.Trajectories![positionOrTrajectoryIndex];
                endOfRotationPeriod = highTrajectory.PlanningPeriods - 1;
                financialIndex = Constant.HeuristicDefault.FinancialIndex;
                scenarioName = this.Financial.Name[financialIndex];
                // TODO: support logging of trajectory financials with multiple discount rates
            }
            if (highTrajectory.Heuristic != null)
            {
                heuristicParameters = highTrajectory.Heuristic.GetParameters();
            }

            string heuristicName = "none";
            if (highTrajectory.Heuristic != null)
            {
                heuristicName = highTrajectory.Heuristic.GetName();
            }
            string? heuristicParameterString = null;
            if (heuristicParameters != null)
            {
                heuristicParameterString = heuristicParameters.GetCsvValues();
            }

            string? trajectoryName = highTrajectory.Name;
            if (trajectoryName == null)
            {
                trajectoryName = positionOrTrajectoryIndex.ToString(CultureInfo.InvariantCulture);
            }

            linePrefix = new(trajectoryName + "," + heuristicName + ",");
            if (heuristicParameterString != null)
            {
                linePrefix.Append(heuristicParameterString + ",");
            }

            int firstThinAgeAsInteger = highTrajectory.GetFirstThinAge();
            string? firstThinAge = firstThinAgeAsInteger != Constant.NoThinPeriod ? firstThinAgeAsInteger.ToString(CultureInfo.InvariantCulture) : null;
            int secondThinAgeAsInteger = highTrajectory.GetSecondThinAge();
            string? secondThinAge = secondThinAgeAsInteger != Constant.NoThinPeriod ? secondThinAgeAsInteger.ToString(CultureInfo.InvariantCulture) : null;
            int thirdThinAgeAsInteger = highTrajectory.GetThirdThinAge();
            string? thirdThinAge = thirdThinAgeAsInteger != Constant.NoThinPeriod ? thirdThinAgeAsInteger.ToString(CultureInfo.InvariantCulture) : null;

            int rotationLengthAsInteger = highTrajectory.GetEndOfPeriodAge(endOfRotationPeriod);
            string rotationLength = rotationLengthAsInteger.ToString(CultureInfo.InvariantCulture);

            linePrefix.Append(firstThinAge + "," + secondThinAge + "," + thirdThinAge + "," + rotationLength + "," + scenarioName);
            return highTrajectory;
        }

        protected override void ProcessRecord()
        {
            this.ValidateParameters();

            using StreamWriter writer = this.GetWriter();

            // header
            bool resultsSpecified = this.Results != null;
            if (this.ShouldWriteHeader())
            {
                HeuristicParameters? heuristicParameters = null;
                if (resultsSpecified)
                {
                    heuristicParameters = WriteCmdlet.GetFirstHeuristicParameters(this.Results);
                }
                else if(this.Trajectories![0].Heuristic != null)
                {
                    heuristicParameters = this.Trajectories[0].Heuristic!.GetParameters();
                }

                writer.WriteLine(WriteCmdlet.GetHeuristicAndPositionCsvHeader(heuristicParameters) + ",standAge,TPH,QMD,Htop,BA,SDI,SPH,snagQMD,standingCMH,harvestCMH,standingMBFH,harvestMBFH,BAremoved,BAintensity,TPHdecrease,NPV,LEV,standing2Scmh,standing3Scmh,standing4Scmh,harvest2Scmh,harvest3Scmh,harvest4Scmh,standing2Smbfh,standing3Smbfh,standing4Smbfh,harvest2Smbfh,harvest3Smbfh,harvest4Smbfh,NPV2S,NPV3S,NPV4S,liveBiomass");
            }

            // rows for periods
            FinancialScenarios financialScenarios = this.Results != null ? this.Results.FinancialScenarios : this.Financial;
            long maxFileSizeInBytes = this.GetMaxFileSizeInBytes();
            int maxDistributionOrTrajectoryIndex = resultsSpecified ? this.Results!.PositionsEvaluated.Count : this.Trajectories!.Count;
            for (int positionOrTrajectoryIndex = 0; positionOrTrajectoryIndex < maxDistributionOrTrajectoryIndex; ++positionOrTrajectoryIndex)
            {
                OrganonStandTrajectory highTrajectory = this.GetHighestTrajectoryAndLinePrefix(positionOrTrajectoryIndex, out StringBuilder linePrefix, out int endOfRotationPeriod, out int financialIndex);
                Units trajectoryUnits = highTrajectory.GetUnits();
                if (trajectoryUnits != Units.English)
                {
                    throw new NotSupportedException("Expected Organon stand trajectory with English Units.");
                }
                highTrajectory.GetMerchantableVolumes(out StandMerchantableVolume standingVolume, out StandMerchantableVolume harvestedVolume);

                SnagLogTable snagsAndLogs = new(highTrajectory, this.MaximumDiameter, this.DiameterClassSize);

                float totalThinNetPresentValue = 0.0F;
                for (int period = 0; period <= endOfRotationPeriod; ++period)
                {
                    // get density and volumes
                    float basalAreaRemoved = Constant.AcresPerHectare * Constant.MetersPerFoot * Constant.MetersPerFoot * highTrajectory.BasalAreaRemoved[period]; // m²/acre
                    float basalAreaIntensity = 0.0F;
                    if (period > 0)
                    {
                        basalAreaIntensity = basalAreaRemoved / highTrajectory.DensityByPeriod[period - 1].BasalAreaPerAcre;
                    }
                    float harvestVolumeScribner = harvestedVolume.GetScribnerTotal(period); // MBF/ha
                    Debug.Assert((harvestVolumeScribner == 0.0F && basalAreaRemoved == 0.0F) || (harvestVolumeScribner > 0.0F && basalAreaRemoved > 0.0F));

                    float treesPerAcreDecrease = 0.0F;
                    if (period > 0)
                    {
                        OrganonStandDensity previousDensity = highTrajectory.DensityByPeriod[period - 1];
                        OrganonStandDensity currentDensity = highTrajectory.DensityByPeriod[period];
                        if ((currentDensity == null) || (previousDensity == null))
                        {
                            throw new ParameterOutOfRangeException(null, "Stand density information is missing. Did the heuristic perform at least one fully simulated move?");
                        }
                        treesPerAcreDecrease = 1.0F - currentDensity.TreesPerAcre / previousDensity.TreesPerAcre;
                    }

                    OrganonStand stand = highTrajectory.StandByPeriod[period] ?? throw new NotSupportedException("Stand information missing for period " + period + ".");
                    OrganonStandDensity density = highTrajectory.DensityByPeriod[period];
                    float quadraticMeanDiameterInCm = stand.GetQuadraticMeanDiameterInCentimeters(); // 1/(10 in * 2.54 cm/in) = 0.03937008
                    float topHeightInM = stand.GetTopHeightInMeters();
                    float reinekeStandDensityIndex = Constant.AcresPerHectare * density.TreesPerAcre * MathF.Pow(0.03937008F * quadraticMeanDiameterInCm, Constant.ReinekeExponent);

                    float treesPerHectare = Constant.AcresPerHectare * density.TreesPerAcre;
                    float basalAreaPerHectare = Constant.AcresPerHectare * Constant.MetersPerFoot * Constant.MetersPerFoot * density.BasalAreaPerAcre;
                    float treesPerHectareDecrease = Constant.AcresPerHectare * treesPerAcreDecrease;

                    // NPV and LEV
                    float thinNetPresentValue = financialScenarios.GetNetPresentThinningValue(highTrajectory, financialIndex, period, out float thin2SawNpv, out float thin3SawNpv, out float thin4SawNpv);
                    totalThinNetPresentValue += thinNetPresentValue;
                    float standingNetPresentValue = financialScenarios.GetNetPresentRegenerationHarvestValue(highTrajectory, financialIndex, period, out float standing2SawNpv, out float standing3SawNpv, out float standing4SawNpv);
                    float reforestationNetPresentValue = financialScenarios.GetNetPresentReforestationValue(financialIndex, highTrajectory.PlantingDensityInTreesPerHectare);
                    float periodNetPresentValue = totalThinNetPresentValue + standingNetPresentValue + reforestationNetPresentValue;

                    float presentToFutureConversionFactor = financialScenarios.GetAppreciationFactor(financialIndex, highTrajectory.GetEndOfPeriodAge(endOfRotationPeriod));
                    float landExpectationValue = presentToFutureConversionFactor * periodNetPresentValue / (presentToFutureConversionFactor - 1.0F);

                    // pond NPV by grade
                    float netPresentValue2Saw = standing2SawNpv + thin2SawNpv;
                    float netPresentValue3Saw = standing3SawNpv + thin3SawNpv;
                    float netPresentValue4Saw = standing4SawNpv + thin4SawNpv;

                    // biomass
                    float liveBiomass = 0.001F * stand.GetLiveBiomass(); // Mg/ha

                    writer.WriteLine(linePrefix + "," +
                                     stand.AgeInYears.ToString(CultureInfo.InvariantCulture) + "," +
                                     treesPerHectare.ToString("0.0", CultureInfo.InvariantCulture) + "," +
                                     quadraticMeanDiameterInCm.ToString("0.00", CultureInfo.InvariantCulture) + "," +
                                     topHeightInM.ToString("0.00", CultureInfo.InvariantCulture) + "," +
                                     basalAreaPerHectare.ToString("0.0", CultureInfo.InvariantCulture) + "," +
                                     reinekeStandDensityIndex.ToString("0.0", CultureInfo.InvariantCulture) + "," +
                                     snagsAndLogs.SnagsPerHectareByPeriod[period].ToString("0.0", CultureInfo.InvariantCulture) + "," +
                                     snagsAndLogs.SnagQmdInCentimetersByPeriod[period].ToString("0.00", CultureInfo.InvariantCulture) + "," +
                                     standingVolume.GetCubicTotal(period).ToString("0.000", CultureInfo.InvariantCulture) + "," +  // m³/ha
                                     harvestedVolume.GetCubicTotal(period).ToString("0.000", CultureInfo.InvariantCulture) + "," +  // m³/ha
                                     standingVolume.GetScribnerTotal(period).ToString("0.000", CultureInfo.InvariantCulture) + "," +  // MBF/ha
                                     harvestVolumeScribner.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     basalAreaRemoved.ToString("0.0", CultureInfo.InvariantCulture) + "," +
                                     basalAreaIntensity.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     treesPerHectareDecrease.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     periodNetPresentValue.ToString("0", CultureInfo.InvariantCulture) + "," +
                                     landExpectationValue.ToString("0", CultureInfo.InvariantCulture) + "," +
                                     standingVolume.Cubic2Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     standingVolume.Cubic3Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     standingVolume.Cubic4Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     harvestedVolume.Cubic2Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     harvestedVolume.Cubic3Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     harvestedVolume.Cubic4Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     standingVolume.Scribner2Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     standingVolume.Scribner3Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     standingVolume.Scribner4Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     harvestedVolume.Scribner2Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     harvestedVolume.Scribner3Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     harvestedVolume.Scribner4Saw[period].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     standing2SawNpv.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     standing3SawNpv.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     standing4SawNpv.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     liveBiomass.ToString("0.00", CultureInfo.InvariantCulture));
                }

                if (writer.BaseStream.Length > maxFileSizeInBytes)
                {
                    this.WriteWarning("Write-StandTrajectory: File size limit of " + this.LimitGB.ToString("0.00") + " GB exceeded.");
                    break;
                }
            }
        }

        protected void ValidateParameters()
        {
            if ((this.Results == null) && (this.Trajectories == null))
            {
                throw new ParameterOutOfRangeException(nameof(this.Trajectories), "Niether of " + nameof(this.Results) + " or " + nameof(this.Trajectories) + " are specified. Specify one or the other.");
            }
            if ((this.Results != null) && (this.Trajectories != null))
            {
                throw new ParameterOutOfRangeException(nameof(this.Trajectories), "Both " + nameof(this.Results) + " and " + nameof(this.Trajectories) + " are specified. Specify one or the other.");
            }
            if ((this.Results != null) && (this.Results.PositionsEvaluated.Count < 1))
            {
                throw new ParameterOutOfRangeException(nameof(this.Results), nameof(this.Results) + " is empty. At least one run must be present.");
            }
            if ((this.Trajectories != null) && (this.Trajectories.Count < 1))
            {
                throw new ParameterOutOfRangeException(nameof(this.Trajectories), nameof(this.Trajectories) + " is empty. At least one run must be present.");
            }
        }
    }
}
