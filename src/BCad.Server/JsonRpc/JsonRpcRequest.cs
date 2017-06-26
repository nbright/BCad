// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Newtonsoft.Json.Linq;

namespace BCad.Server.JsonRpc
{
    public class JsonRpcRequest
    {
        public string Method { get; set; }
        public JToken[] Params { get; set; }
        public int? Id { get; set; }

        public JsonRpcResponse CreateResponse(JToken result)
        {
            return new JsonRpcResponse() { Result = result, Id = Id };
        }

        public JsonRpcResponse CreateErrorResponse(JToken error)
        {
            return new JsonRpcResponse() { Error = error, Id = Id };
        }
    }
}
