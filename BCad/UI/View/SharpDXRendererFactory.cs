﻿using BCad.Services;

namespace BCad.UI.View
{
    [ExportRendererFactory("Hardware")]
    internal class SharpDXRendererFactory : IRendererFactory
    {
        public IRenderer CreateRenderer(IViewControl viewControl, IWorkspace workspace, IInputService inputService)
        {
            return new SharpDXRenderer(viewControl, workspace, inputService);
        }
    }
}
