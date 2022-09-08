using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Net;
using System.ServiceModel.Channels;
using System.Text;
using System.Xml;

partial class Program
{

    private static readonly string _response = "<s:Envelope xmlns:a=\"http://www.w3.org/2005/08/addressing\" xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">\r\n  <s:Header>\r\n    <a:Action s:mustUnderstand=\"1\">http://tempuri.org/Service1.svc/OperationName</a:Action>\r\n    <a:MessageID>urn:uuid:{MESSAGE_ID}</a:MessageID>\r\n    <a:ReplyTo>\r\n      <a:Address>http://www.w3.org/2005/08/addressing/anonymous</a:Address>\r\n    </a:ReplyTo>\r\n    <ObjectID xmlns=\"http://tempuri.org/\">{MESSAGE_ID}</ObjectID>\r\n    <a:To s:mustUnderstand=\"1\">http://tempuri.org/Service1.svc/OperationName</a:To>\r\n  </s:Header>\r\n  <s:Body>\r\n    <OperationName xmlns=\"http://tempuri.org/\">\r\n      <response xmlns:d4p1=\"http://schemas.datacontract.org/2004/07/MyMessaging\" xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n      </response>\r\n    </OperationName>\r\n  </s:Body>\r\n</s:Envelope>";
    private static readonly MessageEncoder _innerEncoder = GetMessageEncoder();

    private static MessageEncoder GetMessageEncoder()
    {
        var serviceModelAssembly = typeof(MessageVersion).Assembly;
        var binaryVersionType = serviceModelAssembly.GetType("System.ServiceModel.Channels.BinaryVersion");
        dynamic factory = Activator.CreateInstance(serviceModelAssembly.
                GetType("System.ServiceModel.Channels.BinaryMessageEncoderFactory"), new object[]
            {
                MessageVersion.Soap12WSAddressing10,
                int.MaxValue,
                int.MaxValue,
                int.MaxValue,
                XmlDictionaryReaderQuotas.Max,
                long.MaxValue,
                binaryVersionType.GetField("Version1", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).GetValue(null),
                CompressionFormat.None
            });
        return factory.Encoder;
    }

    private static ArraySegment<byte> DecompressBuffer(ArraySegment<byte> buffer, BufferManager bufferManager)
    {
        using (MemoryStream memoryStream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count))
        {
            MemoryStream decompressedStream = new MemoryStream();
            int totalRead = 0;
            int blockSize = 1024;
            byte[] tempBuffer = bufferManager.TakeBuffer(blockSize);
            using (Stream compressedStream = new DeflateStream(memoryStream, CompressionMode.Decompress))
            {
                while (true)
                {
                    int bytesRead = compressedStream.Read(tempBuffer, 0, blockSize);
                    if (bytesRead == 0)
                        break;
                    decompressedStream.Write(tempBuffer, 0, bytesRead);
                    totalRead += bytesRead;
                }
            }
            bufferManager.ReturnBuffer(tempBuffer);

            byte[] decompressedBytes = decompressedStream.ToArray();
            byte[] bufferManagerBuffer = bufferManager.TakeBuffer(decompressedBytes.Length + buffer.Offset);
            Array.Copy(buffer.Array, 0, bufferManagerBuffer, 0, buffer.Offset);
            Array.Copy(decompressedBytes, 0, bufferManagerBuffer, buffer.Offset, decompressedBytes.Length);

            var byteArray = new ArraySegment<byte>(bufferManagerBuffer, buffer.Offset, decompressedBytes.Length);
            bufferManager.ReturnBuffer(buffer.Array);
            return byteArray;
        }
    }

    private static ArraySegment<byte> CompressBuffer(ArraySegment<byte> buffer, BufferManager bufferManager, int messageOffset)
    {
        using (MemoryStream memoryStream = new MemoryStream())
        {
            using (Stream compressedStream = (Stream)new DeflateStream(memoryStream, CompressionMode.Compress, true))
            {
                compressedStream.Write(buffer.Array, buffer.Offset, buffer.Count);
            }

            byte[] compressedBytes = memoryStream.ToArray();
            int totalLength = messageOffset + compressedBytes.Length;
            byte[] bufferedBytes = bufferManager.TakeBuffer(totalLength);

            Array.Copy(compressedBytes, 0, bufferedBytes, messageOffset, compressedBytes.Length);

            bufferManager.ReturnBuffer(buffer.Array);
            var byteArray = new ArraySegment<byte>(bufferedBytes, messageOffset, compressedBytes.Length);

            return byteArray;
        }
    }

    private static Message GetRequestMessage(byte[] message)
    {
        var bufMan = BufferManager.CreateBufferManager(20, 20);
        return _innerEncoder.ReadMessage(new ArraySegment<byte>(DecompressBuffer(new
            ArraySegment<byte>(message), bufMan).Array), bufMan);
    }

    private static byte[] GetResponse(string response)
    {
        var bufMan = BufferManager.CreateBufferManager(20, 20);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(response));
        var xdr = XmlDictionaryReader.CreateTextReader(ms, new XmlDictionaryReaderQuotas());
        var newMessage = Message.CreateMessage(xdr, int.MaxValue, MessageVersion.Soap12WSAddressing10);
        MessageBuffer messageBuffer = newMessage.CreateBufferedCopy(int.MaxValue);
        Message message = messageBuffer.CreateMessage();
        var buffer = _innerEncoder.WriteMessage(message, int.MaxValue, bufMan, 0);
        return CompressBuffer(buffer, bufMan, 0).ToArray();
    }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.MapPost("/Service1.svc", async (HttpContext context) =>
        {
            byte[] resultBuffer = null;
            using (var ms = new MemoryStream())
            {
                await context.Request.Body.CopyToAsync(ms);
                resultBuffer = ms.ToArray();
            }
            var requestMessage = GetRequestMessage(resultBuffer);
            // TODO: Use requestMessage and create response accordingly 
            var serializedResponse = _response.Replace("{MESSAGE_ID}", Guid.NewGuid().ToString());

            var responseMessage = GetResponse(serializedResponse);
            return Results.File(responseMessage, contentType: "application/x-deflate");
        });

        app.Run();
    }

}