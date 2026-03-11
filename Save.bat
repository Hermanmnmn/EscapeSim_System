@echo off
git add .
set /p msg="請輸入存檔紀錄名稱: "
git commit -m "%msg%"
echo 存檔完成！
pause