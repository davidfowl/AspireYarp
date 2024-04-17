var builder = DistributedApplication.CreateBuilder(args);

var app1 = builder.AddProject<Projects.WebApplication1>("app1");

var app2 = builder.AddProject<Projects.WebApplication2>("app2");

builder.AddYarp("ingress")
    .WithHttpEndpoint(port: 8001)
    .WithReference(app1)
    .WithReference(app2)
    .LoadFromConfiguration("ReverseProxy");

builder.Build().Run();
