﻿using System;
using System.Collections.Generic;

namespace Osu.Cof.Organon.Heuristics
{
    public abstract class Heuristic : RandomNumberConsumer
    {
        public float BestObjectiveFunction { get; protected set; }
        public StandTrajectory BestTrajectory { get; protected set; }
        public StandTrajectory CurrentTrajectory { get; protected set; }
        public Objective Objective { get; protected set; }
        public List<float> ObjectiveFunctionByIteration { get; protected set; }

        protected Heuristic(Stand stand, OrganonConfiguration organonConfiguration, int harvestPeriods, int planningPeriods, Objective objective)
        {
            this.BestTrajectory = new StandTrajectory(stand, organonConfiguration, harvestPeriods, planningPeriods, objective.VolumeUnits);
            this.CurrentTrajectory = new StandTrajectory(stand, organonConfiguration, harvestPeriods, planningPeriods, objective.VolumeUnits);
            this.Objective = objective;

            this.BestTrajectory.Simulate();
            this.BestObjectiveFunction = this.GetObjectiveFunction(this.BestTrajectory);
            this.CurrentTrajectory.Copy(this.BestTrajectory);
        }
        
        protected int TreeRecordCount
        {
            get { return this.CurrentTrajectory.StandByPeriod[0].TreeRecordCount; }
        }

        public abstract string GetColumnName();

        public float GetObjectiveFunction(StandTrajectory trajectory)
        {
            // find objective function value
            // Volume objective functions are in m³/ha or MBF/ac.
            double objectiveFunction = 0.0F;
            if (this.Objective.IsNetPresentValue)
            {
                if (trajectory.VolumeUnits == VolumeUnits.CubicMetersPerHectare)
                {
                    // TODO
                    // TODO: also, check tree model is using a five year time step
                    throw new NotSupportedException();
                }

                // net present value
                // Harvest and standing volumes are in board feet and prices are in MBF, hence multiplications by 0.001.
                // TODO: support per species pricing
                double appreciatedPricePerMbf;
                double discountFactor;
                for (int periodIndex = 1; periodIndex < trajectory.HarvestVolumesByPeriod.Length; ++periodIndex)
                {
                    double thinVolumeInMbf = 0.001 * trajectory.HarvestVolumesByPeriod[periodIndex];
                    if (thinVolumeInMbf > 0.0)
                    {
                        appreciatedPricePerMbf = this.Objective.DouglasFirPricePerMbf * Math.Pow(1.0 + this.Objective.TimberAppreciationRate, Constant.DefaultTimeStepInYears * (periodIndex - 1) + 0.5 * Constant.DefaultTimeStepInYears);
                        discountFactor = 1.0 / Math.Pow(1.0 + this.Objective.DiscountRate, Constant.DefaultTimeStepInYears * (periodIndex - 1) + 0.5 * Constant.DefaultTimeStepInYears);
                        objectiveFunction += discountFactor * (appreciatedPricePerMbf * thinVolumeInMbf - this.Objective.FixedThinningCostPerAcre);
                    }
                }

                appreciatedPricePerMbf = this.Objective.DouglasFirPricePerMbf * Math.Pow(1.0 + this.Objective.TimberAppreciationRate, Constant.DefaultTimeStepInYears * (trajectory.StandingVolumeByPeriod.Length - 1) + 0.5 * Constant.DefaultTimeStepInYears);
                discountFactor = 1.0 / Math.Pow(1.0 + this.Objective.DiscountRate, Constant.DefaultTimeStepInYears * (trajectory.StandingVolumeByPeriod.Length - 1) + 0.5 * Constant.DefaultTimeStepInYears);
                double endStandingVolumeInMbf = 0.001 * trajectory.StandingVolumeByPeriod[trajectory.StandingVolumeByPeriod.Length - 1];
                objectiveFunction += discountFactor * (appreciatedPricePerMbf * endStandingVolumeInMbf - this.Objective.FixedRegenerationHarvestCostPerAcre);

                // convert from US$/ac to k$/ac
                objectiveFunction *= 0.001;
            }
            else
            {
                // direct volume addition
                for (int periodIndex = 1; periodIndex < trajectory.HarvestVolumesByPeriod.Length; ++periodIndex)
                {
                    objectiveFunction += trajectory.HarvestVolumesByPeriod[periodIndex];
                }
                objectiveFunction += trajectory.StandingVolumeByPeriod[trajectory.StandingVolumeByPeriod.Length - 1];
                if (trajectory.VolumeUnits == VolumeUnits.ScribnerBoardFeetPerAcre)
                {
                    objectiveFunction *= 0.001F;
                }
            }

            return (float)objectiveFunction;
        }

        public void RandomizeSchedule()
        {
            if (this.Objective.HarvestPeriodSelection == HarvestPeriodSelection.All)
            {
                double harvestPeriodScalingFactor = ((double)this.CurrentTrajectory.HarvestPeriods - Constant.RoundToZeroTolerance) / (double)byte.MaxValue;
                for (int treeIndex = 0; treeIndex < this.TreeRecordCount; ++treeIndex)
                {
                    int harvestPeriod = (int)(harvestPeriodScalingFactor * this.GetPseudorandomByteAsFloat());
                    this.CurrentTrajectory.SetTreeSelection(treeIndex, harvestPeriod);
                }
            }
            else if (this.Objective.HarvestPeriodSelection == HarvestPeriodSelection.NoneOrLast)
            {
                double unityScalingFactor = 1.0 / (double)byte.MaxValue;
                for (int treeIndex = 0; treeIndex < this.TreeRecordCount; ++treeIndex)
                {
                    int harvestPeriod = unityScalingFactor * this.GetPseudorandomByteAsFloat() > 0.5 ? this.CurrentTrajectory.HarvestPeriods - 1 : 0;
                    this.CurrentTrajectory.SetTreeSelection(treeIndex, harvestPeriod);
                }
            }
            else
            {
                throw new NotSupportedException(String.Format("Unhandled harvest period selection {0}.", this.Objective.HarvestPeriodSelection));
            }

            this.CurrentTrajectory.Simulate();
            this.BestTrajectory.Copy(this.CurrentTrajectory);

            this.BestObjectiveFunction = this.GetObjectiveFunction(this.CurrentTrajectory);
            if (this.ObjectiveFunctionByIteration.Count > 0)
            {
                this.ObjectiveFunctionByIteration[0] = this.BestObjectiveFunction;
            }
        }

        public abstract TimeSpan Run();
    }
}