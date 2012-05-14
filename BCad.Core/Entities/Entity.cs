﻿using System.Collections.Generic;
using BCad.Primitives;
using BCad.SnapPoints;

namespace BCad.Entities
{
    public abstract class Entity
    {
        public abstract IEnumerable<IPrimitive> GetPrimitives();
        public abstract IEnumerable<SnapPoint> GetSnapPoints();
        public abstract EntityKind Kind { get; }
        public abstract BoundingBox BoundingBox { get; }

        private static uint nextId = 1;

        public uint Id { get; private set; }

        public Entity()
        {
            this.Id = nextId++;
        }

        public override int GetHashCode()
        {
            return (int)this.Id;
        }
    }
}
