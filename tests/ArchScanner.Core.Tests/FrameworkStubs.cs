namespace ArchScanner.Core.Tests;

/// <summary>
/// Minimal stand-ins for the framework types the heuristics key off (MediatR, EF Core,
/// ASP.NET Core Mvc, Extensions.Hosting/Options, xUnit) — Section 8.1's recommended approach:
/// build a tiny in-memory compilation rather than referencing the real NuGet packages.
/// </summary>
public static class FrameworkStubs
{
    public const string Source = """
        namespace MediatR
        {
            public interface IRequestHandler<TRequest, TResponse> { }
            public interface IRequestHandler<TRequest> { }
            public interface INotificationHandler<TNotification> { }
            public interface IRequest<TResponse> { }
            public interface IRequest { }
            public interface INotification { }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbSet<TEntity> { }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class ControllerBase { }
            public class Controller : ControllerBase { }

            public class ApiControllerAttribute : System.Attribute { }
            public class RouteAttribute : System.Attribute
            {
                public RouteAttribute(string template) { }
            }
            public class HttpGetAttribute : System.Attribute
            {
                public HttpGetAttribute() { }
                public HttpGetAttribute(string template) { }
            }
            public class HttpPostAttribute : System.Attribute
            {
                public HttpPostAttribute() { }
                public HttpPostAttribute(string template) { }
            }
        }

        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostedService { }
            public abstract class BackgroundService : IHostedService { }
        }

        namespace Microsoft.Extensions.Options
        {
            public interface IOptions<TOptions> { }
            public interface IOptionsSnapshot<TOptions> { }
            public interface IOptionsMonitor<TOptions> { }
        }

        namespace Xunit
        {
            public class FactAttribute : System.Attribute { }
            public class TheoryAttribute : System.Attribute { }
        }

        namespace MassTransit
        {
            public interface IConsumer<TMessage> { }

            public interface IPublishEndpoint
            {
                void Publish<T>(T message);
            }

            public interface IBus
            {
                void Send<T>(T message);
            }
        }

        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static void MapGet(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder builder, string pattern, System.Delegate handler) { }
                public static void MapPost(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder builder, string pattern, System.Delegate handler) { }
            }
        }
        """;
}
