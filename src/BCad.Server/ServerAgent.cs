// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Text;
using BCad.Entities;
using BCad.Server.JsonRpc;
using Newtonsoft.Json.Linq;

namespace BCad.Server
{
    public class ServerAgent
    {
        private IWorkspace _workspace;
        public JsonRpcAgent Agent { get; }

        public ServerAgent(IWorkspace workspace, TextReader input, TextWriter output)
        {
            _workspace = workspace;
            Agent = new JsonRpcAgent(input, output);
            Agent.RegisterHandler("File.Open", OpenDrawing);
            Agent.RegisterHandler("GetDrawing", GetDrawing);
            Agent.RegisterHandler("ZoomIn", ZoomIn);
            Agent.RegisterHandler("ZoomOut", ZoomOut);
            Agent.RegisterDefaultHandler(DefaultHandler);
        }

        private async void OpenDrawing(JsonRpcRequest request)
        {
            var commandResult = await _workspace.ExecuteCommand("File.Open");
            var response = request.CreateResponse(new JValue(commandResult));
            Agent.SendResponse(response);
        }

        private void GetDrawing(JsonRpcRequest request)
        {
            var width = request.Params[0].ToObject<int>();
            var height = request.Params[1].ToObject<int>();
            var transform = _workspace.ActiveViewPort.GetTransformationMatrixWindowsStyle(width, height);
            var sb = new StringBuilder();
            //sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" viewBox=\"0 0 {width} {height}\">");
            sb.Append($"<svg width=\"{width}\" height=\"{height}\">");
            sb.Append("<g stroke=\"black\">");
            foreach (var line in _workspace.Drawing.GetEntities().OfType<Line>())
            {
                var p1 = transform.Transform(line.P1);
                var p2 = transform.Transform(line.P2);
                sb.Append($"<line x1=\"{p1.X}\" y1=\"{p1.Y}\" x2=\"{p2.X}\" y2=\"{p2.Y}\" />");
            }

            sb.Append("</g>");
            sb.Append("</svg>");
            var response = request.CreateResponse(new JValue(sb.ToString()));
            Agent.SendResponse(response);
        }

        private void ZoomIn(JsonRpcRequest request)
        {
            _workspace.Update(activeViewPort: _workspace.ActiveViewPort.Update(viewHeight: _workspace.ActiveViewPort.ViewHeight * 0.8));
            Agent.SendResponse(request.CreateResponse(new JValue("ok")));
        }

        private void ZoomOut(JsonRpcRequest request)
        {
            _workspace.Update(activeViewPort: _workspace.ActiveViewPort.Update(viewHeight: _workspace.ActiveViewPort.ViewHeight * 1.25));
            Agent.SendResponse(request.CreateResponse(new JValue("ok")));
        }

        private void DefaultHandler(JsonRpcRequest request)
        {
            if (request.Id.HasValue)
            {
                Agent.SendResponse(request.CreateErrorResponse(new JValue($"Unsupported method '{request.Method}'")));
            }
        }
    }
}
