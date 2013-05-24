﻿using System.IO;
using BCad.Collections;
using BCad.Entities;
using BCad.FileHandlers;
using BCad.Iegs;
using BCad.Iegs.Entities;

namespace BCad.Commands.FileHandlers
{
    [ExportFileReader(IegsFileReader.DisplayName, IegsFileReader.FileExtension)]
    internal class IegsFileReader : IFileReader
    {
        public const string DisplayName = "IGES Files (" + FileExtension + ")";
        public const string FileExtension = ".igs";

        public void ReadFile(string fileName, Stream stream, out Drawing drawing, out ViewPort activeViewPort)
        {
            var file = IegsFile.Load(stream);
            var layer = new Layer("igs", Color.Auto);
            foreach (var entity in file.Entities)
            {
                var cadEntity = ToEntity(entity);
                if (cadEntity != null)
                {
                    layer = layer.Add(cadEntity);
                }
            }

            drawing = new Drawing(
                new DrawingSettings(fileName, UnitFormat.None, -1),
                new ReadOnlyTree<string, Layer>().Insert(layer.Name, layer));

            activeViewPort = new ViewPort(
                Point.Origin,
                Vector.ZAxis,
                Vector.YAxis,
                100.0);
        }

        private static Entity ToEntity(IegsEntity entity)
        {
            Entity result = null;
            switch (entity.Type)
            {
                case IegsEntityType.Circle:
                    result = ToCircle((IegsCircle)entity);
                    break;
                case IegsEntityType.Line:
                    result = ToLine((IegsLine)entity);
                    break;
            }

            return result;
        }

        private static Line ToLine(IegsLine line)
        {
            // TODO: transforms
            return new Line(ToPoint(line.P1), ToPoint(line.P2), ToColor(line.Color));
        }

        private static Circle ToCircle(IegsCircle circle)
        {
            // TODO: handle start/end points and arcs
            var center = ToPoint(circle.Center);
            var other = ToPoint(circle.StartPoint);
            var radius = (other - center).Length;
            // TODO: transforms and normal
            return new Circle(center, radius, Vector.ZAxis, ToColor(circle.Color));
        }

        private static Color ToColor(IegsColorNumber color)
        {
            return new Color((byte)color);
        }

        private static Point ToPoint(IegsPoint point)
        {
            return new Point(point.X, point.Y, point.Z);
        }
    }
}
