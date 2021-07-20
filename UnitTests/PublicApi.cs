using Microsoft.VisualStudio.TestTools.UnitTesting;
using Osu.Cof.Ferm.Data;
using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using Osu.Cof.Ferm.Tree;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Osu.Cof.Ferm.Test
{
    [TestClass]
    public class PublicApi : OrganonTest
    {
        private readonly float biomassTolerance;
        private readonly float qmdTolerance;
        private readonly float topHeightTolerance;
        private readonly float volumeTolerance;

        public TestContext? TestContext { get; set; }

        public PublicApi()
        {
            this.biomassTolerance = 1.01F;
            this.qmdTolerance = 1.01F;
            this.topHeightTolerance = 1.01F;
            this.volumeTolerance = 1.01F;
        }

        private static HeuristicResultPosition CreateDefaultSolutionPosition()
        {
            HeuristicResultPosition position = new()
            {
                ParameterIndex = 0,
                FinancialIndex = Constant.HeuristicDefault.FinancialIndex,
                FirstThinPeriodIndex = 0,
                SecondThinPeriodIndex = 0,
                ThirdThinPeriodIndex = 0,
                RotationIndex = 0
            };
            return position;
        }

        private static HeuristicResults<HeuristicParameters> CreateResults(HeuristicParameters parameters, int rotationLength)
        {
            List<HeuristicParameters> parameterCombinations = new() { parameters };
            FinancialScenarios financialScenarios = new();
            List<int> noThin = new() { Constant.NoThinPeriod };
            List<int> planningPeriods = new() { rotationLength };
            HeuristicResults<HeuristicParameters> results = new(parameterCombinations, noThin, noThin, noThin, planningPeriods, financialScenarios, TestConstant.SolutionPoolSize);

            HeuristicResultPosition position = new()
            {
                FinancialIndex = Constant.HeuristicDefault.FinancialIndex,
                FirstThinPeriodIndex = 0,
                SecondThinPeriodIndex = 0,
                ThirdThinPeriodIndex = 0,
                ParameterIndex = Constant.HeuristicDefault.RotationIndex,
                RotationIndex = 0
            };
            results.AddEvaluatedPosition(position);
            
            return results;
        }

        private static PlotsWithHeight GetNelder()
        {
            string plotFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OSU", "Organon", "Malcolm Knapp Nelder 1.xlsx");
            PlotsWithHeight plot = new(new List<int>() { 1 }, 1.327F);
            plot.Read(plotFilePath, "1");
            return plot;
        }

        private static PlotsWithHeight GetPlot14()
        {
            string plotFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OSU", "Organon", "Malcolm Knapp plots 14-18+34 Ministry.xlsx");
            PlotsWithHeight plot = new(new List<int>() { 14 }, 4.48F);
            plot.Read(plotFilePath, "0.2 ha");
            return plot;
        }

        [TestMethod]
        public void HuffmanPeakNobleFir()
        {
            string plotFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OSU", "Organon", "HPNF.xlsx");
            PspStand huffmanPeak = new(plotFilePath, "HPNF", 0.2F);
            OrganonVariant variant = new OrganonVariantSwo(); // SWO allows mapping ABAM -> ABGR and ABPR -> ABCO
            OrganonConfiguration configuration = OrganonTest.CreateOrganonConfiguration(variant);
            TestStand stand = huffmanPeak.ToStand(configuration, 80.0F);
            int startYear = 1980;
            stand.WriteCompetitionAsCsv("HPNF initial competition.csv", variant, startYear);
            OrganonTest.GrowPspStand(huffmanPeak, stand, variant, startYear, 2015, Path.GetFileNameWithoutExtension(plotFilePath));

            TreeQuantiles measuredQuantiles = new(stand, huffmanPeak, startYear);
            using StreamWriter quantileWriter = measuredQuantiles.WriteToCsv("HPNF measured quantiles.csv", variant, startYear);
            foreach (int measurementYear in huffmanPeak.MeasurementYears)
            {
                if (measurementYear != startYear)
                {
                    measuredQuantiles = new TreeQuantiles(stand, huffmanPeak, measurementYear);
                    measuredQuantiles.WriteToCsv(quantileWriter, variant, measurementYear);
                }
            }
        }

        [TestMethod]
        public void NelderCircularHeuristics()
        {
            int thinningPeriod = 4;
            int treeCount = 100;
            float minObjectiveFunctionWithScaledVolume = 5.516F; // USk$/ha
            #if DEBUG
            treeCount = 48;
            minObjectiveFunctionWithScaledVolume = 3.060F; // USk$/ha, bilinear interpolation: 1 cm diameter classes, 1 m height classes, mean timber prices through June 2021
            #endif

            PlotsWithHeight nelder = PublicApi.GetNelder();
            OrganonConfiguration configuration = new(new OrganonVariantNwo());
            OrganonStand stand = nelder.ToOrganonStand(configuration, 20, 130.0F, treeCount);
            stand.PlantingDensityInTreesPerHectare = TestConstant.NelderReplantingDensityInTreesPerHectare;

            RunParameters landExpectationValueIts = new(new List<int>() { 9 }, configuration)
            {
                MaximizeForPlanningPeriod = 9
            };
            landExpectationValueIts.Treatments.Harvests.Add(new ThinByIndividualTreeSelection(thinningPeriod));

            // first improving circular search
            HeuristicResults<HeuristicParameters> results = PublicApi.CreateResults(new(), landExpectationValueIts.MaximizeForPlanningPeriod);
            FirstImprovingCircularSearch firstCircular = new(stand, results.ParameterCombinations[0], landExpectationValueIts)
            {
                //IsStochastic = true,
                MaximumIterations = 10 * treeCount
            };
            HeuristicResultPosition defaultPosition = PublicApi.CreateDefaultSolutionPosition();
            HeuristicPerformanceCounters firstImprovingCounters = firstCircular.Run(defaultPosition, results);
            results.AssimilateHeuristicRunIntoPosition(firstCircular, firstImprovingCounters, defaultPosition);

            // hero
            Hero hero = new(stand, results.ParameterCombinations[0], landExpectationValueIts)
            {
                //IsStochastic = true,
                MaximumIterations = 10
            };
            // debugging note: it can be helpful to set fully greedy heuristic parameters so the initial prescription remains as no trees selected
            //hero.CurrentTrajectory.SetTreeSelection(0, thinningPeriod);
            HeuristicPerformanceCounters heroCounters = hero.Run(defaultPosition, results);
            results.AssimilateHeuristicRunIntoPosition(hero, heroCounters, defaultPosition);

            // prescription coordinate descent
            RunParameters landExpectationValuePrescription = new(new List<int>() { landExpectationValueIts.MaximizeForPlanningPeriod }, configuration)
            {
                MaximizeForPlanningPeriod = landExpectationValueIts.MaximizeForPlanningPeriod
            };
            landExpectationValuePrescription.Treatments.Harvests.Add(new ThinByPrescription(thinningPeriod));
            PrescriptionParameters prescriptionParameters = new()
            {
                MinimumIntensity = 0.0F
            };
            PrescriptionCoordinateAscent prescriptionDescent = new(stand, prescriptionParameters, landExpectationValuePrescription)
            {
                //IsStochastic = true
            };
            HeuristicPerformanceCounters prescriptionDescentCounters = prescriptionDescent.Run(defaultPosition, results);
            results.AssimilateHeuristicRunIntoPosition(prescriptionDescent, prescriptionDescentCounters, defaultPosition);

            this.Verify(firstCircular, firstImprovingCounters);
            this.Verify(hero, heroCounters);
            this.Verify(prescriptionDescent, prescriptionDescentCounters);
            PublicApi.Verify(results, defaultPosition, landExpectationValueIts.MaximizeForPlanningPeriod);

            TreeSelection firstCircularTreeSelection = firstCircular.GetBestTrajectoryWithDefaulting(defaultPosition).IndividualTreeSelectionBySpecies[FiaCode.PseudotsugaMenziesii];
            TreeSelection heroTreeSelection = hero.GetBestTrajectoryWithDefaulting(defaultPosition).IndividualTreeSelectionBySpecies[FiaCode.PseudotsugaMenziesii];
            TreeSelection prescriptionTreeSelection = prescriptionDescent.GetBestTrajectoryWithDefaulting(defaultPosition).IndividualTreeSelectionBySpecies[FiaCode.PseudotsugaMenziesii];
            int treesThinnedByFirstCircular = 0;
            int treesThinnedByHero = 0;
            int treesThinnedByPrescription = 0;
            for (int treeIndex = 0; treeIndex < heroTreeSelection.Count; ++treeIndex)
            {
                if (firstCircularTreeSelection[treeIndex] != Constant.NoHarvestPeriod)
                {
                    ++treesThinnedByFirstCircular;
                }
                if (heroTreeSelection[treeIndex] != Constant.NoHarvestPeriod)
                {
                    ++treesThinnedByHero;
                }
                if (prescriptionTreeSelection[treeIndex] != Constant.NoHarvestPeriod)
                {
                    ++treesThinnedByPrescription;
                }
            }

            Assert.IsTrue(treesThinnedByFirstCircular == 0); // highest financial value solution happens to be the unthinned one
            Assert.IsTrue(treesThinnedByHero == 0);
            Assert.IsTrue(treesThinnedByPrescription == 0);

            float highestFirstCircularFinancialValue = firstCircular.FinancialValue.GetHighestValue();
            float highestHeroFinancialValue = hero.FinancialValue.GetHighestValue();
            float highestPrescriptionFinancialValue = hero.FinancialValue.GetHighestValue();
            this.TestContext!.WriteLine("highest first circular financial value: {0} observed, near {1} expected", highestFirstCircularFinancialValue, minObjectiveFunctionWithScaledVolume);
            this.TestContext!.WriteLine("highest hero financial value: {0} observed, near {1} expected", highestHeroFinancialValue, minObjectiveFunctionWithScaledVolume);
            this.TestContext!.WriteLine("highest prescription descent value: {0} observed, near {1} expected", highestPrescriptionFinancialValue, minObjectiveFunctionWithScaledVolume);

            Assert.IsTrue(highestFirstCircularFinancialValue > minObjectiveFunctionWithScaledVolume);
            Assert.IsTrue(highestFirstCircularFinancialValue < 1.02F * minObjectiveFunctionWithScaledVolume);
            Assert.IsTrue(highestPrescriptionFinancialValue < 1.02F * minObjectiveFunctionWithScaledVolume);

            Assert.IsTrue(highestHeroFinancialValue > minObjectiveFunctionWithScaledVolume);
            Assert.IsTrue(highestHeroFinancialValue < 1.02F * minObjectiveFunctionWithScaledVolume);
            Assert.IsTrue(highestPrescriptionFinancialValue < 1.02F * minObjectiveFunctionWithScaledVolume);
        }

        [TestMethod]
        public void NelderOtherHeuristics()
        {
            int thinningPeriod = 4;
            int treeCount = 75;
            #if DEBUG
                treeCount = 25;
            #endif

            PlotsWithHeight nelder = PublicApi.GetNelder();
            OrganonConfiguration configuration = OrganonTest.CreateOrganonConfiguration(new OrganonVariantNwo());
            OrganonStand stand = nelder.ToOrganonStand(configuration, 20, 130.0F, treeCount);
            stand.PlantingDensityInTreesPerHectare = TestConstant.NelderReplantingDensityInTreesPerHectare;

            // heuristics optimizing for LEV
            RunParameters runForLandExpectationValue = new(new List<int>() { 9 }, configuration)
            {
                MaximizeForPlanningPeriod = 9
            };
            runForLandExpectationValue.Treatments.Harvests.Add(new ThinByIndividualTreeSelection(thinningPeriod));
            HeuristicResults<HeuristicParameters> levResults = PublicApi.CreateResults(new(), runForLandExpectationValue.MaximizeForPlanningPeriod);
            HeuristicResultPosition levPosition = levResults.PositionsEvaluated[0];
            HeuristicObjectiveDistribution levDistribution = levResults[levPosition].Distribution;
            HeuristicPerformanceCounters totalCounters = new();

            GeneticParameters geneticParameters = new(treeCount)
            {
                PopulationSize = 7,
                MaximumGenerations = 5,
            };
            GeneticAlgorithm genetic = new(stand, geneticParameters, runForLandExpectationValue);
            HeuristicPerformanceCounters geneticCounters = genetic.Run(levPosition, levResults);
            totalCounters += geneticCounters;
            levResults.AssimilateHeuristicRunIntoPosition(genetic, geneticCounters, levPosition);

            GreatDeluge deluge = new(stand, levResults.ParameterCombinations[0], runForLandExpectationValue)
            {
                RainRate = 5,
                LowerWaterAfter = 9,
                StopAfter = 10
            };
            HeuristicPerformanceCounters delugeCounters = deluge.Run(levPosition, levResults);
            totalCounters += delugeCounters;
            levResults.AssimilateHeuristicRunIntoPosition(deluge, delugeCounters, levPosition);

            RecordTravel recordTravel = new(stand, levResults.ParameterCombinations[0], runForLandExpectationValue)
            {
                StopAfter = 10
            };
            HeuristicPerformanceCounters recordCounters = recordTravel.Run(levPosition, levResults);
            levResults.AssimilateHeuristicRunIntoPosition(recordTravel, recordCounters, levPosition);

            SimulatedAnnealing annealer = new(stand, levResults.ParameterCombinations[0], runForLandExpectationValue)
            {
                Iterations = 100
            };
            HeuristicPerformanceCounters annealerCounters = annealer.Run(levPosition, levResults);
            totalCounters += annealerCounters;
            levResults.AssimilateHeuristicRunIntoPosition(annealer, annealerCounters, levPosition);

            TabuParameters tabuParameters = new();
            TabuSearch tabu = new(stand, tabuParameters, runForLandExpectationValue)
            {
                Iterations = 7,
                //Jump = 2,
                MaximumTenure = 5
            };
            HeuristicPerformanceCounters tabuCounters = tabu.Run(levPosition, levResults);
            totalCounters += tabuCounters;
            levResults.AssimilateHeuristicRunIntoPosition(tabu, tabuCounters, levPosition);

            ThresholdAccepting thresholdAcceptor = new(stand, levResults.ParameterCombinations[0], runForLandExpectationValue);
            thresholdAcceptor.IterationsPerThreshold.Clear();
            thresholdAcceptor.Thresholds.Clear();
            thresholdAcceptor.IterationsPerThreshold.Add(10);
            thresholdAcceptor.Thresholds.Add(1.0F);
            HeuristicPerformanceCounters thresholdCounters = thresholdAcceptor.Run(levPosition, levResults);
            totalCounters += thresholdCounters;
            levResults.AssimilateHeuristicRunIntoPosition(thresholdAcceptor, thresholdCounters, levPosition);

            PrescriptionParameters prescriptionParameters = new()
            {
                MaximumIntensity = 60.0F,
                MinimumIntensity = 50.0F,
                DefaultIntensityStepSize = 10.0F,
            };
            runForLandExpectationValue.Treatments.Harvests[0] = new ThinByPrescription(thinningPeriod); // must change from individual tree selection
            PrescriptionEnumeration enumerator = new(stand, prescriptionParameters, runForLandExpectationValue);
            HeuristicPerformanceCounters enumerationCounters = enumerator.Run(levPosition, levResults);
            totalCounters += enumerationCounters;
            levResults.AssimilateHeuristicRunIntoPosition(enumerator, enumerationCounters, levPosition);

            // check solution pool
            HeuristicSolutionPool levEliteSolutions = PublicApi.Verify(levResults, levPosition, runForLandExpectationValue.MaximizeForPlanningPeriod);

            // heuristic optimizing for volume
            RunParameters runForVolume = new(runForLandExpectationValue.RotationLengths, configuration)
            {
                MaximizeForPlanningPeriod = runForLandExpectationValue.MaximizeForPlanningPeriod,
                TimberObjective = TimberObjective.ScribnerVolume
            };
            runForVolume.Treatments.Harvests.Add(new ThinByIndividualTreeSelection(thinningPeriod));

            HeuristicResults<HeuristicParameters> volumeResults = PublicApi.CreateResults(new(), runForLandExpectationValue.MaximizeForPlanningPeriod);
            HeuristicResultPosition volumePosition = volumeResults.PositionsEvaluated[0];
            AutocorrelatedWalk autocorrelated = new(stand, volumeResults.ParameterCombinations[0], runForVolume)
            {
                Iterations = 4
            };
            HeuristicPerformanceCounters autocorrlatedCounters = autocorrelated.Run(volumePosition, volumeResults);
            totalCounters += autocorrlatedCounters;
            volumeResults[volumePosition].Distribution.AddSolution(autocorrelated, volumePosition, autocorrlatedCounters);
            volumeResults[volumePosition].Pool.TryAddOrReplace(autocorrelated, volumePosition);

            // heuristics assigned to net present value optimization
            this.Verify(genetic, geneticCounters);
            this.Verify(deluge, delugeCounters);
            this.Verify(recordTravel, recordCounters);
            this.Verify(annealer, annealerCounters);
            this.Verify(tabu, tabuCounters);
            this.Verify(thresholdAcceptor, thresholdCounters);
            this.Verify(enumerator, enumerationCounters);

            // heuristic assigned to volume optimization
            this.Verify(autocorrelated, autocorrlatedCounters);

            // verify distribution
            float maxHeuristicLev = new float[] { deluge.FinancialValue.GetHighestValue(), annealer.FinancialValue.GetHighestValue(), thresholdAcceptor.FinancialValue.GetHighestValue(), genetic.FinancialValue.GetHighestValue(), enumerator.FinancialValue.GetHighestValue(), recordTravel.FinancialValue.GetHighestValue(), tabu.FinancialValue.GetHighestValue() }.Max();
            float maxLevInDistribution = levDistribution.HighestFinancialValueBySolution.Max();
            Assert.IsTrue(maxHeuristicLev == maxLevInDistribution);
            Assert.IsTrue(maxHeuristicLev == levEliteSolutions.High!.FinancialValue.GetHighestValue());

            int maxMove = levDistribution.GetMaximumMoves();
            Assert.IsTrue(maxMove > 0);
            List<float> landExpectationVaues = new(8);
            for (int moveIndex = 0; moveIndex < maxMove; ++moveIndex)
            {
                PublicApi.TryAppendAcceptedFinancialValue(landExpectationVaues, deluge, moveIndex);
                PublicApi.TryAppendAcceptedFinancialValue(landExpectationVaues, annealer, moveIndex);
                PublicApi.TryAppendAcceptedFinancialValue(landExpectationVaues, thresholdAcceptor, moveIndex);
                PublicApi.TryAppendAcceptedFinancialValue(landExpectationVaues, genetic, moveIndex);
                PublicApi.TryAppendAcceptedFinancialValue(landExpectationVaues, enumerator, moveIndex);
                PublicApi.TryAppendAcceptedFinancialValue(landExpectationVaues, recordTravel, moveIndex);
                PublicApi.TryAppendAcceptedFinancialValue(landExpectationVaues, tabu, moveIndex);
                maxHeuristicLev = landExpectationVaues.Max();
                float minHeuristicLev = landExpectationVaues.Min();

                DistributionStatistics moveStatistics = levDistribution.GetFinancialStatisticsForMove(moveIndex);
                maxLevInDistribution = moveStatistics.Maximum;
                float minDistributionObjective = moveStatistics.Minimum;
                int distributionRunCount = moveStatistics.Count;

                Assert.IsTrue(distributionRunCount > 0);
                Assert.IsTrue(distributionRunCount <= 8);
                Assert.IsTrue(maxLevInDistribution <= maxHeuristicLev);
                Assert.IsTrue(minDistributionObjective >= minHeuristicLev);
                Assert.IsTrue(moveStatistics.Mean >= minHeuristicLev);
                Assert.IsTrue(moveStatistics.Mean <= maxHeuristicLev);
                Assert.IsTrue(moveStatistics.Median >= minHeuristicLev);
                Assert.IsTrue(moveStatistics.Median <= maxHeuristicLev);
                // not enough heuristic runs to check percentiles

                landExpectationVaues.Clear();
            }

            // verify solution pooling
            levResults.GetPoolPerformanceCounters(out int solutionsCached, out int solutionsAccepted, out int solutionsRejected);
            Assert.IsTrue(solutionsCached <= TestConstant.SolutionPoolSize);
            Assert.IsTrue(solutionsAccepted >= solutionsCached);
            Assert.IsTrue(solutionsRejected >= 0);
        }

        [TestMethod]
        public void NelderTrajectory()
        {
            int expectedUnthinnedTreeRecordCount = 661;
            int lastPeriod = 9;

            PlotsWithHeight nelder = PublicApi.GetNelder();
            OrganonConfiguration configuration = OrganonTest.CreateOrganonConfiguration(new OrganonVariantNwo());
            OrganonStand stand = nelder.ToOrganonStand(configuration, 20, 130.0F);
            stand.PlantingDensityInTreesPerHectare = TestConstant.NelderReplantingDensityInTreesPerHectare;

            OrganonStandTrajectory unthinnedTrajectory = new(stand, configuration, TreeVolume.Default, lastPeriod);
            int unthinnedTimeSteps = unthinnedTrajectory.Simulate();
            Assert.IsTrue(unthinnedTimeSteps == lastPeriod);

            int firstThinPeriod = 3;
            ThinByPrescription firstThinPrescription = new(firstThinPeriod)
            {
                FromAbovePercentage = 20.0F, // by basal area
                ProportionalPercentage = 15.0F,
                FromBelowPercentage = 10.0F
            };
            OrganonStandTrajectory oneThinTrajectory = new(unthinnedTrajectory); // cover caching of previously simulated timesteps
            oneThinTrajectory.Treatments.Harvests.Add(firstThinPrescription);
            Assert.IsTrue(oneThinTrajectory.StandByPeriod[0]!.GetTreeRecordCount() == expectedUnthinnedTreeRecordCount);
            int oneThinTimeSteps = oneThinTrajectory.Simulate();
            Assert.IsTrue(oneThinTimeSteps == lastPeriod - firstThinPeriod + 1);

            int secondThinPeriod = 6;
            ThinByPrescription secondThinPrescription = new(secondThinPeriod)
            {
                FromAbovePercentage = 0.0F,
                ProportionalPercentage = 20.0F,
                FromBelowPercentage = 0.0F
            };
            OrganonStandTrajectory twoThinTrajectory = new(stand, configuration, TreeVolume.Default, lastPeriod);
            twoThinTrajectory.Treatments.Harvests.Add(firstThinPrescription);
            twoThinTrajectory.Treatments.Harvests.Add(secondThinPrescription);
            AssertNullable.IsNotNull(twoThinTrajectory.StandByPeriod[0]);
            Assert.IsTrue(twoThinTrajectory.StandByPeriod[0]!.GetTreeRecordCount() == expectedUnthinnedTreeRecordCount);
            int twoThinTimeSteps = twoThinTrajectory.Simulate(); // resimulate as cross check on unthinned trajectory
            Assert.IsTrue(twoThinTimeSteps == lastPeriod);

            int thirdThinPeriod = 8;
            OrganonStandTrajectory threeThinTrajectory = new(twoThinTrajectory);  // cover caching of previously simulated timesteps and thinning state
            threeThinTrajectory.Treatments.Harvests.Add(new ThinByPrescription(thirdThinPeriod)
            {
                FromAbovePercentage = 0.0F,
                ProportionalPercentage = 10.0F,
                FromBelowPercentage = 10.0F
            });
            AssertNullable.IsNotNull(threeThinTrajectory.StandByPeriod[0]);
            Assert.IsTrue(threeThinTrajectory.StandByPeriod[0]!.GetTreeRecordCount() == expectedUnthinnedTreeRecordCount);
            int threeThinTimeSteps = threeThinTrajectory.Simulate();
            Assert.IsTrue(threeThinTimeSteps == lastPeriod - thirdThinPeriod + 1);

            // verify unthinned trajectory
            // find/replace regular expression for cleaning up watch window copy/paste: \s+\w+\.\w+\[\d+\]\.\w+\(\)\s+(\d+.\d{1,2})\d*\s+float\r?\n -> $1F, 
            //                                                                          \s+\w+\.\w+\(\d+\)\s+(\d+.\d{1,2})\d*\s+float\r?\n
            ExpectedStandTrajectory unthinnedExpected = new()
            {
                //                         0       1       2       3       4       5       6       7       8       9
                MinimumQmd = new float[] { 16.84F, 20.97F, 24.63F, 27.94F, 31.02F, 33.98F, 36.88F, 39.74F, 42.60F, 45.44F }, // cm
                //                               0       1       2       3       4       5       6       7       8       9
                MinimumTopHeight = new float[] { 16.50F, 20.69F, 24.50F, 27.94F, 31.08F, 33.94F, 36.56F, 38.96F, 41.17F, 43.21F }, // m
                // Poudel 2018 + Scribner long log net MBF/ha
                // bilinear interpolation: 1 cm diameter classes, 1 m height classes
                MinimumStandingCubic = new float[] { 65.52F, 152.02F, 250.95F, 359.85F, 462.89F, 552.85F, 632.43F, 702.43F, 764.29F, 820.69F },
                MinimumStandingMbf = new float[] { 9.64F, 18.88F, 30.71F, 46.73F, 61.79F, 74.36F, 88.18F, 102.41F, 115.29F, 127.26F },
                MinimumHarvestCubic = new float[lastPeriod + 1],
                MinimumHarvestMbf = new float[lastPeriod + 1]
            };

            foreach (Stand? unthinnedStand in unthinnedTrajectory.StandByPeriod)
            {
                AssertNullable.IsNotNull(unthinnedStand);
                Assert.IsTrue(unthinnedStand.GetTreeRecordCount() == expectedUnthinnedTreeRecordCount);
            }

            this.Verify(unthinnedTrajectory, unthinnedExpected, configuration.Variant.TimeStepInYears);

            // verify one thin trajectory
            ExpectedStandTrajectory oneThinExpected = new()
            {
                FirstThinPeriod = firstThinPeriod,
                MinimumTreesSelected = 200,
                MaximumTreesSelected = 400,
                //                         0       1       2       3       4       5       6       7       8       9
                MinimumQmd = new float[] { 16.84F, 20.97F, 24.63F, 30.23F, 34.49F, 38.11F, 41.35F, 44.31F, 47.06F, 49.67F }, // cm
                //                               0       1       2       3       4       5       6       7       8       9
                MinimumTopHeight = new float[] { 16.50F, 20.69F, 24.50F, 26.95F, 30.02F, 32.94F, 35.64F, 38.12F, 40.40F, 42.51F }, // m
                // Poudel 2018 + Scribner long log net MBF/ha
                // bilinear interpolation: 1 cm diameter classes, 1 m height classes
                MinimumStandingCubic = new float[] { 65.52F, 152.02F, 250.95F, 225.01F, 321.43F, 415.73F, 505.62F, 590.82F, 670.77F, 743.62F },
                MinimumStandingMbf = new float[] { 9.64F, 18.88F, 30.71F, 27.76F, 41.29F, 53.85F, 67.46F, 83.72F, 99.19F, 113.17F },
                MinimumHarvestCubic = new float[] { 0.0F, 0.0F, 0.0F, 105.89F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F },
                MinimumHarvestMbf = new float[] { 0.0F, 0.0F, 0.0F, 14.82F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F }
            };

            for (int periodIndex = 0; periodIndex < firstThinPeriod; ++periodIndex)
            {
                Stand? unthinnedStand = oneThinTrajectory.StandByPeriod[periodIndex];
                AssertNullable.IsNotNull(unthinnedStand);
                Assert.IsTrue(unthinnedStand.GetTreeRecordCount() == expectedUnthinnedTreeRecordCount);
            }
            int expectedFirstThinTreeRecordCount = 328; // must be updated if prescription changes
            for (int periodIndex = firstThinPeriod; periodIndex < oneThinTrajectory.PlanningPeriods; ++periodIndex)
            {
                Stand? thinnedStand = oneThinTrajectory.StandByPeriod[periodIndex];
                AssertNullable.IsNotNull(thinnedStand);
                Assert.IsTrue(thinnedStand.GetTreeRecordCount() == expectedFirstThinTreeRecordCount);
            }

            this.Verify(oneThinTrajectory, oneThinExpected, configuration.Variant.TimeStepInYears);
            this.Verify(oneThinTrajectory, oneThinExpected);

            // verify two thin trajectory
            ExpectedStandTrajectory twoThinExpected = new()
            {
                FirstThinPeriod = firstThinPeriod,
                SecondThinPeriod = secondThinPeriod,
                MinimumTreesSelected = 200,
                MaximumTreesSelected = 400,
                //                         0       1       2       3       4       5       6       7       8       9
                MinimumQmd = new float[] { 16.84F, 20.97F, 24.63F, 30.23F, 34.49F, 38.11F, 41.92F, 45.34F, 48.40F, 51.22F }, // cm
                //                               0       1       2       3       4       5       6       7       8       9
                MinimumTopHeight = new float[] { 16.50F, 20.69F, 24.50F, 26.95F, 30.02F, 32.94F, 35.12F, 37.58F, 39.89F, 42.04F }, // m
                // Poudel 2018 + Scribner long log net MBF/ha
                // bilinear interpolation: 1 cm diameter classes, 1 m height classes
                MinimumStandingCubic = new float[] { 65.52F, 152.02F, 250.95F, 225.01F, 321.43F, 415.73F, 415.75F, 500.65F, 581.91F, 658.85F },
                MinimumStandingMbf = new float[] { 9.64F, 18.88F, 30.71F, 27.76F, 41.29F, 53.85F, 55.10F, 70.41F, 85.62F, 100.06F },
                MinimumHarvestCubic = new float[] { 0.0F, 0.0F, 0.0F, 105.89F, 0.0F, 0.0F, 81.09F, 0.0F, 0.0F, 0.0F },
                MinimumHarvestMbf = new float[] { 0.0F, 0.0F, 0.0F, 14.82F, 0.0F, 0.0F, 11.99F, 0.0F, 0.0F, 0.0F }
            };
            float[] minimumTwoThinLiveBiomass = new float[] { 85531F, 146983F, 213170F, 168041F, 226421F, 283782F, 278175F, 329697F, 378050F, 422956F }; // kg/ha

            for (int periodIndex = 0; periodIndex < firstThinPeriod; ++periodIndex)
            {
                Stand? unthinnedStand = twoThinTrajectory.StandByPeriod[periodIndex];
                AssertNullable.IsNotNull(unthinnedStand);
                Assert.IsTrue(unthinnedStand.GetTreeRecordCount() == expectedUnthinnedTreeRecordCount);
            }
            for (int periodIndex = firstThinPeriod; periodIndex < secondThinPeriod; ++periodIndex)
            {
                Stand? thinnedStand = twoThinTrajectory.StandByPeriod[periodIndex];
                AssertNullable.IsNotNull(thinnedStand);
                Assert.IsTrue(thinnedStand.GetTreeRecordCount() == expectedFirstThinTreeRecordCount);
            }
            int expectedSecondThinTreeRecordCount = 263; // must be updated if prescription changes
            for (int periodIndex = secondThinPeriod; periodIndex < twoThinTrajectory.PlanningPeriods; ++periodIndex)
            {
                Stand? thinnedStand = twoThinTrajectory.StandByPeriod[periodIndex];
                AssertNullable.IsNotNull(thinnedStand);
                Assert.IsTrue(thinnedStand.GetTreeRecordCount() == expectedSecondThinTreeRecordCount);
            }

            this.Verify(twoThinTrajectory, twoThinExpected, configuration.Variant.TimeStepInYears);
            this.Verify(twoThinTrajectory, twoThinExpected);

            for (int periodIndex = 0; periodIndex < twoThinTrajectory.PlanningPeriods; ++periodIndex)
            {
                float liveBiomass = twoThinTrajectory.StandByPeriod[periodIndex]!.GetLiveBiomass();
                Assert.IsTrue(liveBiomass > minimumTwoThinLiveBiomass[periodIndex]);
                Assert.IsTrue(liveBiomass < this.biomassTolerance * minimumTwoThinLiveBiomass[periodIndex]);
            }

            // verify three thin trajectory
            ExpectedStandTrajectory threeThinExpected = new()
            {
                FirstThinPeriod = firstThinPeriod,
                SecondThinPeriod = secondThinPeriod,
                ThirdThinPeriod = thirdThinPeriod,
                MinimumTreesSelected = 200,
                MaximumTreesSelected = 485,
                //                                          0       1       2       3       4       5       6       7       8       9
                MinimumQmd = new float[] { 16.84F, 20.97F, 24.63F, 30.23F, 34.49F, 38.11F, 41.92F, 45.34F, 51.91F, 55.19F }, // cm
                //                                                0       1       2       3       4       5       6       7       8       9
                MinimumTopHeight = new float[] { 16.50F, 20.69F, 24.50F, 26.95F, 30.02F, 32.94F, 35.12F, 37.58F, 39.42F, 41.54F }, // m
                // Poudel 2018 + Scribner long log net MBF/ha
                // bilinear interpolation: 1 cm diameter classes, 1 m height classes
                MinimumStandingCubic = new float[] { 65.52F, 152.02F, 250.95F, 225.01F, 321.43F, 415.73F, 415.75F, 500.65F, 477.03F, 552.72F },
                MinimumStandingMbf = new float[] { 9.64F, 18.88F, 30.71F, 27.76F, 41.29F, 53.85F, 55.10F, 70.41F, 70.01F, 84.01F },
                MinimumHarvestCubic = new float[] { 0.0F, 0.0F, 0.0F, 105.89F, 0.0F, 0.0F, 81.09F, 0.0F, 97.91F, 0.0F },
                MinimumHarvestMbf = new float[] { 0.0F, 0.0F, 0.0F, 14.82F, 0.0F, 0.0F, 11.99F, 0.0F, 15.20F, 0.0F }
            };

            for (int periodIndex = 0; periodIndex < firstThinPeriod; ++periodIndex)
            {
                Stand? unthinnedStand = threeThinTrajectory.StandByPeriod[periodIndex];
                AssertNullable.IsNotNull(unthinnedStand);
                Assert.IsTrue(unthinnedStand.GetTreeRecordCount() == expectedUnthinnedTreeRecordCount);
            }
            for (int periodIndex = firstThinPeriod; periodIndex < secondThinPeriod; ++periodIndex)
            {
                Stand? thinnedStand = threeThinTrajectory.StandByPeriod[periodIndex];
                AssertNullable.IsNotNull(thinnedStand);
                Assert.IsTrue(thinnedStand.GetTreeRecordCount() == expectedFirstThinTreeRecordCount);
            }
            for (int periodIndex = secondThinPeriod; periodIndex < thirdThinPeriod; ++periodIndex)
            {
                Stand? thinnedStand = threeThinTrajectory.StandByPeriod[periodIndex];
                AssertNullable.IsNotNull(thinnedStand);
                Assert.IsTrue(thinnedStand.GetTreeRecordCount() == expectedSecondThinTreeRecordCount);
            }
            int expectedThirdThinTreeRecordCount = 180; // must be updated if prescription changes
            for (int periodIndex = thirdThinPeriod; periodIndex < threeThinTrajectory.PlanningPeriods; ++periodIndex)
            {
                Stand? thinnedStand = threeThinTrajectory.StandByPeriod[periodIndex];
                AssertNullable.IsNotNull(thinnedStand);
                Assert.IsTrue(thinnedStand.GetTreeRecordCount() == expectedThirdThinTreeRecordCount);
            }

            this.Verify(threeThinTrajectory, threeThinExpected, configuration.Variant.TimeStepInYears);
            this.Verify(threeThinTrajectory, threeThinExpected);
        }

        [TestMethod]
        public void OrganonStandGrowthApi()
        {
            TestStand.WriteTreeHeader(this.TestContext!);
            OrganonTreatments treatments = new();
            foreach (OrganonVariant variant in TestConstant.Variants)
            {
                OrganonConfiguration configuration = OrganonTest.CreateOrganonConfiguration(variant);

                // check crown competition API
                TestStand stand = OrganonTest.CreateDefaultStand(configuration);
                float crownCompetitionFactor = OrganonStandDensity.GetCrownCompetitionByHeight(variant, stand)[0];
                Assert.IsTrue(crownCompetitionFactor >= 0.0F);
                Assert.IsTrue(crownCompetitionFactor <= TestConstant.Maximum.CrownCompetitionFactor);
                OrganonTest.Verify(ExpectedTreeChanges.NoDiameterOrHeightGrowth | ExpectedTreeChanges.NoDiameterOrHeightGrowth, stand, variant);

                // recalculate heights and crown ratios for all trees
                SortedList<FiaCode, SpeciesCalibration> calibrationBySpecies = configuration.CreateSpeciesCalibration();
                foreach (Trees treesOfSpecies in stand.TreesBySpecies.Values)
                {
                    OrganonGrowth.SetIngrowthHeightAndCrownRatio(variant, stand, treesOfSpecies, treesOfSpecies.Count, calibrationBySpecies);
                }
                OrganonTest.Verify(ExpectedTreeChanges.NoDiameterOrHeightGrowth | ExpectedTreeChanges.NoDiameterOrHeightGrowth, stand, variant);

                // run Organon growth simulation
                stand = OrganonTest.CreateDefaultStand(configuration);
                if (configuration.IsEvenAge)
                {
                    // stand error if less than one year to grow to breast height
                    stand.AgeInYears = stand.BreastHeightAgeInYears + 2;
                }
                stand.SetQuantiles();
                stand.WriteTreesAsCsv(this.TestContext!, variant, 0, false);

                TestStand initialStand = new(stand);
                TreeLifeAndDeath treeGrowth = new();
                for (int simulationStep = 0; simulationStep < TestConstant.Default.SimulationCyclesToRun; ++simulationStep)
                {
                    OrganonGrowth.Grow(simulationStep + 1, configuration, treatments, stand, calibrationBySpecies);
                    treeGrowth.AccumulateGrowthAndMortality(stand);
                    OrganonTest.Verify(ExpectedTreeChanges.DiameterGrowth | ExpectedTreeChanges.HeightGrowth, OrganonWarnings.LessThan50TreeRecords, stand, variant);

                    stand.WriteTreesAsCsv(this.TestContext!, variant, variant.GetEndYear(simulationStep), false);
                }

                OrganonTest.Verify(ExpectedTreeChanges.DiameterGrowth | ExpectedTreeChanges.HeightGrowth, treeGrowth, initialStand, stand);
                OrganonTest.Verify(calibrationBySpecies);
            }
        }

        [TestMethod]
        public void Plot14ImmediateThin()
        {
            int thinPeriod = 1;
            int lastPeriod = 4;

            PlotsWithHeight plot14 = PublicApi.GetPlot14();
            OrganonConfiguration configuration = OrganonTest.CreateOrganonConfiguration(new OrganonVariantNwo());
            OrganonStand stand = plot14.ToOrganonStand(configuration, 30, 130.0F);
            stand.PlantingDensityInTreesPerHectare = TestConstant.Plot14ReplantingDensityInTreesPerHectare;

            OrganonStandTrajectory thinnedTrajectory = new(stand, configuration, TreeVolume.Default, lastPeriod);
            thinnedTrajectory.Treatments.Harvests.Add(new ThinByPrescription(thinPeriod)
            {
                FromAbovePercentage = 0.0F,
                ProportionalPercentage = 30.0F,
                FromBelowPercentage = 0.0F
            });
            AssertNullable.IsNotNull(thinnedTrajectory.StandByPeriod[0]);
            Assert.IsTrue(thinnedTrajectory.StandByPeriod[0]!.GetTreeRecordCount() == 222);
            long immediateThinTimeSteps = thinnedTrajectory.Simulate();
            Assert.IsTrue(immediateThinTimeSteps == lastPeriod);

            // verify thinned trajectory
            // find/replace regular expressions: function call \w+\.\w+\[\d+\]\.\w+\(\)\s+(\d+.\d{1,2})\d*\s+float\r?\n
            //                                                 \s+\w+\.\w+\(\d+\)\s+(\d+.\d{1,2})\d*\s+float\r?\n
            //                                   array element \[\d+\]\s+(\d+.\d{1,3})\d*\s+float\r?\n -> $1F, 
            ExpectedStandTrajectory immediateThinExpected = new()
            {
                FirstThinPeriod = thinPeriod,
                MinimumTreesSelected = 65,
                MaximumTreesSelected = 70,
                //                                 0       1       2       3       4     
                MinimumQmd = new float[] { 23.33F, 26.88F, 30.17F, 33.13F, 35.93F }, // cm
                //                                       0       1       2       3       4     
                MinimumTopHeight = new float[] { 28.32F, 30.81F, 33.54F, 36.14F, 38.54F }, // m
                // Poudel 2018 + Scribner long log net MBF/ha
                // bilinear interpolation: 1 cm diameter classes, 1 m height classes
                MinimumStandingCubic = new float[] { 356.30F, 356.29F, 468.33F, 566.24F, 652.70F },
                MinimumStandingMbf = new float[] { 50.62F, 51.01F, 66.46F, 80.78F, 95.77F },
                MinimumHarvestCubic = new float[] { 0.0F, 103.05F, 0.0F, 0.0F, 0.0F },
                MinimumHarvestMbf = new float[] { 0.0F, 15.18F, 0.0F, 0.0F, 0.0F }
            };

            this.Verify(thinnedTrajectory, immediateThinExpected, configuration.Variant.TimeStepInYears);
            this.Verify(thinnedTrajectory, immediateThinExpected);
            Assert.IsTrue(thinnedTrajectory.GetFirstThinAge() == 30);
            Assert.IsTrue(thinnedTrajectory.StandByPeriod[^1]!.GetTreeRecordCount() == 156);

            // verify snag and log calculations
            SnagLogTable snagsAndLogs = new(thinnedTrajectory, Constant.Bucking.DefaultMaximumDiameterInCentimeters, Constant.Bucking.DiameterClassSizeInCentimeters);
            PublicApi.Verify(snagsAndLogs, thinnedTrajectory);
        }

        [TestMethod]
        public void RS39()
        {
            string plotFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OSU", "Organon", "RS39 lower half.xlsx");
            PspStand rs39 = new(plotFilePath, "RS39 lower half", 0.154441F);
            OrganonVariant variant = new OrganonVariantNwo();
            OrganonConfiguration configuration = new(variant);
            TestStand stand = rs39.ToStand(configuration, 105.0F);
            int startYear = 1992;
            stand.WriteCompetitionAsCsv("RS39 lower half initial competition.csv", variant, startYear);
            OrganonTest.GrowPspStand(rs39, stand, variant, startYear, 2019, Path.GetFileNameWithoutExtension(plotFilePath));

            TreeQuantiles measuredQuantiles = new(stand, rs39, startYear);
            using StreamWriter quantileWriter = measuredQuantiles.WriteToCsv("RS39 lower half measured quantiles.csv", variant, startYear);
            foreach (int measurementYear in rs39.MeasurementYears)
            {
                if (measurementYear != startYear)
                {
                    measuredQuantiles = new TreeQuantiles(stand, rs39, measurementYear);
                    measuredQuantiles.WriteToCsv(quantileWriter, variant, measurementYear);
                }
            }
        }

        // basic growth model performance benchmark: 3 runs of hero with 300 trees
        [TestMethod]
        public void StandGrowthPerformance()
        {
            int thinningPeriod = 4;
            int runs = 4; // 1 warmup run + measured runs
            int trees = 300;
            #if DEBUG
            runs = 1; // do only functional validation of test on DEBUG builds to reduce test execution time
            trees = 10;
            #endif

            List<float> discountRates = new() {  Constant.Financial.DefaultAnnualDiscountRate };
            PlotsWithHeight nelder = PublicApi.GetNelder();
            OrganonConfiguration configuration = PublicApi.CreateOrganonConfiguration(new OrganonVariantNwo());
            OrganonStand stand = nelder.ToOrganonStand(configuration, 20, 130.0F, trees);
            stand.PlantingDensityInTreesPerHectare = TestConstant.NelderReplantingDensityInTreesPerHectare;

            RunParameters landExpectationValue = new(new List<int>() { 9 }, configuration)
            {
                MaximizeForPlanningPeriod = 9
            };
            landExpectationValue.Treatments.Harvests.Add(new ThinByIndividualTreeSelection(thinningPeriod));
            HeuristicResults<HeuristicParameters> results = PublicApi.CreateResults(new(), landExpectationValue.MaximizeForPlanningPeriod);
            HeuristicResultPosition defaultPosition = PublicApi.CreateDefaultSolutionPosition();

            TimeSpan runtime = TimeSpan.Zero;
            for (int run = 0; run < runs; ++run)
            {
                // after warmup: 3 runs * 300 trees = 900 measured growth simulations on i7-3770 (4th gen, Sandy Bridge)
                // dispersion of 5 runs                   min   mean  median  max
                // .NET 5.0 with removed tree compaction  1.67  1.72  1.72    1.82
                Hero hero = new(stand, results.ParameterCombinations[0], landExpectationValue)
                {
                    IsStochastic = false,
                    MaximumIterations = 2
                };
                if (run > 0)
                {
                    // skip first run as a warmup run
                    HeuristicPerformanceCounters heroCounters = hero.Run(defaultPosition, results);
                    runtime += heroCounters.Duration;
                }
            }
            this.TestContext!.WriteLine(runtime.TotalSeconds.ToString());
        }

        private static void TryAppendAcceptedFinancialValue(List<float> objectives, Heuristic heuristic, int moveIndex)
        {
            HeuristicResultPosition position = PublicApi.CreateDefaultSolutionPosition();
            IList<float> acceptedMoveFinancialValues = heuristic.FinancialValue.GetAcceptedValuesWithDefaulting(position);
            if (acceptedMoveFinancialValues.Count > moveIndex)
            {
                float acceptedFinancialValue = acceptedMoveFinancialValues[moveIndex];
                objectives.Add(acceptedFinancialValue);
                Assert.IsTrue(acceptedFinancialValue >= heuristic.FinancialValue.GetCandidateValuesWithDefaulting(position)[moveIndex]);
            }
        }

        private void Verify(GeneticAlgorithm genetic, HeuristicPerformanceCounters perfCounters)
        {
            this.Verify((Heuristic<GeneticParameters>)genetic, perfCounters);

            PopulationStatistics statistics = genetic.PopulationStatistics;
            Assert.IsTrue(statistics.Generations <= genetic.HeuristicParameters.MaximumGenerations);
            Assert.IsTrue(statistics.CoefficientOfVarianceByGeneration.Count == statistics.Generations);
            Assert.IsTrue(statistics.MeanAllelesPerLocusByGeneration.Count == statistics.Generations);
            Assert.IsTrue(statistics.MeanHeterozygosityByGeneration.Count == statistics.Generations);
            Assert.IsTrue(statistics.NewIndividualsByGeneration.Count == statistics.Generations);
            Assert.IsTrue(statistics.PolymorphismByGeneration.Count == statistics.Generations);
            for (int generationIndex = 0; generationIndex < statistics.Generations; ++generationIndex)
            {
                float coefficientOfVariation = statistics.CoefficientOfVarianceByGeneration[generationIndex];
                float meanAllelesPerLocus = statistics.MeanAllelesPerLocusByGeneration[generationIndex];
                float meanHeterozygosity = statistics.MeanHeterozygosityByGeneration[generationIndex];
                int newIndividuals = statistics.NewIndividualsByGeneration[generationIndex];
                float polymorphism = statistics.PolymorphismByGeneration[generationIndex];

                Assert.IsTrue(coefficientOfVariation >= 0.0F);
                Assert.IsTrue((meanAllelesPerLocus >= 0.0F) && (meanAllelesPerLocus <= 2.0F)); // assumes HarvestPeriodSelection.All
                Assert.IsTrue((meanHeterozygosity >= 0.0F) && (meanHeterozygosity <= 1.0F));
                Assert.IsTrue((newIndividuals >= 0) && (newIndividuals <= 2 * genetic.HeuristicParameters.PopulationSize)); // two children per breeding
                Assert.IsTrue((polymorphism >= 0.0F) && (polymorphism <= 1.0F));
            }
        }

        private void Verify<TParameters>(Heuristic<TParameters> heuristic, HeuristicPerformanceCounters perfCounters) where TParameters : HeuristicParameters
        {
            // check objective functions
            HeuristicResultPosition position = PublicApi.CreateDefaultSolutionPosition();
            OrganonStandTrajectory bestTrajectory = heuristic.GetBestTrajectoryWithDefaulting(position);
            Assert.IsTrue(bestTrajectory.PlantingDensityInTreesPerHectare == heuristic.CurrentTrajectory.PlantingDensityInTreesPerHectare);
            Assert.IsTrue(bestTrajectory.PlantingDensityInTreesPerHectare >= TestConstant.NelderReplantingDensityInTreesPerHectare);

            float recalculatedHighestFinancialValue = heuristic.GetFinancialValue(bestTrajectory, Constant.HeuristicDefault.FinancialIndex);
            float highestFinancialValue = heuristic.FinancialValue.GetHighestValue();
            float highestFinancialValueRatio = highestFinancialValue / recalculatedHighestFinancialValue;
            this.TestContext!.WriteLine("{0} best objective: {1} with ratio {2}", heuristic.GetName(), highestFinancialValue, highestFinancialValueRatio);
            if (heuristic.RunParameters.TimberObjective == TimberObjective.LandExpectationValue)
            {
                Assert.IsTrue(highestFinancialValue > -0.70F);
            }
            else
            {
                Assert.IsTrue(highestFinancialValue > 0.0F);
            }

            IList<float> candidateMoveFinancialValues = heuristic.FinancialValue.GetCandidateValuesWithDefaulting(position);
            float beginFinancialValue = candidateMoveFinancialValues[0];
            Assert.IsTrue(highestFinancialValue >= beginFinancialValue);

            Assert.IsTrue(heuristic.FinancialValue.GetAcceptedValuesWithDefaulting(position).Count >= 3);
            Assert.IsTrue(highestFinancialValueRatio > 0.99999);
            Assert.IsTrue(highestFinancialValueRatio < 1.00001);

            float recalculatedCurrentObjectiveFunction = heuristic.GetFinancialValue(heuristic.CurrentTrajectory, Constant.HeuristicDefault.FinancialIndex);
            Assert.IsTrue(recalculatedCurrentObjectiveFunction <= highestFinancialValue);

            // only guaranteed for monotonic heuristics: hero, prescription enumeration, others depending on configuration
            IList<float> acceptedMoveFinancialValues = heuristic.FinancialValue.GetAcceptedValuesWithDefaulting(position);
            if ((heuristic is Hero) || (heuristic is PrescriptionEnumeration) || (heuristic is ThresholdAccepting))
            {
                Assert.IsTrue(highestFinancialValue == acceptedMoveFinancialValues[^1]);
                Assert.IsTrue(recalculatedCurrentObjectiveFunction >= beginFinancialValue);
            }
            else
            {
                Assert.IsTrue(highestFinancialValue > 0.95F * acceptedMoveFinancialValues[^1]);
                if (heuristic is AutocorrelatedWalk)
                {
                    Assert.IsTrue(recalculatedCurrentObjectiveFunction > 0.50F * beginFinancialValue);
                }
                else if (heuristic is SimulatedAnnealing)
                {
                    Assert.IsTrue(recalculatedCurrentObjectiveFunction > 0.72F * beginFinancialValue);
                }
                else
                {
                    Assert.IsTrue(recalculatedCurrentObjectiveFunction > 0.85F * beginFinancialValue);
                }
            }

            float endCandidateFinancialValue = candidateMoveFinancialValues[^1];
            Assert.IsTrue(endCandidateFinancialValue <= highestFinancialValue);

            // check harvest schedule
            int firstThinningPeriod = bestTrajectory.Treatments.GetThinningPeriods()[0];
            foreach (KeyValuePair<FiaCode, TreeSelection> selectionForSpecies in bestTrajectory.IndividualTreeSelectionBySpecies)
            {
                TreeSelection bestTreeSelection = selectionForSpecies.Value;
                TreeSelection currentTreeSelection = heuristic.CurrentTrajectory.IndividualTreeSelectionBySpecies[selectionForSpecies.Key];
                Assert.IsTrue(bestTreeSelection.Capacity == currentTreeSelection.Capacity);
                Assert.IsTrue(bestTreeSelection.Count == currentTreeSelection.Count);
                for (int treeIndex = 0; treeIndex < bestTreeSelection.Count; ++treeIndex)
                {
                    Assert.IsTrue((bestTreeSelection[treeIndex] == Constant.NoHarvestPeriod) || (bestTreeSelection[treeIndex] == firstThinningPeriod));
                    Assert.IsTrue((currentTreeSelection[treeIndex] == Constant.NoHarvestPeriod) || (currentTreeSelection[treeIndex] == firstThinningPeriod));
                }
            }

            // check volumes
            Assert.IsTrue(firstThinningPeriod != Constant.NoHarvestPeriod);
            bestTrajectory.GetMerchantableVolumes(out StandMerchantableVolume bestStandingVolume, out StandMerchantableVolume bestHarvestedVolume);
            heuristic.CurrentTrajectory.GetMerchantableVolumes(out StandMerchantableVolume currentStandingVolume, out StandMerchantableVolume currentHarvestedVolume);
            float previousBestCubicStandingVolume = Single.NaN;
            float previousCurrentCubicStandingVolume = Single.NaN;
            for (int periodIndex = 0; periodIndex < bestTrajectory.PlanningPeriods; ++periodIndex)
            {
                float bestCubicStandingVolume = bestStandingVolume.GetCubicTotal(periodIndex);
                float bestCubicThinningVolume = bestHarvestedVolume.GetCubicTotal(periodIndex);
                float currentCubicStandingVolume = currentStandingVolume.GetCubicTotal(periodIndex);
                float currentCubicThinningVolume = currentHarvestedVolume.GetCubicTotal(periodIndex);

                float bestCubicStandingCheckVolume = bestStandingVolume.Cubic2Saw[periodIndex] + bestStandingVolume.Cubic3Saw[periodIndex] + bestStandingVolume.Cubic4Saw[periodIndex];
                float bestCubicThinningCheckVolume = bestHarvestedVolume.Cubic2Saw[periodIndex] + bestHarvestedVolume.Cubic3Saw[periodIndex] + bestHarvestedVolume.Cubic4Saw[periodIndex];
                float currentCubicStandingCheckVolume = currentStandingVolume.Cubic2Saw[periodIndex] + currentStandingVolume.Cubic3Saw[periodIndex] + currentStandingVolume.Cubic4Saw[periodIndex];
                float currentCubicThinningCheckVolume = currentHarvestedVolume.Cubic2Saw[periodIndex] + currentHarvestedVolume.Cubic3Saw[periodIndex] + currentHarvestedVolume.Cubic4Saw[periodIndex];
                Assert.IsTrue(MathF.Abs(bestCubicStandingVolume - bestCubicStandingCheckVolume) < 0.000001F);
                Assert.IsTrue(MathF.Abs(bestCubicThinningVolume - bestCubicThinningCheckVolume) < 0.000001F);
                Assert.IsTrue(MathF.Abs(currentCubicStandingVolume - currentCubicStandingCheckVolume) < 0.000001F);
                Assert.IsTrue(MathF.Abs(currentCubicThinningVolume - currentCubicThinningCheckVolume) < 0.000001F);

                float bestThinNpv = FinancialScenarios.Default.GetNetPresentThinningValue(bestTrajectory, Constant.HeuristicDefault.FinancialIndex, periodIndex, out float bestThin2SawNpv, out float bestThin3SawNpv, out float bestThin4SawNpv);
                float currentThinNpv = FinancialScenarios.Default.GetNetPresentThinningValue(bestTrajectory, Constant.HeuristicDefault.FinancialIndex, periodIndex, out float currentThin2SawNpv, out float currentThin3SawNpv, out float currentThin4SawNpv);
                if (periodIndex == firstThinningPeriod)
                {
                    Assert.IsTrue(bestTrajectory.BasalAreaRemoved[periodIndex] >= 0.0F); // best selection with debug stand is no harvest
                    Assert.IsTrue(bestTrajectory.BasalAreaRemoved[periodIndex] <= 200.0F);

                    Assert.IsTrue(bestTrajectory.GetTotalScribnerVolumeThinned(periodIndex) >= 0.0F);
                    Assert.IsTrue(bestTrajectory.GetTotalScribnerVolumeThinned(periodIndex) < bestTrajectory.GetTotalStandingScribnerVolume(periodIndex - 1) + 0.000001F); // allow for numerical error in case where all trees are harvested
                    Assert.IsTrue(bestHarvestedVolume.Scribner2Saw[periodIndex] >= 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Scribner3Saw[periodIndex] >= 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Scribner4Saw[periodIndex] >= 0.0F);

                    Assert.IsTrue(bestCubicThinningVolume <= previousBestCubicStandingVolume);
                    Assert.IsTrue(bestHarvestedVolume.Cubic2Saw[periodIndex] >= 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Cubic3Saw[periodIndex] >= 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Cubic4Saw[periodIndex] >= 0.0F);

                    Assert.IsTrue(bestThin2SawNpv >= 0.0F);
                    Assert.IsTrue(bestThin3SawNpv >= 0.0F);
                    Assert.IsTrue(bestThin4SawNpv >= 0.0F);

                    Assert.IsTrue(heuristic.CurrentTrajectory.BasalAreaRemoved[periodIndex] >= 0.0F);
                    Assert.IsTrue(heuristic.CurrentTrajectory.BasalAreaRemoved[periodIndex] <= 200.0F);
                    Assert.IsTrue(heuristic.CurrentTrajectory.GetTotalScribnerVolumeThinned(periodIndex) >= 0.0F);
                    Assert.IsTrue(heuristic.CurrentTrajectory.GetTotalScribnerVolumeThinned(periodIndex) < heuristic.CurrentTrajectory.GetTotalStandingScribnerVolume(periodIndex - 1) + 0.000001F); // numerical error

                    Assert.IsTrue(currentCubicThinningVolume >= 0.0F);
                    Assert.IsTrue(currentCubicThinningVolume <= previousCurrentCubicStandingVolume);

                    Assert.IsTrue(currentThin2SawNpv >= 0.0F);
                    Assert.IsTrue(currentThin3SawNpv >= 0.0F);
                    Assert.IsTrue(currentThin4SawNpv >= 0.0F);
                }
                else
                {
                    // for now, harvest should occur only in the one indicated period
                    Assert.IsTrue(bestTrajectory.BasalAreaRemoved[periodIndex] == 0.0F);
                    Assert.IsTrue(bestTrajectory.GetTotalScribnerVolumeThinned(periodIndex) == 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Scribner2Saw[periodIndex] == 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Scribner3Saw[periodIndex] == 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Scribner4Saw[periodIndex] == 0.0F);

                    Assert.IsTrue(bestCubicThinningVolume == 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Cubic2Saw[periodIndex] == 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Cubic3Saw[periodIndex] == 0.0F);
                    Assert.IsTrue(bestHarvestedVolume.Cubic4Saw[periodIndex] == 0.0F);

                    Assert.IsTrue(bestThin2SawNpv == 0.0F);
                    Assert.IsTrue(bestThin3SawNpv == 0.0F);
                    Assert.IsTrue(bestThin4SawNpv == 0.0F);

                    Assert.IsTrue(heuristic.CurrentTrajectory.GetTotalScribnerVolumeThinned(periodIndex) == 0.0F);
                    Assert.IsTrue(currentHarvestedVolume.Scribner2Saw[periodIndex] == 0.0F);
                    Assert.IsTrue(currentHarvestedVolume.Scribner3Saw[periodIndex] == 0.0F);
                    Assert.IsTrue(currentHarvestedVolume.Scribner4Saw[periodIndex] == 0.0F);

                    Assert.IsTrue(heuristic.CurrentTrajectory.BasalAreaRemoved[periodIndex] == 0.0F);
                    Assert.IsTrue(currentCubicThinningVolume == 0.0F);
                    Assert.IsTrue(currentHarvestedVolume.Cubic2Saw[periodIndex] == 0.0F);
                    Assert.IsTrue(currentHarvestedVolume.Cubic3Saw[periodIndex] == 0.0F);
                    Assert.IsTrue(currentHarvestedVolume.Cubic4Saw[periodIndex] == 0.0F);

                    Assert.IsTrue(currentThin2SawNpv == 0.0F);
                    Assert.IsTrue(currentThin3SawNpv == 0.0F);
                    Assert.IsTrue(currentThin4SawNpv == 0.0F);
                }

                if (periodIndex == 0)
                {
                    // zero merchantable on Nelder 1 at age 20 with Poudel 2018 net volume
                    Assert.IsTrue(bestCubicStandingVolume >= 0.0F);
                    Assert.IsTrue(bestTrajectory.GetTotalStandingScribnerVolume(periodIndex) >= 0.0F);
                    Assert.IsTrue(currentCubicStandingVolume >= 0.0F);
                    Assert.IsTrue(heuristic.CurrentTrajectory.GetTotalStandingScribnerVolume(periodIndex) >= 0.0F);
                }
                else
                {
                    Assert.IsTrue(bestCubicStandingVolume > 0.0F);
                    Assert.IsTrue(bestTrajectory.GetTotalStandingScribnerVolume(periodIndex) > 0.0F);
                    Assert.IsTrue(currentCubicStandingVolume > 0.0F);
                    Assert.IsTrue(heuristic.CurrentTrajectory.GetTotalStandingScribnerVolume(periodIndex) > 0.0F);

                    if (periodIndex != firstThinningPeriod)
                    {
                        // for now, assume monotonic increase in standing volumes except in harvest periods
                        Assert.IsTrue(bestCubicStandingVolume > previousBestCubicStandingVolume);
                        Assert.IsTrue(bestTrajectory.GetTotalStandingScribnerVolume(periodIndex) > bestTrajectory.GetTotalStandingScribnerVolume(periodIndex - 1));
                        Assert.IsTrue(currentCubicStandingVolume > previousCurrentCubicStandingVolume);
                        Assert.IsTrue(heuristic.CurrentTrajectory.GetTotalStandingScribnerVolume(periodIndex) > heuristic.CurrentTrajectory.GetTotalStandingScribnerVolume(periodIndex - 1));
                    }
                }

                previousBestCubicStandingVolume = bestCubicStandingVolume;
                previousCurrentCubicStandingVolume = currentCubicStandingVolume;
            }

            // check moves
            Assert.IsTrue(acceptedMoveFinancialValues.Count == candidateMoveFinancialValues.Count);
            Assert.IsTrue(acceptedMoveFinancialValues[0] == candidateMoveFinancialValues[0]);
            for (int moveIndex = 1; moveIndex < acceptedMoveFinancialValues.Count; ++moveIndex)
            {
                float acceptedFinancialValue = acceptedMoveFinancialValues[moveIndex];
                Assert.IsTrue(acceptedFinancialValue <= highestFinancialValue);
                // heuristics capable of reheating and tabu search do not have monotonically increasing accepted move values
                if ((heuristic is GeneticAlgorithm) || (heuristic is Hero))
                {
                    Assert.IsTrue(acceptedMoveFinancialValues[moveIndex - 1] <= acceptedFinancialValue);
                }
                Assert.IsTrue(candidateMoveFinancialValues[moveIndex] <= acceptedFinancialValue);
            }

            IHeuristicMoveLog? moveLog = heuristic.GetMoveLog();
            if (moveLog != null)
            {
                string moveCsvHeader = moveLog.GetCsvHeader("prefix");
                Assert.IsTrue(String.IsNullOrWhiteSpace(moveCsvHeader) == false);

                for (int moveIndex = 0; moveIndex < acceptedMoveFinancialValues.Count; ++moveIndex)
                {
                    string moveCsvValues = moveLog.GetCsvValues(position, moveIndex);
                    Assert.IsTrue(String.IsNullOrWhiteSpace(moveCsvValues) == false);
                }
            }

            // check parameters
            HeuristicParameters parameters = heuristic.GetParameters();
            string parameterCsvHeader = parameters.GetCsvHeader();
            string parameterCsvValues = parameters.GetCsvValues();

            Assert.IsTrue(String.IsNullOrWhiteSpace(parameterCsvHeader) == false);
            Assert.IsTrue(String.IsNullOrWhiteSpace(parameterCsvValues) == false);

            // check performance counters
            Assert.IsTrue(perfCounters.Duration > TimeSpan.Zero);
            Assert.IsTrue(perfCounters.GrowthModelTimesteps > 0);
            Assert.IsTrue(perfCounters.MovesAccepted >= 0); // random guessing may or may not yield improving or disimproving moves
            Assert.IsTrue(perfCounters.MovesRejected >= 0);
            Assert.IsTrue((perfCounters.MovesAccepted + perfCounters.MovesRejected) >= 0);
            
            int treeRecordCount = bestTrajectory.GetInitialTreeRecordCount();
            if (heuristic is GeneticAlgorithm genetic)
            {
                Assert.IsTrue(perfCounters.TreesRandomizedInConstruction > 0.4F * genetic.HeuristicParameters.PopulationSize * treeRecordCount);
                Assert.IsTrue(perfCounters.TreesRandomizedInConstruction < 0.6F * genetic.HeuristicParameters.PopulationSize * treeRecordCount);
            }
            else if (heuristic is PrescriptionEnumeration)
            {
                Assert.IsTrue(perfCounters.TreesRandomizedInConstruction == 0);
            }
            else
            {
                Assert.IsTrue(perfCounters.TreesRandomizedInConstruction >= 0);
                Assert.IsTrue(perfCounters.TreesRandomizedInConstruction <= treeRecordCount);
            }
        }

        private void Verify(OrganonStandTrajectory thinnedTrajectory, ExpectedStandTrajectory expectedTrajectory)
        {
            for (int periodIndex = 0; periodIndex < thinnedTrajectory.PlanningPeriods; ++periodIndex)
            {
                if ((periodIndex == expectedTrajectory.FirstThinPeriod) || 
                    ((expectedTrajectory.SecondThinPeriod != Constant.NoThinPeriod) && (periodIndex == expectedTrajectory.SecondThinPeriod)) ||
                    ((expectedTrajectory.ThirdThinPeriod != Constant.NoThinPeriod) && (periodIndex == expectedTrajectory.ThirdThinPeriod)))
                {
                    Assert.IsTrue(thinnedTrajectory.BasalAreaRemoved[periodIndex] > 0.0F);
                    Assert.IsTrue(thinnedTrajectory.BasalAreaRemoved[periodIndex] < thinnedTrajectory.DensityByPeriod[periodIndex - 1].BasalAreaPerAcre); // assume <50% thin by volume
                    Assert.IsTrue(thinnedTrajectory.GetTotalScribnerVolumeThinned(periodIndex) >= expectedTrajectory.MinimumHarvestMbf[periodIndex]);
                    Assert.IsTrue(thinnedTrajectory.GetTotalScribnerVolumeThinned(periodIndex) <= this.volumeTolerance * expectedTrajectory.MinimumHarvestMbf[periodIndex]);
                }
                else
                {
                    Assert.IsTrue(thinnedTrajectory.BasalAreaRemoved[periodIndex] == 0.0F);
                    Assert.IsTrue(thinnedTrajectory.GetTotalScribnerVolumeThinned(periodIndex) == 0.0F);
                }
            }
        }

        private void Verify(OrganonStandTrajectory trajectory, ExpectedStandTrajectory expectedTrajectory, int timeStepInYears)
        {
            Assert.IsTrue(trajectory.BasalAreaRemoved.Length == expectedTrajectory.Length);
            Assert.IsTrue(trajectory.BasalAreaRemoved[0] == 0.0F);
            Assert.IsTrue(trajectory.GetTotalScribnerVolumeThinned(0) == 0.0F);
            foreach (TreeSpeciesMerchantableVolume thinVolumeForSpecies in trajectory.ThinningVolumeBySpecies.Values)
            {
                Assert.IsTrue(thinVolumeForSpecies.Scribner2Saw.Length == expectedTrajectory.Length);
                Assert.IsTrue(thinVolumeForSpecies.Scribner3Saw.Length == expectedTrajectory.Length);
                Assert.IsTrue(thinVolumeForSpecies.Scribner4Saw.Length == expectedTrajectory.Length);
            }
            Assert.IsTrue(String.IsNullOrEmpty(trajectory.Name) == false);
            Assert.IsTrue(trajectory.PeriodLengthInYears == timeStepInYears);
            Assert.IsTrue(trajectory.PlanningPeriods == expectedTrajectory.Length); // BUGBUG: clean off by one semantic

            Assert.IsTrue(trajectory.GetFirstThinPeriod() == expectedTrajectory.FirstThinPeriod);
            Assert.IsTrue(trajectory.GetSecondThinPeriod() == expectedTrajectory.SecondThinPeriod);

            IList<int> thinningPeriods = trajectory.Treatments.GetHarvestPeriods();
            Assert.IsTrue(thinningPeriods[^1] == Constant.NoHarvestPeriod);
            if (expectedTrajectory.FirstThinPeriod != Constant.NoThinPeriod)
            {
                Assert.IsTrue(thinningPeriods[0] == expectedTrajectory.FirstThinPeriod);
                if (expectedTrajectory.ThirdThinPeriod != Constant.NoThinPeriod)
                {
                    Assert.IsTrue(thinningPeriods[1] == expectedTrajectory.SecondThinPeriod);
                    Assert.IsTrue(thinningPeriods[2] == expectedTrajectory.ThirdThinPeriod);
                    Assert.IsTrue(thinningPeriods.Count == 4);
                }
                else if (expectedTrajectory.SecondThinPeriod != Constant.NoThinPeriod)
                {
                    Assert.IsTrue(thinningPeriods[1] == expectedTrajectory.SecondThinPeriod);
                    Assert.IsTrue(thinningPeriods.Count == 3);
                }
                else
                {
                    Assert.IsTrue(thinningPeriods.Count == 2);
                }
            }
            else
            {
                Assert.IsTrue(thinningPeriods.Count == 1);
            }

            PublicApi.Verify(trajectory.IndividualTreeSelectionBySpecies, expectedTrajectory);

            for (int periodIndex = 0; periodIndex < trajectory.PlanningPeriods; ++periodIndex)
            {
                Assert.IsTrue(trajectory.DensityByPeriod[periodIndex].BasalAreaPerAcre > 0.0F);
                Assert.IsTrue(trajectory.DensityByPeriod[periodIndex].BasalAreaPerAcre <= TestConstant.Maximum.TreeBasalAreaLarger);

                Assert.IsTrue(trajectory.GetTotalStandingCubicVolume(periodIndex) > expectedTrajectory.MinimumStandingCubic[periodIndex]);
                Assert.IsTrue(trajectory.GetTotalStandingCubicVolume(periodIndex) < this.volumeTolerance * expectedTrajectory.MinimumStandingCubic[periodIndex]);
                Assert.IsTrue(trajectory.GetTotalCubicVolumeThinned(periodIndex) >= expectedTrajectory.MinimumHarvestCubic[periodIndex]);
                Assert.IsTrue(trajectory.GetTotalCubicVolumeThinned(periodIndex) <= this.volumeTolerance * expectedTrajectory.MinimumHarvestCubic[periodIndex]);

                Assert.IsTrue(trajectory.GetTotalStandingScribnerVolume(periodIndex) > expectedTrajectory.MinimumStandingMbf[periodIndex]);
                Assert.IsTrue(trajectory.GetTotalStandingScribnerVolume(periodIndex) < this.volumeTolerance * expectedTrajectory.MinimumStandingMbf[periodIndex]);
                Assert.IsTrue(trajectory.GetTotalScribnerVolumeThinned(periodIndex) >= expectedTrajectory.MinimumHarvestMbf[periodIndex]);
                Assert.IsTrue(trajectory.GetTotalScribnerVolumeThinned(periodIndex) <= this.volumeTolerance * expectedTrajectory.MinimumHarvestMbf[periodIndex]);

                OrganonStand stand = trajectory.StandByPeriod[periodIndex] ?? throw new NotSupportedException("Stand information missing for period " + periodIndex + ".");
                float qmdInCm = stand.GetQuadraticMeanDiameterInCentimeters();
                float topHeight = stand.GetTopHeightInMeters();
                int treeRecords = stand.GetTreeRecordCount();

                Assert.IsTrue((stand.Name != null) && (trajectory.Name != null) && stand.Name.StartsWith(trajectory.Name));
                Assert.IsTrue(qmdInCm > expectedTrajectory.MinimumQmd[periodIndex]);
                Assert.IsTrue(qmdInCm < this.qmdTolerance * expectedTrajectory.MinimumQmd[periodIndex]);
                Assert.IsTrue(topHeight > expectedTrajectory.MinimumTopHeight[periodIndex]);
                Assert.IsTrue(topHeight < this.topHeightTolerance * expectedTrajectory.MinimumTopHeight[periodIndex]);
                Assert.IsTrue(treeRecords > 0);
                Assert.IsTrue(treeRecords < 666);

                // TODO: check qmd against QMD from basal area
            }
        }

        private static HeuristicSolutionPool Verify(HeuristicResults results, HeuristicResultPosition position, int endOfRotationPeriod)
        {
            Assert.IsTrue(results.TryGetSelfOrFindNearestNeighbor(position, out HeuristicSolutionPool? eliteSolutions, out HeuristicResultPosition? eliteSolutionsPosition));
            Assert.IsTrue(eliteSolutions!.High != null);
            Assert.IsTrue(eliteSolutions.Low != null);
            Assert.IsTrue(eliteSolutions.SolutionsInPool == Math.Min(eliteSolutions.SolutionsAccepted, TestConstant.SolutionPoolSize));
            Assert.IsTrue(position == eliteSolutionsPosition!);

            for (int solutionIndex = 0; solutionIndex < eliteSolutions.SolutionsInPool; ++solutionIndex)
            {
                // check distance matrix: should be symmetric with a zero diagonal
                Assert.IsTrue(eliteSolutions.DistanceMatrix[solutionIndex, solutionIndex] == 0);
                for (int neighborIndex = solutionIndex + 1; neighborIndex < eliteSolutions.SolutionsInPool; ++neighborIndex)
                {
                    int distance1 = eliteSolutions.DistanceMatrix[solutionIndex, neighborIndex];
                    int distance2 = eliteSolutions.DistanceMatrix[neighborIndex, solutionIndex];
                    Assert.IsTrue(distance1 == distance2);
                }

                // check neighbors
                int nearestNeighborIndex = eliteSolutions.NearestNeighborIndex[solutionIndex];
                if (nearestNeighborIndex != SolutionPool.UnknownNeighbor)
                {
                    int nearestNeighborDistance = eliteSolutions.DistanceMatrix[solutionIndex, nearestNeighborIndex];

                    Assert.IsTrue(nearestNeighborIndex != solutionIndex);
                    Assert.IsTrue((nearestNeighborDistance > 0) || (nearestNeighborDistance == SolutionPool.UnknownDistance));
                }
            }

            OrganonStandTrajectory eliteSolution = eliteSolutions.GetEliteSolution(position);
            Assert.IsTrue(eliteSolution.EarliestPeriodChangedSinceLastSimulation == endOfRotationPeriod + 1);

            return eliteSolutions;
        }

        private static void Verify(SnagLogTable snagsAndLogs, OrganonStandTrajectory trajectory)
        {
            Assert.IsTrue(snagsAndLogs.DiameterClasses == 121);
            Assert.IsTrue(snagsAndLogs.DiameterClassSizeInCentimeters == Constant.Bucking.DiameterClassSizeInCentimeters);
            Assert.IsTrue(snagsAndLogs.MaximumDiameterInCentimeters == Constant.Bucking.DefaultMaximumDiameterInCentimeters);
            Assert.IsTrue(snagsAndLogs.Periods == trajectory.PlanningPeriods);

            Assert.IsTrue(snagsAndLogs.LogQmdInCentimetersByPeriod.Length == snagsAndLogs.Periods);
            Assert.IsTrue(snagsAndLogs.LogsPerHectareByPeriod.Length == snagsAndLogs.Periods);
            Assert.IsTrue(snagsAndLogs.LogsPerHectareBySpeciesAndDiameterClass.Count == 1);
            Assert.IsTrue(snagsAndLogs.LogsPerHectareBySpeciesAndDiameterClass[FiaCode.PseudotsugaMenziesii].GetLength(0) == snagsAndLogs.Periods);
            Assert.IsTrue(snagsAndLogs.LogsPerHectareBySpeciesAndDiameterClass[FiaCode.PseudotsugaMenziesii].GetLength(1) == snagsAndLogs.DiameterClasses);

            Assert.IsTrue(snagsAndLogs.SnagQmdInCentimetersByPeriod.Length == snagsAndLogs.Periods);
            Assert.IsTrue(snagsAndLogs.SnagsPerHectareByPeriod.Length == snagsAndLogs.Periods);
            Assert.IsTrue(snagsAndLogs.SnagsPerHectareBySpeciesAndDiameterClass.Count == 1);
            Assert.IsTrue(snagsAndLogs.SnagsPerHectareBySpeciesAndDiameterClass[FiaCode.PseudotsugaMenziesii].GetLength(0) == snagsAndLogs.Periods);
            Assert.IsTrue(snagsAndLogs.SnagsPerHectareBySpeciesAndDiameterClass[FiaCode.PseudotsugaMenziesii].GetLength(1) == snagsAndLogs.DiameterClasses);

            float initialTreesPerHectare = Constant.AcresPerHectare * trajectory.DensityByPeriod[0].TreesPerAcre;
            float initialStemsPerHectare = initialTreesPerHectare + snagsAndLogs.LogsPerHectareByPeriod[0] + snagsAndLogs.SnagsPerHectareByPeriod[0];
            for (int period = 0; period < snagsAndLogs.Periods; ++period)
            {
                float logsPerHectare = snagsAndLogs.LogsPerHectareByPeriod[period];
                float snagPerHectare = snagsAndLogs.SnagsPerHectareByPeriod[period];
                float treesPerHectare = Constant.AcresPerHectare * trajectory.DensityByPeriod[period].TreesPerAcre;
                float stemsPerHectare = treesPerHectare + snagPerHectare + logsPerHectare;

                Assert.IsTrue(snagsAndLogs.LogQmdInCentimetersByPeriod[period] >= 0.0F);
                Assert.IsTrue(logsPerHectare >= 0.0F);

                Assert.IsTrue(snagsAndLogs.SnagQmdInCentimetersByPeriod[period] >= 0.0F);
                Assert.IsTrue(snagPerHectare >= 0.0F);

                // for now, assume no ingrowth
                Assert.IsTrue(initialStemsPerHectare >= stemsPerHectare);
            }
        }

        private static void Verify(SortedList<FiaCode, TreeSelection> individualTreeSelectionBySpecies, ExpectedStandTrajectory expectedTrajectory)
        {
            int outOfRangeTrees = 0;
            int treesSelected = 0;
            foreach (TreeSelection individualTreeSelection in individualTreeSelectionBySpecies.Values)
            {
                for (int treeIndex = 0; treeIndex < individualTreeSelection.Count; ++treeIndex)
                {
                    int treeSelection = individualTreeSelection[treeIndex];
                    bool isOutOfRange = (treeSelection != 0) && (treeSelection != expectedTrajectory.FirstThinPeriod);
                    if (expectedTrajectory.SecondThinPeriod != Constant.NoThinPeriod)
                    {
                        isOutOfRange &= treeSelection != expectedTrajectory.SecondThinPeriod;
                    }
                    if (expectedTrajectory.ThirdThinPeriod != Constant.NoThinPeriod)
                    {
                        isOutOfRange &= treeSelection != expectedTrajectory.ThirdThinPeriod;
                    }
                    if (isOutOfRange)
                    {
                        ++outOfRangeTrees;
                    }

                    bool isSelected = treeSelection != 0;
                    if (isSelected)
                    {
                        ++treesSelected;
                    }
                }
            }

            Assert.IsTrue(outOfRangeTrees == 0);
            Assert.IsTrue(treesSelected >= expectedTrajectory.MinimumTreesSelected);
            Assert.IsTrue(treesSelected <= expectedTrajectory.MaximumTreesSelected);
        }

        private class ExpectedStandTrajectory
        {
            public int FirstThinPeriod { get; init; }
            public int SecondThinPeriod { get; init; }
            public int ThirdThinPeriod { get; init; }
            public int MaximumTreesSelected { get; init; }
            public int MinimumTreesSelected { get; init; }

            public float[] MinimumHarvestCubic { get; init; }
            public float[] MinimumHarvestMbf { get; init; }
            public float[] MinimumQmd { get; init; }
            public float[] MinimumStandingCubic { get; init; }
            public float[] MinimumStandingMbf { get; init; }
            public float[] MinimumTopHeight { get; init; }

            public ExpectedStandTrajectory()
            {
                this.FirstThinPeriod = Constant.NoThinPeriod;
                this.SecondThinPeriod = Constant.NoThinPeriod;
                this.ThirdThinPeriod = Constant.NoThinPeriod;
                this.MaximumTreesSelected = 0;
                this.MinimumTreesSelected = 0;
                this.MinimumHarvestCubic = Array.Empty<float>();
                this.MinimumHarvestMbf = Array.Empty<float>();
                this.MinimumQmd = Array.Empty<float>();
                this.MinimumStandingCubic = Array.Empty<float>();
                this.MinimumStandingMbf = Array.Empty<float>();
                this.MinimumTopHeight = Array.Empty<float>();
            }

            public int Length
            {
                get { return this.MinimumHarvestCubic.Length; }
            }
        }
    }
}
