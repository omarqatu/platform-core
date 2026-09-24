// Plant for Check 9: a scope variable taken from a request header — self-escalation.
namespace Api.Selftest;

public static class Planted
{
    public static string Sql(string headerValue) => "SET LOCAL app.scope_all = '" + headerValue + "'";
}
