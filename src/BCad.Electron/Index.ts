// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

import { RpcServer, RpcRequest, RpcResponse } from './Rpc';
import FileSystemService from './FileSystemService';
import Client from './Client';

var fss = new FileSystemService();
fss.start();
RpcServer.startServer();

var client = new Client();
