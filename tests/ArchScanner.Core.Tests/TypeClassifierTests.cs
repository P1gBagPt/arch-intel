using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchScanner.Core.Tests;

public class TypeClassifierTests
{
    [Fact]
    public void Controller_BaseTypeChain_ClassifiesAsController()
    {
        const string code = """
            namespace Sample
            {
                public class OrderController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet("/orders")]
                    public void Get() { }
                }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        var controller = Assert.Single(nodes, n => n.NodeType == NodeType.Controller);
        Assert.Equal("OrderController", controller.Name);

        var action = Assert.Single(nodes, n => n.NodeType == NodeType.Method && n.Name == "Get");
        Assert.Equal("GET", action.Metadata[MetadataKeys.HttpMethod]);
        Assert.Equal("/orders", action.Metadata[MetadataKeys.RouteTemplate]);
    }

    [Fact]
    public void ApiControllerAttribute_WithoutBaseClass_StillClassifiesAsController()
    {
        const string code = """
            namespace Sample
            {
                [Microsoft.AspNetCore.Mvc.ApiController]
                public class OrdersEndpoints { }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        Assert.Contains(nodes, n => n.NodeType == NodeType.Controller && n.Name == "OrdersEndpoints");
    }

    [Fact]
    public void PlainClass_DoesNotClassifyAsController()
    {
        const string code = """
            namespace Sample
            {
                public class OrderHelper { }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        var node = Assert.Single(nodes, n => n.Name == "OrderHelper");
        Assert.Equal(NodeType.Class, node.NodeType);
    }

    [Fact]
    public void DbContext_And_ItsDbSetEntity_ClassifyAsEfDbContextAndEfEntity()
    {
        const string code = """
            namespace Sample
            {
                public class Order { }

                public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
                {
                    public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
                }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        Assert.Contains(nodes, n => n.NodeType == NodeType.EfDbContext && n.Name == "AppDbContext");
        Assert.Contains(nodes, n => n.NodeType == NodeType.EfEntity && n.Name == "Order");
    }

    [Fact]
    public void MediatRHandler_ImplementingIRequestHandler_ClassifiesAsMediatRHandler()
    {
        const string code = """
            namespace Sample
            {
                public class CreateOrder : MediatR.IRequest<int> { }

                public class CreateOrderHandler : MediatR.IRequestHandler<CreateOrder, int> { }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        Assert.Contains(nodes, n => n.NodeType == NodeType.MediatRHandler && n.Name == "CreateOrderHandler");
        // CreateOrder implements IRequest<int> only — not a handler and not a notification, so it
        // keeps its structural NodeType (02's enum has no dedicated "MediatRRequest" node type).
        Assert.Contains(nodes, n => n.NodeType == NodeType.Class && n.Name == "CreateOrder");
    }

    [Fact]
    public void DomainEvent_ImplementingINotification_WithMatchingNamingConvention_IsTaggedAsMatch()
    {
        const string code = """
            namespace Sample.Events
            {
                public class OrderCreatedEvent : MediatR.INotification { }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        var domainEvent = Assert.Single(nodes, n => n.NodeType == NodeType.DomainEvent);
        Assert.Equal("true", domainEvent.Metadata[MetadataKeys.NamingConventionMatch]);
    }

    [Fact]
    public void DomainEvent_ImplementingINotification_WithoutNamingConvention_IsStillEmitted_ButTaggedAsNonMatch()
    {
        const string code = """
            namespace Sample
            {
                public class OrderCreated : MediatR.INotification { }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        var domainEvent = Assert.Single(nodes, n => n.NodeType == NodeType.DomainEvent);
        Assert.Equal("false", domainEvent.Metadata[MetadataKeys.NamingConventionMatch]);
    }

    [Fact]
    public void BackgroundService_ClassifiesAsBackgroundWorker_AndPlainIHostedService_ClassifiesAsHostedService()
    {
        const string code = """
            namespace Sample
            {
                public class OrderSyncWorker : Microsoft.Extensions.Hosting.BackgroundService { }

                public class OrderPoller : Microsoft.Extensions.Hosting.IHostedService { }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        Assert.Contains(nodes, n => n.NodeType == NodeType.BackgroundWorker && n.Name == "OrderSyncWorker");
        Assert.Contains(nodes, n => n.NodeType == NodeType.HostedService && n.Name == "OrderPoller");
    }

    [Fact]
    public void Repository_MatchingNamingAndInterface_ClassifiesWithHighestConfidence()
    {
        const string code = """
            namespace Sample
            {
                public interface IOrderRepository { }

                public class OrderRepository : IOrderRepository { }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        var repo = Assert.Single(nodes, n => n.NodeType == NodeType.Repository);
        Assert.Equal("NamingAndInterface", repo.Metadata[MetadataKeys.DetectionConfidence]);
    }

    [Fact]
    public void Repository_NamingOnly_WithoutMatchingInterface_StillClassifies_ButWithLowerConfidence()
    {
        const string code = """
            namespace Sample
            {
                public class OrderRepository { }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        var repo = Assert.Single(nodes, n => n.NodeType == NodeType.Repository);
        Assert.Equal("NamingOnly", repo.Metadata[MetadataKeys.DetectionConfidence]);
    }

    [Fact]
    public void ConfigurationSection_InjectedViaIOptions_ReclassifiesTheOptionsType()
    {
        const string code = """
            namespace Sample
            {
                public class SmtpSettings { }

                public class EmailSender
                {
                    public EmailSender(Microsoft.Extensions.Options.IOptions<SmtpSettings> options) { }
                }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        Assert.Contains(nodes, n => n.NodeType == NodeType.ConfigurationSection && n.Name == "SmtpSettings");
        // EmailSender itself is untouched — it's an ordinary class, not a settings type.
        Assert.Contains(nodes, n => n.NodeType == NodeType.Class && n.Name == "EmailSender");
    }

    [Fact]
    public void TestClass_WithFactAttributeMethod_ClassifiesAsTestClassAndTestMethod()
    {
        const string code = """
            namespace Sample.Tests
            {
                public class OrderServiceTests
                {
                    [Xunit.Fact]
                    public void CreatesOrder() { }

                    public void HelperNotATest() { }
                }
            }
            """;

        var (nodes, _) = HeuristicTestHarness.ClassifyWithStubs(code);

        Assert.Contains(nodes, n => n.NodeType == NodeType.TestClass && n.Name == "OrderServiceTests");
        Assert.Contains(nodes, n => n.NodeType == NodeType.TestMethod && n.Name == "CreatesOrder");
        Assert.Contains(nodes, n => n.NodeType == NodeType.Method && n.Name == "HelperNotATest");
    }
}
