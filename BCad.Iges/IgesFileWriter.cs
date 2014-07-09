﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BCad.Iges.Directory;

namespace BCad.Iges
{
    internal class IgesFileWriter
    {
        public void Write(IgesFile file, Stream stream)
        {
            var writer = new StreamWriter(stream);

            //// write start section
            //foreach (var section in new IgesSection[] { startSection, globalSection })
            //{
            //    int line = 1;
            //    foreach (var data in section.GetData())
            //    {
            //        writer.WriteLine(string.Format("{0,72}{1,1}{2,7}", data, SectionTypeChar(section.SectionType), line));
            //        line++;
            //    }
            //}

            // prepare entities
            var startLines = new List<string>();
            var globalLines = new List<string>();
            var directoryLines = new List<string>();
            var parameterLines = new List<string>();

            startLines.Add(new string(' ', IgesFile.MaxDataLength));

            foreach (var entity in file.Entities)
            {
                entity.AddDirectoryAndParameterLines(directoryLines, parameterLines, file.FieldDelimiter, file.RecordDelimiter);
            }

            PopulateGlobalLines(file, globalLines);

            // write start line
            WriteLines(writer, IgesSectionType.Start, startLines);

            // write global lines
            WriteLines(writer, IgesSectionType.Global, globalLines);

            // write directory lines
            WriteLines(writer, IgesSectionType.Directory, directoryLines);

            // write parameter lines
            WriteLines(writer, IgesSectionType.Parameter, parameterLines); // TODO: ensure space in column 65 and directory pointer in next 7

            // write terminator line
            writer.WriteLine(MakeFileLine(IgesSectionType.Terminate,
                string.Format("{0}{1,7}{2}{3,7}{4}{5,7}{6}{7,7}",
                    SectionTypeChar(IgesSectionType.Start),
                    startLines.Count,
                    SectionTypeChar(IgesSectionType.Global),
                    globalLines.Count,
                    SectionTypeChar(IgesSectionType.Directory),
                    directoryLines.Count,
                    SectionTypeChar(IgesSectionType.Parameter),
                    parameterLines.Count),
                1));

            writer.Flush();
        }

        private static void PopulateGlobalLines(IgesFile file, List<string> globalLines)
        {
            var fields = new object[26];
            fields[0] = file.FieldDelimiter.ToString();
            fields[1] = file.RecordDelimiter.ToString();
            fields[2] = file.Identification;
            fields[3] = file.FullFileName;
            fields[4] = file.SystemIdentifier;
            fields[5] = file.SystemVersion;
            fields[6] = file.IntegerSize;
            fields[7] = file.SingleSize;
            fields[8] = file.DecimalDigits;
            fields[9] = file.DoubleMagnitude;
            fields[10] = file.DoublePrecision;
            fields[11] = file.Identifier;
            fields[12] = file.ModelSpaceScale;
            fields[13] = (int)file.ModelUnits;
            fields[14] = file.CustomModelUnits;
            fields[15] = file.MaxLineWeightGraduations;
            fields[16] = file.MaxLineWeight;
            fields[17] = file.TimeStamp;
            fields[18] = file.MinimumResolution;
            fields[19] = file.MaxCoordinateValue;
            fields[20] = file.Author;
            fields[21] = file.Organization;
            fields[22] = (int)file.IgesVersion;
            fields[23] = (int)file.DraftingStandard;
            fields[24] = file.ModifiedTime;
            fields[25] = file.ApplicationProtocol;

            AddParametersToStringList(fields, globalLines, file.FieldDelimiter, file.RecordDelimiter);
        }

        internal static void AddParametersToStringList(object[] parameters, List<string> stringList, char fieldDelimiter, char recordDelimiter, int maxLength = IgesFile.MaxDataLength, string lineSuffix = null)
        {
            int suffixLength = lineSuffix == null ? 0 : lineSuffix.Length;
            var sb = new StringBuilder();
            Action addLine = () =>
            {
                // ensure proper length
                sb.Append(new string(' ', maxLength - sb.Length - suffixLength));

                // add suffix
                sb.Append(lineSuffix);
                stringList.Add(sb.ToString());
                sb.Clear();
            };
            for (int i = 0; i < parameters.Length; i++)
            {
                var delim = (i == parameters.Length - 1) ? recordDelimiter : fieldDelimiter;
                var parameter = parameters[i];
                var paramString = ParameterToString(parameter) + delim;
                if (sb.Length + paramString.Length + suffixLength <= maxLength)
                {
                    // if there's enough space on the current line, do it
                    sb.Append(paramString);
                }
                else if (paramString.Length + suffixLength <= maxLength)
                {
                    // else if it will fit onto a new line, commit the current line and start a new one
                    addLine();
                    sb.Append(paramString);
                }
                else
                {
                    // otherwise, write as much as we can and wrap the rest
                    while (paramString.Length > 0)
                    {
                        var allowed = maxLength - sb.Length - suffixLength;
                        if (paramString.Length <= allowed)
                        {
                            // write all of it and be done
                            sb.Append(paramString);
                            paramString = string.Empty;
                        }
                        else
                        {
                            // write as much as possible
                            sb.Append(paramString.Substring(0, allowed));
                            Debug.Assert(sb.Length == maxLength - suffixLength, "This should have been a full line");

                            // and commit it
                            addLine();
                            paramString = paramString.Substring(allowed);
                        }
                    }
                }
            }

            // commit any remaining text
            if (sb.Length > 0)
                addLine();
        }

        private static string ParameterToString(object parameter)
        {
            if (parameter == null)
                return string.Empty;
            var type = parameter.GetType();
            if (type == typeof(int))
                return ParameterToString((int)parameter);
            if (type == typeof(double))
                return ParameterToString((double)parameter);
            else if (type == typeof(string))
                return ParameterToString((string)parameter);
            else if (type == typeof(DateTime))
                return ParameterToString((DateTime)parameter);
            else
            {
                Debug.Assert(false, "Unsupported parameter type: " + type.ToString());
                return string.Empty;
            }
        }

        private static string ParameterToString(int parameter)
        {
            return parameter.ToString();
        }

        private static string ParameterToString(double parameter)
        {
            var str = parameter.ToString();
            if (!(str.Contains(".") || str.Contains("e") || str.Contains("E") || str.Contains("d") || str.Contains("D")))
                str += '.'; // add trailing decimal point
            return str;
        }

        private static string ParameterToString(string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return string.Empty;
            else
                return string.Format("{0}H{1}", parameter.Length, parameter);
        }

        private static string ParameterToString(DateTime parameter)
        {
            return ParameterToString(parameter.ToString("yyyyMMdd.HHmmss"));
        }

        private static void WriteLines(StreamWriter writer, IgesSectionType sectionType, List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var line = MakeFileLine(sectionType, lines[i], i + 1);
                writer.WriteLine(line);
            }
        }

        private static string MakeFileLine(IgesSectionType sectionType, string line, int lineNumber)
        {
            line = line ?? string.Empty;
            if (line.Length > 72)
                throw new IgesException("Line is too long");

            var fullLine = string.Format("{0,-72}{1}{2,7}", line, SectionTypeChar(sectionType), lineNumber);
            return fullLine;
        }

        private static char SectionTypeChar(IgesSectionType type)
        {
            switch (type)
            {
                case IgesSectionType.Start: return 'S';
                case IgesSectionType.Global: return 'G';
                case IgesSectionType.Directory: return 'D';
                case IgesSectionType.Parameter: return 'P';
                case IgesSectionType.Terminate: return 'T';
                default:
                    throw new IgesException("Unexpected section type " + type);
            }
        }
    }
}
