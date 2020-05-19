﻿using Osu.Cof.Ferm.Heuristics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Osu.Cof.Ferm.Cmdlets
{
    public class HeuristicSolutionDistribution
    {
        public List<float> BestObjectiveFunctionBySolution { get; private set; }
        public Heuristic BestSolution { get; private set; }
        public float DefaultSelectionProbability { get; set; }

        public List<int> CountByMove { get; private set; }
        public List<float> FifthPercentileByMove { get; private set; }
        public List<float> LowerQuartileByMove { get; private set; }
        public List<float> MaximumObjectiveFunctionByMove { get; private set; }
        public List<float> MeanObjectiveFunctionByMove { get; private set; }
        public List<float> MedianObjectiveFunctionByMove { get; private set; }
        public List<float> MinimumObjectiveFunctionByMove { get; private set; }
        public List<float> NinetyFifthPercentileByMove { get; private set; }
        public List<List<float>> ObjectiveFunctionValuesByMove { get; private set; }
        public List<float> UpperQuartileByMove { get; private set; }
        public List<float> VarianceByMove { get; private set; }

        public List<TimeSpan> RuntimeBySolution { get; private set; }

        public TimeSpan TotalCoreSeconds { get; private set; }
        public int TotalMoves { get; private set; }
        public int TotalRuns { get; private set; }

        public HeuristicSolutionDistribution()
        {
            int defaultMoveCapacity = 1024;

            this.BestObjectiveFunctionBySolution = new List<float>(100);
            this.BestSolution = null;
            this.CountByMove = new List<int>(defaultMoveCapacity);
            this.FifthPercentileByMove = new List<float>(defaultMoveCapacity);
            this.LowerQuartileByMove = new List<float>(defaultMoveCapacity);
            this.MaximumObjectiveFunctionByMove = new List<float>(defaultMoveCapacity);
            this.MeanObjectiveFunctionByMove = new List<float>(defaultMoveCapacity);
            this.MedianObjectiveFunctionByMove = new List<float>(defaultMoveCapacity);
            this.MinimumObjectiveFunctionByMove = new List<float>(defaultMoveCapacity);
            this.NinetyFifthPercentileByMove = new List<float>(defaultMoveCapacity);
            this.ObjectiveFunctionValuesByMove = new List<List<float>>(defaultMoveCapacity);
            this.RuntimeBySolution = new List<TimeSpan>(defaultMoveCapacity);
            this.TotalCoreSeconds = TimeSpan.Zero;
            this.TotalMoves = 0;
            this.TotalRuns = 0;
            this.UpperQuartileByMove = new List<float>(defaultMoveCapacity);
            this.VarianceByMove = new List<float>(defaultMoveCapacity);
        }

        public void AddRun(Heuristic heuristic, TimeSpan coreSeconds)
        {
            this.BestObjectiveFunctionBySolution.Add(heuristic.BestObjectiveFunction);
            this.RuntimeBySolution.Add(coreSeconds);

            for (int moveIndex = 0; moveIndex < heuristic.ObjectiveFunctionByMove.Count; ++moveIndex)
            {
                float objectiveFunction = heuristic.ObjectiveFunctionByMove[moveIndex];
                if (moveIndex >= this.CountByMove.Count)
                {
                    // all quantiles are found in OnRunsComplete()
                    this.CountByMove.Add(1);
                    this.MaximumObjectiveFunctionByMove.Add(objectiveFunction);
                    this.MeanObjectiveFunctionByMove.Add(objectiveFunction);
                    this.MinimumObjectiveFunctionByMove.Add(objectiveFunction);
                    this.ObjectiveFunctionValuesByMove.Add(new List<float>() { objectiveFunction });
                    this.VarianceByMove.Add(objectiveFunction * objectiveFunction);
                }
                else
                {
                    ++this.CountByMove[moveIndex];

                    float maxObjectiveFunction = this.MaximumObjectiveFunctionByMove[moveIndex];
                    if (objectiveFunction > maxObjectiveFunction)
                    {
                        this.MaximumObjectiveFunctionByMove[moveIndex] = objectiveFunction;
                    }

                    // division and convergence to variance are done in OnRunsComplete()
                    this.MeanObjectiveFunctionByMove[moveIndex] += objectiveFunction;
                    this.VarianceByMove[moveIndex] += objectiveFunction * objectiveFunction;

                    float minObjectiveFunction = this.MinimumObjectiveFunctionByMove[moveIndex];
                    if (objectiveFunction < minObjectiveFunction)
                    {
                        this.MinimumObjectiveFunctionByMove[moveIndex] = objectiveFunction;
                    }

                    this.ObjectiveFunctionValuesByMove[moveIndex].Add(objectiveFunction);
                }
            }

            this.TotalCoreSeconds += coreSeconds;
            this.TotalMoves += heuristic.ObjectiveFunctionByMove.Count;
            ++this.TotalRuns;

            if ((this.BestSolution == null) || (heuristic.BestObjectiveFunction > this.BestSolution.BestObjectiveFunction))
            {
                this.BestSolution = heuristic;
            }
        }

        public void OnRunsComplete()
        {
            Debug.Assert(this.MeanObjectiveFunctionByMove.Count == this.VarianceByMove.Count);

            // find objective function statistics
            // Quantile calculations assume number of objective functions observed is constant or decreases monotonically with the number of moves the 
            // heuristic made.
            for (int moveIndex = 0; moveIndex < this.MeanObjectiveFunctionByMove.Count; ++moveIndex)
            {
                float runsAsFloat = this.CountByMove[moveIndex];
                this.MeanObjectiveFunctionByMove[moveIndex] /= runsAsFloat;
                this.VarianceByMove[moveIndex] = this.VarianceByMove[moveIndex] / runsAsFloat - this.MeanObjectiveFunctionByMove[moveIndex] * this.MeanObjectiveFunctionByMove[moveIndex];

                List<float> objectiveFunctions = this.ObjectiveFunctionValuesByMove[moveIndex];
                if (objectiveFunctions.Count > 2)
                {
                    objectiveFunctions.Sort();

                    float median;
                    bool exactMedian = (objectiveFunctions.Count % 2) == 1;
                    if (exactMedian)
                    {
                        median = objectiveFunctions[objectiveFunctions.Count / 2]; // x.5 truncates to x, matching middle element due zero based indexing
                    }
                    else
                    {
                        int halfIndex = objectiveFunctions.Count / 2;
                        median = 0.5F * objectiveFunctions[halfIndex - 1] + 0.5F * objectiveFunctions[halfIndex];

                        Debug.Assert(median > objectiveFunctions[0]);
                        Debug.Assert(median < objectiveFunctions[^1]);
                    }
                    this.MedianObjectiveFunctionByMove.Add(median);

                    if (objectiveFunctions.Count > 4)
                    {
                        bool exactQuartiles = (objectiveFunctions.Count % 4) == 0;
                        if (exactQuartiles)
                        {
                            this.LowerQuartileByMove.Add(objectiveFunctions[objectiveFunctions.Count / 4]);
                            this.UpperQuartileByMove.Add(objectiveFunctions[3 * objectiveFunctions.Count / 4]);
                        }
                        else
                        {
                            float lowerQuartilePosition = 0.25F * objectiveFunctions.Count;
                            float ceilingIndex = MathF.Ceiling(lowerQuartilePosition);
                            float floorIndex = MathF.Floor(lowerQuartilePosition);
                            float ceilingWeight = 1.0F + lowerQuartilePosition - ceilingIndex;
                            float floorWeight = 1.0F - lowerQuartilePosition + floorIndex;
                            float lowerQuartile = floorWeight * objectiveFunctions[(int)floorIndex] + ceilingWeight * objectiveFunctions[(int)ceilingIndex];
                            this.LowerQuartileByMove.Add(lowerQuartile);

                            float upperQuartilePosition = 0.75F * objectiveFunctions.Count;
                            ceilingIndex = MathF.Ceiling(upperQuartilePosition);
                            floorIndex = MathF.Floor(upperQuartilePosition);
                            ceilingWeight = 1.0F + upperQuartilePosition - ceilingIndex;
                            floorWeight = 1.0F - upperQuartilePosition + floorIndex;
                            float upperQuartile = floorWeight * objectiveFunctions[(int)floorIndex] + ceilingWeight * objectiveFunctions[(int)ceilingIndex];
                            this.UpperQuartileByMove.Add(upperQuartile);

                            Debug.Assert(lowerQuartile > objectiveFunctions[0]);
                            Debug.Assert(lowerQuartile < median);
                            Debug.Assert(upperQuartile > median);
                            Debug.Assert(upperQuartile < objectiveFunctions[^1]);
                        }

                        if (objectiveFunctions.Count > 19)
                        {
                            bool exactPercentiles = (objectiveFunctions.Count % 20) == 0;
                            if (exactPercentiles)
                            {
                                this.FifthPercentileByMove.Add(objectiveFunctions[objectiveFunctions.Count / 20]);
                                this.NinetyFifthPercentileByMove.Add(objectiveFunctions[19 * objectiveFunctions.Count / 20]);
                            }
                            else
                            {
                                float fifthPercentilePosition = 0.05F * objectiveFunctions.Count;
                                float ceilingIndex = MathF.Ceiling(fifthPercentilePosition);
                                float floorIndex = MathF.Floor(fifthPercentilePosition);
                                float ceilingWeight = 1.0F + fifthPercentilePosition - ceilingIndex;
                                float floorWeight = 1.0F - fifthPercentilePosition + floorIndex;
                                float fifthPercentile = floorWeight * objectiveFunctions[(int)floorIndex] + ceilingWeight * objectiveFunctions[(int)ceilingIndex];
                                this.FifthPercentileByMove.Add(fifthPercentile);

                                float ninetyFifthPercentilePosition = 0.95F * objectiveFunctions.Count;
                                ceilingIndex = MathF.Ceiling(ninetyFifthPercentilePosition);
                                floorIndex = MathF.Floor(ninetyFifthPercentilePosition);
                                ceilingWeight = 1.0F + ninetyFifthPercentilePosition - ceilingIndex;
                                floorWeight = 1.0F - ninetyFifthPercentilePosition + floorIndex;
                                float ninetyFifthPercentile = floorWeight * objectiveFunctions[(int)floorIndex] + ceilingWeight * objectiveFunctions[(int)ceilingIndex];
                                this.NinetyFifthPercentileByMove.Add(ninetyFifthPercentile);

                                Debug.Assert(fifthPercentile > objectiveFunctions[0]);
                                Debug.Assert(fifthPercentile < median);
                                Debug.Assert(ninetyFifthPercentile > median);
                                Debug.Assert(ninetyFifthPercentile < objectiveFunctions[^1]);
                            }
                        }
                    }
                }
            }
        }
    }
}
