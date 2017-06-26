// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Threading.Tasks;
using BCad.Server.JsonRpc;
using BCad.Services;

namespace BCad.Server
{
    [ExportWorkspaceService, Shared]
    internal class FileSystemService : IFileSystemService
    {
        internal static JsonRpcAgent Agent;

        public async Task<string> GetFileNameFromUserForOpen()
        {
            var result = await Agent.SendRequestAsync(new JsonRpcRequest() { Method = "GetFileNameFromUserForOpen" });
            var fileName = result.Result.ToObject<string>();
            return fileName;
        }

        public Task<string> GetFileNameFromUserForSave()
        {
            throw new NotImplementedException();
        }

        public Task<string> GetFileNameFromUserForWrite(IEnumerable<FileSpecification> fileSpecifications)
        {
            throw new NotImplementedException();
        }

        public Task<Stream> GetStreamForReading(string fileName)
        {
            return Task.FromResult((Stream)File.Open(fileName, FileMode.Open));
        }

        public Task<Stream> GetStreamForWriting(string fileName)
        {
            throw new NotImplementedException();
        }
    }
}
