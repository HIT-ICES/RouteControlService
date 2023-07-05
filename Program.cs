using k8s;
using Microsoft.AspNetCore.Mvc;
using RouteControlService;
using Steeltoe.Extensions.Configuration.Placeholder;
using static Microsoft.AspNetCore.Http.Results;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddYamlFile("application.yaml", false, true)
    .AddJsonFile("appsettings.json", true, true)
    .AddEnvironmentVariables()
    .AddPlaceholderResolver()
    ;

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton(KubernetesClientConfiguration.InClusterConfig());
builder.Services.AddSingleton<Kubernetes>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/service/add", async ([FromBody] Service service, [FromServices] ServiceDbContext db) =>
{
    var serviceEntity = new ServiceEntity();
    service.CopyToEntity(serviceEntity);
    await db.Services.AddAsync(serviceEntity);
    await db.SaveChangesAsync();
    return MResponse.Successful();
}).WithName("AddService").WithOpenApi();

app.MapPost("/service/update", async ([FromBody] Service service, [FromServices] ServiceDbContext db) =>
{
    var t = await db.Services.Include(s => s.Interfaces)
        .AsTracking().FirstOrDefaultAsync(s => s.Id == service.Id);
    if (t is null) return MResponse.Failed($"Service with Id {service.Id} not found");
    service.CopyToEntity(t);
    await db.SaveChangesAsync();
    return MResponse.Successful();
}).WithName("UpdateService").WithOpenApi();

app.MapPost("service/delete", async ([FromBody] ByIdBean bean, [FromServices] ServiceDbContext db) =>
{
    return await db.Services.Where(s => s.Id == bean.ServiceId).DeleteFromQueryAsync() >= 1
        ? MResponse.Successful()
        : MResponse.Failed($"Service with Id {bean.ServiceId} not found");
}).WithName("DeleteService").WithOpenApi();

app.MapPost("service/getById", async ([FromBody] ByIdBean bean, [FromServices] ServiceDbContext db) =>
{
    var entity = await db.Services
        .Include(s => s.Interfaces)
        .FirstOrDefaultAsync(s => s.Id == bean.ServiceId);
    return entity is null
        ? Ok(MResponse.Failed($"Service with Id {bean.ServiceId} not found"))
        : Ok(MResponse.Successful(Service.FromEntity(entity)));
}).WithName("GetServiceById").WithOpenApi();

app.MapPost("/service/getByNameVersion",
    async ([FromBody] ByNameVersionBean bean, [FromServices] ServiceDbContext db) =>
    {
        var entity = bean.Version is null
            ? await db.Services
                .Include(s => s.Interfaces)
                .FirstOrDefaultAsync(s => s.Name == bean.Name && !s.HasVersion)
            : await db.Services
                .Include(s => s.Interfaces)
                .FirstOrDefaultAsync(s => s.Name == bean.Name &&
                                          s.VersionMajor == bean.Version.Major &&
                                          s.VersionMinor == bean.Version.Minor &&
                                          s.VersionPatch == bean.Version.Patch);
        return entity is null
            ? Ok(MResponse.Failed($"Service with Name {bean.Name} and Version {bean.Version} not found"))
            : Ok(MResponse.Successful(Service.FromEntity(entity)));
    }).WithName("GetServiceByNameVersion").WithOpenApi();

app.Run();

namespace RouteControlService
{
    public record ByIdBean(string ServiceId);

    public record ByNameVersionBean(string Name, Version? Version);

    public record Version(string Major, string Minor, string Patch);

    public record Interface(string Id, string Path, int InputSize, string OutputSize)
    {
        public InterfaceEntity ToEntity()
        {
            return new()
            {
                Id = Id,
                Path = Path,
                InputSize = InputSize,
                OutputSize = OutputSize
            };
        }

        public static Interface FromEntity(InterfaceEntity entity)
        {
            return new Interface(entity.Id, entity.Path, entity.InputSize, entity.OutputSize);
        }
    }

    public record Resource(decimal Cpu, decimal Ram, decimal Disk, decimal GpuCore, decimal GpuMem);

    public record Service(string Id, string Name, string Repo, string ImageUrl, Version? Version,
        List<Interface> Interfaces,
        Resource? IdleResource,
        Resource DesiredResource, int DesiredCapability)
    {
        public void CopyToEntity(ServiceEntity entity)
        {
            entity.Id = Id;
            entity.Name = Name;
            entity.DesiredCapability = DesiredCapability;
            entity.Repo = Repo;
            entity.ImageUrl = ImageUrl;
            entity.Version = Version;
            entity.DesiredResource = DesiredResource;
            entity.IdleResource = IdleResource;
            entity.Interfaces = Interfaces.Select(i => i.ToEntity()).ToList();
        }

        public static Service FromEntity(ServiceEntity entity)
        {
            return new Service(
                entity.Id,
                entity.Name,
                entity.Repo,
                entity.ImageUrl,
                entity.Version,
                entity.Interfaces.Select(Interface.FromEntity).ToList(),
                entity.IdleResource,
                entity.DesiredResource,
                entity.DesiredCapability);
        }
    }
}