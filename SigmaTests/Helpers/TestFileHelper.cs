using Microsoft.AspNetCore.Http;

namespace SigmaTests.Helpers;

public static class TestFileHelper
{
    public static IFormFile CreateTestImageFile(string fileName = "test.jpg")
    {
        var contentBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var stream = new MemoryStream(contentBytes);

        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };
    }

    public static IFormFile CreateTestTextFile(string fileName = "test.txt", string content = "Hello World")
    {
        var contentBytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(contentBytes);

        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
    }
}