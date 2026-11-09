@echo off
echo ========================================================
echo KHOI DONG HE THONG THU VIEN PHAN TAN (DOCKER COMPOSE)
echo ========================================================
docker compose up --build -d
echo.
echo Dang cho cac container khoi dong...
timeout /t 5 /nobreak >nul
echo.
echo ========================================================
echo HE THONG DA SAN SANG:
echo - Frontend Web UI:      http://localhost:3000
echo - Backend API:          http://localhost:5000
echo - RabbitMQ Dashboard:   http://localhost:15672 (guest / guest)
echo ========================================================
start http://localhost:3000
pause

