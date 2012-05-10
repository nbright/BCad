﻿using System.Collections.Generic;
using BCad.SnapPoints;

namespace BCad.Entities
{
    public class Arc : Entity, IPrimitive
    {
        private readonly Point center;
        private readonly Vector normal;
        private readonly double radius;
        private readonly double startAngle;
        private readonly double endAngle;
        private readonly Color color;
        private readonly Point endPoint1;
        private readonly Point endPoint2;
        private readonly Point midPoint;

        public Point Center { get { return center; } }

        public Vector Normal { get { return normal; } }

        public double Radius { get { return radius; } }

        public double StartAngle { get { return startAngle; } }

        public double EndAngle { get { return endAngle; } }

        public Color Color { get { return color; } }

        public Arc(Point center, double radius, double startAngle, double endAngle, Vector normal, Color color)
        {
            this.center = center;
            this.radius = radius;
            this.startAngle = startAngle;
            this.endAngle = endAngle;
            this.normal = normal;
            this.color = color;

            var points = Circle.TransformedPoints(this.center, this.normal, this.radius, this.radius, startAngle, endAngle, (startAngle + endAngle) / 2.0);
            this.endPoint1 = points[0];
            this.endPoint2 = points[1];
            this.midPoint = points[2];
        }

        public Point EndPoint1 { get { return this.endPoint1; } }

        public Point EndPoint2 { get { return this.endPoint2; } }

        public Point MidPoint { get { return this.midPoint; } }

        public override IEnumerable<IPrimitive> GetPrimitives()
        {
            return new[] { this };
        }

        public override IEnumerable<SnapPoint> GetSnapPoints()
        {
            return new SnapPoint[]
            {
                new CenterPoint(Center),
                new EndPoint(EndPoint1),
                new EndPoint(EndPoint2),
                new MidPoint(MidPoint)
            };
        }

        public PrimitiveKind Kind
        {
            get { return PrimitiveKind.Arc; }
        }

        public Arc Update(Point center = null, double? radius = null, double? startAngle = null, double? endAngle = null, Vector normal = null, Color? color = null)
        {
            return new Arc(
                center ?? this.Center,
                radius ?? this.Radius,
                startAngle ?? this.StartAngle,
                endAngle ?? this.EndAngle,
                normal ?? this.Normal,
                color ?? this.Color);
        }
    }
}
