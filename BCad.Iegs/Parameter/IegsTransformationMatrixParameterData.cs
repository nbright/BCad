﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BCad.Iegs.Directory;
using BCad.Iegs.Entities;

namespace BCad.Iegs.Parameter
{
    internal class IegsTransformationMatrixParameterData : IegsParameterData
    {
        public double R11 { get; set; }
        public double R12 { get; set; }
        public double R13 { get; set; }

        public double R21 { get; set; }
        public double R22 { get; set; }
        public double R23 { get; set; }

        public double R31 { get; set; }
        public double R32 { get; set; }
        public double R33 { get; set; }

        public double T1 { get; set; }
        public double T2 { get; set; }
        public double T3 { get; set; }

        public override IegsEntity ToEntity(IegsDirectoryData dir)
        {
            return new IegsTransformationMatrix();
        }
    }
}
