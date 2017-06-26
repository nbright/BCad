// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BCad.Server.JsonRpc
{
    public class JsonRpcAgent
    {
        private readonly TextReader _input;
        private readonly TextWriter _output;
        private readonly JsonSerializerSettings _settings;
        private int _requestId = 0;
        private Dictionary<string, List<Action<JsonRpcRequest>>> _handlers = new Dictionary<string, List<Action<JsonRpcRequest>>>();
        private List<Action<JsonRpcRequest>> _defaultHandlers = new List<Action<JsonRpcRequest>>();
        private Dictionary<int, TaskCompletionSource<JsonRpcResponse>> _awaitingResponse = new Dictionary<int, TaskCompletionSource<JsonRpcResponse>>();

        private Queue<JObject> _pendingInbound = new Queue<JObject>();
        private Queue<string> _pendingOutbound = new Queue<string>();

        public JsonRpcAgent(TextReader input, TextWriter output)
        {
            _input = input;
            _output = output;
            _settings = new JsonSerializerSettings() { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        }

        public void RegisterHandler(string method, Action<JsonRpcRequest> handler)
        {
            if (!_handlers.ContainsKey(method))
            {
                _handlers.Add(method, new List<Action<JsonRpcRequest>>());
            }

            _handlers[method].Add(handler);
        }

        public void RegisterDefaultHandler(Action<JsonRpcRequest> handler)
        {
            _defaultHandlers.Add(handler);
        }

        public void StartListeners()
        {
            File.WriteAllText("log.log", "start logging\r\n");
            var t1 = new Thread(new ThreadStart(() => { var _ = ProcessInboundRequest(); }));
            var t2 = new Thread(new ThreadStart(() => { var _ = ProcessOutboundRequest(); }));
            var t3 = new Thread(new ThreadStart(() => { var _ = ListenerMethod(); }));

            t1.Start();
            t2.Start();
            t3.Start();

            //t1.Join();
            //t2.Join();
        }

        private async Task ListenerMethod()
        {
            while (true)
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
                    var obj = JObject.Parse(content);
                    _pendingInbound.Enqueue(obj);
                }
            }
        }

        private async Task ProcessInboundRequest()
        {
            while (true)
            {
                while (_pendingInbound.Count == 0)
                {
                    await Task.Delay(50);
                }

                var obj = _pendingInbound.Dequeue();
                File.AppendAllText("log.log", $"processing inbound: {obj}\r\n");
                if (obj.TryGetValue("method", out var method))
                {
                    var request = JsonConvert.DeserializeObject<JsonRpcRequest>(obj.ToString(), _settings);
                    var methodName = method.ToString();
                    if (!_handlers.TryGetValue(methodName, out var handlers))
                    {
                        handlers = _defaultHandlers;
                    }

                    DispatchHandlers(request, handlers);
                }
                else if (obj.TryGetValue("id", out var responseId))
                {
                    switch (responseId.Type)
                    {
                        case JTokenType.Integer:
                            var id = responseId.ToObject<int>();
                            var response = JsonConvert.DeserializeObject<JsonRpcResponse>(obj.ToString(), _settings);
                            DispatchResponse(response);
                            break;
                    }
                }
            }
        }

        private async Task ProcessOutboundRequest()
        {
            while (true)
            {
                while (_pendingOutbound.Count == 0)
                {
                    await Task.Delay(50);
                }

                var body = _pendingOutbound.Dequeue();
                File.AppendAllText("log.log", $"sending outbound: {body}\r\n");
                var content = new StringBuilder();
                content.Append($"Content-Length: {body.Length}\r\n");
                content.Append("\r\n");
                content.Append(body);
                await _output.WriteAsync(content.ToString());
                await _output.FlushAsync();
            }
        }

        private void DispatchHandlers(JsonRpcRequest request, IEnumerable<Action<JsonRpcRequest>> handlers)
        {
            foreach (var handler in handlers)
            {
                DispatchHandler(handler, request);
            }
        }

        private void DispatchHandler(Action<JsonRpcRequest> handler, JsonRpcRequest request)
        {
            try
            {
                handler(request);
            }
            catch (Exception e)
            {
                File.AppendAllText("log.log", e.ToString());
            }
        }

        private void DispatchResponse(JsonRpcResponse response)
        {
            var id = response.Id.GetValueOrDefault();
            if (_awaitingResponse.TryGetValue(id, out var responseTaskCompletionSource))
            {
                _awaitingResponse.Remove(id);
                responseTaskCompletionSource.SetResult(response);
            }
        }

        public void SendResponse(JsonRpcResponse response)
        {
            var body = JsonConvert.SerializeObject(response, _settings);
            _pendingOutbound.Enqueue(body);
        }

        public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request)
        {
            request.Id = _requestId++;
            var taskCompletionSource = new TaskCompletionSource<JsonRpcResponse>();
            _awaitingResponse[request.Id.GetValueOrDefault()] = taskCompletionSource;
            var body = JsonConvert.SerializeObject(request, _settings);
            _pendingOutbound.Enqueue(body);
            return taskCompletionSource.Task;
        }

        public void SendNotificationAsync(JsonRpcRequest request)
        {
            request.Id = null;
            var body = JsonConvert.SerializeObject(request, _settings);
            _pendingOutbound.Enqueue(body);
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
