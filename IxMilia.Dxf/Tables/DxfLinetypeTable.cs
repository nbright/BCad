﻿using System.Collections.Generic;
using System.Linq;
using IxMilia.Dxf.Sections;

namespace IxMilia.Dxf.Tables
{
    public class DxfLinetypeTable : DxfTable
    {
        public override DxfTableType TableType
        {
            get { return DxfTableType.LType; }
        }

        public List<DxfLinetype> Linetypes { get; private set; }

        public DxfLinetypeTable()
            : this(new DxfLinetype[0])
        {
        }

        public DxfLinetypeTable(IEnumerable<DxfLinetype> linetypes)
        {
            Linetypes = new List<DxfLinetype>(linetypes);
        }

        internal override IEnumerable<DxfCodePair> GetValuePairs(DxfAcadVersion version)
        {
            if (Linetypes.Count == 0)
                yield break;
            foreach (var common in CommonCodePairs(version))
            {
                yield return common;
            }

            foreach (var linetype in Linetypes.OrderBy(l => l.Name))
            {
                foreach (var pair in linetype.GetValuePairs())
                    yield return pair;
            }

            yield return new DxfCodePair(0, DxfSection.EndTableText);
        }

        internal static DxfLinetypeTable LinetypeTableFromBuffer(DxfCodePairBufferReader buffer)
        {
            var table = new DxfLinetypeTable();
            while (buffer.ItemsRemain)
            {
                var pair = buffer.Peek();
                buffer.Advance();
                if (DxfTablesSection.IsTableEnd(pair))
                {
                    break;
                }

                if (pair.Code == 0 && pair.StringValue == DxfTable.LTypeText)
                {
                    var linetype = DxfLinetype.FromBuffer(buffer);
                    table.Linetypes.Add(linetype);
                }
            }

            return table;
        }
    }
}
