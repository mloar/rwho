.SILENT:
rwho.exe: src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs
	csc /nologo /debug /optimize /win32icon:res\App.ico /unsafe src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs
!IF "$(FRAMEWORKVERSION)"=="v1.1.4322"
	signcode -cn "Special Interest Group for Windows Development" rwho.exe
!ELSE
	signtool sign /n "Special Interest Group for Windows Development" rwho.exe
!ENDIF

rwhod.exe: rwhod.obj
	link /nologo kernel32.lib secur32.lib advapi32.lib ws2_32.lib wtsapi32.lib user32.lib rwhod.obj

rwhod.obj: src\rwhod.c
	cl /nologo /c src\rwhod.c

rwho.msi: rwho.exe rwho.wxs
	candle /nologo rwho.wxs
	light /nologo rwho.wixobj
!IF "$(FRAMEWORKVERSION)"=="v1.1.4322"
	signcode -cn "Special Interest Group for Windows Development" rwho.msi
!ELSE
	signtool sign /n "Special Interest Group for Windows Development" rwho.msi
!ENDIF

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
