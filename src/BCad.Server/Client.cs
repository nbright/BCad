// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BCad.Server
{
    public class Client
    {
        private readonly TextReader _input;
        private readonly TextWriter _output;
        private readonly JsonSerializerSettings _settings;

        public Client(TextReader input, TextWriter output)
        {
            _input = input;
            _output = output;
            _settings = new JsonSerializerSettings() { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        }

        public async Task<Request> ListenForRequestAsync()
        {
            // read header
            var headerLines = new List<string>();
            string line;
            while ((line = await _input.ReadLineAsync()) != null)
            {
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }
                else
                {
                    headerLines.Add(line);
                }
            }

            var header = ProcessHeader(headerLines);

            // read body
            if (header.TryGetValue("content-length", out var lengthStr) && int.TryParse(lengthStr, out var length))
            {
                var buffer = new char[length];
                var actuallyRead = await _input.ReadBlockAsync(buffer, 0, buffer.Length);
                var content = string.Join(string.Empty, buffer);

                // TODO: what if actuallyRead != length?
                var request = JsonConvert.DeserializeObject<Request>(content, _settings);
                return request;
            }

            return null;
        }

        public async Task SendResponseAsync(Response response)
        {
            var body = JsonConvert.SerializeObject(response, _settings);
            var content = new StringBuilder();
            content.Append($"Content-Length: {body.Length}\r\n");
            content.Append("\r\n");
            content.Append(body);
            await _output.WriteAsync(content.ToString());
            await _output.FlushAsync();
        }

        private Dictionary<string, string> ProcessHeader(IEnumerable<string> headerLines)
        {
            var header = new Dictionary<string, string>();
            foreach (var line in headerLines)
            {
                var parts = line.Split(new[] { ':' }, 2);
                header[parts[0].Trim().ToLowerInvariant()] = parts[1].Trim();
            }

            return header;
        }
    }
}
