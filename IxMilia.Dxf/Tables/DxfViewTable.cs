﻿using System.Collections.Generic;
using System.Linq;
using IxMilia.Dxf.Sections;

namespace IxMilia.Dxf.Tables
{
    public class DxfViewTable : DxfTable
    {
        public override DxfTableType TableType
        {
            get { return DxfTableType.View; }
        }

        public List<DxfView> Views { get; private set; }

        public DxfViewTable()
        {
            Views = new List<DxfView>();
        }

        internal override IEnumerable<DxfCodePair> GetValuePairs(DxfAcadVersion version)
        {
            if (Views.Count == 0)
                yield break;
            foreach (var common in CommonCodePairs(version))
            {
                yield return common;
            }

            foreach (var view in Views.OrderBy(d => d.Name))
            {
                foreach (var pair in view.GetValuePairs())
                    yield return pair;
            }

            yield return new DxfCodePair(0, DxfSection.EndTableText);
        }

        internal static DxfViewTable ViewTableFromBuffer(DxfCodePairBufferReader buffer)
        {
            var table = new DxfViewTable();
            while (buffer.ItemsRemain)
            {
                var pair = buffer.Peek();
                buffer.Advance();
                if (DxfTablesSection.IsTableEnd(pair))
                {
                    break;
                }

                if (pair.Code == 0 && pair.StringValue == DxfTable.ViewText)
                {
                    var view = DxfView.FromBuffer(buffer);
                    table.Views.Add(view);
                }
            }

            return table;
        }
    }
}
