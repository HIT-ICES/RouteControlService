using k8s;
using Microsoft.AspNetCore.Mvc;
using RouteControlService;
using Steeltoe.Extensions.Configuration.Placeholder;
using static Microsoft.AspNetCore.Http.Results;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddPlaceholderResolver()
    ;

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();

builder.Services.AddSingleton(KubernetesClientConfiguration.InClusterConfig());
builder.Services.AddSingleton<Kubernetes>();

var app = builder.Build();

app.UseCors(cors => cors.AllowAnyMethod().AllowAnyOrigin().AllowAnyHeader());

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/route-rules/all",
    async ([FromQuery] bool exact, [FromBody] RouteRuleId id) =>
{

}).WithName("Get all route rules").WithOpenApi();

app.MapPost("/route-rules/add",
    async ([FromQuery] bool allowOverwrite, [FromBody] RouteRule id) =>
    {

    }).WithName("Get all route rules").WithOpenApi();

app.MapPost("/route-rules/delete",
    async ([FromQuery] bool exact, [FromBody] RouteRuleId id) =>
    {

    }).WithName("Get all route rules").WithOpenApi();

app.Run();
