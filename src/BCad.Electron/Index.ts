// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

import { RpcServer, RpcRequest } from './Rpc';

RpcServer.startServer();
document.getElementById("clicky").addEventListener('click', function () {
    try {
        RpcServer.sendRequest(RpcRequest.createRequest('ping', []), function (response) {
            alert('got response: ' + response.result);
        });
    }
    catch (e) {
        alert('exception: ' + e.toString());
    }
});
