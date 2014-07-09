﻿using System.Collections.Generic;
using System.Linq;
using BCad.Dxf.Sections;

namespace BCad.Dxf.Tables
{
    public class DxfAppIdTable : DxfTable
    {
        public override DxfTableType TableType
        {
            get { return DxfTableType.AppId; }
        }

        public List<DxfAppId> ApplicationIds { get; private set; }

        public DxfAppIdTable()
        {
            ApplicationIds = new List<DxfAppId>();
        }

        internal override IEnumerable<DxfCodePair> GetValuePairs()
        {
            if (ApplicationIds.Count == 0)
                yield break;
            yield return new DxfCodePair(0, DxfSection.TableText);
            yield return new DxfCodePair(2, DxfTable.AppIdText);
            foreach (var appId in ApplicationIds.OrderBy(d => d.Name))
            {
                foreach (var pair in appId.GetValuePairs())
                    yield return pair;
            }

            yield return new DxfCodePair(0, DxfSection.EndTableText);
        }

        internal static DxfAppIdTable AppIdTableFromBuffer(DxfCodePairBufferReader buffer)
        {
            var table = new DxfAppIdTable();
            while (buffer.ItemsRemain)
            {
                var pair = buffer.Peek();
                buffer.Advance();
                if (DxfTablesSection.IsTableEnd(pair))
                {
                    break;
                }

                if (pair.Code == 0 && pair.StringValue == DxfTable.AppIdText)
                {
                    var appId = DxfAppId.FromBuffer(buffer);
                    table.ApplicationIds.Add(appId);
                }
            }

            return table;
        }
    }
}
