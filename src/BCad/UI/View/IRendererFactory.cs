﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace BCad.UI.View
{
    public interface IRendererFactory
    {
        AbstractCadRenderer CreateRenderer(IViewControl viewControl, IWorkspace workspace);
    }
}
