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
builder.Services.AddSingleton(new DelegatingHandler[] { });
if (builder.Environment.IsProduction())
{
    builder.Services.AddSingleton(KubernetesClientConfiguration.InClusterConfig());
    builder.Services.AddSingleton<Kubernetes>();
}
else if (builder.Environment.IsDevelopment())

{
    builder.Services.AddSingleton
        (KubernetesClientConfiguration.BuildConfigFromConfigFile(builder.Configuration["K8s:profile"]));
    builder.Services.AddSingleton<Kubernetes>();
}


builder.Services.AddSingleton<IRouteController, RouteController>();

var app = builder.Build();

app.UseCors(cors => cors.AllowAnyMethod().AllowAnyOrigin().AllowAnyHeader());

app.UseSwagger();
app.UseSwaggerUI();

var routeController = app.Services.GetRequiredService<IRouteController>();

app.MapPost
    (
        "/route-rules/all",
        async ([FromQuery] bool exact, [FromBody] RouteRuleId id) =>
        {
            try
            {
                var all = await routeController.GetAllAsync(new KubernetesResourceId(id.Namespace, id.DesService));
                return all != null
                           ? Ok
                           (
                               all.Where
                                   (
                                       r => exact
                                                ? r.Name == id.Name
                                                : string.IsNullOrWhiteSpace(id.Name) || r.Name.Contains(id.Name ?? "")
                                   )
                                  .ToArray()
                           )
                           : Ok(Array.Empty<RouteRule>());
            }
            catch (RouteControllingException e)
            {
                return onRouteControllingException(e, $"/route-rules/all?exact={exact}");
            }
        }
    )
   .WithName("Get all route rules")
   .WithOpenApi();

app.MapPost
    (
        "/route-rules/add",
        async ([FromQuery] bool allowOverwrite, [FromBody] RouteRule rule) =>
        {
            var serviceRef = new KubernetesResourceId(rule.Namespace, rule.DesService);
            var uri = $"/route-rules/add?allowOverwrite={allowOverwrite}";
            try
            {
                var all = await routeController.GetAllAsync(serviceRef);
                if (all is null)
                {
                    await routeController.CreateAllAsync(serviceRef, [rule]);
                }
                else
                {
                    if (all.Any(r => r.Name == rule.Name) && !allowOverwrite)
                        return Conflict(MResponse.Failed("Failed To add route rule: Already Existing!"));

                    await routeController.UpdateAllAsync
                    (
                        serviceRef,
                        all.Where(r => r.Name != rule.Name).Append(rule).ToArray()
                    );
                }
            }
            catch (RouteControllingException e)
            {
                return onRouteControllingException(e, uri);
            }


            return Ok(MResponse.Successful());
        }
    )
   .WithName("Create or Update route rule")
   .WithOpenApi();

app.MapPost
    (
        "/route-rules/delete",
        async ([FromQuery] bool exact, [FromBody] RouteRuleId id) =>
        {
            var serviceRef = new KubernetesResourceId(id.Namespace, id.DesService);
            try
            {
                var all = await routeController.GetAllAsync(serviceRef);
                if (all is not null)
                    await routeController.UpdateAllAsync
                    (
                        serviceRef,
                        all.Where(r => exact ? r.Name != id.Name : !r.Name.Contains(r.Name)).ToArray()
                    );
            }
            catch (RouteControllingException e)
            {
                return onRouteControllingException(e, $"/route-rules/delete?exact={exact}");
            }

            return Ok(MResponse.Successful());
        }
    )
   .WithName("Delete route rules")
   .WithOpenApi();

app.Run();
return;

IResult onRouteControllingException(RouteControllingException routeControllingException, string uri)
{
    switch (routeControllingException.Type)
    {
    case RouteControllingExceptionType.BadUpstream:
        return Problem
        (
            routeControllingException.Message,
            uri,
            503,
            "Upstream Down",
            Enum.GetName(routeControllingException.Type)
        );
    case RouteControllingExceptionType.UnmanagedPods:
        return BadRequest(routeControllingException);
    case RouteControllingExceptionType.ConcurrencyConflict:
        return Conflict(routeControllingException);
    default:
        return Problem
        (
            routeControllingException.Message,
            uri,
            500,
            "Upstream Down",
            Enum.GetName(routeControllingException.Type)
        );
    }
}