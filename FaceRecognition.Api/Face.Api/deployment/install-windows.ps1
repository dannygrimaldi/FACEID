# Instalar como Windows Service usando NSSM (Non-Sucking Service Manager)

# Descargar NSSM
# wget https://nssm.cc/release/nssm-2.24.zip
# unzip nssm-2.24.zip

# Instalar servicio
nssm install FaceApi "C:\Program Files\dotnet\dotnet.exe"
nssm set FaceApi AppDirectory "C:\inetpub\face-api"
nssm set FaceApi AppParameters "Face.Api.dll"
nssm set FaceApi AppEnvironmentExtra QDRANT__BASEURL=http://127.0.0.1:6333/ QDRANT__KEY=YOUR_QDRANT_API_KEY ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://0.0.0.0:5000

# Iniciar servicio
nssm start FaceApi

# Verificar
nssm status FaceApi