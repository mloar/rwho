.SILENT:
rwho.exe: src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs
	csc /nologo /optimize /win32icon:res\App.ico /unsafe src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs
	signcode -cn "Special Interest Group for Windows Development" rwho.exe

rwhod.exe: rwhod.obj
	link /nologo kernel32.lib secur32.lib advapi32.lib ws2_32.lib wtsapi32.lib user32.lib rwhod.obj

rwhod.obj: src\rwhod.c
	cl /nologo /c src\rwhod.c

rwho.msi: rwho.exe rwho.wxs
	candle /nologo rwho.wxs
	light /nologo rwho.wixobj
	signcode -cn "Special Interest Group for Windows Development" rwho.msi

all: rwho.exe rwho.msi

dist: rwho.exe rwho.msi
	copy /y rwho.exe H:\Public
	copy /y rwho.exe H:\public_html\rwho
	copy /y rwho.msi H:\public_html\rwho
	copy /y rwho.msi H:\sigwin\www\wipt

clean:
	-del rwho.msi
	-del rwho.exe
	-del rwhod.exe
	-del rwhod.obj
	-del rwho.wixobj
