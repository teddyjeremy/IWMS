namespace IWMS.Application.Common;

public static class DateTimeExtensions
{
    public static DateTime AsUtc(this DateTime value) =>
        value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}