# Base runtime compatible con FFmpeg 4 (Ubuntu 22.04)
FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy

WORKDIR /app

# Instalar dependencias necesarias para OpenCV
RUN apt-get update && apt-get install -y \
    ffmpeg \
    libtesseract4 \
    libgtk2.0-0 \
    libdc1394-25 \
    libjpeg-turbo8 \
    libpng16-16 \
    libtiff5 \
    libopenjp2-7 \
    libopenexr25 \
    && rm -rf /var/lib/apt/lists/*

# Copiar publish
COPY publish/ .

EXPOSE 5000

ENTRYPOINT ["dotnet", "Face.Api.dll"]