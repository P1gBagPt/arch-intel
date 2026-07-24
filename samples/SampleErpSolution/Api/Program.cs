using Microsoft.EntityFrameworkCore;
using SampleErp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SampleErp.Application.CreateOrderCommand).Assembly));
builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("SampleErp"));
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddInfrastructure();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();

app.MapGet("/health", () => "OK");

app.Run();
