﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;

namespace BCad.Commands
{
    [ExportCadCommand("Edit.Delete", "DELETE", ModifierKeys.None, Key.Delete, "delete", "d", "del")]
    public class DeleteCommand : ICadCommand
    {
        public async Task<bool> Execute(IWorkspace workspace, object arg)
        {
            var entities = await workspace.InputService.GetEntities();
            if (entities.Cancel || !entities.HasValue)
            {
                return false;
            }

            if (!entities.Value.Any())
            {
                return true;
            }

            var dwg = workspace.Drawing;
            foreach (var ent in entities.Value)
            {
                dwg = dwg.Remove(ent);
            }

            workspace.Update(drawing: dwg);
            return true;
        }
    }
}
