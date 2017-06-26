// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

import { spawn, ChildProcess } from 'child_process';

export class RpcRequest {
    private static requestId = 0;

    method: string;
    params: any[];
    id?: number;

    private constructor(method: string, params: any[], id?: number) {
        this.method = method;
        this.params = params;
        this.id = id;
    }

    static fromJson(jsonrpc: any) {
        return new RpcRequest(jsonrpc.method, jsonrpc.params, jsonrpc.id);
    }

    static createRequest(method: string, params: any[]) {
        return new RpcRequest(method, params, RpcRequest.requestId++);
    }

    static createEvent(method: string, params: any[]) {
        return new RpcRequest(method, params, null);
    }
}

export class RpcResponse {
    result: any;
    error: any;
    id?: number;

    constructor(result: any, error: any, id?: number) {
        this.result = result;
        this.error = error;
        this.id = id;
    }

    static fromJson(jsonrpc: any) {
        return new RpcResponse(jsonrpc.result, jsonrpc.error, jsonrpc.id);
    }
}

export class RpcServer {
    private static server: ChildProcess = null;
    private static headerParts: object = {};
    private static awaitingResponse: Map<number, { (response: RpcResponse): void }> = new Map<number, { (response: RpcResponse): void }>();
    private static requestHandlers: Map<string, { (request: RpcRequest): void }[]> = new Map<string, { (request: RpcRequest): void }[]>();
    private static remainder: string = '';

    private static onDataReceived(data: string) {
        try {
            var toProcess = RpcServer.remainder + data;
            while (toProcess.length > 0) {
                var line = '';
                var i = 0;
                for (i = 0; i < toProcess.length; i++) {
                    var ch = toProcess.substr(i, 1);
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
                if (toProcess.length >= i + length) {
                    var bodyStr = toProcess.substr(i, length);
                    RpcServer.remainder = toProcess.substr(i + length);
                    toProcess = RpcServer.remainder;
                    var body = JSON.parse(bodyStr);
                    if (body.method) {
                        var request = RpcRequest.fromJson(body);
                        RpcServer.dispatchRequest(request);
                    } else if (body.result || body.error) {
                        var response = RpcResponse.fromJson(body);
                        RpcServer.dispatchResponseReceived(response);
                    } else {
                        // unknown
                    }
                } else {
                    // not enough data yet
                    RpcServer.remainder = toProcess;
                }
            }
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
    private static dispatchRequest(request: RpcRequest) {
        if (RpcServer.requestHandlers.has(request.method)) {
            var handlers = RpcServer.requestHandlers.get(request.method);
            for (var i = 0; i < handlers.length; i++) {
                try {
                    handlers[i](request);
                }
                catch (e) {
                    alert('error executing handler: ' + e.toString());
                }
            }
        }
    }
    static addRequestHandler(method: string, handler: { (request: RpcRequest): void }) {
        if (!RpcServer.requestHandlers.has(method)) {
            RpcServer.requestHandlers.set(method, []);
        }

        RpcServer.requestHandlers.get(method).push(handler);
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
    private static sendObj(obj: any) {
        var body = JSON.stringify(obj);
        RpcServer.server.stdin.write('Content-Length: ' + body.length + '\r\n');
        RpcServer.server.stdin.write('\r\n');
        RpcServer.server.stdin.write(body);
    }
    static sendRequest(request: RpcRequest, callback?: { (response: RpcResponse): void }) {
        if (callback != null && request.id != null) {
            RpcServer.awaitingResponse.set(request.id, callback);
        }
        var req = {
            'method': request.method,
            'params': request.params,
            'id': request.id
        };
        RpcServer.sendObj(req);
    }
    static sendResponse(response: RpcResponse) {
        var resp = {
            'result': response.result,
            'error': response.error,
            'id': response.id
        };
        RpcServer.sendObj(resp);
    }
}
