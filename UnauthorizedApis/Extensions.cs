using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
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
            builder.WithMetadata(new MinimalApiEndpointMetadata());
            return builder;
        }
    }

    public class ConfigureCookieAuthenticationOptions : IConfigureOptions<CookieAuthenticationOptions>, IConfigureNamedOptions<CookieAuthenticationOptions>
    {
        private readonly IEnumerable<ICanChangeAuthorizationRedirectBehavior> _redirectBehaviors;

        public ConfigureCookieAuthenticationOptions(IEnumerable<ICanChangeAuthorizationRedirectBehavior> redirectBehaviors)
        {
            // This gets called
            _redirectBehaviors = redirectBehaviors;
        }

        public void Configure(CookieAuthenticationOptions options)
        {
            // But this doesn't?!?!
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
                    break;
                case StatusCodes.Status403Forbidden:
                    problem.Title = "Forbidden";
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
                        Title = "Unauthorized"
                    });
                    break;

                case StatusCodes.Status403Forbidden:
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        Status = httpContext.Response.StatusCode,
                        Title = "Forbidden"
                    });
                    break;
            }
        }
    }

    public class MinimalApiEndpointMetadata
    {
    }

    public interface IPreferJsonResponses
    {

    }
}
