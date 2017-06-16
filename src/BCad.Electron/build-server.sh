#!/bin/sh -e

SERVERDIR=../BCad.Server
NODEBINDIR=node_modules
dotnet restore $SERVERDIR/BCad.Server.csproj
dotnet publish $SERVERDIR/BCad.Server.csproj
rm -rf $NODEBINDIR/BCad.Server
cp -r $SERVERDIR/bin/Debug/netcoreapp1.1/publish/ $NODEBINDIR/BCad.Server
