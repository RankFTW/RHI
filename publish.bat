@echo off
dotnet publish RenoDXCommander\RenoDXCommander.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:Platform=x64 --self-contained false -o "C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RDXC"
