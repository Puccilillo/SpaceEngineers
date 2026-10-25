@echo off

echo Exporting namespace script...
if not "%src_dirname%" == "%namespace%" (
  echo [WARN] "%src_dirname%" dir and "%namespace%" namespace do not match!
)
echo Name matched.

rem  Will be used to store the line numbers and then the raw ingame script part
rem  before it will be sanitized and saved to destination.
set "tmp=%dest_dir%\tmp"
echo Using temporary file: "%tmp%"

rem  Set ingame region patterns based on namespace.
set region="/^\s*#region %namespace%/="
set endregion="/^\s*#endregion \/\/ %namespace%/="
echo Using region pattern: %region%
echo Using endregion pattern: %endregion%

rem  Find and save the bounds of the ingame region.
"%sed%" -n %region% "%src_cs%" > "%tmp%"
set /p start_line_num= < "%tmp%"
"%sed%" -n %endregion% "%src_cs%" > "%tmp%"
set /p end_line_num= < "%tmp%"
echo Ingame script part lines: %start_line_num% to %end_line_num%

rem  Copy the raw ingame script part.
"%sed%" -n "%start_line_num%,%end_line_num%p" "%src_cs%" > "%tmp%"
if %ERRORLEVEL% NEQ 0 (
  echo Cannot extract ingame script part.
  del "%tmp%"
  del "%dest_dir%\%CS%" 2>NUL
  exit /b 15
)

rem  Remove the first indent (tab or 4 spaces) at the start of each line and
rem  save the script into the final file.
set TAB_STOP=4
"%sed%" "s/^\(\s\{%TAB_STOP%\}\|\t\)//" "%tmp%" > "%dest_dir%\%CS%"

del "%tmp%"
