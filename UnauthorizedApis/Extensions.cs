using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace UnauthorizedApis
{
    public static class Extensions
    {
        public static IServiceCollection AddUnauthorizedEndpointsBehavior(this IServiceCollection services)
        {
            // MVC would add this
            services.AddTransient<ICanChangeAuthorizationRedirectBehavior, MvcAuthorizationRedirectBehavior>();

            // Minimal APIs would add this
            services.AddTransient<ICanChangeAuthorizationRedirectBehavior, MinimalApisAuthorizationRedirectBehavior>();

            // Authorization would add this (kinda)
            services.ConfigureOptions<ConfigureCookieAuthenticationOptions>();

            return services;
        }

        public static IEndpointConventionBuilder FixApiMetadata(this IEndpointConventionBuilder builder)
        {
            builder
                .WithMetadata(new MinimalApiEndpointMetadata());
            return builder;
        }
    }

    public class ConfigureCookieAuthenticationOptions : IConfigureOptions<CookieAuthenticationOptions>, IConfigureNamedOptions<CookieAuthenticationOptions>
    {
        private readonly IEnumerable<ICanChangeAuthorizationRedirectBehavior> _redirectBehaviors;

        public ConfigureCookieAuthenticationOptions(IEnumerable<ICanChangeAuthorizationRedirectBehavior> redirectBehaviors)
        {
            _redirectBehaviors = redirectBehaviors;
        }

        public void Configure(CookieAuthenticationOptions options)
        {
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = async (ctx) =>
                {
                    if (_redirectBehaviors.Any())
                    {
                        foreach (var behavior in _redirectBehaviors)
                        {
                            await behavior.OnRedirectToLogin(ctx);
                        }
                    }
                },

                OnRedirectToAccessDenied = async (ctx) =>
                {
                    if (_redirectBehaviors.Any())
                    {
                        foreach (var behavior in _redirectBehaviors)
                        {
                            await behavior.OnRedirectToAccessDenied(ctx);
                        }
                    }
                }
            };
        }

        public void Configure(string name, CookieAuthenticationOptions options)
        {
            Configure(options);
        }
    }

    public interface ICanChangeAuthorizationRedirectBehavior
    {
        Task OnRedirectToLogin(RedirectContext<CookieAuthenticationOptions> context);
        Task OnRedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context);
    }

    public class MvcAuthorizationRedirectBehavior : ICanChangeAuthorizationRedirectBehavior
    {
        public async Task OnRedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
        {
            if (IsApiControllerEndpoint(context.HttpContext))
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await FormatProblemDetailsResponse(context.HttpContext);
            }
        }

        public async Task OnRedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
        {
            if (IsApiControllerEndpoint(context.HttpContext))
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await FormatProblemDetailsResponse(context.HttpContext);
            }
        }

        private static bool IsApiControllerEndpoint(HttpContext httpContext)
        {
            var endpoint = httpContext.GetEndpoint();
            var isApiController = endpoint.Metadata.GetMetadata<ApiControllerAttribute>() != null;

            return isApiController;
        }

        private static async Task FormatProblemDetailsResponse(HttpContext httpContext)
        {
            var problem = new ProblemDetails
            {
                Status = httpContext.Response.StatusCode
            };

            switch (problem.Status)
            {
                case StatusCodes.Status401Unauthorized:
                    problem.Title = "Unauthorized";
                    problem.Type = "https://datatracker.ietf.org/doc/html/rfc7235#section-3.1";
                    break;
                case StatusCodes.Status403Forbidden:
                    problem.Title = "Forbidden";
                    problem.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3";
                    break;
            }

            await httpContext.Response.WriteAsJsonAsync(problem);
        }
    }

    public class MinimalApisAuthorizationRedirectBehavior : ICanChangeAuthorizationRedirectBehavior
    {
        public async Task OnRedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
        {
            if (IsApiEndpoint(context.HttpContext))
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await FormatProblemDetailsResponse(context.HttpContext);
            }
        }

        public async Task OnRedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
        {
            if (IsApiEndpoint(context.HttpContext))
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await FormatProblemDetailsResponse(context.HttpContext);
            }
        }

        private static bool IsApiEndpoint(HttpContext httpContext)
        {
            var endpoint = httpContext.GetEndpoint();
            var isApiController = endpoint.Metadata.GetMetadata<MinimalApiEndpointMetadata>() != null;

            return isApiController;
        }

        private static async Task FormatProblemDetailsResponse(HttpContext httpContext)
        {
            switch (httpContext.Response.StatusCode)
            {
                case StatusCodes.Status401Unauthorized:
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        Status = httpContext.Response.StatusCode,
                        Title = "Unauthorized",
                        Type = "https://datatracker.ietf.org/doc/html/rfc7235#section-3.1",
                    });
                    break;

                case StatusCodes.Status403Forbidden:
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        Status = httpContext.Response.StatusCode,
                        Title = "Forbidden",
                        Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3"
                    });
                    break;
            }
        }
    }

    public class MinimalApiEndpointMetadata : IPreferNoRedirects
    {
    }

    public interface IPreferJsonResponses
    {

    }

    public interface IPreferNoRedirects
    {

    }

    public class MvcDeveloperPageExceptionFilter : IDeveloperPageExceptionFilter
    {
        public async Task HandleExceptionAsync(ErrorContext errorContext, Func<ErrorContext, Task> next)
        {
            var httpContext = errorContext.HttpContext;
            if (IsApiControllerEndpoint(httpContext))
            {
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await FormatProblemDetailsResponse(httpContext, errorContext.Exception);
            }
            else
            {
                await next(errorContext);
            }
        }

        private static bool IsApiControllerEndpoint(HttpContext httpContext)
        {
            if (httpContext.Request.Query.Keys.Contains("forcehtml"))
            {
                return false;
            }

            var endpoint = httpContext.GetEndpoint();
            var isApiController = endpoint?.Metadata.GetMetadata<ApiControllerAttribute>() != null;

            return isApiController;
        }

        private static async Task FormatProblemDetailsResponse(HttpContext httpContext, Exception exception)
        {
            var problem = new ProblemDetails
            {
                Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
                Title = "An unhandled exception occurred while processing the request",
                Status = httpContext.Response.StatusCode,
                Detail = exception.ToString()
            };
            problem.Extensions.Add("traceId", Activity.Current?.Id ?? httpContext.TraceIdentifier);

            await httpContext.Response.WriteAsJsonAsync(problem);
        }
    }

}
