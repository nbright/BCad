﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using BCad.Helpers;

namespace BCad
{
    internal struct Quaternion
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double W { get; set; }

        public Quaternion(Vector axisOfRotation, double angleInDegrees)
            : this()
        {
            angleInDegrees %= 360.0;
            var angleInRadians = angleInDegrees * MathHelper.DegreesToRadians;
            var length = axisOfRotation.Length;
            if (length == 0.0)
            {
                throw new InvalidOperationException("Axis must have non-zero length");
            }

            var v = (axisOfRotation / length) * Math.Sin(0.5 * angleInRadians);
            X = v.X;
            Y = v.Y;
            Z = v.Z;
            W = Math.Cos(0.5 * angleInRadians);
        }
    }
}
