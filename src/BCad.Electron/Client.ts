// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

import { RpcServer, RpcRequest, RpcResponse } from './Rpc';

export default class Client {
    private _zoomInButton: HTMLButtonElement;
    private _zoomOutButton: HTMLButtonElement;
    private _openButton: HTMLButtonElement;
    private _outputPane: HTMLDivElement;
    constructor() {
        this._zoomInButton = <HTMLButtonElement> document.getElementById('zoom-in-button');
        this._zoomOutButton = <HTMLButtonElement> document.getElementById('zoom-out-button');
        this._openButton = <HTMLButtonElement> document.getElementById('open-button');
        this._outputPane = <HTMLDivElement> document.getElementById('output-pane');
        this.prepareEvents();
    }
    private prepareEvents() {
        this._zoomInButton.addEventListener('click', () => { this.zoomIn((_) => this.updateDrawing()); });
        this._zoomOutButton.addEventListener('click', () => { this.zoomOut((_) => this.updateDrawing()); });
        this._openButton.addEventListener('click', () => { this.sendRequest('File.Open', [], (_) => this.updateDrawing()); });
    }
    private sendRequest(method: string, params: any[], callback?: { (response: RpcResponse): void }) {
        RpcServer.sendRequest(RpcRequest.createRequest(method, params), (response: RpcResponse) => {
            if (callback != null) {
                callback(response);
            }
        });
    }
    public updateDrawing() {
        this.sendRequest('GetDrawing', [this._outputPane.clientWidth, this._outputPane.clientHeight], (response: RpcResponse) => {
            this._outputPane.innerHTML = response.result;
        });
    }
    public zoomIn(callback?: { (response: RpcResponse): void }) {
        this.sendRequest('ZoomIn', [], callback);
    }
    public zoomOut(callback?: { (response: RpcResponse): void }) {
        this.sendRequest('ZoomOut', [], callback);
    }
}
