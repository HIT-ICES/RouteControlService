using k8s;
using Microsoft.AspNetCore.Mvc;
using RouteControlService;
using RouteControlService.RoutControlling;
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
builder.Services.AddSingleton<IRouteController, FakeRouteController>();

var app = builder.Build();

app.UseCors(cors => cors.AllowAnyMethod().AllowAnyOrigin().AllowAnyHeader());

app.UseSwagger();
app.UseSwaggerUI();

var routectl = app.Services.GetRequiredService<IRouteController>();

app.MapPost("/route-rules/all",
    async ([FromQuery] bool exact, [FromBody] RouteRuleId id) =>
{
    var all = await routectl.GetAllAsync(id.Namespace, id.DesService);
    return all != null ?
    Ok(all.Where(r => exact ? r.Name == id.Name : r.Name.Contains(id.Name ?? "")).ToArray()) :
    Ok(Array.Empty<RouteRule>());
}).WithName("Get all route rules").WithOpenApi();

app.MapPost("/route-rules/add",
    async ([FromQuery] bool allowOverwrite, [FromBody] RouteRule rule) =>
    {
        var all = await routectl.GetAllAsync(rule.Namespace, rule.DesService);
        if (all is null)
        {
            await routectl.CreateAllAsync(rule.Namespace, rule.DesService new[] { rule });
        }
        else
        {
            if (all.Any(r => r.Name == rule.Name) && !allowOverwrite)
            {
                return MResponse.Failed("Failed To add route rule: Already Existing!");
            }
            await routectl.UpdateAllAsync(rule.Namespace, rule.DesService,
                all.Where(r => r.Name != rule.Name).Append(rule).ToArray());

        }
        return MResponse.Successful();
    }).WithName("Get all route rules").WithOpenApi();

app.MapPost("/route-rules/delete",
    async ([FromQuery] bool exact, [FromBody] RouteRuleId id) =>
    {
        var all = await routectl.GetAllAsync(id.Namespace, id.DesService);
        if (all is not null)
        {

            await routectl.UpdateAllAsync(id.Namespace, id.DesService,
                all.Where(r => exact? 
                r.Name != id.Name:!r.Name.Contains(r.Name)).ToArray());

        }
        return MResponse.Successful();
    }).WithName("Get all route rules").WithOpenApi();

app.Run();
