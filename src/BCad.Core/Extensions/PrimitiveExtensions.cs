﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BCad.Entities;
using BCad.Helpers;
using BCad.Primitives;

namespace BCad.Extensions
{
    public static partial class PrimitiveExtensions
    {
        private static double[] SIN;
        private static double[] COS;
        public const int DefaultPixelBuffer = 20;

        static PrimitiveExtensions()
        {
            SIN = new double[360];
            COS = new double[360];
            double rad;
            for (int i = 0; i < 360; i++)
            {
                rad = (double)i * MathHelper.DegreesToRadians;
                SIN[i] = Math.Sin(rad);
                COS[i] = Math.Cos(rad);
            }
        }

        public static double GetThickness(this IPrimitive primitive)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Ellipse:
                    return ((PrimitiveEllipse)primitive).Thickness;
                case PrimitiveKind.Line:
                    return ((PrimitiveLine)primitive).Thickness;
                case PrimitiveKind.Point:
                case PrimitiveKind.Text:
                case PrimitiveKind.Bezier:
                    return 0.0;
                default:
                    throw new InvalidOperationException("Unsupported primitive.");
            }
        }

        public static Point ClosestPoint(this PrimitiveLine line, Point point, bool withinBounds = true)
        {
            var v = line.P1;
            var w = line.P2;
            var p = point;
            var wv = w - v;
            var l2 = wv.LengthSquared;
            if (Math.Abs(l2) < MathHelper.Epsilon)
                return v;
            var t = (p - v).Dot(wv) / l2;
            if (withinBounds)
            {
                t = Math.Max(t, 0.0 - MathHelper.Epsilon);
                t = Math.Min(t, 1.0 + MathHelper.Epsilon);
            }

            var result = v + (wv) * t;
            return result;
        }

        public static double Slope(this PrimitiveLine line)
        {
            var denom = line.P2.X - line.P1.X;
            return denom == 0.0 ? double.NaN : (line.P2.Y - line.P1.Y) / denom;
        }

        public static double PerpendicularSlope(this PrimitiveLine line)
        {
            var slope = line.Slope();
            if (double.IsNaN(slope))
                return 0.0;
            else if (slope == 0.0)
                return double.NaN;
            else
                return -1.0 / slope;
        }

        public static bool IsAngleContained(this PrimitiveEllipse ellipse, double angle)
        {
            angle = angle.CorrectAngleDegrees();
            var startAngle = ellipse.StartAngle;
            var endAngle = ellipse.EndAngle;
            if (endAngle < startAngle)
            {
                // we pass zero.  angle should either be [startAngle, 360.0] or [0.0, endAngle]
                return MathHelper.Between(startAngle, 360.0, angle)
                    || MathHelper.Between(0.0, endAngle, angle);
            }
            else
            {
                // we're within normal bounds
                return MathHelper.Between(startAngle, endAngle, angle)
                    || MathHelper.Between(startAngle, endAngle, angle + 360.0);
            }
        }

        public static PrimitiveLine Transform(this PrimitiveLine line, Matrix4 matrix)
        {
            return new PrimitiveLine(matrix.Transform(line.P1), matrix.Transform(line.P2), line.Color);
        }

        public static IEnumerable<Point> IntersectionPoints(this IPrimitive primitive, IPrimitive other, bool withinBounds = true)
        {
            IEnumerable<Point> result;
            switch (primitive.Kind)
            {
                case PrimitiveKind.Line:
                    result = ((PrimitiveLine)primitive).IntersectionPoints(other, withinBounds);
                    break;
                case PrimitiveKind.Ellipse:
                    result = ((PrimitiveEllipse)primitive).IntersectionPoints(other, withinBounds);
                    break;
                case PrimitiveKind.Point:
                    result = ((PrimitivePoint)primitive).IntersectionPoints(other, withinBounds);
                    break;
                case PrimitiveKind.Text:
                    result = Enumerable.Empty<Point>();
                    break;
                case PrimitiveKind.Bezier:
                    result = ((PrimitiveBezier)primitive).IntersectionPoints(other, withinBounds);
                    break;
                default:
                    Debug.Assert(false, "Unsupported primitive type");
                    result = Enumerable.Empty<Point>();
                    break;
            }

            return result;
        }

        #region Point-primitive intersection

        public static IEnumerable<Point> IntersectionPoints(this PrimitivePoint point, IPrimitive other, bool withinBounds = true)
        {
            IEnumerable<Point> result;
            switch (other.Kind)
            {
                case PrimitiveKind.Ellipse:
                    result = ((PrimitiveEllipse)other).IntersectionPoints(point, withinBounds);
                    break;
                case PrimitiveKind.Line:
                    result = ((PrimitiveLine)other).IntersectionPoints(point, withinBounds);
                    break;
                case PrimitiveKind.Point:
                    result = point.IntersectionPoints((PrimitivePoint)other);
                    break;
                case PrimitiveKind.Text:
                    result = Enumerable.Empty<Point>();
                    break;
                case PrimitiveKind.Bezier:
                    result = ((PrimitiveBezier)other).IntersectionPoints(point, withinBounds);
                    break;
                default:
                    Debug.Assert(false, "Unsupported primitive type");
                    result = Enumerable.Empty<Point>();
                    break;
            }

            return result;
        }

        public static IEnumerable<Point> IntersectionPoints(this PrimitivePoint first, PrimitivePoint second)
        {
            if (first.Location.CloseTo(second.Location))
                return new Point[] { first.Location };
            else
                return Enumerable.Empty<Point>();
        }

        #endregion

        #region Line-primitive intersection

        public static IEnumerable<Point> IntersectionPoints(this PrimitiveLine line, IPrimitive other, bool withinBounds = true)
        {
            IEnumerable<Point> result;
            switch (other.Kind)
            {
                case PrimitiveKind.Line:
                    var p = line.IntersectionPoint((PrimitiveLine)other, withinBounds);
                    if (p.HasValue)
                        result = new[] { p.Value };
                    else
                        result = new Point[0];
                    break;
                case PrimitiveKind.Ellipse:
                    result = line.IntersectionPoints((PrimitiveEllipse)other, withinBounds);
                    break;
                case PrimitiveKind.Point:
                    var point = (PrimitivePoint)other;
                    if (line.IsPointOnPrimitive(point.Location, withinBounds))
                    {
                        result = new Point[] { point.Location };
                    }
                    else
                    {
                        result = new Point[0];
                    }
                    break;
                case PrimitiveKind.Text:
                    result = Enumerable.Empty<Point>();
                    break;
                case PrimitiveKind.Bezier:
                    result = ((PrimitiveBezier)other).IntersectionPoints(line, withinBounds);
                    break;
                default:
                    Debug.Assert(false, "Unsupported primitive type");
                    result = Enumerable.Empty<Point>();
                    break;
            }

            return result;
        }

        #endregion

        #region Line-line intersection

        public static Point? IntersectionPoint(this PrimitiveLine first, PrimitiveLine second, bool withinSegment = true)
        {
            // TODO: need to handle a line's endpoint lying directly on the other line
            // TODO: also need to handle colinear lines

            var minLength = 0.0000000001;

            //http://local.wasp.uwa.edu.au/~pbourke/geometry/lineline3d/
            // find real 3D intersection within a minimum distance
            var p1 = first.P1;
            var p2 = first.P2;
            var p3 = second.P1;
            var p4 = second.P2;
            var p13 = p1 - p3;
            var p43 = p4 - p3;

            if (p43.LengthSquared < MathHelper.Epsilon)
                return null;

            var p21 = p2 - p1;
            if (p21.LengthSquared < MathHelper.Epsilon)
                return null;

            var d1343 = p13.Dot(p43);
            var d4321 = p43.Dot(p21);
            var d1321 = p13.Dot(p21);
            var d4343 = p43.Dot(p43);
            var d2121 = p21.Dot(p21);

            var denom = d2121 * d4343 - d4321 * d4321;
            if (Math.Abs(denom) < MathHelper.Epsilon)
                return null;

            var num = d1343 * d4321 - d1321 * d4343;
            var mua = num / denom;
            var mub = (d1343 + d4321 * mua) / d4343;

            if (withinSegment)
            {
                if (!MathHelper.Between(0.0, 1.0, mua) ||
                    !MathHelper.Between(0.0, 1.0, mub))
                {
                    return null;
                }
            }

            var connector = new PrimitiveLine((p21 * mua) + p1, (p43 * mub) + p3);
            var cv = connector.P1 - connector.P2;
            if (cv.LengthSquared > minLength)
                return null;

            var point = (Point)((connector.P1 + connector.P2) / 2);
            return point;
        }

        #endregion

        #region Line-circle intersection

        public static IEnumerable<Point> IntersectionPoints(this PrimitiveLine line, PrimitiveEllipse ellipse, bool withinSegment = true)
        {
            var empty = Enumerable.Empty<Point>();

            var l0 = line.P1;
            var l = line.P2 - line.P1;
            var p0 = ellipse.Center;
            var n = ellipse.Normal;

            var right = ellipse.MajorAxis.Normalize();
            var up = ellipse.Normal.Cross(right).Normalize();
            var radiusX = ellipse.MajorAxis.Length;
            var radiusY = radiusX * ellipse.MinorAxisRatio;
            var transform = ellipse.FromUnitCircle;
            var inverse = transform.Inverse();

            var denom = l.Dot(n);
            var num = (p0 - l0).Dot(n);

            var flatPoints = new List<Point>();

            if (Math.Abs(denom) < MathHelper.Epsilon)
            {
                // plane either contains the line entirely or is parallel
                if (Math.Abs(num) < MathHelper.Epsilon)
                {
                    // parallel.  normalize the plane and find the intersection
                    var flatLine = line.Transform(inverse);
                    // the ellipse is now centered at the origin with a radius of 1.
                    // find the intersection points then reproject
                    var dv = flatLine.P2 - flatLine.P1;
                    var dx = dv.X;
                    var dy = dv.Y;
                    var dr2 = dx * dx + dy * dy;
                    var D = flatLine.P1.X * flatLine.P2.Y - flatLine.P2.X * flatLine.P1.Y;
                    var det = dr2 - D * D;
                    var points = new List<Point>();
                    if (det < 0.0 || Math.Abs(dr2) < MathHelper.Epsilon)
                    {
                        // no intersection or line is too short
                    }
                    else if (Math.Abs(det) < MathHelper.Epsilon)
                    {
                        // 1 point
                        var x = (D * dy) / dr2;
                        var y = (-D * dx) / dr2;
                        points.Add(new Point(x, y, 0.0));
                    }
                    else
                    {
                        // 2 points
                        var sgn = dy < 0.0 ? -1.0 : 1.0;
                        var det2 = Math.Sqrt(det);
                        points.Add(
                            new Point(
                                (D * dy + sgn * dx * det2) / dr2,
                                (-D * dx + Math.Abs(dy) * det2) / dr2,
                                0.0));
                        points.Add(
                            new Point(
                                (D * dy - sgn * dx * det2) / dr2,
                                (-D * dx - Math.Abs(dy) * det2) / dr2,
                                0.0));
                    }

                    // ensure the points are within appropriate bounds
                    if (withinSegment)
                    {
                        // line test
                        points = points.Where(p =>
                            MathHelper.Between(flatLine.P1.X, flatLine.P2.X, p.X) &&
                            MathHelper.Between(flatLine.P1.Y, flatLine.P2.Y, p.Y)).ToList();

                        // circle test
                        points = points.Where(p => ellipse.IsAngleContained(((Vector)p).ToAngle())).ToList();
                    }

                    return points.Select(p => (Point)transform.Transform(p));
                }
            }
            else
            {
                // otherwise line and plane intersect in only 1 point, p
                var d = num / denom;
                var p = (Point)(l * d + l0);

                // verify within the line segment
                if (withinSegment && !MathHelper.Between(0.0, 1.0, d))
                {
                    return empty;
                }

                // p is the point of intersection.  verify if on transformed unit circle
                var unitVector = (Vector)inverse.Transform(p);
                if (Math.Abs(unitVector.Length - 1.0) < MathHelper.Epsilon)
                {
                    // point is on the unit circle
                    if (withinSegment)
                    {
                        // verify within the angles specified
                        var angle = Math.Atan2(unitVector.Y, unitVector.X) * MathHelper.RadiansToDegrees;
                        if (MathHelper.Between(ellipse.StartAngle, ellipse.EndAngle, angle.CorrectAngleDegrees()))
                        {
                            return new[] { p };
                        }
                    }
                    else
                    {
                        return new[] { p };
                    }
                }
            }

            // point is not on unit circle
            return empty;
        }

        #endregion

        #region Circle-primitive intersection

        public static IEnumerable<Point> IntersectionPoints(this PrimitiveEllipse ellipse, IPrimitive primitive, bool withinBounds = true)
        {
            IEnumerable<Point> result;
            switch (primitive.Kind)
            {
                case PrimitiveKind.Ellipse:
                    result = ellipse.IntersectionPoints((PrimitiveEllipse)primitive, withinBounds);
                    break;
                case PrimitiveKind.Line:
                    result = ((PrimitiveLine)primitive).IntersectionPoints(ellipse, withinBounds);
                    break;
                case PrimitiveKind.Point:
                    result = ellipse.IntersectionPoints((PrimitivePoint)primitive, withinBounds);
                    break;
                case PrimitiveKind.Text:
                    result = Enumerable.Empty<Point>();
                    break;
                case PrimitiveKind.Bezier:
                    result = ((PrimitiveBezier)primitive).IntersectionPoints(ellipse, withinBounds);
                    break;
                default:
                    Debug.Assert(false, "Unsupported primitive type");
                    result = Enumerable.Empty<Point>();
                    break;
            }

            return result;
        }

        #endregion

        #region Circle-point intersection

        public static IEnumerable<Point> IntersectionPoints(this PrimitiveEllipse ellipse, PrimitivePoint point, bool withinBounds = true)
        {
            var fromUnit = ellipse.FromUnitCircle;
            var toUnit = fromUnit.Inverse();
            var pointOnUnit = (Vector)toUnit.Transform(point.Location);
            if (MathHelper.CloseTo(pointOnUnit.LengthSquared, 1.0))
            {
                if (!withinBounds)
                    return new Point[] { point.Location }; // always a match
                else
                {
                    var pointAngle = pointOnUnit.ToAngle();
                    if (ellipse.IsAngleContained(pointAngle))
                        return new Point[] { point.Location };
                    else
                        return new Point[0];
                }
            }
            else
            {
                // wrong distance
                return new Point[0];
            }
        }

        #endregion

        #region Circle-circle intersection

        public static IEnumerable<Point> IntersectionPoints(this PrimitiveEllipse first, PrimitiveEllipse second, bool withinBounds = true)
        {
            var empty = Enumerable.Empty<Point>();
            IEnumerable<Point> results;

            // planes either intersect in a line or are parallel
            var lineVector = first.Normal.Cross(second.Normal);
            if (lineVector.IsZeroVector)
            {
                // parallel or the same plane
                if ((first.Center == second.Center) ||
                    Math.Abs((second.Center - first.Center).Dot(first.Normal)) < MathHelper.Epsilon)
                {
                    // if they share a point or second.Center is on the first plane, they are the same plane
                    // project second back to a unit circle and find intersection points
                    var fromUnit = first.FromUnitCircle;
                    var toUnit = fromUnit.Inverse();

                    // transform second ellipse to be on the unit circle's plane
                    var secondCenter = toUnit.Transform(second.Center);
                    var secondMajorEnd = toUnit.Transform(second.Center + second.MajorAxis);
                    var secondMinorEnd = toUnit.Transform(second.Center + (second.Normal.Cross(second.MajorAxis).Normalize() * second.MajorAxis.Length * second.MinorAxisRatio));
                    RoundedDouble a = (secondMajorEnd - secondCenter).Length;
                    RoundedDouble b = (secondMinorEnd - secondCenter).Length;

                    if (a == b)
                    {
                        // if second ellipse is a circle we can absolutely solve for the intersection points
                        // rotate to place the center of the second circle on the x-axis
                        var angle = ((Vector)secondCenter).ToAngle();
                        var rotation = Matrix4.RotateAboutZ(angle);
                        var returnTransform = fromUnit * Matrix4.RotateAboutZ(-angle);
                        var newSecondCenter = rotation.Transform(secondCenter);
                        var secondRadius = a;

                        if (Math.Abs(newSecondCenter.X) > secondRadius + 1.0)
                        {
                            // no points of intersection
                            results = empty;
                        }
                        else
                        {
                            // 2 points of intersection
                            var x = (secondRadius * secondRadius - newSecondCenter.X * newSecondCenter.X - 1.0)
                                / (-2.0 * newSecondCenter.X);
                            var y = Math.Sqrt(1.0 - x * x);
                            results = new[]
                                {
                                    new Point(x, y, 0),
                                    new Point(x, -y, 0)
                                };
                        }

                        results = results.Distinct().Select(p => returnTransform.Transform(p));
                    }
                    else
                    {
                        // rotate about the origin to make the major axis align with the x-axis
                        var angle = (secondMajorEnd - secondCenter).ToAngle();
                        var rotation = Matrix4.RotateAboutZ(angle);
                        var finalCenter = rotation.Transform(secondCenter);
                        fromUnit = fromUnit * Matrix4.RotateAboutZ(-angle);
                        toUnit = fromUnit.Inverse();

                        if (a < b)
                        {
                            // rotate to ensure a > b
                            fromUnit = fromUnit * Matrix4.RotateAboutZ(90);
                            toUnit = fromUnit.Inverse();
                            finalCenter = Matrix4.RotateAboutZ(-90).Transform(finalCenter);

                            // and swap a and b
                            var temp = a;
                            a = b;
                            b = temp;
                        }

                        RoundedDouble h = finalCenter.X;
                        RoundedDouble k = finalCenter.Y;
                        var h2 = h * h;
                        var k2 = k * k;
                        var a2 = a * a;
                        var b2 = b * b;
                        var a4 = a2 * a2;
                        var b4 = b2 * b2;

                        // RoundedDouble is used to ensure all operations are rounded to a certain precision
                        if (Math.Abs(h) < MathHelper.Epsilon)
                        {
                            // ellipse x = 0; wide
                            // find x values
                            var A = a2 - a2 * b2 + a2 * k2 - ((2 * a4 * k2) / (a2 - b2));
                            var B = (2 * a2 * k * Math.Sqrt(b2 * (-a2 + a4 + b2 - a2 * b2 + a2 * k2))) / (a2 - b2);
                            var C = (RoundedDouble)Math.Sqrt(a2 - b2);

                            var x1 = -Math.Sqrt(A + B) / C;
                            var x2 = (RoundedDouble)(-x1);
                            var x3 = -Math.Sqrt(A - B) / C;
                            var x4 = (RoundedDouble)(-x3);

                            // find y values
                            var D = a2 * k;
                            var E = Math.Sqrt(-a2 * b2 + a4 * b2 + b4 - a2 * b4 + a2 * b2 * k2);
                            var F = a2 - b2;

                            var y1 = (D - E) / F;
                            var y2 = (D + E) / F;

                            results = new[] {
                                new Point(x1, y1, 0),
                                new Point(x2, y1, 0),
                                new Point(x3, y2, 0),
                                new Point(x4, y2, 0)
                            };
                        }
                        else if (Math.Abs(k) < MathHelper.Epsilon)
                        {
                            // ellipse y = 0; wide
                            // find x values
                            var A = -b2 * h;
                            var B = Math.Sqrt(a4 - a2 * b2 - a4 * b2 + a2 * b4 + a2 * b2 * h2);
                            var C = a2 - b2;                            

                            var x1 = (A - B) / C;
                            var x2 = (A + B) / C;
                            
                            // find y values
                            var D = -b2 + a2 * b2 - b2 * h2 - ((2 * b4 * h2) / (a2 - b2));
                            var E = (2 * b2 * h * Math.Sqrt(a2 * (a2 - b2 - a2 * b2 + b4 + b2 * h2))) / (a2 - b2);
                            var F = (RoundedDouble)Math.Sqrt(a2 - b2);

                            var y1 = -Math.Sqrt(D - E) / F;
                            var y2 = (RoundedDouble)(-y1);
                            var y3 = -Math.Sqrt(D + E) / F;
                            var y4 = (RoundedDouble)(-y3);

                            results = new[] {
                                new Point(x1, y1, 0),
                                new Point(x1, y2, 0),
                                new Point(x2, y3, 0),
                                new Point(x2, y4, 0)
                            };
                        }
                        else
                        {
                            // can't be solved, fall back to bezier curve approximations
                            var points = new List<Point>();
                            foreach (var curveA in first.AsBezierCurves())
                            {
                                foreach (var curveB in second.AsBezierCurves())
                                {
                                    foreach (var point in curveA.IntersectionPoints(curveB))
                                    {
                                        if (!points.Any(p => p.CloseTo(point)))
                                        {
                                            points.Add(point);
                                        }
                                    }
                                }
                            }

                            results = points;
                        }

                        results = results
                            .Where(p => !(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z)))
                            .Where(p => !(double.IsInfinity(p.X) || double.IsInfinity(p.Y) || double.IsInfinity(p.Z)))
                            .Distinct()
                            .Select(p => fromUnit.Transform(p))
                            .Select(p => new Point((RoundedDouble)p.X, (RoundedDouble)p.Y, (RoundedDouble)p.Z));
                    }
                }
                else
                {
                    // parallel with no intersections
                    results = empty;
                }
            }
            else
            {
                // intersection was a line
                // find a common point to complete the line then intersect that line with the circles
                double x = 0, y = 0, z = 0;
                var n = first.Normal;
                var m = second.Normal;
                var q = first.Center;
                var r = second.Center;
                if (Math.Abs(lineVector.X) >= MathHelper.Epsilon)
                {
                    x = 0.0;
                    y = (-m.Z * n.X * q.X - m.Z * n.Y * q.Y - m.Z * n.Z * q.Z + m.X * n.Z * r.X + m.Y * n.Z * r.Y + m.Z * n.Z * r.Z)
                        / (-m.Z * n.Y + m.Y * n.Z);
                    z = (-m.Y * n.X * q.X - m.Y * n.Y * q.Y - m.Y * n.Z * q.Z + m.X * n.Y * r.X + m.Y * n.Y * r.Y + m.Z * n.Y * r.Z)
                        / (-m.Z * n.Y + m.Y * n.Z);
                }
                else if (Math.Abs(lineVector.Y) >= MathHelper.Epsilon)
                {
                    x = (-m.Z * n.X * q.X - m.Z * n.Y * q.Y - m.Z * n.Z * q.Z + m.X * n.Z * r.X + m.Y * n.Z * r.Y + m.Z * n.Z * r.Z)
                        / (-m.Z * n.X + m.X * n.Z);
                    y = 0.0;
                    z = (-m.X * n.X * q.X - m.X * n.Y * q.Y - m.X * n.Z * q.Z + m.X * n.X * r.X + m.Y * n.X * r.Y + m.Z * n.X * r.Z)
                        / (-m.Z * n.X + m.X * n.Z);
                }
                else if (Math.Abs(lineVector.Z) >= MathHelper.Epsilon)
                {
                    x = (-m.Y * n.X * q.X - m.Y * n.Y * q.Y - m.Y * n.Z * q.Z + m.X * n.Y * r.X + m.Y * n.Y * r.Y + m.Z * n.Y * r.Z)
                        / (m.Y * n.X - m.X * n.Y);
                    y = (-m.X * n.X * q.X - m.X * n.Y * q.Y - m.X * n.Z * q.Z + m.X * n.X * r.X + m.Y * n.X * r.Y + m.Z * n.X * r.Z)
                        / (m.Y * n.X - m.X * n.Y);
                    z = 0.0;
                }
                else
                {
                    Debug.Assert(false, "zero-vector shouldn't get here");
                }

                var point = new Point(x, y, z);
                var other = point + lineVector;
                var intersectionLine = new PrimitiveLine(point, other);
                var firstIntersections = intersectionLine.IntersectionPoints(first, false)
                    .Select(p => new Point((RoundedDouble)p.X, (RoundedDouble)p.Y, (RoundedDouble)p.Z));
                var secondIntersections = intersectionLine.IntersectionPoints(second, false)
                    .Select(p => new Point((RoundedDouble)p.X, (RoundedDouble)p.Y, (RoundedDouble)p.Z));
                results = firstIntersections
                    .Union(secondIntersections)
                    .Distinct();
            }

            // verify points are in angle bounds
            if (withinBounds)
            {
                var toFirstUnit = first.FromUnitCircle.Inverse();
                var toSecondUnit = second.FromUnitCircle.Inverse();
                results = from res in results
                          // verify point is within first ellipse's angles
                          let trans1 = toFirstUnit.Transform(res)
                          let ang1 = ((Vector)trans1).ToAngle()
                          where first.IsAngleContained(ang1)
                          // and same for second
                          let trans2 = toSecondUnit.Transform(res)
                          let ang2 = ((Vector)trans2).ToAngle()
                          where second.IsAngleContained(ang2)
                          select res;
            }

            return results;
        }

        #endregion

        #region Bezier-primitive intersection

        public static IEnumerable<Point> IntersectionPoints(this PrimitiveBezier bezier, IPrimitive other, bool withinBounds = true)
        {
            IEnumerable<Point> result;
            switch (other.Kind)
            {
                case PrimitiveKind.Ellipse:
                    result = bezier.IntersectionPoints((PrimitiveEllipse)other, withinBounds);
                    break;
                case PrimitiveKind.Line:
                    result = bezier.IntersectionPoints((PrimitiveLine)other, withinBounds);
                    break;
                case PrimitiveKind.Point:
                    var point = (PrimitivePoint)other;
                    result = bezier.IsPointOnPrimitive(point.Location) ? new[] { point.Location } : Enumerable.Empty<Point>();
                    break;
                case PrimitiveKind.Text:
                    result = Enumerable.Empty<Point>();
                    break;
                case PrimitiveKind.Bezier:
                    result = bezier.IntersectionPoints((PrimitiveBezier)other);
                    break;
                default:
                    Debug.Assert(false, "Unsupported primitive type");
                    result = Enumerable.Empty<Point>();
                    break;
            }

            return result;
        }

        private static IEnumerable<Point> IntersectionPoints(this PrimitiveBezier bezier, PrimitiveEllipse ellipse, bool withinBounds = true)
        {
            var ellipseCurves = ellipse.AsBezierCurves();
            var allPoints = ellipseCurves.SelectMany(ec => bezier.IntersectionPoints(ec, withinBounds));

            // correct all points to be exactly on the ellipse according to their angle
            // this accounts for very minor innacuracies with bezier-bezier intersections and
            // ensures all found points lie exactly on the ellipse
            var fromUnitCircle = ellipse.FromUnitCircle;
            var toUnitCircle = fromUnitCircle.Inverse();
            var pointAngles = allPoints.Select(toUnitCircle.Transform)
                .Select(p => Math.Atan2(p.Y, p.X));
            var angleCorrectedPoints = pointAngles.Select(a => new Point(Math.Cos(a), Math.Sin(a), 0.0))
                .Select(fromUnitCircle.Transform);
            if (withinBounds)
            {
                // some points might not be on the actual ellipse, so we need to check this again
                angleCorrectedPoints = angleCorrectedPoints.Where(p => ellipse.IsPointOnPrimitive(p));
            }

            return angleCorrectedPoints;
        }

        private static IEnumerable<Point> IntersectionPoints(this PrimitiveBezier bezier, PrimitiveLine line, bool withinBounds = true)
        {
            // rotate curve and line such that the line now lies on the X-axis; find zeros of curve and undo the transformation
            var delta = line.P2 - line.P1;
            var transformToOrigin =
                Matrix4.RotateAboutZ(Math.Atan2(delta.Y, delta.X) * MathHelper.RadiansToDegrees) *
                Matrix4.CreateTranslate(-line.P1.X, -line.P1.Y, 0.0);

            // verify the transformation is correct
            Debug.Assert(transformToOrigin.Transform(line.P1).CloseTo(Point.Origin));
            Debug.Assert(MathHelper.CloseTo(0.0, transformToOrigin.Transform(line.P2).Y));

            var transformBack = transformToOrigin.Inverse();
            var transformedBezier = new PrimitiveBezier(
                transformToOrigin.Transform(bezier.P1),
                transformToOrigin.Transform(bezier.P2),
                transformToOrigin.Transform(bezier.P3),
                transformToOrigin.Transform(bezier.P4));
            var zeros = transformedBezier.FindYRoots();
            if (withinBounds)
            {
                zeros = zeros.Where(t => t >= 0.0 && t <= 1.0);
            }

            var zeroPoints = zeros
                .Select(transformedBezier.ComputeParameterizedPoint)
                .Select(transformBack.Transform);

            if (withinBounds)
            {
                zeroPoints = zeroPoints.Where(line.IsPointOnPrimitive);
            }

            return zeroPoints;
        }

        private static IEnumerable<Point> IntersectionPoints(this PrimitiveBezier bezier, PrimitiveBezier other)
        {
            var points = new List<Point>();
            var queue = new Queue<Tuple<PrimitiveBezier, PrimitiveBezier>>();
            queue.Enqueue(Tuple.Create(bezier, other));
            while (queue.Count > 0)
            {
                var pair = queue.Dequeue();
                var first = pair.Item1;
                var second = pair.Item2;
                var boxA = first.GetBoundingBox();
                var boxB = second.GetBoundingBox();
                if (boxA.Intersects(boxB))
                {
                    var epsilon = MathHelper.Epsilon;
                    if (boxA.Size.X <= epsilon && boxB.Size.X <= epsilon &&
                        boxA.Size.Y <= epsilon && boxB.Size.Y <= epsilon &&
                        boxA.Size.Z <= epsilon && boxB.Size.Z <= epsilon)
                    {
                        // if bounding boxes are sufficiently small, it's an intersection point
                        var candidatePoint = boxA.MinimumPoint;
                        if (!points.Any(p => p.CloseTo(candidatePoint)))
                        {
                            // only keep unique-ish points, since bezier-bezier intersections aren't exact
                            // checking all points shouldn't be too expensive since there are a maximum of 9
                            // intersection points between any two given bezier curves
                            points.Add(candidatePoint);
                        }
                    }
                    else
                    {
                        // otherwise split all curves and continue the loop
                        var pair1 = first.Split(0.5);
                        var pair2 = second.Split(0.5);
                        var a = pair1.Item1;
                        var b = pair1.Item2;
                        var c = pair2.Item1;
                        var d = pair2.Item2;
                        queue.Enqueue(Tuple.Create(a, c));
                        queue.Enqueue(Tuple.Create(a, d));
                        queue.Enqueue(Tuple.Create(b, c));
                        queue.Enqueue(Tuple.Create(b, d));
                    }
                }
                else
                {
                    // no intersection of bounding boxes so no intersection of curves
                }
            }

            return points;
        }

        #endregion

        public static Entity ToEntity(this IPrimitive primitive)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Ellipse:
                    var el = (PrimitiveEllipse)primitive;
                    if (el.MinorAxisRatio == 1.0)
                    {
                        // circle or arc
                        if (el.IsClosed)
                        {
                            // circle
                            return new Circle(el);
                        }
                        else
                        {
                            // arc
                            return new Arc(el);
                        }
                    }
                    else
                    {
                        return new Ellipse(el);
                    }
                case PrimitiveKind.Line:
                    return new Line((PrimitiveLine)primitive);
                case PrimitiveKind.Point:
                    return new Location((PrimitivePoint)primitive);
                case PrimitiveKind.Text:
                    return new Text((PrimitiveText)primitive);
                case PrimitiveKind.Bezier:
                    return Spline.FromBezier((PrimitiveBezier)primitive);
                default:
                    throw new ArgumentException("primitive.Kind");
            }
        }

        public static IPrimitive Move(this IPrimitive primitive, Vector offset)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Ellipse:
                    var el = (PrimitiveEllipse)primitive;
                    return new PrimitiveEllipse(
                        el.Center + offset,
                        el.MajorAxis,
                        el.Normal,
                        el.MinorAxisRatio,
                        el.StartAngle,
                        el.EndAngle,
                        el.Color);
                case PrimitiveKind.Line:
                    var line = (PrimitiveLine)primitive;
                    return new PrimitiveLine(
                        line.P1 + offset,
                        line.P2 + offset,
                        line.Color);
                case PrimitiveKind.Point:
                    var point = (PrimitivePoint)primitive;
                    return new PrimitivePoint(
                        point.Location + offset,
                        point.Color);
                case PrimitiveKind.Text:
                    var text = (PrimitiveText)primitive;
                    return new PrimitiveText(
                        text.Value,
                        text.Location + offset,
                        text.Height,
                        text.Normal,
                        text.Rotation,
                        text.Color);
                case PrimitiveKind.Bezier:
                    var bezier = (PrimitiveBezier)primitive;
                    return new PrimitiveBezier(
                        bezier.P1 + offset,
                        bezier.P2 + offset,
                        bezier.P3 + offset,
                        bezier.P4 + offset,
                        bezier.Color);
                default:
                    throw new ArgumentException("primitive.Kind");
            }
        }

        public static bool IsPointOnPrimitive(this IPrimitive primitive, Point point)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Line:
                    return IsPointOnPrimitive((PrimitiveLine)primitive, point);
                case PrimitiveKind.Ellipse:
                    return IsPointOnPrimitive((PrimitiveEllipse)primitive, point);
                case PrimitiveKind.Text:
                    return IsPointOnPrimitive((PrimitiveText)primitive, point);
                case PrimitiveKind.Bezier:
                    return IsPointOnPrimitive((PrimitiveBezier)primitive, point);
                default:
                    Debug.Assert(false, "unexpected primitive: " + primitive.Kind);
                    return false;
            }
        }

        private static bool IsPointOnPrimitive(this PrimitiveLine line, Point point, bool withinSegment = true)
        {
            if (point == line.P1)
                return true;
            var lineVector = line.P2 - line.P1;
            var pointVector = point - line.P1;
            return (lineVector.Normalize().CloseTo(pointVector.Normalize()))
                && (!withinSegment
                    || MathHelper.Between(0.0, lineVector.LengthSquared, pointVector.LengthSquared));
        }

        private static bool IsPointOnPrimitive(this PrimitiveEllipse el, Point point)
        {
            var unitPoint = el.FromUnitCircle.Inverse().Transform(point);
            return MathHelper.CloseTo(0.0, unitPoint.Z) // on the XY plane
                && MathHelper.CloseTo(1.0, ((Vector)unitPoint).LengthSquared) // on the unit circle
                && el.IsAngleContained(Math.Atan2(unitPoint.Y, unitPoint.X) * MathHelper.RadiansToDegrees); // within angle bounds
        }

        private static bool IsPointOnPrimitive(this PrimitiveText text, Point point)
        {
            // check for plane containment
            var plane = new Plane(text.Location, text.Normal);
            if (plane.Contains(point))
            {
                // check for horizontal/vertical containment
                var right = Vector.RightVectorFromNormal(text.Normal);
                var up = text.Normal.Cross(right).Normalize();
                var projection = Matrix4.FromUnitCircleProjection(text.Normal, right, up, text.Location, 1.0, 1.0, 1.0).Inverse();

                var projected = projection.Transform(point);
                if (MathHelper.Between(0.0, text.Width, projected.X) &&
                    MathHelper.Between(0.0, text.Height, projected.Y))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointOnPrimitive(this PrimitiveBezier bezier, Point point)
        {
            var t = bezier.GetParameterValueForPoint(point);
            return t.HasValue;
        }

        public static double GetAngle(this PrimitiveEllipse ellipse, Point point)
        {
            var transform = ellipse.FromUnitCircle.Inverse();
            var pointFromUnitCircle = transform.Transform(point);
            var angle = Math.Atan2(pointFromUnitCircle.Y, pointFromUnitCircle.X) * MathHelper.RadiansToDegrees;
            return angle.CorrectAngleDegrees();
        }

        public static Point GetPoint(this PrimitiveEllipse ellipse, double angle)
        {
            var pointUnit = new Point(Math.Cos(angle * MathHelper.DegreesToRadians), Math.Sin(angle * MathHelper.DegreesToRadians), 0.0);
            var pointTransformed = ellipse.FromUnitCircle.Transform(pointUnit);
            return pointTransformed;
        }

        public static Point[] GetInterestingPoints(this IPrimitive primitive)
        {
            Point[] points;
            switch (primitive.Kind)
            {
                case PrimitiveKind.Ellipse:
                    var ellipse = (PrimitiveEllipse)primitive;
                    points = ellipse.GetInterestingPoints(360);
                    break;
                case PrimitiveKind.Line:
                    var line = (PrimitiveLine)primitive;
                    points = new[] { line.P1, line.P2 };
                    break;
                case PrimitiveKind.Point:
                    points = new[] { ((PrimitivePoint)primitive).Location };
                    break;
                case PrimitiveKind.Text:
                    var text = (PrimitiveText)primitive;
                    var rad = text.Rotation * MathHelper.DegreesToRadians;
                    var right = new Vector(Math.Cos(rad), Math.Sin(rad), 0.0).Normalize() * text.Width;
                    var up = text.Normal.Cross(right).Normalize() * text.Height;
                    points = new[]
                        {
                            text.Location,
                            text.Location + right,
                            text.Location + right + up,
                            text.Location + up,
                            text.Location
                        };
                    break;
                case PrimitiveKind.Bezier:
                    var bezier = (PrimitiveBezier)primitive;
                    points = new[] { bezier.P1, bezier.P2, bezier.P3, bezier.P4 };
                    break;
                default:
                    throw new InvalidOperationException();
            }

            return points;
        }

        public static Point[] GetInterestingPoints(this PrimitiveEllipse ellipse, int maxSeg)
        {
            var startAngleDeg = ellipse.StartAngle;
            var endAngleDeg = ellipse.EndAngle;
            if (endAngleDeg < startAngleDeg)
                endAngleDeg += MathHelper.ThreeSixty;
            var startAngleRad = startAngleDeg * MathHelper.DegreesToRadians;
            var endAngleRad = endAngleDeg * MathHelper.DegreesToRadians;
            if (endAngleRad < startAngleRad)
                endAngleRad += MathHelper.TwoPI;
            var vertexCount = (int)Math.Ceiling((endAngleDeg - startAngleDeg) / MathHelper.ThreeSixty * maxSeg);
            var points = new Point[vertexCount + 1];
            var angleDelta = MathHelper.ThreeSixty / maxSeg * MathHelper.DegreesToRadians;
            var trans = ellipse.FromUnitCircle;
            double angle;
            int i;
            for (angle = startAngleRad, i = 0; i < vertexCount; angle += angleDelta, i++)
            {
                points[i] = trans.Transform(new Point(Math.Cos(angle), Math.Sin(angle), 0.0));
            }

            points[i] = trans.Transform(new Point(Math.Cos(angle), Math.Sin(angle), 0.0));
            return points;
        }

        public static ViewPort ShowAllViewPort(this IEnumerable<IPrimitive> primitives, Vector sight, Vector up, double viewPortWidth, double viewPortHeight, int pixelBuffer = DefaultPixelBuffer)
        {
            var drawingPlane = new Plane(Point.Origin, sight);
            var planeProjection = drawingPlane.ToXYPlaneProjection();
            var allPoints = primitives
                .SelectMany(p => p.GetInterestingPoints())
                .Select(p => planeProjection.Transform(p));

            if (allPoints.Count() < 2)
                return new ViewPort(Point.Origin, Vector.ZAxis, Vector.YAxis, 10.0);

            var first = allPoints.First();
            var minx = first.X;
            var miny = first.Y;
            var maxx = first.X;
            var maxy = first.Y;
            foreach (var point in allPoints.Skip(1))
            {
                if (point.X < minx)
                    minx = point.X;
                if (point.X > maxx)
                    maxx = point.X;
                if (point.Y < miny)
                    miny = point.Y;
                if (point.Y > maxy)
                    maxy = point.Y;
            }

            var deltaX = maxx - minx;
            var deltaY = maxy - miny;

            // translate back out of XY plane
            var unproj = planeProjection.Inverse();
            var bottomLeft = unproj.Transform(new Point(minx, miny, 0));
            var topRight = unproj.Transform(new Point(minx + deltaX, miny + deltaY, 0));
            var drawingHeight = deltaY;
            var drawingWidth = deltaX;

            double viewHeight, drawingToViewScale;
            var viewRatio = viewPortWidth / viewPortHeight;
            var drawingRatio = drawingWidth / drawingHeight;
            if (MathHelper.CloseTo(0.0, drawingHeight) || drawingRatio > viewRatio)
            {
                // fit to width
                var viewWidth = drawingWidth;
                viewHeight = viewPortHeight * viewWidth / viewPortWidth;
                drawingToViewScale = viewPortWidth / viewWidth;

                // add a buffer of some amount of pixels
                var pixelWidth = drawingWidth * drawingToViewScale;
                var newPixelWidth = pixelWidth + (pixelBuffer * 2);
                viewHeight *= newPixelWidth / pixelWidth;
            }
            else
            {
                // fit to height
                viewHeight = drawingHeight;
                drawingToViewScale = viewPortHeight / viewHeight;

                // add a buffer of some amount of pixels
                var pixelHeight = drawingHeight * drawingToViewScale;
                var newPixelHeight = pixelHeight + (pixelBuffer * 2);
                viewHeight *= newPixelHeight / pixelHeight;
            }

            // center viewport
            var tempViewport = new ViewPort(bottomLeft, sight, up, viewHeight);
            var pixelMatrix = tempViewport.GetTransformationMatrixWindowsStyle(viewPortWidth, viewPortHeight);
            var bottomLeftScreen = pixelMatrix.Transform(bottomLeft);
            var topRightScreen = pixelMatrix.Transform(topRight);

            // center horizontally
            var leftXGap = bottomLeftScreen.X;
            var rightXGap = viewPortWidth - topRightScreen.X;
            var xAdjust = Math.Abs((rightXGap - leftXGap) / 2.0) / drawingToViewScale;

            // center vertically
            var topYGap = topRightScreen.Y;
            var bottomYGap = viewPortHeight - bottomLeftScreen.Y;
            var yAdjust = Math.Abs((topYGap - bottomYGap) / 2.0) / drawingToViewScale;

            var newBottomLeft = new Point(bottomLeft.X - xAdjust, bottomLeft.Y - yAdjust, bottomLeft.Z);

            var newVp = tempViewport.Update(bottomLeft: newBottomLeft, viewHeight: viewHeight);
            return newVp;
        }

        public static Point StartPoint(this IPrimitive primitive)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Line:
                    return ((PrimitiveLine)primitive).P1;
                case PrimitiveKind.Ellipse:
                    var el = (PrimitiveEllipse)primitive;
                    return el.GetPoint(el.StartAngle);
                case PrimitiveKind.Point:
                    return ((PrimitivePoint)primitive).Location;
                case PrimitiveKind.Bezier:
                    return ((PrimitiveBezier)primitive).P1;
                default:
                    throw new ArgumentException($"{nameof(primitive)}.{nameof(primitive.Kind)}");
            }
        }

        public static Point EndPoint(this IPrimitive primitive)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Line:
                    return ((PrimitiveLine)primitive).P2;
                case PrimitiveKind.Ellipse:
                    var el = (PrimitiveEllipse)primitive;
                    return el.GetPoint(el.EndAngle);
                case PrimitiveKind.Point:
                    return ((PrimitivePoint)primitive).Location;
                case PrimitiveKind.Bezier:
                    return ((PrimitiveBezier)primitive).P4;
                default:
                    throw new ArgumentException($"{nameof(primitive)}.{nameof(primitive.Kind)}");
            }
        }

        public static Point MidPoint(this IPrimitive primitive)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Line:
                    var line = (PrimitiveLine)primitive;
                    return (line.P1 + line.P2) / 2;
                case PrimitiveKind.Ellipse:
                    var el = (PrimitiveEllipse)primitive;
                    var endAngle = el.EndAngle;
                    if (endAngle < el.StartAngle)
                    {
                        endAngle += MathHelper.ThreeSixty;
                    }

                    return el.GetPoint((el.StartAngle + endAngle) * 0.5);
                case PrimitiveKind.Point:
                    return ((PrimitivePoint)primitive).Location;
                case PrimitiveKind.Bezier:
                    return ((PrimitiveBezier)primitive).ComputeParameterizedPoint(0.5); // find midpoint by length?
                case PrimitiveKind.Text:
                default:
                    throw new ArgumentException($"{nameof(primitive)}.{nameof(primitive.Kind)}");
            }
        }

        public static IEnumerable<Point> GetProjectedVerticies(this IPrimitive primitive, Matrix4 transformationMatrix)
        {
            switch (primitive.Kind)
            {
                case PrimitiveKind.Line:
                case PrimitiveKind.Point:
                case PrimitiveKind.Text:
                case PrimitiveKind.Bezier:
                    return primitive.GetInterestingPoints().Select(p => transformationMatrix.Transform(p));
                case PrimitiveKind.Ellipse:
                    return ((PrimitiveEllipse)primitive).GetProjectedVerticies(transformationMatrix, 360);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static IEnumerable<Point> GetProjectedVerticies(this PrimitiveEllipse ellipse, Matrix4 transformationMatrix, int maxSeg)
        {
            return ellipse.GetInterestingPoints(maxSeg)
                .Select(p => transformationMatrix.Transform(p));
        }

        public static IEnumerable<IEnumerable<IPrimitive>> GetLineStripsFromPrimitives(this IEnumerable<IPrimitive> primitives)
        {
            var remainingPrimitives = new HashSet<IPrimitive>(primitives);
            var result = new List<List<IPrimitive>>();
            while (remainingPrimitives.Count > 1)
            {
                var shapePrimitives = new List<IPrimitive>();
                var first = remainingPrimitives.First();
                remainingPrimitives.Remove(first);
                shapePrimitives.Add(first);

                while (remainingPrimitives.Count > 0)
                {
                    var nextPrimitive = remainingPrimitives.FirstOrDefault(l => l.StartPoint().CloseTo(shapePrimitives.Last().EndPoint()) || l.EndPoint().CloseTo(shapePrimitives.Last().EndPoint()));
                    if (nextPrimitive == null)
                    {
                        // no more segments
                        break;
                    }

                    remainingPrimitives.Remove(nextPrimitive);
                    if (!nextPrimitive.StartPoint().CloseTo(shapePrimitives.Last().EndPoint()) && nextPrimitive.Kind == PrimitiveKind.Line)
                    {
                        // need to flip the line; arcs are handled elsewhere
                        var line = (PrimitiveLine)nextPrimitive;
                        nextPrimitive = new PrimitiveLine(line.P2, line.P1, line.Color);
                    }

                    shapePrimitives.Add(nextPrimitive);
                }

                result.Add(shapePrimitives);
            }

            return result;
        }

        public static IEnumerable<Polyline> GetPolylinesFromSegments(this IEnumerable<IPrimitive> primitives)
        {
            return primitives
                .GetLineStripsFromPrimitives()
                .GetPolylinesFromPrimitives();
        }
    }
}
