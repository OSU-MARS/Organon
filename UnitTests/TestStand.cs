﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Osu.Cof.Organon.Test
{
    public class TestStand : Stand
    {
        public int[] QuantileByInitialDbh { get; private set; }
        public Dictionary<FiaCode, List<int>> TreeIndicesBySpecies { get; private set; }

        public TestStand(OrganonVariant variant, int ageInYears, int treeCount, float primarySiteIndex)
            : base(ageInYears, treeCount, primarySiteIndex, (variant.Variant == Variant.Swo) ? 4 : 2)
        {
            this.QuantileByInitialDbh = new int[this.TreeRecordCount];
            this.TreeIndicesBySpecies = new Dictionary<FiaCode, List<int>>(this.TreeRecordCount);

            this.SetDefaultAndMortalitySiteIndices(variant);
        }

        protected TestStand(TestStand other)
            : base(other)
        {
        }

        public TestStand Clone()
        {
            return new TestStand(this);
        }

        public int GetBigSixSpeciesRecordCount()
        {
            int bigSixRecords = 0;
            for (int treeIndex = 0; treeIndex < this.TreeRecordCount; ++treeIndex)
            {
                int speciesGroup = this.SpeciesGroup[treeIndex];
                if (speciesGroup <= this.MaxBigSixSpeciesGroupIndex)
                {
                    ++bigSixRecords;
                }
            }
            return bigSixRecords;
        }

        public void SetQuantiles()
        {
            // index trees by species
            this.TreeIndicesBySpecies.Clear();
            for (int treeIndex = 0; treeIndex < this.TreeRecordCount; ++treeIndex)
            {
                FiaCode species = this.Species[treeIndex];
                if (this.TreeIndicesBySpecies.TryGetValue(species, out List<int> speciesIndices) == false)
                {
                    speciesIndices = new List<int>();
                    this.TreeIndicesBySpecies.Add(species, speciesIndices);
                }

                speciesIndices.Add(treeIndex);
            }

            // find DBH sort order of trees in each species
            // Since trees are entered by their initial diameter regardless of their ingrowth time, this includes ingrowth in quintiles
            // even though it doesn't yet exist.
            foreach (KeyValuePair<FiaCode, List<int>> treeIndicesForSpecies in this.TreeIndicesBySpecies)
            {
                // gather diameters of trees of this species and find their sort order
                List<int> speciesIndices = treeIndicesForSpecies.Value;
                float[] dbh = new float[speciesIndices.Count];
                for (int index = 0; index < speciesIndices.Count; ++index)
                {
                    dbh[index] = this.Dbh[speciesIndices[index]];
                }
                int[] dbhIndices = Enumerable.Range(0, speciesIndices.Count).ToArray();
                Array.Sort(dbh, dbhIndices);

                // assign trees to quantiles
                double dbhQuantilesAsDouble = (double)TestConstant.DbhQuantiles;
                double speciesCountAsDouble = (double)speciesIndices.Count;
                for (int index = 0; index < speciesIndices.Count; ++index)
                {
                    int treeIndex = speciesIndices[dbhIndices[index]];
                    this.QuantileByInitialDbh[treeIndex] = (int)Math.Floor(dbhQuantilesAsDouble * (double)index / speciesCountAsDouble);
                }
            }
        }

        public void WriteTreesAsCsv(TestContext testContext, OrganonVariant variant, int year, bool omitExpansionFactorZeroTrees)
        {
            for (int treeIndex = 0; treeIndex < this.TreeRecordCount; ++treeIndex)
            {
                float expansionFactor = this.LiveExpansionFactor[treeIndex];
                if (omitExpansionFactorZeroTrees && (expansionFactor == 0))
                {
                    continue;
                }

                int id = this.Tag[treeIndex] > 0 ? this.Tag[treeIndex] : treeIndex;
                FiaCode species = this.Species[treeIndex];
                int speciesGroup = this.SpeciesGroup[treeIndex];
                float dbhInInches = this.Dbh[treeIndex];
                float heightInFeet = this.Height[treeIndex];
                float crownRatio = this.CrownRatio[treeIndex];
                float deadExpansionFactor = this.DeadExpansionFactor[treeIndex];
                float dbhGrowth = this.DbhGrowth[treeIndex];
                float heightGrowth = this.HeightGrowth[treeIndex];
                testContext.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                                      variant.Variant, year, id, species, speciesGroup, 
                                      dbhInInches, heightInFeet, expansionFactor, deadExpansionFactor,
                                      crownRatio, dbhGrowth, heightGrowth);
            }
        }

        public static void WriteTreeHeader(TestContext testContext)
        {
            testContext.WriteLine("variant,year,tree,species,species group,DBH,height,expansion factor,dead expansion factor,crown ratio,diameter growth,height growth");
        }

        public StreamWriter WriteTreesToCsv(string filePath, OrganonVariant variant, int year)
        {
            FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            StreamWriter writer = new StreamWriter(stream);
            writer.WriteLine("variant,year,tree,species,species group,DBH,height,expansion factor,dead expansion factor,crown ratio,diameter growth,height growth");
            this.WriteTreesToCsv(writer, variant, year);
            return writer;
        }

        public void WriteTreesToCsv(StreamWriter writer, OrganonVariant variant, int year)
        {
            for (int treeIndex = 0; treeIndex < this.TreeRecordCount; ++treeIndex)
            {
                float expansionFactor = 2.47105F * this.LiveExpansionFactor[treeIndex];
                if (expansionFactor == 0)
                {
                    continue;
                }

                int id = this.Tag[treeIndex] > 0 ? this.Tag[treeIndex] : treeIndex;
                FiaCode species = this.Species[treeIndex];
                int speciesGroup = this.SpeciesGroup[treeIndex];
                float dbhInCentimeters = TestConstant.CmPerInch * this.Dbh[treeIndex];
                float heightInMeters = TestConstant.MetersPerFoot * this.Height[treeIndex];
                float crownRatio = this.CrownRatio[treeIndex];
                float deadExpansionFactor = TestConstant.AcresPerHectare * this.DeadExpansionFactor[treeIndex];
                float dbhGrowth = TestConstant.CmPerInch * this.DbhGrowth[treeIndex];
                float heightGrowth = TestConstant.MetersPerFoot * this.HeightGrowth[treeIndex];
                writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                                 variant.Variant, year, id, species, speciesGroup, dbhInCentimeters, heightInMeters, 
                                 expansionFactor, deadExpansionFactor, crownRatio, dbhGrowth, heightGrowth);
            }
        }
    }
}
