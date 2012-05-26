﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BCad.Dxf.Entities
{
    public enum DxfEntityType
    {
        Line,
        Circle,
        Arc,
        Ellipse
    }

    public abstract class DxfEntity
    {
        public const string LineType = "LINE";
        public const string CircleType = "CIRCLE";
        public const string ArcType = "ARC";
        public const string EllipseType = "ELLIPSE";

        public abstract DxfEntityType EntityType { get; }

        public abstract string SubclassMarker { get; }

        public string Handle { get; set; }

        public string Layer { get; set; }

        public DxfColor Color { get; set; }

        public IEnumerable<DxfCodePair> ValuePairs
        {
            get
            {
                if (!string.IsNullOrEmpty(Handle))
                    yield return new DxfCodePair(5, Handle);
                if (!string.IsNullOrEmpty(Layer))
                    yield return new DxfCodePair(8, Layer);
                if (!Color.IsByLayer)
                    yield return new DxfCodePair(62, Color.RawValue);
                yield return new DxfCodePair(100, SubclassMarker);
                foreach (var pair in GetEntitySpecificPairs())
                    yield return pair;
            }
        }

        protected abstract IEnumerable<DxfCodePair> GetEntitySpecificPairs();

        protected void PopulateDefaultAndCommonValues(IEnumerable<DxfCodePair> pairs)
        {
            // set defaults
            Handle = null;
            Layer = null;
            Color = DxfColor.ByBlock;

            // read specifics
            foreach (var pair in pairs)
            {
                switch (pair.Code)
                {
                    case 5:
                        Handle = pair.HandleValue;
                        break;
                    case 8:
                        Layer = pair.StringValue;
                        break;
                    case 62:
                        Color = DxfColor.FromRawValue(pair.ShortValue);
                        break;
                }
            }
        }

        public static DxfEntity FromCodeValuePairs(IEnumerable<DxfCodePair> pairs)
        {
            var p = pairs.ToList();
            var first = pairs.First();
            if (first.Code != 0)
                throw new DxfReadException("Expected start of entity");
                        
            DxfEntity ent = null;
            switch (first.StringValue)
            {
                case LineType:
                    ent = DxfLine.FromPairs(pairs);
                    break;
                case CircleType:
                    ent = DxfCircle.FromPairs(pairs);
                    break;
                case ArcType:
                    ent = DxfArc.FromPairs(pairs);
                    break;
                case EllipseType:
                    ent = DxfEllipse.FromPairs(pairs);
                    break;
                // TODO:
                // POLYLINE
                // VERTEX
                // SEQEND
                // DIMENSION
                // INSERT
                // ATTRIB
                // MTEXT
                // HATCH
                // LEADER
                default:
                    Debug.WriteLine("Unsupported entity type " + first.StringValue);
                    break;
            }

            return ent;
        }

        public string EntityTypeString
        {
            get
            {
                string name = null;
                switch (EntityType)
                {
                    case DxfEntityType.Line:
                        name = LineType;
                        break;
                    case DxfEntityType.Circle:
                        name = CircleType;
                        break;
                    case DxfEntityType.Arc:
                        name = ArcType;
                        break;
                    case DxfEntityType.Ellipse:
                        name = EllipseType;
                        break;
                    default:
                        throw new NotImplementedException();
                }
                return name;
            }
        }
    }
}
