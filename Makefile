.SILENT:

!IF "$(FRAMEWORK_VER)"=="v1.1.4322"
!ERROR There are issues running an rwho built against .NET 1.1.  Build against 2.0 instead.
!ENDIF

CSFLAGS=/nologo
!IFDEF NODEBUG
CSFLAGS=$(CSFLAGS) /optimize
!ELSE
CSFLAGS=$(CSFLAGS) /debug
!ENDIF

rwho.exe: src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs
	csc $(CSFLAGS) /win32icon:res\App.ico /unsafe src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs
!IFDEF NODEBUG
	signtool sign /n "Special Interest Group for Windows Development" /t "http://timestamp.verisign.com/scripts/timestamp.dll" rwho.exe
!ENDIF

# My attempt at a C implementation
# Not used
#rwhod.exe: rwhod.obj
#	link /nologo kernel32.lib secur32.lib advapi32.lib ws2_32.lib wtsapi32.lib user32.lib rwhod.obj
#
#rwhod.obj: src\rwhod.c
#	cl /nologo /c src\rwhod.c

rwho.msi: rwho.exe rwho.wxs
	candle -nologo rwho.wxs
	light -nologo -ext WixUIExtension -cultures:en-us rwho.wixobj
!IFDEF NODEBUG
	signtool sign /n "Special Interest Group for Windows Development" /t "http://timestamp.verisign.com/scripts/timestamp.dll" rwho.msi
!ENDIF

all: rwho.exe rwho.msi

clean:
	-del rwho.msi
	-del rwho.exe
#	-del rwhod.exe
#	-del rwhod.obj
	-del rwho.pdb
	-del rwho.wixobj
