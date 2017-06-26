// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

import { RpcServer, RpcRequest, RpcResponse } from './Rpc';
import { remote } from 'electron';

export default class FileSystemService {
    public start() {
        RpcServer.addRequestHandler('GetFileNameFromUserForOpen', (request: RpcRequest) => {
            remote.dialog.showOpenDialog({
                filters: [
                    { name: 'DXF Files', extensions: ['dxf', 'dxb'] }
                ] },
                function (fileNames) {
                    var fileName = fileNames[0];
                    RpcServer.sendResponse(new RpcResponse(fileName, null, request.id));
                }
            );
        });
    }
}
