// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BCad.Server.Test
{
    public class ServerTests
    {
        [Fact]
        public async void DoubleBounceTest()
        {
            // client              server
            // request "ping" ->
            //          <- request "ping"
            // reply "pong" ->
            //            <- reply "pong"
            using (var istream = new MemoryStream())
            using (var ostream = new MemoryStream())
            using (var clientReader = new StreamReader(istream, Encoding.UTF8, false, 1024, true))
            using (var clientWriter = new StreamWriter(ostream, Encoding.UTF8, 1024, true))
            using (var serverReader = new StreamReader(ostream, Encoding.UTF8, false, 1024, true))
            using (var serverWriter = new StreamWriter(istream, Encoding.UTF8, 1024, true))
            {
                var client = new Client(clientReader, clientWriter);
                var server = new Client(serverReader, serverWriter);
                var counter = 1;
                var hitServer1 = 0;
                var hitServer2 = 0;
                var hitClient = 0;

                server.RegisterHandler("ping", async request =>
                {
                    hitServer1 = counter++; // 1
                    var resp = await server.SendRequestAsync(new Request() { Method = "ping" });
                    hitServer2 = counter++; // 3
                    server.SendResponseAsync(new Response() { Result = resp.Result, Id = request.Id });
                });
                client.RegisterHandler("ping", request =>
                {
                    hitClient = counter++; //2
                    client.SendResponseAsync(new Response() { Result = new JValue("pong"), Id = request.Id });
                });
                server.StartListeners();
                client.StartListeners();

                var response = await client.SendRequestAsync(new Request() { Method = "ping" });
                Assert.Equal("pong", response.Result.ToObject<string>());
                Assert.Equal(1, hitServer1);
                Assert.Equal(2, hitClient);
                Assert.Equal(3, hitServer2);
                Assert.Equal(4, counter);
            }
        }
    }
}
