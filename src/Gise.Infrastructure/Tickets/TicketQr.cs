using QRCoder;

namespace Gise.Infrastructure.Tickets;

public static class TicketQr
{
    /// <summary>A PNG of the code, black on white with a quiet zone, at medium error correction: readable off a cracked phone screen.</summary>
    public static byte[] Png(string code, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    /// <summary>The code's modules, quiet zone included, for drawing it as vector shapes: <c>true</c> is dark.</summary>
    public static bool[,] Modules(string code)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.M);
        var size = data.ModuleMatrix.Count;
        var modules = new bool[size, size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                modules[y, x] = data.ModuleMatrix[y][x];
            }
        }

        return modules;
    }
}
