var builder = DistributedApplication.CreateBuilder(args);

var app1 = builder.AddProject<Projects.WebApplication1>("app1");

var app2 = builder.AddProject<Projects.WebApplication2>("app2");

builder.AddYarp("ingress")
    .WithEndpoint(hostPort: 8001, scheme: "http")
    .Route("app1", path: "/app1", target: app1)
    .Route("app2", path: "/app2", target: app2);

builder.Build().Run();
