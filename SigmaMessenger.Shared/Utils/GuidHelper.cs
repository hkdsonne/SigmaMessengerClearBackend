namespace Shared.Utils;

public static class GuidHelper
{
    public static string ToUrlString(this Guid guid) => guid.ToString("N");
}