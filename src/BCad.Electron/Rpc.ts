// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

import { spawn, ChildProcess } from 'child_process';

export class RpcRequest {
    private static requestId = 0;

    method: string;
    params: object[];
    id?: number;

    private constructor(method: string, params: object[], id?: number) {
        this.method = method;
        this.params = params;
        this.id = id;
    }

    static createRequest(method: string, params: object[]) {
        return new RpcRequest(method, params, RpcRequest.requestId++);
    }

    static createEvent(method: string, params: object[]) {
        return new RpcRequest(method, params, null);
    }
}

export class RpcResponse {
    result: string;
    error: any;
    id?: number;

    constructor(jsonrpc: any) {
        this.result = jsonrpc.result;
        this.error = jsonrpc.error;
        this.id = jsonrpc.id;
    }
}

export class RpcServer {
    private static server: ChildProcess = null;
    private static headerParts: object = {};
    private static awaitingResponse: Map<number, { (response: RpcResponse): void }> = new Map<number, { (response: RpcResponse): void }>();
    private static onDataReceived(data: string) {
        try {
            var line = '';
            var i = 0;
            for (i = 0; i < data.length; i++) {
                var ch = data.substr(i, 1);
                if (ch === '\n') {
                    line = line.trim();
                    if (line === '') {
                        i++; // swallow newline
                        break;
                    } else {
                        var parts = line.split(':', 2);
                        var key = parts[0].trim();
                        var value = parts[1].trim();
                        RpcServer.headerParts[key] = value;
                        line = '';
                    }
                } else {
                    line += ch;
                }
            }

            var length = parseInt(RpcServer.headerParts['Content-Length'] || '0');
            RpcServer.headerParts = {};
            var bodyStr = data.substr(i, length);
            var body = JSON.parse(bodyStr);
            var response = new RpcResponse(body);
            RpcServer.dispatchResponseReceived(response);
        }
        catch (e) {
            alert('error receiving data: ' + e.toString());
        }
    }
    private static dispatchResponseReceived(response: RpcResponse) {
        if (response.id != null && RpcServer.awaitingResponse.has(response.id)) {
            let callback = RpcServer.awaitingResponse.get(response.id);
            RpcServer.awaitingResponse.delete(response.id);
            try {
                callback(response);
            }
            catch (e) {
                alert('error executing callback: ' + e.toString());
            }
        }
    }
    static startServer() {
        try {
            if (RpcServer.server !== null) {
                return;
            }

            var path = __dirname + '/node_modules/BCad.Server/BCad.Server.dll';
            var server: ChildProcess = spawn('dotnet', [path]);
            server.stdout.setEncoding('utf8');
            server.stdout.on('data', RpcServer.onDataReceived);
            server.stderr.on('data', function (data) {
                alert('error = ' + data.toString());
            });
            server.on('exit', function (code) {
                alert('server edited with code ' + code.toString());
            });
            RpcServer.server = server;
        }
        catch (e) {
            alert('unable to start server: ' + e.toString());
        }
    }
    static sendRequest(request: RpcRequest, callback?: { (response: RpcResponse): void }) {
        if (callback != null && request.id != null) {
            RpcServer.awaitingResponse.set(request.id, callback);
        }
        var req = {
            'jsonrpc': '2.0',
            'method': request.method,
            'params': request.params,
            'id': request.id
        };
        var body = JSON.stringify(req);
        RpcServer.server.stdin.write('Content-Length: ' + body.length + '\r\n');
        RpcServer.server.stdin.write('\r\n');
        RpcServer.server.stdin.write(body);
    }
}
