@echo off
echo =========================================
echo    TEMPLATE DATABASE CLEAR UTILITY
echo =========================================
echo.
echo WARNING: This will DELETE ALL templates from the database!
echo This action cannot be undone.
echo.
echo Press CTRL+C to cancel, or...
pause

echo.
echo Attempting to clear template database...

REM Try to delete the database file directly
if exist templates.db (
    del /f templates.db
    echo Database file deleted successfully.
) else (
    echo No database file found in current directory.
)

REM Also check bin\Debug directory
if exist bin\Debug\templates.db (
    del /f bin\Debug\templates.db
    echo Database file in bin\Debug deleted successfully.
)

REM Clear thumbnails directory
set THUMBNAIL_DIR=%APPDATA%\Photobooth\Thumbnails
if exist "%THUMBNAIL_DIR%" (
    echo Clearing thumbnail files...
    del /q "%THUMBNAIL_DIR%\*.png" 2>nul
    echo Thumbnails cleared.
)

echo.
echo =========================================
echo    OPERATION COMPLETE
echo =========================================
echo.
echo The template database has been cleared.
echo The application will create a new empty database on next run.
echo.
pause