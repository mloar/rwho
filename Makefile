.SILENT:
rwho.exe: src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs
	csc /nologo $(DEBUG) /win32icon:res\App.ico /unsafe src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs
!IFNDEF DEBUG
	signtool sign /n "Special Interest Group for Windows Development" /t "http://timestamp.verisign.com/scripts/timestamp.dll" rwho.exe
!ENDIF

rwhod.exe: rwhod.obj
	link /nologo kernel32.lib secur32.lib advapi32.lib ws2_32.lib wtsapi32.lib user32.lib rwhod.obj

rwhod.obj: src\rwhod.c
	cl /nologo /c src\rwhod.c

rwho.msi: rwho.exe rwho.wxs
	candle /nologo rwho.wxs
	light /nologo rwho.wixobj
!IFNDEF DEBUG
	signtool sign /n "Special Interest Group for Windows Development" /t "http://timestamp.verisign.com/scripts/timestamp.dll" rwho.msi
!ENDIF

all: rwho.exe rwho.msi

clean:
	-del rwho.msi
	-del rwho.exe
	-del rwhod.exe
	-del rwhod.obj
	-del rwho.pdb
	-del rwho.wixobj
