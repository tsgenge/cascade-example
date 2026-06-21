using CascadeEsdm.SharedKernel.Security;

namespace Cascade.Example.Api.Extensions;

public static class HttpContextExtensions
{
    public static AuthenticatedContext ToAuthenticatedContext(this HttpContext httpContext)
    {
        return AuthenticatedContext.Empty;
    }
}