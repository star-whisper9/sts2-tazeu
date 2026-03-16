using Godot;
using QRCoder;

namespace TazeU.Scripts;

/// <summary>
/// QR 码生成 → Godot ImageTexture 转换工具。
/// 使用 QRCoder（MIT）生成 PNG 字节 → Godot Image.LoadPngFromBuffer。
/// </summary>
internal static class QRCodeHelper
{
    /// <summary>
    /// 为指定 URL 生成 QR 码纹理。
    /// </summary>
    /// <param name="url">二维码内容</param>
    /// <param name="pixelsPerModule">每个模块的像素数（控制二维码大小）</param>
    internal static ImageTexture GenerateQRTexture(string url, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(data);
        var pngBytes = qrCode.GetGraphic(pixelsPerModule);

        var image = new Image();
        image.LoadPngFromBuffer(pngBytes);
        return ImageTexture.CreateFromImage(image);
    }
}
