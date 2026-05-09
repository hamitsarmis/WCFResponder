# WCFResponder

A minimal ASP.NET Core stub server that mimics a WCF endpoint speaking SOAP 1.2 with WS-Addressing 1.0, the .NET binary message encoding (`application/soap+msbin1`), and `Deflate` HTTP compression on top. It accepts a compressed binary SOAP request, decodes it through the internal `BinaryMessageEncoderFactory`, and returns a hard-coded SOAP response re-encoded the same way.

It exists for testing and reverse-engineering scenarios where you need a server that "looks like" a WCF service to a client, without standing up a full WCF host. The response payload is fixed; only the message ID is randomised per request.

## Why this exists

WCF clients configured with `BinaryMessageEncodingBindingElement` and `DeflateMessageEncoder` don't speak plain HTTP/JSON. To reply to one with a stub, you have to:

1. Inflate the request body.
2. Hand the inflated bytes to the binary encoder so it can parse the binary XML dictionary.
3. Build a SOAP `Message` for the response.
4. Encode it back through the binary encoder.
5. Deflate the result before writing it to the HTTP response.

This project does exactly that, as an ASP.NET Core minimal API.

## Tech stack

- .NET 10 (target framework `net10.0`)
- ASP.NET Core minimal APIs
- `System.ServiceModel.Primitives` — for `MessageVersion`, `BufferManager`, `Message`, and reflection access to the internal `BinaryMessageEncoderFactory`
- `Swashbuckle.AspNetCore` — Swagger UI in development
- `System.IO.Compression.DeflateStream` — request/response compression

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An IDE that understands the `.slnx` solution format (Visual Studio 17.10+, JetBrains Rider 2024.1+, or VS Code with the C# Dev Kit). The `dotnet` CLI handles `.slnx` natively from .NET 9.0.200 onwards.

## Build and run

```bash
dotnet restore
dotnet build
dotnet run
```

By default the app listens on:

- `https://localhost:7026`
- `http://localhost:5026`

The launch profile (`Properties/launchSettings.json`) opens Swagger UI at `/swagger` in the browser when you run the `WCFResponder` profile.

## Endpoint

| Method | Path             | Request body                                 | Response body                                  |
| ------ | ---------------- | -------------------------------------------- | ---------------------------------------------- |
| `POST` | `/Service1.svc`  | Deflate-compressed binary SOAP 1.2 message   | Deflate-compressed binary SOAP 1.2 message     |

Response `Content-Type` is `application/x-deflate`. The hard-coded SOAP body responds to an `OperationName` action under the `http://tempuri.org/` namespace; edit `_response` in `Program.cs` to change it.

### Example client snippet

```csharp
var binding = new CustomBinding(
    new BinaryMessageEncodingBindingElement
    {
        MessageVersion = MessageVersion.Soap12WSAddressing10,
        CompressionFormat = CompressionFormat.Deflate
    },
    new HttpTransportBindingElement());

var factory = new ChannelFactory<IService1>(
    binding,
    new EndpointAddress("http://localhost:5026/Service1.svc"));
```

## How the request/response pipeline works

`Program.cs` is a single file. The interesting pieces:

- `GetMessageEncoder()` — uses reflection to instantiate `System.ServiceModel.Channels.BinaryMessageEncoderFactory`, which is `internal` in `System.ServiceModel.Primitives`. The factory is configured for SOAP 1.2 + WS-Addressing 1.0 and `BinaryVersion.Version1`, with no built-in compression (compression is handled separately in this project).
- `DecompressBuffer` / `CompressBuffer` — wrap `DeflateStream` and reuse a `BufferManager` so the binary encoder gets buffers it can return to the pool.
- `GetRequestMessage` — inflates the incoming bytes, then asks the binary encoder to parse them into a `Message`.
- `GetResponse` — parses the hard-coded XML SOAP envelope into a `Message`, runs it through the binary encoder, and deflates the output.

The `MapPost("/Service1.svc", ...)` handler currently ignores the parsed request and always returns the same response with a fresh `MessageID`. The `// TODO` comment in the handler marks the spot where you would dispatch on the request and return different responses per operation.

## Configuration

Standard ASP.NET Core configuration applies. `appsettings.json` and `appsettings.Development.json` only set logging levels today. Override URLs with the usual `ASPNETCORE_URLS` environment variable or `--urls` command-line argument.

## Solution format

The solution is stored in the modern XML-based `.slnx` format (`WCFResponder.slnx`) instead of the legacy `.sln` text format. Most tools that support `.slnx` will also fall back to `.sln` if both exist, so only one is checked in.

## Limitations and known issues

- The response is hard-coded. No request inspection or routing.
- HTTPS redirection is enabled (`UseHttpsRedirection`) — call the HTTPS URL or disable it if your client only speaks plain HTTP.
- `BinaryMessageEncoderFactory` is accessed reflectively. A future runtime change to that internal type can break the encoder construction; pin `System.ServiceModel.Primitives` accordingly when upgrading.
- Buffer pool sizes (`BufferManager.CreateBufferManager(20, 20)`) are tiny and intended for low-throughput debugging, not production load.

## License

No license file is present; treat the code as "all rights reserved" unless the author adds one.
