// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Newtonsoft.Json.Linq;

namespace BCad.Server.JsonRpc
{
    public class JsonRpcResponse
    {
        public JToken Result { get; set; }
        public JToken Error { get; set; }
        public int? Id { get; set; }
    }
}
