﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Composition;
using BCad.UI.View;

namespace BCad.UI
{
    [MetadataAttribute]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ExportRendererFactoryAttribute : ExportAttribute
    {
        public string FactoryName { get; private set; }

        public ExportRendererFactoryAttribute(string factoryName)
            : base(typeof(IRendererFactory))
        {
            FactoryName = factoryName;
        }
    }
}
