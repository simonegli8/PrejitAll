#!/usr/bin/env bash

PackageVersion="2.1.0"
Configuration="Release"

rm -f nupkg/*.nupkg
rm -f nupkg/*.snupkg

dotnet pack PrejitAll.slnx -c "$Configuration" \
    -p:Version="$PackageVersion" \
    -p:FileVersion="${PackageVersion}.0" \
    -p:AssemblyVersion="$PackageVersion"

dotnet pack PrejitAll.slnx -c "$Configuration" \
    -p:Version="$PackageVersion" \
    -p:FileVersion="${PackageVersion}.0" \
    -p:AssemblyVersion="$PackageVersion" \
    -p:DebugType=portable \
    -p:DebugSymbols=true \
    -p:IncludeSymbols=true \
    -p:SymbolPackageFormat=snupkg

dotnet pack PrejitAll.slnx -c "$Configuration" \
    -p:Version="$PackageVersion" \
    -p:FileVersion="${PackageVersion}.0" \
    -p:AssemblyVersion="$PackageVersion" \
    -p:PackAsTool=false

dotnet pack PrejitAll.slnx -c "$Configuration" \
    -p:Version="$PackageVersion" \
    -p:FileVersion="${PackageVersion}.0" \
    -p:AssemblyVersion="$PackageVersion" \
    -p:DebugType=portable \
    -p:DebugSymbols=true \
    -p:IncludeSymbols=true \
    -p:SymbolPackageFormat=snupkg \
    -p:PackAsTool=false