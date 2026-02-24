# Publicar para Linux x64
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish/linux-x64

# Publicar para Windows x64
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish/win-x64

# Variables de entorno requeridas:
# QDRANT__BASEURL=http://your-qdrant-server:6333/
# QDRANT__KEY=your-api-key-here

# Ejecutar:
# Linux: ./Face.Api
# Windows: Face.Api.exe