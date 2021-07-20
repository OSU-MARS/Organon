using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using Osu.Cof.Ferm.Tree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Management.Automation;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Osu.Cof.Ferm.Cmdlets
{
    public abstract class OptimizeCmdlet<TParameters> : Cmdlet where TParameters : HeuristicParameters
    {
        [Parameter]
        [ValidateRange(1, 10000)]
        public int BestOf { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(Constant.Grasp.FullyRandomConstructionForMaximization, Constant.Grasp.FullyGreedyConstructionForMaximization)]
        public List<float> ConstructionGreediness { get; set; }

        [Parameter]
        [ValidateNotNull]
        public FinancialScenarios Financial { get; set; }
        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(Constant.NoThinPeriod, 100)]
        public List<int> FirstThinPeriod { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 1.0F)]
        public List<float> InitialThinningProbability { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0, 100)]
        public List<int> PlanningPeriods { get; set; }
        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(Constant.NoThinPeriod, 100)]
        public List<int> SecondThinPeriod { get; set; }

        [Parameter]
        [ValidateRange(1, 100)]
        public int SolutionPoolSize { get; set; }

        [Parameter(Mandatory = true)]
        [ValidateNotNull]
        public OrganonStand? Stand { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(Constant.NoThinPeriod, 100)]
        public List<int> ThirdThinPeriod { get; set; }

        [Parameter]
        [ValidateRange(1, 100)]
        public int Threads { get; set; }

        [Parameter]
        public TimberObjective TimberObjective { get; set; }
        [Parameter]
        public TreeModel TreeModel { get; set; }

        public OptimizeCmdlet()
        {
            this.BestOf = 1;
            this.Threads = Environment.ProcessorCount / 2; // assume all cores are hyperthreaded

            this.Financial = FinancialScenarios.Default;
            this.FirstThinPeriod = new() { Constant.DefaultThinningPeriod };
            this.PlanningPeriods = new() { Constant.DefaultPlanningPeriods };
            this.ConstructionGreediness = new() { Constant.Grasp.DefaultMinimumConstructionGreedinessForMaximization };
            this.InitialThinningProbability = new() { Constant.HeuristicDefault.InitialThinningProbability };
            this.SecondThinPeriod = new() { Constant.NoThinPeriod };
            this.SolutionPoolSize = Constant.DefaultSolutionPoolSize;
            this.ThirdThinPeriod = new() { Constant.NoThinPeriod };
            this.TimberObjective = TimberObjective.LandExpectationValue;
            this.TreeModel = TreeModel.OrganonNwo;
        }

        protected virtual bool HeuristicEvaluatesAcrossPlanningPeriodsAndDiscountRates
        {
            get { return false; }
        }

        protected abstract Heuristic<TParameters> CreateHeuristic(TParameters heuristicParameters, RunParameters runParameters);

        private RunParameters CreateRunParameters(HeuristicResultPosition position, OrganonConfiguration organonConfiguration)
        {
            RunParameters runParameters = new(this.PlanningPeriods, organonConfiguration)
            {
                Financial = this.Financial,
                MaximizeForPlanningPeriod = this.HeuristicEvaluatesAcrossPlanningPeriodsAndDiscountRates ? Constant.MaximizeForAllPlanningPeriods : this.PlanningPeriods[position.RotationIndex],
                TimberObjective = this.TimberObjective
            };

            int lastThinPeriod = Constant.NoThinPeriod;
            if (this.TryCreateFirstThin(position.FirstThinPeriodIndex, out IHarvest? firstThin))
            {
                lastThinPeriod = Math.Max(lastThinPeriod, this.FirstThinPeriod[position.FirstThinPeriodIndex]);
                runParameters.Treatments.Harvests.Add(firstThin);

                if (this.TryCreateSecondThin(position.SecondThinPeriodIndex, out IHarvest? secondThin))
                {
                    lastThinPeriod = Math.Max(lastThinPeriod, this.SecondThinPeriod[position.SecondThinPeriodIndex]);
                    runParameters.Treatments.Harvests.Add(secondThin);

                    if (this.TryCreateThirdThin(position.ThirdThinPeriodIndex, out IHarvest? thirdThin))
                    {
                        lastThinPeriod = Math.Max(lastThinPeriod, this.ThirdThinPeriod[position.ThirdThinPeriodIndex]);
                        runParameters.Treatments.Harvests.Add(thirdThin);
                    }
                }
            }

            runParameters.LastThinPeriod = lastThinPeriod;
            return runParameters;
        }

        protected virtual IHarvest CreateThin(int thinPeriodIndex)
        {
            return new ThinByIndividualTreeSelection(thinPeriodIndex);
        }

        protected IList<HeuristicParameters> GetDefaultParameterCombinations()
        {
            List<HeuristicParameters> parameterCombinations = new();
            foreach (float constructionGreediness in this.ConstructionGreediness)
            {
                if ((constructionGreediness < Constant.Grasp.FullyRandomConstructionForMaximization) || (constructionGreediness > Constant.Grasp.FullyGreedyConstructionForMaximization))
                {
                    throw new ParameterOutOfRangeException(nameof(this.ConstructionGreediness));
                }

                foreach (float thinningProbability in this.InitialThinningProbability)
                {
                    if ((thinningProbability < 0.0F) || (thinningProbability > 1.0F))
                    {
                        throw new ParameterOutOfRangeException(nameof(this.InitialThinningProbability));
                    }

                    parameterCombinations.Add(new HeuristicParameters()
                    {
                        MinimumConstructionGreediness = constructionGreediness,
                        InitialThinningProbability = thinningProbability
                    });
                }
            }
            return parameterCombinations;
        }

        // for now, assume runtime complexity is linear with the number of periods requiring thinning optimization
        // This is simplified, but it's more accurate than assuming all runs have equal cost regardless of the number of timesteps required and is
        // a reasonable approximation for hero, Monte Carlo heuristics, prescription enumeration, and genetic algorithms.
        protected static int EstimateRuntimeCost(HeuristicResultPosition position, HeuristicResults<TParameters> results)
        {
            int periodWeight = 0; // no thins so only a single trajectory need be simulated; assume cost is negligible as no optimization occurs
            int firstThinPeriod = results.FirstThinPeriods[position.FirstThinPeriodIndex];
            if (firstThinPeriod != Constant.NoThinPeriod)
            {
                int endOfRotationPeriod;
                if (position.RotationIndex == Constant.AllRotationPosition)
                {
                    endOfRotationPeriod = results.RotationLengths.Max();
                }
                else
                {
                    endOfRotationPeriod = results.RotationLengths[position.RotationIndex];
                }
                periodWeight = endOfRotationPeriod - firstThinPeriod;

                int secondThinPeriod = results.SecondThinPeriods[position.SecondThinPeriodIndex];
                if (secondThinPeriod != Constant.NoThinPeriod)
                {
                    periodWeight += endOfRotationPeriod - secondThinPeriod;

                    int thirdThinPeriod = results.ThirdThinPeriods[position.ThirdThinPeriodIndex];
                    if (thirdThinPeriod == Constant.NoThinPeriod)
                    {
                        return endOfRotationPeriod - thirdThinPeriod;
                    }
                }
            }

            return periodWeight;
        }

        protected abstract string GetName();

        // ideally this would be virtual but, as of C# 9.0, the nature of C# generics requires GetDefaultParameterCombinations() to be separate
        protected abstract IList<TParameters> GetParameterCombinations();

        private string GetStatusDescription(IList<TParameters> parameterCombinations, HeuristicResultPosition currentPosition)
        {
            string mostRecentEvaluationDescription = String.Empty;
            if (parameterCombinations.Count > 1)
            {
                mostRecentEvaluationDescription += ", parameters " + currentPosition.ParameterIndex + "/" + parameterCombinations.Count;
            }
            if (this.FirstThinPeriod.Count > 1)
            {
                mostRecentEvaluationDescription += ", thin 1 " + currentPosition.FirstThinPeriodIndex + "/" + this.FirstThinPeriod.Count;
            }
            if (this.SecondThinPeriod.Count > 1)
            {
                mostRecentEvaluationDescription += ", 2 " + currentPosition.SecondThinPeriodIndex + "/" + this.SecondThinPeriod.Count;
            }
            if (this.ThirdThinPeriod.Count > 1)
            {
                mostRecentEvaluationDescription += ", 3 " + currentPosition.ThirdThinPeriodIndex + "/" + this.ThirdThinPeriod.Count;
            }
            if (this.HeuristicEvaluatesAcrossPlanningPeriodsAndDiscountRates == false)
            {
                if (this.PlanningPeriods.Count > 1)
                {
                    mostRecentEvaluationDescription += ", rotation " + currentPosition.RotationIndex + "/" + this.PlanningPeriods.Count;
                }
                if (this.Financial.Count > 1)
                {
                    mostRecentEvaluationDescription += ", scenario " + currentPosition.FinancialIndex + "/" + this.Financial.Count;
                }
            }
            mostRecentEvaluationDescription += " (" + this.Threads + " threads)";
            return mostRecentEvaluationDescription;
        }

        protected override void ProcessRecord()
        {
            if (this.ConstructionGreediness.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.ConstructionGreediness));
            }
            if (this.Financial.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.Financial));
            }
            if (this.FirstThinPeriod.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.FirstThinPeriod));
            }
            if (this.InitialThinningProbability.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.InitialThinningProbability));
            }
            if (this.PlanningPeriods.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.PlanningPeriods));
            }
            if (this.SecondThinPeriod.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.SecondThinPeriod));
            }
            if (this.ThirdThinPeriod.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.ThirdThinPeriod));
            }
            if ((this.TimberObjective == TimberObjective.ScribnerVolume) && (this.Financial.Count > 1) && (this.HeuristicEvaluatesAcrossPlanningPeriodsAndDiscountRates == false))
            {
                // low priority to improve this but at least warn about limited support
                this.WriteWarning("Timber optimization objective is " + this.TimberObjective + " but multiple discount rates are specified. Optimization will be unnecessarily repeated for each discount rate.");
            }

            Stopwatch stopwatch = new();
            stopwatch.Start();
            HeuristicPerformanceCounters totalPerfCounters = new();
            int totalRuntimeCost = 0;

            int treeCount = this.Stand!.GetTreeRecordCount();
            List<HeuristicResultPosition> combinationsToEvaluate = new();
            IList<TParameters> parameterCombinationsForHeuristic = this.GetParameterCombinations();
            HeuristicResults<TParameters> results = new(parameterCombinationsForHeuristic, this.FirstThinPeriod, this.SecondThinPeriod, this.ThirdThinPeriod, this.PlanningPeriods, this.Financial, this.SolutionPoolSize);
            for (int parameterIndex = 0; parameterIndex < parameterCombinationsForHeuristic.Count; ++parameterIndex)
            {
                for (int firstThinIndex = 0; firstThinIndex < this.FirstThinPeriod.Count; ++firstThinIndex)
                {
                    int firstThinPeriod = this.FirstThinPeriod[firstThinIndex];
                    if (firstThinPeriod == 0)
                    {
                        throw new ParameterOutOfRangeException(nameof(this.FirstThinPeriod), "First thinning period cannot be zero.");
                    }

                    for (int secondThinIndex = 0; secondThinIndex < this.SecondThinPeriod.Count; ++secondThinIndex)
                    {
                        int secondThinPeriod = this.SecondThinPeriod[secondThinIndex];
                        if (secondThinPeriod == 0)
                        {
                            throw new ParameterOutOfRangeException(nameof(this.SecondThinPeriod), "Second thinning period cannot be zero.");
                        }
                        if (secondThinPeriod != Constant.NoThinPeriod)
                        {
                            if ((firstThinPeriod == Constant.NoThinPeriod) || (firstThinPeriod >= secondThinPeriod))
                            {
                                // can't perform a second thin if
                                // - there was no first thin
                                // - the second thin would occur before or in the same period as the first thin
                                // - the second thin would occur before or in the same period as final harvest
                                continue;
                            }
                        }

                        int lastOfFirstOrSecondThinPeriod = Math.Max(firstThinPeriod, secondThinPeriod);
                        for (int thirdThinIndex = 0; thirdThinIndex < this.ThirdThinPeriod.Count; ++thirdThinIndex)
                        {
                            int thirdThinPeriod = this.ThirdThinPeriod[thirdThinIndex];
                            if (thirdThinPeriod == 0)
                            {
                                throw new ParameterOutOfRangeException(nameof(this.ThirdThinPeriod), "Third thinning period cannot be zero.");
                            }
                            if (thirdThinPeriod != Constant.NoThinPeriod)
                            {
                                if ((secondThinPeriod == Constant.NoThinPeriod) || (secondThinPeriod >= thirdThinPeriod))
                                {
                                    // can't perform a third thin if
                                    // - there was no second thin
                                    // - the third thin would occur before or in the same period as the second thin
                                    // - the third thin would occur before or in the same period as final harvest
                                    continue;
                                }
                            }

                            if (this.HeuristicEvaluatesAcrossPlanningPeriodsAndDiscountRates)
                            {
                                HeuristicResultPosition position = new()
                                {
                                    FinancialIndex = Constant.AllFinancialScenariosPosition,
                                    FirstThinPeriodIndex = firstThinIndex,
                                    ParameterIndex = parameterIndex,
                                    RotationIndex = Constant.AllRotationPosition,
                                    SecondThinPeriodIndex = secondThinIndex,
                                    ThirdThinPeriodIndex = thirdThinIndex
                                };
                                combinationsToEvaluate.Add(position);
                                totalRuntimeCost += OptimizeCmdlet<TParameters>.EstimateRuntimeCost(position, results);
                            }
                            else
                            {
                                int lastOfFirstSecondOrThirdThinPeriod = Math.Max(lastOfFirstOrSecondThinPeriod, thirdThinPeriod);
                                for (int rotationIndex = 0; rotationIndex < this.PlanningPeriods.Count; ++rotationIndex)
                                {
                                    int planningPeriods = this.PlanningPeriods[rotationIndex];
                                    if (lastOfFirstSecondOrThirdThinPeriod >= planningPeriods)
                                    {
                                        // last thin would occur before or in the same period as the final harvest
                                        continue;
                                    }

                                    // heuristic optimizes for one financial scenario at a time
                                    for (int financialIndex = 0; financialIndex < this.Financial.Count; ++financialIndex)
                                    {
                                        // distributions are unique per combination of run tuple (discount rate, thins 1, 2, 3, and rotation)
                                        // and heuristic parameters but solution pools are shared across heuristic parameters
                                        HeuristicResultPosition position = new()
                                        {
                                            FinancialIndex = financialIndex,
                                            FirstThinPeriodIndex = firstThinIndex,
                                            ParameterIndex = parameterIndex,
                                            RotationIndex = rotationIndex,
                                            SecondThinPeriodIndex = secondThinIndex,
                                            ThirdThinPeriodIndex = thirdThinIndex
                                        };
                                        combinationsToEvaluate.Add(position);
                                        totalRuntimeCost += OptimizeCmdlet<TParameters>.EstimateRuntimeCost(position, results);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            OrganonConfiguration organonConfiguration = new(OrganonVariant.Create(this.TreeModel));
            int runsCompleted = 0;
            int runsStarted = -1; // step through combinationsToEvaluate sequentially
            int runtimeCostCompleted = 0;
            int totalRuns = this.BestOf * combinationsToEvaluate.Count;
            totalRuntimeCost *= this.BestOf;
            Task runs = Task.Run(() =>
            {
                Task[] workers = new Task[this.Threads];
                for (int workerThread = 0; workerThread < workers.Length; ++workerThread)
                {
                    workers[workerThread] = Task.Run(() =>
                    {
                        for (int runNumber = Interlocked.Increment(ref runsStarted); runNumber < totalRuns; runNumber = Interlocked.Increment(ref runsStarted))
                        {
                            if (this.Stopping)
                            {
                                return;
                            }

                            int combinationIndex = runNumber / this.BestOf;
                            HeuristicResultPosition position = combinationsToEvaluate[combinationIndex];
                            try
                            {
                                TParameters heuristicParameters = parameterCombinationsForHeuristic[position.ParameterIndex];
                                RunParameters runParameters = this.CreateRunParameters(position, organonConfiguration);
                                Heuristic<TParameters> currentHeuristic = this.CreateHeuristic(heuristicParameters, runParameters);
                                HeuristicPerformanceCounters perfCounters = currentHeuristic.Run(position, results);

                                // accumulate run into results and tracking counters
                                int estimatedRuntimeCost = OptimizeCmdlet<TParameters>.EstimateRuntimeCost(position, results);
                                int runWithinCombination = runNumber - this.BestOf * combinationIndex;
                                lock (results)
                                {
                                    if (this.HeuristicEvaluatesAcrossPlanningPeriodsAndDiscountRates)
                                    {
                                        // scatter heuristic's results to rotation lengths and discount rates
                                        HeuristicPerformanceCounters perfCountersToAssmilate = perfCounters;
                                        bool perfCountersLogged = false;
                                        for (int rotationIndex = 0; rotationIndex < this.PlanningPeriods.Count; ++rotationIndex)
                                        {
                                            int endOfRotationPeriod = this.PlanningPeriods[rotationIndex];
                                            if (endOfRotationPeriod <= runParameters.LastThinPeriod)
                                            {
                                                continue; // not a valid position because end of rotation would occur before or in the same period as the last thin
                                            }

                                            for (int financialIndex = 0; financialIndex < this.Financial.Count; ++financialIndex)
                                            {
                                                HeuristicResultPosition evaluatedPosition = new(position) // must be new each time to populate results.CombinationsEvaluated
                                                {
                                                    FinancialIndex = financialIndex,
                                                    RotationIndex = rotationIndex
                                                };

                                                results.AssimilateHeuristicRunIntoPosition(currentHeuristic, perfCountersToAssmilate, evaluatedPosition);
                                                if (runWithinCombination == this.BestOf - 1)
                                                {
                                                    // last run in combination adds the evaluated positions to CombinationsEvaluated
                                                    // The evaluated position is added because it is specific to a particular rotation and
                                                    // financial scenario. In this case the heuristic position spans rotations and scenarios.
                                                    results.AddEvaluatedPosition(evaluatedPosition);
                                                }

                                                if (perfCountersLogged == false)
                                                {
                                                    perfCountersLogged = true;
                                                    perfCountersToAssmilate = HeuristicPerformanceCounters.Zero;
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // heuristic is specific to a single discount rate
                                        results.AssimilateHeuristicRunIntoPosition(currentHeuristic, perfCounters, position);
                                        if (runWithinCombination == this.BestOf - 1)
                                        {
                                            // last run in combination adds the position to CombinationsEvaluated
                                            results.AddEvaluatedPosition(position);
                                        }
                                    }

                                    totalPerfCounters += perfCounters;
                                    ++runsCompleted;
                                    runtimeCostCompleted += estimatedRuntimeCost;
                                }
                            }
                            catch (Exception exception)
                            {
                                string rotationLength = "vectorized";
                                string financialScenario = "vectorized";
                                if (this.HeuristicEvaluatesAcrossPlanningPeriodsAndDiscountRates == false)
                                {
                                    financialScenario = results.FinancialScenarios.DiscountRate[position.FinancialIndex].ToString();
                                    rotationLength = results.RotationLengths[position.RotationIndex].ToString();
                                }
                                throw new AggregateException("Exception encountered during optimization. Parameters " + position.ParameterIndex +
                                                             ", first thin period " + results.FirstThinPeriods[position.FirstThinPeriodIndex] +
                                                             ", second thin period " + results.SecondThinPeriods[position.SecondThinPeriodIndex] +
                                                             ", third thin period " + results.ThirdThinPeriods[position.ThirdThinPeriodIndex] +
                                                             ", rotation length " + rotationLength +
                                                             ", financial scenario " + financialScenario +
                                                             ".",
                                                             exception);
                            }
                        }
                    });
                }

                Task.WaitAll(workers);
            });

            string cmdletName = this.GetName();
            bool progressWritten = false;
            int sleepsSinceLastStatusUpdate = 0;
            while (runs.IsCompleted == false)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1.0));
                ++sleepsSinceLastStatusUpdate;

                if (runs.IsFaulted)
                {
                    Debug.Assert(runs.Exception != null && runs.Exception.InnerException != null);
                    // per https://stackoverflow.com/questions/20170527/how-to-correctly-rethrow-an-exception-of-task-already-in-faulted-state
                    ExceptionDispatchInfo.Capture(runs.Exception.InnerException).Throw();
                }
                if (sleepsSinceLastStatusUpdate > 30)
                {
                    int currentRun = Math.Min(runsCompleted + 1, totalRuns);
                    int currentPositionIndex = Math.Min(currentRun / this.BestOf, combinationsToEvaluate.Count - 1);
                    HeuristicResultPosition mostRecentCombination = combinationsToEvaluate[currentPositionIndex];

                    string mostRecentEvaluationDescription = "run " + currentRun + "/" + totalRuns + this.GetStatusDescription(parameterCombinationsForHeuristic, mostRecentCombination);
                    double fractionComplete = (double)runtimeCostCompleted / (double)totalRuntimeCost;
                    double secondsElapsed = stopwatch.Elapsed.TotalSeconds;
                    double secondsRemaining = secondsElapsed * (1.0 / fractionComplete - 1.0);
                    this.WriteProgress(new ProgressRecord(0, cmdletName, mostRecentEvaluationDescription.ToString())
                    {
                        PercentComplete = (int)(100.0 * fractionComplete),
                        SecondsRemaining = (int)Math.Round(secondsRemaining)
                    });

                    progressWritten = true;
                    sleepsSinceLastStatusUpdate = 0;
                }
            }
            runs.GetAwaiter().GetResult(); // propagate any exceptions since last IsFaulted check
            stopwatch.Stop();
            this.WriteObject(results);
            
            if (progressWritten)
            {
                // write progress complete
                string mostRecentEvaluationDescription = totalRuns + " runs " + this.GetStatusDescription(parameterCombinationsForHeuristic, combinationsToEvaluate[^1]);
                this.WriteProgress(new ProgressRecord(0, cmdletName, mostRecentEvaluationDescription)
                {
                    PercentComplete = 100,
                    SecondsRemaining = 0
                });
            }

            if (results.PositionsEvaluated.Count == 1)
            {
                HeuristicResultPosition firstPosition = results.PositionsEvaluated[0];
                this.WriteSingleDistributionSummary(results[firstPosition], totalPerfCounters, stopwatch.Elapsed);
            }
            else if (results.PositionsEvaluated.Count > 1)
            {
                this.WriteMultipleDistributionSummary(results, totalPerfCounters, stopwatch.Elapsed);
            }
            else
            {
                this.WriteWarning("No valid parameter combininations found. No runs performed.");
            }
        }

        private void WriteMultipleDistributionSummary(HeuristicResults<TParameters> results, HeuristicPerformanceCounters totalPerfCounters, TimeSpan elapsedTime)
        {
            results.GetPoolPerformanceCounters(out int solutionsCached, out int solutionsAccepted, out int solutionsRejected);

            this.WriteVerbose("{0}: {1} configurations with {2} runs in {3:0.00} minutes ({4:0.00}M timesteps in {5:0.00} core-minutes, {6:0.00}% move acceptance, {7} solutions pooled, {8:0.00}% pool acceptance).",
                              this.GetName(),
                              results.PositionsEvaluated.Count,
                              this.BestOf * results.PositionsEvaluated.Count,
                              elapsedTime.TotalMinutes,
                              1E-6F * totalPerfCounters.GrowthModelTimesteps,
                              totalPerfCounters.Duration.TotalMinutes,
                              100.0F * totalPerfCounters.MovesAccepted / (totalPerfCounters.MovesAccepted + totalPerfCounters.MovesRejected),
                              solutionsCached,
                              100.0F * solutionsAccepted / (solutionsAccepted + solutionsRejected));
        }

        private void WriteSingleDistributionSummary(HeuristicResult result, HeuristicPerformanceCounters totalPerfCounters, TimeSpan elapsedTime)
        {
            Heuristic? highHeuristic = result.Pool.High;
            if (highHeuristic == null)
            {
                throw new ArgumentNullException(nameof(result), "Result's solution pool does not have a high heuristic.");
            }

            base.WriteVerbose(String.Empty); // Visual Studio code workaround
            int totalMoves = totalPerfCounters.MovesAccepted + totalPerfCounters.MovesRejected;
            this.WriteVerbose("{0}: {1} moves, {2} changing ({3:0%}), {4} unchanging ({5:0%})", highHeuristic.GetName(), totalMoves, totalPerfCounters.MovesAccepted, (float)totalPerfCounters.MovesAccepted / (float)totalMoves, totalPerfCounters.MovesRejected, (float)totalPerfCounters.MovesRejected / (float)totalMoves);
            this.WriteVerbose("objective: best {0:0.00#}, mean {1:0.00#} ending {2:0.00#}.", highHeuristic.FinancialValue.GetHighestValue(), result.Distribution.HighestFinancialValueBySolution.Average(), highHeuristic.FinancialValue.GetAcceptedValuesWithDefaulting(Constant.HeuristicDefault.RotationIndex, Constant.HeuristicDefault.FinancialIndex).Last());

            double totalSeconds = totalPerfCounters.Duration.TotalSeconds;
            double movesPerSecond = totalMoves / totalSeconds;
            double movesPerSecondMultiplier = movesPerSecond > 1E3 ? 1E-3 : 1.0;
            string movesPerSecondScale = movesPerSecond > 1E3 ? "k" : String.Empty;
            this.WriteVerbose("{0} moves in {1:0.000} core-s and {2:0.000}s clock time ({3:0.00} {4} moves/core-s).", totalMoves, totalSeconds, elapsedTime.TotalSeconds, movesPerSecondMultiplier * movesPerSecond, movesPerSecondScale);
        }

        private bool TryCreateFirstThin(int firstThinPeriodIndex, [NotNullWhen(true)] out IHarvest? firstThin)
        {
            return this.TryCreateThin(this.FirstThinPeriod[firstThinPeriodIndex], out firstThin);
        }

        private bool TryCreateSecondThin(int secondThinPeriodIndex, [NotNullWhen(true)] out IHarvest? secondThin)
        {
            return this.TryCreateThin(this.SecondThinPeriod[secondThinPeriodIndex], out secondThin);
        }

        private bool TryCreateThirdThin(int thirdThinPeriodIndex, [NotNullWhen(true)] out IHarvest? thirdThin)
        {
            return this.TryCreateThin(this.ThirdThinPeriod[thirdThinPeriodIndex], out thirdThin);
        }

        private bool TryCreateThin(int thinPeriodIndex, [NotNullWhen(true)] out IHarvest? thin)
        {
            if (thinPeriodIndex == Constant.NoThinPeriod)
            {
                thin = null;
                return false;
            }

            thin = this.CreateThin(thinPeriodIndex);
            return true;
        }

        protected void WriteVerbose(string format, params object[] args)
        {
            base.WriteVerbose(String.Format(format, args));
        }
    }
}
