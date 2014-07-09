﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BCad.Dxf.Blocks;
using BCad.Dxf.Entities;
using BCad.Dxf.Sections;
using BCad.Dxf.Tables;

namespace BCad.Dxf
{
    public class DxfFile
    {
        public const string BinarySentinel = "AutoCAD Binary DXF";
        public const string EofText = "EOF";

        internal DxfHeaderSection HeaderSection { get; private set; }
        internal DxfClassesSection ClassSection { get; private set; }
        internal DxfTablesSection TablesSection { get; private set; }
        internal DxfBlocksSection BlocksSection { get; private set; }
        internal DxfEntitiesSection EntitiesSection { get; private set; }

        public List<DxfEntity> Entities { get { return EntitiesSection.Entities; } }

        public List<DxfClass> Classes { get { return ClassSection.Classes; } }

        public List<DxfBlock> Blocks { get { return BlocksSection.Blocks; } }

        public DxfHeader Header { get { return HeaderSection.Header; } }

        public List<DxfLayer> Layers { get { return TablesSection.LayerTable.Layers; } }

        public List<DxfViewPort> ViewPorts { get { return TablesSection.ViewPortTable.ViewPorts; } }

        public List<DxfDimStyle> DimensionStyles { get { return TablesSection.DimStyleTable.DimensionStyles; } }

        public List<DxfView> Views { get { return TablesSection.ViewTable.Views; } }

        public List<DxfUcs> UserCoordinateSystems { get { return TablesSection.UcsTable.UserCoordinateSystems; } }

        public List<DxfAppId> ApplicationIds { get { return TablesSection.AppIdTable.ApplicationIds; } }

        public List<DxfBlockRecord> BlockRecords { get { return TablesSection.BlockRecordTable.BlockRecords; } }

        public List<DxfLinetype> Linetypes { get { return TablesSection.LTypeTable.Linetypes; } }

        public List<DxfStyle> Styles { get { return TablesSection.StyleTable.Styles; } }

        internal IEnumerable<DxfSection> Sections
        {
            get
            {
                yield return this.HeaderSection;
                yield return this.ClassSection;
                yield return this.TablesSection;
                yield return this.BlocksSection;
                yield return this.EntitiesSection;
            }
        }

        public DxfFile()
        {
            this.HeaderSection = new DxfHeaderSection();
            this.ClassSection = new DxfClassesSection();
            this.TablesSection = new DxfTablesSection();
            this.BlocksSection = new DxfBlocksSection();
            this.EntitiesSection = new DxfEntitiesSection();
        }

        public static DxfFile Load(Stream stream)
        {
            var file = new DxfFile();
            var reader = new DxfReader(stream);
            var buffer = new DxfCodePairBufferReader(reader.ReadCodePairs());
            while (buffer.ItemsRemain)
            {
                var pair = buffer.Peek();
                if (DxfCodePair.IsSectionStart(pair))
                {
                    buffer.Advance(); // swallow (0, SECTION) pair
                    var section = DxfSection.FromBuffer(buffer);
                    if (section != null)
                    {
                        switch (section.Type)
                        {
                            case DxfSectionType.Blocks:
                                file.BlocksSection = (DxfBlocksSection)section;
                                break;
                            case DxfSectionType.Entities:
                                file.EntitiesSection = (DxfEntitiesSection)section;
                                break;
                            case DxfSectionType.Classes:
                                file.ClassSection = (DxfClassesSection)section;
                                break;
                            case DxfSectionType.Header:
                                file.HeaderSection = (DxfHeaderSection)section;
                                break;
                            case DxfSectionType.Tables:
                                file.TablesSection = (DxfTablesSection)section;
                                break;
                        }
                    }
                }
                else if (DxfCodePair.IsEof(pair))
                {
                    // swallow and quit
                    buffer.Advance();
                    break;
                }
                else if (DxfCodePair.IsComment(pair))
                {
                    // swallow comments
                    buffer.Advance();
                }
                else
                {
                    // swallow unexpected code pair
                    buffer.Advance();
                }
            }

            Debug.Assert(!buffer.ItemsRemain);

            return file;
        }

        public void Save(Stream stream, bool asText = true)
        {
            WriteStream(stream, asText);
        }

        private void WriteStream(Stream stream, bool asText)
        {
            var writer = new DxfWriter(stream, asText);
            writer.Open();

            // write sections
            foreach (var section in Sections)
            {
                foreach (var pair in section.GetValuePairs(Header.Version))
                    writer.WriteCodeValuePair(pair);
            }

            writer.Close();
        }
    }
}
