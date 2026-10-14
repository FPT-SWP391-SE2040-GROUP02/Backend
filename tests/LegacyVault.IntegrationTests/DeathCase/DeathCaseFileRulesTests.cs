using System.Text;
using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Nhận loại tệp thật và làm sạch tên tệp giấy chứng tử (NFR-09, SEC-12).</summary>
public sealed class DeathCaseFileRulesTests
{
    [Fact]
    public void Nfr09_DetectsPdfJpegPngWebpByContent()
    {
        // Chuẩn bị
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ");
        byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        byte[] webp = Encoding.ASCII.GetBytes("RIFF\u0024\u0000\u0000\u0000WEBPVP8 ");

        // Hành động
        string? pdfType = DeathCaseFileRules.DetectContentType(pdf);
        string? jpegType = DeathCaseFileRules.DetectContentType(jpeg);
        string? pngType = DeathCaseFileRules.DetectContentType(png);
        string? webpType = DeathCaseFileRules.DetectContentType(webp);

        // Kiểm tra
        Assert.Equal("application/pdf", pdfType);
        Assert.Equal("image/jpeg", jpegType);
        Assert.Equal("image/png", pngType);
        Assert.Equal("image/webp", webpType);
    }

    [Fact]
    public void Ac70_Nfr09_FakeExtensionIsRejected()
    {
        // Chuẩn bị: tệp .exe (MZ) đổi đuôi .pdf, tệp RIFF nhưng là WAVE, tệp quá ngắn, tệp rỗng
        byte[] exe = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00 };
        byte[] wave = Encoding.ASCII.GetBytes("RIFF\u0024\u0000\u0000\u0000WAVEfmt ");
        byte[] tooShort = Encoding.ASCII.GetBytes("%PD");
        byte[] empty = Array.Empty<byte>();

        // Hành động
        string? exeType = DeathCaseFileRules.DetectContentType(exe);
        string? waveType = DeathCaseFileRules.DetectContentType(wave);
        string? shortType = DeathCaseFileRules.DetectContentType(tooShort);
        string? emptyType = DeathCaseFileRules.DetectContentType(empty);

        // Kiểm tra
        Assert.Null(exeType);
        Assert.Null(waveType);
        Assert.Null(shortType);
        Assert.Null(emptyType);
    }

    [Fact]
    public void Sec12_FileNameDropsPathSegments()
    {
        // Chuẩn bị
        string traversal = "../../etc/passwd";
        string windowsPath = "C:\\Users\\an\\giay\u0007-chung-tu.pdf";
        string onlyDots = "..";
        string tooLong = new string('a', 300) + ".pdf";

        // Hành động
        string traversalName = DeathCaseFileRules.SanitizeFileName(traversal);
        string windowsName = DeathCaseFileRules.SanitizeFileName(windowsPath);
        string dotsName = DeathCaseFileRules.SanitizeFileName(onlyDots);
        string nullName = DeathCaseFileRules.SanitizeFileName(null);
        string longName = DeathCaseFileRules.SanitizeFileName(tooLong);

        // Kiểm tra
        Assert.Equal("passwd", traversalName);
        Assert.Equal("giay-chung-tu.pdf", windowsName);
        Assert.Equal(DeathCaseFileRules.DefaultFileName, dotsName);
        Assert.Equal(DeathCaseFileRules.DefaultFileName, nullName);
        Assert.Equal(255, longName.Length);
    }
}
