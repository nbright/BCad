// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BCad.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Task.Run(new Program().Run).Wait();
        }

        private async Task Run()
        {
            var client = new Client(Console.In, Console.Out);
            while (true)
            {
                var request = await client.ListenForRequestAsync();
                if (request != null && request.Id.HasValue)
                {
                    Response response = null;
                    switch (request.Method)
                    {
                        case "ping":
                            response = new Response() { Result = new JValue("pong") };
                            break;
                        default:
                            response = new Response() { Error = new JValue($"Unknown method: {request.Method}.") };
                            break;
                    }

                    if (response != null)
                    {
                        response.Id = request.Id;
                        await client.SendResponseAsync(response);
                    }
                }
            }
        }
    }
}
