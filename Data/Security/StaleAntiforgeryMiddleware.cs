using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;

namespace MedicalManager.Data.Security;

/// <summary>
/// Each new container used to create a new data-protection key. Browsers still holding
/// the previous sign-in cookie then got an empty 400, with no buttons and no form.
/// Drop that cookie and return to the page so the next sign-in can succeed.
/// </summary>
public sealed class StaleAntiforgeryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !HasAntiforgeryCookie(context))
        {
            await next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (Exception ex) when (IsStaleKey(ex) && !context.Response.HasStarted)
        {
            DeleteAntiforgeryCookies(context);
            context.Response.Redirect(LocalPath(context));
            return;
        }
        catch (AntiforgeryValidationException)
        {
            // The antiforgery middleware turns a normal mismatch into a 400.
        }

        await next(context);
    }

    private static void DeleteAntiforgeryCookies(HttpContext context)
    {
        foreach (var name in context.Request.Cookies.Keys)
        {
            if (!name.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal))
                continue;

            context.Response.Cookies.Delete(name, new CookieOptions
            {
                Path = "/",
                Secure = context.Request.IsHttps,
                HttpOnly = true,
                SameSite = SameSiteMode.Strict
            });
        }
    }

    private static bool HasAntiforgeryCookie(HttpContext context) =>
        context.Request.Cookies.Keys.Any(name => name.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));

    private static string LocalPath(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/'))
            return "/Account/Login";

        return path;
    }

    private static bool IsStaleKey(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is CryptographicException)
                return true;

            if (current.Message.Contains("key ring", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
