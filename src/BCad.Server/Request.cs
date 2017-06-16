// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Newtonsoft.Json.Linq;

namespace BCad.Server
{
    public class Request
    {
        public string Method { get; set; }
        public JObject[] Params { get; set; }
        public int? Id { get; set; }
    }
}
