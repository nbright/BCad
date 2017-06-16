// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Newtonsoft.Json.Linq;

namespace BCad.Server
{
    public class Response
    {
        public JValue Result { get; set; }
        public JValue Error { get; set; }
        public int? Id { get; set; }
    }
}
