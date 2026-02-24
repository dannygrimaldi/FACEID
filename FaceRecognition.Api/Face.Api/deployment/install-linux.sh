#!/bin/bash

# Instalar .NET 8
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0

# Instalar OpenCV dependencies
sudo apt-get install -y libopencv-dev libgtk-3-dev

# Crear directorio de aplicación
sudo mkdir -p /var/www/face-api
sudo chown -R www-data:www-data /var/www/face-api

# Copiar archivos publicados
# sudo cp -r /path/to/published/app/* /var/www/face-api/

# Instalar servicio
sudo cp face-api.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable face-api
sudo systemctl start face-api

echo "Instalación completada. Servicio iniciado."