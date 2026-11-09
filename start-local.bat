@echo off
echo ========================================================
echo KHOI DONG HE THONG THU VIEN PHAN TAN (LOCAL RUNNER)
echo ========================================================
echo 1. Dang khoi dong Backend API...
start "Library Management API" cmd /k "cd backend/src/LibraryManagement.API && dotnet run"
timeout /t 3 /nobreak >nul

echo 2. Dang khoi dong Reservation Worker Service...
start "Reservation Worker Service" cmd /k "cd backend/src/LibraryManagement.Worker && dotnet run"
timeout /t 2 /nobreak >nul

echo 3. Dang mo giao dien Frontend Web...
start frontend\index.html

echo.
echo ========================================================
echo Cac thanh phan da duoc bat trong cac cua so terminal rieng!
echo - API:      http://localhost:5000
echo - Frontend: frontend\index.html
echo ========================================================
pause

