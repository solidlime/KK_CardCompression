@echo off
echo Building KK_CardCompression BepInEx Plugin...
echo.

dotnet build -c Release

if %ERRORLEVEL% neq 0 (
    echo.
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo Build succeeded!
echo.
echo Output: bin\Release\net48\KK_CardCompression.dll
echo.
echo Installation:
echo   1. Copy KK_CardCompression.dll to BepInEx\plugins\KK_CardCompression\
echo   2. Copy kk_universal_dict.zstd to BepInEx\plugins\KK_CardCompression\
echo   3. Copy ZstdSharp.dll to BepInEx\plugins\KK_CardCompression\
echo.

pause