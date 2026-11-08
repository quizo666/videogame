# Бэкап PixelForge Studio

Репозиторий: https://github.com/quizo666/videogame

В бэкап включаются исходники src/, документация, build.ps1, PixelForge.exe, tools/ с FFmpeg и лицензией, примеры examples/, экспортированные материалы exports/ и галерея library/. Крупные двоичные файлы хранятся через Git LFS. Временные файлы work/, старые сборки backups/, локальная история путей settings/ и секреты из .gitignore не включаются.

Для восстановления установите Git и Git LFS, затем выполните:

```powershell
git lfs install
git clone https://github.com/quizo666/videogame.git
cd videogame
git lfs pull
.\PixelForge.exe
```

Сборка из исходников на Windows с .NET Framework:

```powershell
.\build.ps1
```

Чтобы сохранить следующие изменения в GitHub:

```powershell
git add .
git commit -m "Update PixelForge Studio"
git push
```

Проекты сначала сохраните из приложения, затем сделайте коммит. Секреты и пароли в репозиторий не добавляйте.