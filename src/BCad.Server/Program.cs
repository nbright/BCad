// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Composition;
using BCad.Server.JsonRpc;

namespace BCad.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            new Program().Run();
        }

        private void Run()
        {
            var workspace = new RpcServerWorkspace();
            CompositionContainer.Container.SatisfyImports(workspace);
            var server = new ServerAgent(workspace, Console.In, Console.Out);
            FileSystemService.Agent = server.Agent;
            server.Agent.StartListeners();
        }
    }
}
