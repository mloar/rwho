.SILENT:

!INCLUDE "rwho.ver"

!IF "$(FRAMEWORK_VER)"=="v1.1.4322"
!ERROR There are issues running an rwho built against .NET 1.1.  Build against 2.0 instead.
!ENDIF

CSFLAGS=/nologo
!IFDEF NODEBUG
CSFLAGS=$(CSFLAGS) /optimize
!ELSE
CSFLAGS=$(CSFLAGS) /debug
!ENDIF

rwho.exe: src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs AssemblyVersion.cs
	csc $(CSFLAGS) /win32icon:res\App.ico /unsafe src\rwho.cs src\WTS.cs src\AssemblyInfo.cs src\ProjectInstaller.cs\
	 	AssemblyVersion.cs
!IFDEF NODEBUG
	signtool sign /n "Special Interest Group for Windows Development" /t "http://timestamp.verisign.com/scripts/timestamp.dll" rwho.exe
!ENDIF

AssemblyVersion.cs: rwho.ver
	echo using System.Reflection; > AssemblyVersion.cs
	echo [assembly:AssemblyVersion("$(ProductVersion).*")] >> AssemblyVersion.cs

rwho.msi: rwho.exe rwho.wxs
	candle -nologo -dProductVersion=$(ProductVersion) rwho.wxs
	light -nologo -ext WixUIExtension -cultures:en-us rwho.wixobj
!IFDEF NODEBUG
	signtool sign /n "Special Interest Group for Windows Development" /t "http://timestamp.verisign.com/scripts/timestamp.dll" rwho.msi
!ENDIF

all: rwho.exe rwho.msi

clean:
	-del rwho.msi
	-del rwho.exe
	-del rwho.pdb
	-del rwho.wixobj
	-del AssemblyVersion.cs
