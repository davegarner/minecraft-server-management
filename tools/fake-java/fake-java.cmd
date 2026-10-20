@echo off
rem Windows wrapper so the panel can launch the Python test stub as if it were java.exe.
python "%~dp0fake-java.py" %*
