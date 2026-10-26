# PrejitAll

A cross platform helper tool to prejit everything using Ready2Run in the current folder in just one command. 

# Usage

```ps1
prejitall [path to dlls] { custom crossgen options }
# or if you want to use a custom crossgen2.exe:
prejitall [path to dlls] [path to crossgen2.exe] { custom crossgen options }
```
NOTE: target directory is expected to have all possible dependencies (e.g. result of `dotnet publish` command).

# Installation

```
dotnet tool install -g EstrellasDeEsperanza.PrejitAll
```

# Library
You can also use the EstrellasDeEsperanza.PrejitAll.Library nuget package and then call prejitall like so:

```
using EstrellasDeEsperanza.PrejitAll;
...
Crossgen2.PrejitAll(path);
or
Crossgen2.PrejitAll(path, crossgen2path, crossgenArgument1, crossgenArgument2, ...);
```
