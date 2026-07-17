# AsistenteIAFacturacion

## Configurar OpenAI

Define cualquiera de estas opciones:

1. Variables de entorno:
   - `OPENAI_API_KEY`
   - `OPENAI_MODEL`
2. `appsettings.json`:

```json
{
  "OpenAI": {
    "ApiKey": "tu_api_key",
    "Model": "gpt-4.1-nano",
    "BaseUrl": "https://api.openai.com/v1/"
  }
}
```

## Registrar servicios en `Program.cs`

Agregar:

```csharp
builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));
builder.Services.AddSingleton<IFacturaConversationStore, InMemoryFacturaConversationStore>();
builder.Services.AddScoped<IClienteService, SystemClienteServiceAdapter>();
builder.Services.AddScoped<IProductoService, SystemProductoServiceAdapter>();
builder.Services.AddScoped<IFacturacionService, SystemFacturacionServiceAdapter>();
builder.Services.AddScoped<FacturacionTools>();
builder.Services.AddScoped<ToolDispatcher>();
builder.Services.AddHttpClient<IOpenAIAsistenteService, OpenAIAsistenteService>();
builder.Services.AddScoped<IAsistenteFacturacionService, AsistenteFacturacionService>();
```

## Probar endpoint con Postman

`POST /api/asistente-facturacion/chat`

```json
{
  "sessionId": "abc123",
  "mensaje": "Haz una factura a Juan Pérez de 2 teclados con 5% de descuento",
  "modo": "texto"
}
```

## Diagnostico rapido de configuracion

Si en publicado aparece un error como `Missing bearer or basic authentication in header`, revisa:

- `GET /api/asistente-facturacion/diagnostico-openai`

Debe responder algo como:

```json
{
  "apiKeyConfigured": true,
  "apiKeySource": "env:OPENAI_API_KEY",
  "model": "gpt-4.1-nano",
  "baseUrl": "https://api.openai.com/v1/"
}
```

Si `apiKeyConfigured` sale `false`, el servidor publicado no esta leyendo la clave aunque en tu maquina local si funcione.

## Ejemplos de mensajes

- `Quiero crear una factura`
- `Haz una factura a Juan Pérez`
- `Haz una factura a Juan Pérez de 2 teclados`
- `Factura a Comercial López 3 mouse, 2 teclados y 1 monitor`
- `Agrega otro teclado`
- `Quita el mouse`
- `Cambia el descuento al 10%`
- `Emite la factura`

## Voz a texto

El launcher ya deja preparado un botón de micrófono. Para voz:

1. Capturar audio desde navegador.
2. Enviar audio a un servicio de speech-to-text.
3. Reusar el mismo endpoint `/api/asistente-facturacion/chat` con el texto reconocido.

## Texto a voz

1. Tomar `respuesta` del endpoint.
2. Enviarla a un motor TTS.
3. Reproducir audio en el launcher.

## Notas de integración

- `IClienteService` debe conectarse al servicio real de clientes.
- `IProductoService` debe conectarse al servicio real de productos/inventario.
- `IFacturacionService` debe conectarse al servicio real de emisión actual.
