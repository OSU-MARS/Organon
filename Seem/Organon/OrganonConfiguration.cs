﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Osu.Cof.Ferm.Organon
{
    public class OrganonConfiguration
    {
        public Bucking Bucking { get; private set; }
        public OrganonTreatments Treatments { get; private init; }
        public OrganonVariant Variant { get; private set; }

        // enable per species crown ratio growth multiplier used only for NWO
        public bool CalibrateCrownRatio { get; set; }
        // enable per species diameter growth multiplier for minor species
        public bool CalibrateDiameter { get; set; }
        // enable per species height multiplier
        public bool CalibrateHeight { get; set; }
        // enables genetic growth modifiers
        public bool Genetics { get; set; }
        // hint for error checking age ranges
        public bool IsEvenAge { get; set; }
        // enables Swiss needle cast (Nothophaeocryptopus gaeumanii) growth modifiers, applies only to NWO and SMC variants
        public bool SwissNeedleCast { get; set; }

        // Ignored if less than or equal to zero.
        public float DefaultMaximumSdi { get; set; }
        // Maximum stand density index for Abies species, ignored if less than or equal to zero. Contributes to SWO SDImax.
        public float TrueFirMaximumSdi { get; set; }
        // Maximum stand density index for western hemblock, ignored if less than or equal to zero.
        public float HemlockMaximumSdi { get; set; }
        // RVARS[5] genetic diameter growth modifier (requires Genetics = true)
        public float GWDG { get; set; }
        // RVARS[6] genetic height growth modifier (requires Genetics = true)
        public float GWHG { get; set; }
        // RVARS[7] Swiss needle cast coefficient for diameter and height growth modifiers, accepted range is [0.85 - 4.0]
        public float FR { get; set; }
        // RVARS[8] density correction coefficient for red alder height growth (WHHLB_SI_UC) and additional mortality (Mortality = true)
        public float PDEN { get; set; }

        public OrganonConfiguration(OrganonConfiguration other)
            : this(other.Variant)
        {
            this.CopyFrom(other);
        }

        public OrganonConfiguration(OrganonVariant variant)
        {
            this.Bucking = new Bucking();
            this.Treatments = new OrganonTreatments();
            this.Variant = variant;
            if (this.Variant.TreeModel == TreeModel.OrganonRap)
            {
                // only even age red alder plantations more than 10 years old are supported
                this.IsEvenAge = true;
            }
        }

        public void CopyFrom(OrganonConfiguration other)
        {
            Debug.Assert(Object.ReferenceEquals(this, other) == false);

            this.Bucking = other.Bucking;
            this.Treatments.CopyFrom(other.Treatments);
            this.Variant = other.Variant;

            this.CalibrateCrownRatio = other.CalibrateCrownRatio;
            this.CalibrateDiameter = other.CalibrateDiameter;
            this.CalibrateHeight = other.CalibrateHeight;
            this.Genetics = other.Genetics;
            this.IsEvenAge = other.IsEvenAge;
            this.SwissNeedleCast = other.SwissNeedleCast;

            this.DefaultMaximumSdi = other.DefaultMaximumSdi;
            this.HemlockMaximumSdi = other.HemlockMaximumSdi;
            this.TrueFirMaximumSdi = other.TrueFirMaximumSdi;
            this.GWDG = other.GWDG;
            this.GWHG = other.GWHG;
            this.FR = other.FR;
            this.PDEN = other.PDEN;
        }

        public Dictionary<FiaCode, SpeciesCalibration> CreateSpeciesCalibration()
        {
            ReadOnlyCollection<FiaCode> speciesList = this.Variant.TreeModel switch
            {
                TreeModel.OrganonNwo or 
                TreeModel.OrganonSmc => Constant.NwoSmcSpecies,
                TreeModel.OrganonRap => Constant.RapSpecies,
                TreeModel.OrganonSwo => Constant.SwoSpecies,
                _ => throw OrganonVariant.CreateUnhandledModelException(this.Variant.TreeModel),
            };
            Dictionary<FiaCode, SpeciesCalibration> calibration = new();
            foreach (FiaCode species in speciesList)
            {
                calibration.Add(species, new SpeciesCalibration());
            }
            return calibration;
        }
    }
}
