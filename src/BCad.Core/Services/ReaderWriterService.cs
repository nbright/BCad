﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BCad.FileHandlers;
using BCad.Services;

namespace BCad.Core.Services
{
    [ExportWorkspaceService, Shared]
    internal class ReaderWriterService : IReaderWriterService
    {
        [ImportMany]
        public IEnumerable<Lazy<IFileHandler, FileHandlerMetadata>> FileHandlers { get; set; }

        [Import]
        public IWorkspace Workspace { get; set; }

        private Dictionary<Drawing, INotifyPropertyChanged> _drawingSettingsCache = new Dictionary<Drawing, INotifyPropertyChanged>();

        public Task<bool> TryReadDrawing(string fileName, Stream stream, out Drawing drawing, out ViewPort viewPort)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            drawing = default(Drawing);
            viewPort = default(ViewPort);

            var extension = Path.GetExtension(fileName);
            var reader = ReaderFromExtension(extension);
            if (reader == null)
            {
                throw new Exception("Unknown file extension " + extension);
            }

            reader.ReadDrawing(fileName, stream, out drawing, out viewPort);

            return Task.FromResult(true);
        }

        public async Task<bool> TryWriteDrawing(string fileName, Drawing drawing, ViewPort viewPort, Stream stream, bool preserveSettings = true)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            var extension = Path.GetExtension(fileName);
            var writer = WriterFromExtension(extension);
            if (writer == null)
            {
                throw new Exception("Unknown file extension " + extension);
            }

            INotifyPropertyChanged fileSettings = null;
            if (!preserveSettings)
            {
                fileSettings = writer.GetFileSettingsFromDrawing(drawing);
            }

            if (fileSettings != null)
            {
                var result = await Workspace.DialogFactoryService.ShowDialog("FileSettings", "Default", fileSettings);
                if (result != true)
                {
                    return false;
                }
            }

            _drawingSettingsCache.TryGetValue(drawing, out var previousDrawingSettings);
            writer.WriteDrawing(fileName, stream, drawing, viewPort, fileSettings ?? previousDrawingSettings);

            if (fileSettings != null)
            {
                _drawingSettingsCache[drawing] = fileSettings;
            }

            return true;
        }

        private IFileHandler ReaderFromExtension(string extension)
        {
            var reader = FileHandlers.FirstOrDefault(r => r.Metadata.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) && r.Metadata.CanRead);
            if (reader == null)
                return null;
            return reader.Value;
        }

        private IFileHandler WriterFromExtension(string extension)
        {
            var writer = FileHandlers.FirstOrDefault(r => r.Metadata.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) && r.Metadata.CanWrite);
            if (writer == null)
                return null;
            return writer.Value;
        }
    }
}
